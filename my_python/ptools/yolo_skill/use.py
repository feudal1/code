import os
import cv2
import numpy as np
import tkinter as tk
from tkinter import filedialog
from ultralytics import YOLO
import yaml

def select_model_file():
    """
    弹窗选择模型文件
    
    Returns:
        str: 选择的模型文件路径，如果取消选择则返回None
    """
    root = tk.Tk()
    root.withdraw()  # 隐藏主窗口
    model_path = filedialog.askopenfilename(
        title="选择YOLO模型文件",
        filetypes=[("PyTorch模型文件", "*.pt"), ("所有文件", "*.*")]
    )
    root.destroy()
    return model_path

def select_image_file():
    """
    弹窗选择图像文件
    
    Returns:
        str: 选择的图像文件路径，如果取消选择则返回None
    """
    root = tk.Tk()
    root.withdraw()  # 隐藏主窗口
    image_path = filedialog.askopenfilename(
        title="选择要检测的图像",
        filetypes=[
            ("图像文件", "*.jpg *.jpeg *.png *.bmp *.tiff *.tif"),
            ("所有文件", "*.*")
        ]
    )
    root.destroy()
    return image_path

def load_model(model_path):
    """
    加载YOLO模型
    
    Args:
        model_path: 模型文件路径
        
    Returns:
        YOLO: 加载的模型对象
    """
    try:
        model = YOLO(model_path)
        print(f"成功加载模型: {model_path}")
        return model
    except Exception as e:
        print(f"加载模型失败: {e}")
        return None

def detect_objects(model, image_path, conf_threshold=0.5):
    """
    使用模型检测图像中的对象
    
    Args:
        model: YOLO模型
        image_path: 图像文件路径
        conf_threshold: 置信度阈值
        
    Returns:
        list: 检测结果列表，每个元素包含类别、边界框坐标和置信度
    """
    # 读取图像
    image = cv2.imread(image_path)
    if image is None:
        print(f"无法读取图像: {image_path}")
        return [], None
    
    # 进行预测
    results = model.predict(
        source=image,
        conf=conf_threshold,
        save=False
    )
    
    # 获取检测结果
    detections = []
    result = results[0]  # 获取第一个结果
    
    if result.boxes is not None:
        boxes = result.boxes.xyxy.cpu().numpy()  # 获取边界框坐标 [x1, y1, x2, y2]
        confs = result.boxes.conf.cpu().numpy()  # 获取置信度
        cls_ids = result.boxes.cls.cpu().numpy()  # 获取类别ID
        
        # 获取类别名称（如果模型有类别名称）
        names = result.names if hasattr(result, 'names') else {}
        
        for i in range(len(boxes)):
            x1, y1, x2, y2 = boxes[i]
            conf = confs[i]
            cls_id = int(cls_ids[i])
            class_name = names.get(cls_id, f"Class_{cls_id}")
            
            # 计算边界框中心坐标
            center_x = (x1 + x2) / 2
            center_y = (y1 + y2) / 2
            
            # 计算边界框宽度和高度
            width = x2 - x1
            height = y2 - y1
            
            detections.append({
                'class_id': cls_id,
                'class_name': class_name,
                'bbox': [x1, y1, x2, y2],  # 左上角和右下角坐标
                'center': [center_x, center_y],  # 中心坐标
                'dimensions': [width, height],  # 宽度和高度
                'confidence': conf
            })
    
    return detections, image

def resize_image_for_display(image, max_width=800, max_height=600):
    """
    调整图像大小以适应显示窗口
    
    Args:
        image: 输入图像
        max_width: 最大宽度
        max_height: 最大高度
        
    Returns:
        调整后的图像和缩放比例
    """
    h, w = image.shape[:2]
    
    # 计算缩放比例
    scale_width = max_width / w
    scale_height = max_height / h
    scale = min(scale_width, scale_height)
    
    # 如果图像比显示窗口小，则不放大
    scale = min(scale, 1.0)
    
    new_width = int(w * scale)
    new_height = int(h * scale)
    
    # 调整图像大小
    resized_image = cv2.resize(image, (new_width, new_height), interpolation=cv2.INTER_AREA)
    
    return resized_image, scale

def display_results_with_opencv(image, detections, class_names=None):
    """
    使用OpenCV显示检测结果和坐标信息，支持大图像缩放显示
    """
    # 调整图像大小以适应显示窗口
    resized_img, scale = resize_image_for_display(image, max_width=1200, max_height=800)
    
    # 复制图像以避免修改原始图像
    display_img = resized_img.copy()
    
    # 设置字体
    font = cv2.FONT_HERSHEY_SIMPLEX
    font_scale = 0.6
    font_thickness = 2
    
    for i, detection in enumerate(detections):
        # 将原始坐标按比例缩放
        x1, y1, x2, y2 = detection['bbox']
        x1_scaled = int(x1 * scale)
        y1_scaled = int(y1 * scale)
        x2_scaled = int(x2 * scale)
        y2_scaled = int(y2 * scale)
        
        center_x, center_y = detection['center']
        center_x_scaled = int(center_x * scale)
        center_y_scaled = int(center_y * scale)
        
        conf = detection['confidence']
        class_name = detection['class_name']
        
        # 绘制边界框
        cv2.rectangle(display_img, (x1_scaled, y1_scaled), (x2_scaled, y2_scaled), (0, 255, 0), 2)
        
        # 绘制中心点
        cv2.circle(display_img, (center_x_scaled, center_y_scaled), 5, (0, 0, 255), -1)
        
        # 添加标签文本
        label = f"{class_name}: {conf:.2f}"
        label_size = cv2.getTextSize(label, font, font_scale, font_thickness)[0]
        cv2.rectangle(display_img, (x1_scaled, y1_scaled - label_size[1] - 10), 
                     (x1_scaled + label_size[0], y1_scaled), (0, 255, 0), -1)
        cv2.putText(display_img, label, (x1_scaled, y1_scaled - 5), font, font_scale, (0, 0, 0), font_thickness)
    
    # 显示图像
    cv2.imshow('Detection Results', display_img)
    
    # 创建滑动条用于缩放控制
    def on_trackbar(val):
        pass
    
    cv2.createTrackbar('Scale', 'Detection Results', 100, 200, on_trackbar)
    
    print("检测结果窗口已打开，按任意键关闭...")
    cv2.waitKey(0)
    cv2.destroyAllWindows()

def print_detections(detections):
    """
    打印检测结果
    """
    if not detections:
        print("未检测到任何对象")
        return
    
    print(f"\n检测到 {len(detections)} 个对象:")
    print("-" * 80)
    
    for i, detection in enumerate(detections):
        x1, y1, x2, y2 = detection['bbox']
        center_x, center_y = detection['center']
        width, height = detection['dimensions']
        conf = detection['confidence']
        class_name = detection['class_name']
        class_id = detection['class_id']
        
        print(f"对象 {i+1}:")
        print(f"  类别: {class_name} (ID: {class_id})")
        print(f"  置信度: {conf:.3f}")
        print(f"  边界框坐标: [x1={x1:.2f}, y1={y1:.2f}, x2={x2:.2f}, y2={y2:.2f}]")
        print(f"  中心坐标: [x={center_x:.2f}, y={center_y:.2f}]")
        print(f"  尺寸: [宽度={width:.2f}, 高度={height:.2f}]")
        print("-" * 80)

def main():
    """
    主函数
    """
    print("YOLO模型推理程序")
    print("步骤1: 选择模型文件...")
    model_path = select_model_file()
    
    if not model_path:
        print("未选择模型文件，程序退出。")
        return
    
    print(f"选择的模型文件: {model_path}")
    
    print("\n步骤2: 选择要检测的图像...")
    image_path = select_image_file()
    
    if not image_path:
        print("未选择图像文件，程序退出。")
        return
    
    print(f"选择的图像文件: {image_path}")
    
    # 加载模型
    model = load_model(model_path)
    if model is None:
        return
    
    # 进行检测
    print("\n正在进行检测...")
    detections, image = detect_objects(model, image_path,0.2)
    
    # 打印检测结果
    print_detections(detections)
    
    # 显示结果（可选）
    if image is not None and detections:
        print("\n显示检测结果图像...")
        print("提示: 图像已自动缩放以适应屏幕，边界框和文本已按比例调整")
        display_results_with_opencv(image, detections)
    else:
        print("\n未检测到任何对象或无法读取图像")

if __name__ == "__main__":
    main()