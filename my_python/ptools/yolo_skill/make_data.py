import os
import cv2
import json
from typing import List, Tuple, Dict
import numpy as np
import yaml
from sklearn.model_selection import train_test_split
import shutil
# 添加用于弹窗选择文件夹的库
import tkinter as tk
from tkinter import filedialog, simpledialog
from PIL import Image, ImageDraw, ImageFont
import cv2

# 设置系统编码
import sys
if sys.platform.startswith('win'):
    os.system('chcp 65001 > nul')  # 设置控制台为UTF-8编码

class YOLODatasetCreator:
    def __init__(self, image_folder: str, output_dir: str, class_names: List[str] = None):
        """
        初始化YOLO数据集创建器
        
        Args:
        
            image_folder: 包含图片的文件夹路径
            output_dir: 输出目录路径
            class_names: 类别名称列表
        """
        self.image_folder = image_folder
        self.output_dir = output_dir
        self.class_names = class_names or [f"class_{i}" for i in range(10)]  # 使用传入的类名或默认类名
        self.current_image = None
        self.current_image_path = None
        self.annotations = []
        self.current_bboxes = []
        self.drawing = False
        self.start_point = (0, 0)
        self.end_point = (0, 0)
        self.current_class_id = 0
        
        # 添加缩放和偏移参数，用于坐标转换
        self.scale = 1.0
        self.offset_x = 0
        self.offset_y = 0
        
        # 创建可视化目录
        self.visual_output_dir = os.path.join(output_dir, "visualized_annotations")
        os.makedirs(self.visual_output_dir, exist_ok=True)
        
    def get_image_files(self) -> List[str]:
        """获取文件夹中所有图片文件，支持中文路径"""
        image_extensions = ['.jpg', '.jpeg', '.png', '.bmp', '.tiff', '.tif', '.webp', '.gif']
        image_files = []
        
        for file in os.listdir(self.image_folder):
            if any(file.lower().endswith(ext) for ext in image_extensions):
                image_path = os.path.join(self.image_folder, file)
                # 检查文件是否可以被正确读取
                if self.can_read_image(image_path):
                    image_files.append(image_path)
        
        return sorted(image_files)
    
    def can_read_image(self, image_path: str) -> bool:
        """检查是否可以读取图片，支持中文路径"""
        try:
            img_array = np.fromfile(image_path, dtype=np.uint8)
            img = cv2.imdecode(img_array, cv2.IMREAD_COLOR)
            return img is not None
        except:
            return False

    def mouse_callback(self, event: int, x: int, y: int, flags: int, param):
        """鼠标回调函数，用于绘制边界框"""
        # 将鼠标坐标从显示图像映射到原始图像
        x_raw = (x - self.offset_x) / self.scale
        y_raw = (y - self.offset_y) / self.scale
        
        if event == cv2.EVENT_LBUTTONDOWN:
            self.drawing = True
            self.start_point = (x_raw, y_raw)
            self.end_point = (x_raw, y_raw)
            
        elif event == cv2.EVENT_MOUSEMOVE:
            if self.drawing:
                self.end_point = (x_raw, y_raw)
                
        elif event == cv2.EVENT_LBUTTONUP:
            self.drawing = False
            self.end_point = (x_raw, y_raw)
            
            # 确保坐标顺序正确
            x1, y1 = self.start_point
            x2, y2 = self.end_point
            left = min(x1, x2)
            top = min(y1, y2)
            right = max(x1, x2)
            bottom = max(y1, y2)
            
            # 添加边界框到当前图片的标注
            height, width = self.current_image.shape[:2]
            bbox = {
                'class_id': self.current_class_id,
                'class_name': self.class_names[self.current_class_id] if self.current_class_id < len(self.class_names) else f"class_{self.current_class_id}",
                'bbox': [left, top, right, bottom],  # 原始坐标（已校正）
                'bbox_normalized': [  # YOLO格式 (x_center, y_center, width, height)
                    (left + right) / 2.0 / width,  # x_center
                    (top + bottom) / 2.0 / height,  # y_center
                    (right - left) / width,  # width
                    (bottom - top) / height   # height
                ]
            }
            self.current_bboxes.append(bbox)
            
    def draw_chinese_text(self, img, text, position, font_size=20, color=(255, 0, 0)):
        """
        在图片上绘制中文文本
        """
        # 将OpenCV图像转换为PIL图像
        img_pil = Image.fromarray(cv2.cvtColor(img, cv2.COLOR_BGR2RGB))
        draw = ImageDraw.Draw(img_pil)
        
        # 尝试使用系统字体，如果找不到则使用默认字体
        try:
            # Windows系统常用中文字体路径
            font = ImageFont.truetype("C:/Windows/Fonts/simhei.ttf", font_size)
        except:
            try:
                # Linux系统常用中文字体路径
                font = ImageFont.truetype("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", font_size)
            except:
                # 如果找不到字体，使用默认字体
                font = ImageFont.load_default()
        
        # 绘制文本
        draw.text(position, text, font=font, fill=(color[2], color[1], color[0]))
        
        # 将PIL图像转换回OpenCV格式
        img = cv2.cvtColor(np.array(img_pil), cv2.COLOR_RGB2BGR)
        return img

    def draw_bboxes(self, img: np.ndarray) -> np.ndarray:
        """在图片上绘制当前的边界框"""
        img_copy = img.copy()
        
        # 绘制临时正在绘制的框
        if self.drawing:
            # 将原始坐标转换为显示坐标
            x1 = int(self.start_point[0] * self.scale + self.offset_x)
            y1 = int(self.start_point[1] * self.scale + self.offset_y)
            x2 = int(self.end_point[0] * self.scale + self.offset_x)
            y2 = int(self.end_point[1] * self.scale + self.offset_y)
            cv2.rectangle(img_copy, (x1, y1), (x2, y2), (0, 255, 0), 2)
        
        # 绘制已确认的框
        for bbox in self.current_bboxes:
            x1, y1, x2, y2 = bbox['bbox']
            # 将原始坐标转换为显示坐标
            x1 = int(x1 * self.scale + self.offset_x)
            y1 = int(y1 * self.scale + self.offset_y)
            x2 = int(x2 * self.scale + self.offset_x)
            y2 = int(y2 * self.scale + self.offset_y)
            cv2.rectangle(img_copy, (x1, y1), (x2, y2), (255, 0, 0), 2)
            # 显示类别ID和名称
            class_id = bbox['class_id']
            class_name = bbox['class_name']
            label = f"{class_id}:{class_name}"
            # 使用自定义函数绘制中文文本
            img_copy = self.draw_chinese_text(img_copy, label, (x1, y1 - 20), 16, (255, 0, 0))
        
        return img_copy

    def resize_image_to_window(self, image, max_width, max_height):
        """调整图片大小以适应窗口，保持宽高比，并记录缩放比例和偏移"""
        h, w = image.shape[:2]
        
        # 计算缩放比例，确保图片完全适应窗口
        scale_width = max_width / w
        scale_height = max_height / h
        scale = min(scale_width, scale_height)
        
        # 如果图片比窗口小，则不放大
        scale = min(scale, 1.0)
        
        new_width = int(w * scale)
        new_height = int(h * scale)
        
        resized_image = cv2.resize(image, (new_width, new_height), interpolation=cv2.INTER_AREA)
        
        # 记录缩放比例和偏移量
        self.scale = scale
        self.offset_x = 0
        self.offset_y = 0
        
        # 如果需要，创建一个带有边框的图像以填充整个窗口
        if new_width < max_width or new_height < max_height:
            # 创建一个黑色背景的窗口大小图像
            window_img = np.zeros((max_height, max_width, 3), dtype=np.uint8)
            
            # 计算居中位置
            y_offset = (max_height - new_height) // 2
            x_offset = (max_width - new_width) // 2
            
            # 将缩放后的图像放置在中心位置
            window_img[y_offset:y_offset+new_height, x_offset:x_offset+new_width] = resized_image
            
            # 更新偏移量
            self.offset_x = x_offset
            self.offset_y = y_offset
            return window_img
        
        return resized_image

    def save_annotated_image(self, image_path: str, bboxes: List[Dict], output_dir: str):
        """
        保存带有标注框的图片
        """
        # 读取原始图片
        img_array = np.fromfile(image_path, dtype=np.uint8)
        img = cv2.imdecode(img_array, cv2.IMREAD_COLOR)
        
        # 绘制标注框
        for bbox in bboxes:
            x1, y1, x2, y2 = bbox['bbox']
            cv2.rectangle(img, (int(x1), int(y1)), (int(x2), int(y2)), (255, 0, 0), 2)
            # 显示类别ID和名称
            class_id = bbox['class_id']
            class_name = bbox['class_name']
            label = f"{class_id}:{class_name}"
            img = self.draw_chinese_text(img, label, (int(x1), int(y1) - 10), 16, (255, 0, 0))
        
        # 生成输出路径
        image_filename = os.path.basename(image_path)
        name, ext = os.path.splitext(image_filename)
        output_path = os.path.join(output_dir, f"{name}_annotated{ext}")
        
        # 保存图片（支持中文路径）
        cv2.imencode(ext, img)[1].tofile(output_path)
        
        return output_path

    def run(self):
        """运行标注工具"""
        # 确保输出编码为UTF-8
        if hasattr(sys.stdout, 'reconfigure'):
            sys.stdout.reconfigure(encoding='utf-8')
        
        image_files = self.get_image_files()
        if not image_files:
            print(f"在文件夹 {self.image_folder} 中未找到图片文件")
            return
        
        cv2.namedWindow('YOLO Dataset Creator', cv2.WINDOW_AUTOSIZE)
        
        # 设置固定窗口大小
        WINDOW_WIDTH = 800
        WINDOW_HEIGHT = 600
        cv2.resizeWindow('YOLO Dataset Creator', WINDOW_WIDTH, WINDOW_HEIGHT)
        
        cv2.setMouseCallback('YOLO Dataset Creator', self.mouse_callback)
        
        current_idx = 0
        
        print("可用类别:")
        for i, name in enumerate(self.class_names):
            try:
                print(f"  {i}: {name}")
            except UnicodeEncodeError:
                print(f"  {i}: {name.encode('utf-8', errors='replace').decode('utf-8', errors='replace')}")
        
        while current_idx < len(image_files):
            self.current_image_path = image_files[current_idx]
            
            # 使用支持中文路径的方式读取图像
            try:
                img_array = np.fromfile(self.current_image_path, dtype=np.uint8)
                self.current_image = cv2.imdecode(img_array, cv2.IMREAD_COLOR)
            except Exception as e:
                print(f"无法加载图像: {self.current_image_path}, 错误: {e}")
                current_idx += 1
                continue
            
            # 检查图像是否成功加载
            if self.current_image is None:
                print(f"无法加载图像: {self.current_image_path}")
                current_idx += 1
                continue
            
            # 调整图片大小以适应固定窗口（保持宽高比）
            resized_image = self.resize_image_to_window(self.current_image, WINDOW_WIDTH, WINDOW_HEIGHT)
            
            self.current_bboxes = []
            
            print(f"正在标注: {os.path.basename(self.current_image_path)} ({current_idx + 1}/{len(image_files)})")
            print("操作说明:")
            print("- 鼠标拖拽绘制边界框")
            print("- 按 'n' 进入下一张图片")
            print("- 按 'p' 返回上一张图片")
            print("- 按 'c' 切换类别ID (当前: {} - {})".format(self.current_class_id, self.class_names[self.current_class_id] if self.current_class_id < len(self.class_names) else f"class_{self.current_class_id})"))
            print("- 按 'd' 删除最后一个标注框")
            print("- 按 's' 保存当前进度")
            print("- 按 'q' 退出程序")
            
            while True:
                display_img = self.draw_bboxes(resized_image)
                
                # 显示当前类别ID和名称 - 使用中文绘制函数
                current_class_name = self.class_names[self.current_class_id] if self.current_class_id < len(self.class_names) else f"class_{self.current_class_id}"
                class_info = f"Current Class: {self.current_class_id} - {current_class_name}"
                display_img = self.draw_chinese_text(display_img, class_info, (10, 30), 18, (0, 255, 255))
                
                help_text = "Press 'h' for help"
                display_img = self.draw_chinese_text(display_img, help_text, (10, 60), 14, (0, 255, 255))
                
                # 在窗口标题中显示图片尺寸信息
                original_h, original_w = self.current_image.shape[:2]
                cv2.setWindowTitle('YOLO Dataset Creator', f'YOLO Dataset Creator - {original_w}x{original_h} (resized for display)')
                
                cv2.imshow('YOLO Dataset Creator', display_img)
                key = cv2.waitKey(1) & 0xFF
                
                if key == ord('n'):  # 下一张
                    break
                elif key == ord('p'):  # 上一张
                    if current_idx > 0:
                        current_idx -= 2  # -1是为了抵消循环末尾的+1
                        if current_idx < -1:
                            current_idx = -1
                    break
                elif key == ord('c'):  # 切换类别
                    self.current_class_id = (self.current_class_id + 1) % len(self.class_names)  # 根据类名数量循环
                    current_class_name = self.class_names[self.current_class_id]
                    try:
                        print(f"类别已切换为: {self.current_class_id} - {current_class_name}")
                    except UnicodeEncodeError:
                        print(f"类别已切换为: {self.current_class_id} - {current_class_name.encode('utf-8', errors='replace').decode('utf-8', errors='replace')}")
                elif key == ord('d'):  # 删除最后一个框
                    if self.current_bboxes:
                        self.current_bboxes.pop()
                        print("删除了最后一个边界框")
                elif key == ord('s'):  # 保存当前进度
                    self.save_annotations()
                    print("当前进度已保存")
                elif key == ord('h'):  # 显示帮助
                    print("\n快捷键说明:")
                    print("n - 下一张图片")
                    print("p - 上一张图片")
                    print("c - 切换类别ID")
                    print("d - 删除最后一个标注框")
                    print("s - 保存当前进度")
                    print("q - 退出程序")
                    print("h - 显示帮助\n")
                elif key == ord('q'):  # 退出
                    cv2.destroyAllWindows()
                    self.save_annotations()
                    print("程序已退出，数据已保存")
                    return
            
            # 保存当前图片的标注
            if self.current_bboxes:
                annotation = {
                    'image_path': self.current_image_path,
                    'image_width': self.current_image.shape[1],  # 图像宽度
                    'image_height': self.current_image.shape[0],  # 图像高度
                    'annotations': self.current_bboxes
                }
                self.annotations.append(annotation)
                
                # 同时保存可视化图片
                self.save_annotated_image(self.current_image_path, self.current_bboxes, self.visual_output_dir)
            
            current_idx += 1
        
        cv2.destroyAllWindows()
        self.save_annotations()
        print(f"标注完成！数据已保存到 {self.output_dir}")
        print(f"可视化标注图片已保存到 {self.visual_output_dir}")
    
    def save_annotations(self):
        """保存标注数据到JSON文件"""
        data = {
            'dataset_info': {
                'total_images': len(self.annotations),
                'created_at': str(cv2.getTickCount() / cv2.getTickFrequency()),
                'image_folder': self.image_folder,  # 添加图片文件夹路径信息
                'image_width': self.current_image.shape[1] if self.current_image is not None else 0,  # 当前图像宽度
                'image_height': self.current_image.shape[0] if self.current_image is not None else 0,  # 当前图像高度
                'class_names': self.class_names  # 保存类名信息
            },
            'annotations': self.annotations
        }
        
        json_path = os.path.join(self.output_dir, "annotations.json")
        with open(json_path, 'w', encoding='utf-8') as f:
            json.dump(data, f, ensure_ascii=False, indent=2)
        
        # 保存可视化图片
        for annotation in self.annotations:
            image_path = annotation['image_path']
            bboxes = annotation['annotations']
            self.save_annotated_image(image_path, bboxes, self.visual_output_dir)

def convert_to_yolo_format_and_split(json_file: str, output_dir: str, class_names: List[str] = None):
    """
    将JSON格式的标注转换为YOLO格式的txt文件，并自动划分训练、验证、测试集
    
    Args:
        json_file: 输入的JSON文件路径
        output_dir: 输出的YOLO格式文件目录
        class_names: 类别名称列表
    """
    with open(json_file, 'r', encoding='utf-8') as f:
        data = json.load(f)
    
    # 从JSON文件中获取类名信息，如果传入了class_names则使用传入的
    if class_names is None:
        class_names = data.get('dataset_info', {}).get('class_names', [f"class_{i}" for i in range(10)])
    
    # 创建目录结构
    images_dir = os.path.join(output_dir, "images")
    labels_dir = os.path.join(output_dir, "labels")
    
    os.makedirs(images_dir, exist_ok=True)
    os.makedirs(labels_dir, exist_ok=True)
    
    # 创建训练、验证、测试子目录
    for subdir in ["train", "val", "test"]:
        os.makedirs(os.path.join(images_dir, subdir), exist_ok=True)
        os.makedirs(os.path.join(labels_dir, subdir), exist_ok=True)
    
    # 收集所有图片路径和对应的标注
    all_image_paths = []
    all_annotation_paths = []
    
    for annotation in data['annotations']:
        image_path = annotation['image_path']
        bboxes = annotation['annotations']
        
        # 复制图片到相应目录
        image_filename = os.path.basename(image_path)
        image_name, image_ext = os.path.splitext(image_filename)
        
        # 为每张图片生成YOLO格式的txt文件
        txt_path = os.path.join(labels_dir, "train", f"{image_name}.txt")
        
        with open(txt_path, 'w', encoding='utf-8') as f:
            for bbox in bboxes:
                class_id = bbox['class_id']
                # YOLO格式: class_id x_center y_center width height (全部归一化)
                x_center, y_center, width, height = bbox['bbox_normalized']
                f.write(f"{class_id} {x_center:.6f} {y_center:.6f} {width:.6f} {height:.6f}\n")
        
        # 复制图片到images/train目录
        dst_image_path = os.path.join(images_dir, "train", image_filename)
        # 使用支持中文路径的复制方式
        try:
            # 读取原图片
            img_array = np.fromfile(image_path, dtype=np.uint8)
            img = cv2.imdecode(img_array, cv2.IMREAD_COLOR)
            # 保存到目标位置
            cv2.imencode(image_ext, img)[1].tofile(dst_image_path)
        except:
            # 如果中文路径方式失败，使用原始方式
            shutil.copy2(image_path, dst_image_path)
        
        all_image_paths.append(dst_image_path)
        all_annotation_paths.append(txt_path)
    
    # 检查数据集大小，如果样本数太少则不进行划分
    num_samples = len(all_image_paths)
    if num_samples < 3:
        print(f"数据集样本数为 {num_samples}，不足以进行标准数据集划分，所有数据将放在训练集")
        
        # 从标注中获取实际的类别数量
        all_class_ids = set()
        for annotation in data['annotations']:
            for bbox in annotation['annotations']:
                all_class_ids.add(bbox['class_id'])
        num_classes = max(all_class_ids) + 1 if all_class_ids else len(class_names)
        
        # 生成data.yaml配置文件
        data_yaml = {
            'path': output_dir,
            'train': 'images/train',
            'val': 'images/val', 
            'test': 'images/test',
            'nc': num_classes,
            'names': class_names[:num_classes]
        }
        
        yaml_path = os.path.join(output_dir, "data.yaml")
        with open(yaml_path, 'w', encoding='utf-8') as f:
            yaml.dump(data_yaml, f, default_flow_style=False, allow_unicode=True)
        
        print(f"YOLO格式文件已保存到 {output_dir}")
        print(f"由于样本数量不足，所有 {num_samples} 张图片都放在训练集中")
        print(f"配置文件已生成: {yaml_path}")
        return
    
    # 划分数据集（70%训练，20%验证，10%测试）
    train_img, temp_img, train_lbl, temp_lbl = train_test_split(
        all_image_paths, all_annotation_paths, test_size=0.3, random_state=42
    )
    val_img, test_img, val_lbl, test_lbl = train_test_split(
        temp_img, temp_lbl, test_size=1/3, random_state=42
    )
    
    # 移动文件到对应的子目录
    def move_files_to_subdir(file_paths, subdir):
        """将文件移动到指定子目录，支持中文路径"""
        for file_path in file_paths:
            filename = os.path.basename(file_path)
            dst_path = os.path.join(os.path.dirname(os.path.dirname(file_path)), subdir, filename)
            
            # 如果是图片文件，需要重新复制（处理中文路径）
            if os.path.splitext(filename)[1].lower() in ['.jpg', '.jpeg', '.png', '.bmp', '.tiff', '.tif', '.webp', '.gif']:
                src_img_path = os.path.join(output_dir, "images", "train", filename)
                dst_img_path = os.path.join(output_dir, "images", subdir, filename)
                
                # 读取原图片并保存到新位置
                try:
                    img_array = np.fromfile(src_img_path, dtype=np.uint8)
                    img = cv2.imdecode(img_array, cv2.IMREAD_COLOR)
                    img_ext = os.path.splitext(filename)[1]
                    cv2.imencode(img_ext, img)[1].tofile(dst_img_path)
                    
                    # 删除原文件
                    os.remove(src_img_path)
                except:
                    # 如果中文路径方式失败，使用原始方式
                    if os.path.exists(src_img_path):
                        shutil.move(src_img_path, dst_img_path)
            else:  # 标注文件
                dst_lbl_path = os.path.join(output_dir, "labels", subdir, filename)
                if os.path.exists(file_path):
                    shutil.move(file_path, dst_lbl_path)
    
    move_files_to_subdir(val_img, "val")
    move_files_to_subdir(test_img, "test")
    
    # 生成data.yaml配置文件
    # 从标注中获取实际的类别数量
    all_class_ids = set()
    for annotation in data['annotations']:
        for bbox in annotation['annotations']:
            all_class_ids.add(bbox['class_id'])
    num_classes = max(all_class_ids) + 1 if all_class_ids else len(class_names)
    
    data_yaml = {
        'path': output_dir,  # 数据集根目录
        'train': 'images/train',  # 训练图像目录
        'val': 'images/val',  # 验证图像目录
        'test': 'images/test',  # 测试图像目录（可选）
        'nc': num_classes,  # 类别数量
        'names': class_names[:num_classes]  # 类别名称列表
    }
    
    yaml_path = os.path.join(output_dir, "data.yaml")
    with open(yaml_path, 'w', encoding='utf-8') as f:
        yaml.dump(data_yaml, f, default_flow_style=False, allow_unicode=True)
    
    print(f"YOLO格式文件已保存到 {output_dir}")
    print(f"数据集划分完成：")
    try:
        print(f"- 训练集: {len(train_img)} 张图片")
        print(f"- 验证集: {len(val_img)} 张图片")
        print(f"- 测试集: {len(test_img)} 张图片")
    except UnicodeEncodeError:
        print(f"- 训练集: {len(train_img)} 张图片")
        print(f"- 验证集: {len(val_img)} 张图片")
        print(f"- 测试集: {len(test_img)} 张图片")
    print(f"配置文件已生成: {yaml_path}")

    
def select_folder_dialog():
    """弹窗选择文件夹"""
    # 创建一个隐藏的tkinter窗口
    root = tk.Tk()
    root.withdraw()  # 隐藏主窗口
    
    # 显示文件夹选择对话框
    folder_path = filedialog.askdirectory(title="选择图片文件夹")
    
    # 销毁tkinter窗口
    root.destroy()
    
    return folder_path

def input_class_names():
    """让用户输入类名"""
    root = tk.Tk()
    root.withdraw()  # 隐藏主窗口
    
    # 获取类别数量
    num_classes = simpledialog.askinteger("类别数量", "请输入类别数量:", minvalue=1, maxvalue=100)
    
    if num_classes is None:  # 用户取消输入
        root.destroy()
        return None
    
    class_names = []
    for i in range(num_classes):
        class_name = simpledialog.askstring("类名", f"请输入第 {i+1} 个类别的名称 (ID: {i}):")
        if class_name is None:  # 用户取消输入
            root.destroy()
            return None
        class_names.append(class_name)
    
    root.destroy()
    return class_names

# 使用示例
if __name__ == "__main__":
    # 弹窗选择图片文件夹
    image_folder = select_folder_dialog()
    
    # 如果用户取消选择，则退出
    if not image_folder:
        print("未选择文件夹，程序退出")
        exit()
    
    # 让用户输入类名
    class_names = input_class_names()
    
    # 如果用户取消输入类名，则退出
    if class_names is None:
        print("未输入类名，程序退出")
        exit()
    
    # 设置输出目录
    output_dir = os.path.join(image_folder, "yolo_dataset")
    os.makedirs(output_dir, exist_ok=True)
    
    # 创建数据集标注工具
    creator = YOLODatasetCreator(image_folder, output_dir, class_names)
    
    # 运行标注工具
    creator.run()
    
    # 生成YOLO格式的数据集和配置文件
    json_path = os.path.join(output_dir, "annotations.json")
    if os.path.exists(json_path):
        convert_to_yolo_format_and_split(json_path, output_dir, class_names)
    else:
        print("未找到标注文件，跳过YOLO格式转换")