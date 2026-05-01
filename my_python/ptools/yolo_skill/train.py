import os
import yaml
from ultralytics import YOLO
import cv2
import numpy as np
import tkinter as tk
from tkinter import filedialog
import shutil
import random
import torch

def select_dataset_folder():
    """
    弹窗选择数据集文件夹
    
    Returns:
        str: 选择的文件夹路径，如果取消选择则返回None
    """
    root = tk.Tk()
    root.withdraw()  # 隐藏主窗口
    folder_path = filedialog.askdirectory(title="选择数据集文件夹")
    root.destroy()
    return folder_path

def create_validation_set(train_img_dir, train_lbl_dir, val_img_dir, val_lbl_dir, split_ratio=0.2):
    """
    从训练集分割出验证集
    
    Args:
        train_img_dir: 训练图片目录
        train_lbl_dir: 训练标签目录
        val_img_dir: 验证图片目录
        val_lbl_dir: 验证标签目录
        split_ratio: 分割比例
    """
    # 获取训练集中的图片文件
    img_files = [f for f in os.listdir(train_img_dir) if f.lower().endswith(('.jpg', '.jpeg', '.png', '.bmp', '.tiff', '.tif'))
                 and not is_augmented_image(f)]  # 过滤增强过的图片
    
    # 随机选择一部分图片作为验证集
    num_val = max(1, int(len(img_files) * split_ratio))
    val_files = random.sample(img_files, num_val)
    
    # 移动文件到验证集目录
    for img_file in val_files:
        # 移动图片
        src_img_path = os.path.join(train_img_dir, img_file)
        dst_img_path = os.path.join(val_img_dir, img_file)
        shutil.move(src_img_path, dst_img_path)
        
        # 移动对应的标签文件
        label_file = os.path.splitext(img_file)[0] + '.txt'
        src_lbl_path = os.path.join(train_lbl_dir, label_file)
        dst_lbl_path = os.path.join(val_lbl_dir, label_file)
        
        if os.path.exists(src_lbl_path):
            shutil.move(src_lbl_path, dst_lbl_path)
    
    print(f"创建了包含 {num_val} 个样本的验证集")

def is_augmented_image(filename: str) -> bool:
    """检查文件名是否是增强过的图片"""
    lower_filename = filename.lower()
    # 检查文件名是否包含常见的增强标识
    augmented_indicators = ['_aug', '_enhanced', '_flipped', '_rotated', '_scaled', '_bright', '_contrast', '_aug_']
    for indicator in augmented_indicators:
        if indicator in lower_filename:
            return True
    return False

def train_yolo_model(data_yaml_path, model_type='yolo11n.pt'):
    """
    训练YOLO模型
    
    Args:
        data_yaml_path: 数据配置文件路径
        model_type: YOLO模型类型
    """
    # 加载预训练模型
    model = YOLO(model_type)
    
    # 开始训练
    results = model.train(
        data=data_yaml_path,              # 数据配置文件
        epochs=100,                       # 增加训练轮数
        imgsz=640,                        # 图像尺寸
        batch=4,                         # 批次大小（根据内存调整）
        save_period=10,                   # 每10轮保存一次
        save=True,                        # 保存模型
        project='runs/train',             # 保存项目目录
        name='full_dataset_training',     # 实验名称
        exist_ok=True,                    # 允许覆盖
        pretrained=True,                  # 使用预训练权重
        device=0 if torch.cuda.is_available() else 'cpu',  # 使用GPU如果可用
        workers=0,                        # Windows下设置为0避免多进程问题
        optimizer='AdamW',                # 使用AdamW优化器
        lr0=0.001,                       # 初始学习率
        lrf=0.01,                        # 最终学习率
        momentum=0.937,                  # 动量
        weight_decay=0.0005,             # 权重衰减
        warmup_epochs=3.0,               # 预热轮数
        warmup_momentum=0.8,             # 预热动量
        warmup_bias_lr=0.1,              # 预热偏置学习率
        box=7.5,                         # 框损失增益
        cls=0.5,                         # 分类损失增益
        dfl=1.5,                         # DFL损失增益
        single_cls=False,                # 多类别训练
        rect=False,                      # 不使用矩形训练
        cos_lr=False,                    # 不使用余弦学习率
        close_mosaic=10                  # 在最后10轮关闭马赛克增强
    )
    
    return model, results

def augment_single_image_dataset(image_path, label_path, output_dir, augmentation_factor=10):
    """
    通过数据增强扩展单张图片数据集
    
    Args:
        image_path: 原始图片路径
        label_path: 原始标签路径
        output_dir: 输出目录
        augmentation_factor: 扩增倍数
    """
    try:
        # 读取原始图片和标签
        original_img = cv2.imread(image_path)
        if original_img is None:
            print(f"无法读取图片: {image_path}")
            return
        
        with open(label_path, 'r', encoding='utf-8') as f:
            original_labels = f.readlines()
        
        # 创建输出目录
        aug_img_dir = os.path.join(output_dir, 'images', 'train')
        aug_lbl_dir = os.path.join(output_dir, 'labels', 'train')
        os.makedirs(aug_img_dir, exist_ok=True)
        os.makedirs(aug_lbl_dir, exist_ok=True)
        
        # 获取原始图片名称
        original_name = os.path.splitext(os.path.basename(image_path))[0]
        
        # 检查是否已经存在增强过的图片，避免重复增强
        existing_augmented = [f for f in os.listdir(aug_img_dir) 
                             if f.startswith(original_name + '_aug_') and f.endswith('.jpg')]
        
        if existing_augmented:
            print(f"检测到已存在 {len(existing_augmented)} 个增强图片，跳过数据增强...")
            return
        
        # 复制原始图片
        original_img_path = os.path.join(aug_img_dir, f"{original_name}_0.jpg")
        cv2.imwrite(original_img_path, original_img)
        with open(os.path.join(aug_lbl_dir, f"{original_name}_0.txt"), 'w', encoding='utf-8') as f:
            f.writelines(original_labels)
        
        print(f"已复制原始图片: {original_name}_0.jpg")
        
        # 应用各种数据增强
        try:
            import albumentations as A
        except ImportError:
            print("警告: 未安装albumentations库，跳过数据增强...")
            return
        
        transform = A.Compose([
            A.HorizontalFlip(p=0.5),
            A.RandomBrightnessContrast(brightness_limit=0.2, contrast_limit=0.2, p=0.5),
            A.HueSaturationValue(hue_shift_limit=10, sat_shift_limit=10, val_shift_limit=10, p=0.3),
            A.Rotate(limit=10, p=0.3),
            A.RandomScale(scale_limit=0.1, p=0.3),
            A.GaussNoise(var_limit=(10.0, 50.0), p=0.2),
            A.MotionBlur(blur_limit=3, p=0.1),
            A.CLAHE(clip_limit=2.0, p=0.1)
        ], bbox_params=A.BboxParams(format='yolo', label_fields=['class_labels']))
        
        for i in range(1, augmentation_factor):
            # 解析原始标签
            bboxes = []
            class_labels = []
            
            for line in original_labels:
                parts = line.strip().split()
                if len(parts) == 5:  # class_id x_center y_center width height
                    class_id, x_center, y_center, width, height = map(float, parts)
                    bboxes.append([x_center, y_center, width, height])
                    class_labels.append(int(class_id))
            
            # 应用增强
            try:
                transformed = transform(
                    image=original_img,
                    bboxes=bboxes,
                    class_labels=class_labels
                )
            except Exception as e:
                print(f"数据增强失败: {e}")
                continue
            
            # 保存增强后的图片
            aug_img = transformed['image']
            aug_bboxes = transformed['bboxes']
            
            aug_img_path = os.path.join(aug_img_dir, f"{original_name}_aug_{i}.jpg")
            success, encoded_img = cv2.imencode('.jpg', aug_img)
            if success:
                with open(aug_img_path, 'wb') as f:
                    f.write(encoded_img.tobytes())
            
            # 保存增强后的标签
            aug_lbl_path = os.path.join(aug_lbl_dir, f"{original_name}_aug_{i}.txt")
            with open(aug_lbl_path, 'w', encoding='utf-8') as f:
                for j, bbox in enumerate(aug_bboxes):
                    class_id = class_labels[j]
                    x_center, y_center, width, height = bbox
                    f.write(f"{class_id} {x_center:.6f} {y_center:.6f} {width:.6f} {height:.6f}\n")
            
            print(f"已增强: {original_name}_aug_{i}.jpg")
    
    except Exception as e:
        print(f"增强过程中发生错误: {e}")

def prepare_data_yaml_for_training(output_dir, class_names):
    """
    准备训练所需的数据配置文件
    
    Args:
        output_dir: 输出目录
        class_names: 类别名称列表
    
    Returns:
        str: data.yaml 文件路径
    """
    # 创建验证集目录
    val_img_dir = os.path.join(output_dir, "images", "val")
    val_lbl_dir = os.path.join(output_dir, "labels", "val")
    os.makedirs(val_img_dir, exist_ok=True)
    os.makedirs(val_lbl_dir, exist_ok=True)
    
    # 从训练集中分割出一部分作为验证集
    train_img_dir = os.path.join(output_dir, "images", "train")
    train_lbl_dir = os.path.join(output_dir, "labels", "train")
    
    if os.path.exists(train_img_dir) and os.path.exists(train_lbl_dir):
        # 只对原始图片进行验证集分割，避免增强图片被误分
        create_validation_set(train_img_dir, train_lbl_dir, val_img_dir, val_lbl_dir, split_ratio=0.2)
    
    data_yaml = {
        'path': output_dir,
        'train': 'images/train',
        'val': 'images/val',
        'test': 'images/test',  # 如果没有测试集，可以设为与val相同或留空
        'nc': len(class_names),  # 类别数量
        'names': class_names     # 类别名称列表
    }
    
    yaml_path = os.path.join(output_dir, 'data.yaml')
    with open(yaml_path, 'w', encoding='utf-8') as f:
        yaml.dump(data_yaml, f, default_flow_style=False, allow_unicode=True)
    
    print(f"数据配置文件已保存至: {yaml_path}")
    print(f"配置内容: {data_yaml}")
    
    return yaml_path

def read_class_names_from_yaml(yaml_path):
    """
    从data.yaml文件中读取类别名称
    
    Args:
        yaml_path: data.yaml文件路径
    
    Returns:
        list: 类别名称列表
    """
    with open(yaml_path, 'r', encoding='utf-8') as f:
        data = yaml.safe_load(f)
    
    return data.get('names', [])

def process_all_images_for_training(dataset_dir, augmentation_factor=10):
    """
    处理数据集中的所有图片用于训练
    
    Args:
        dataset_dir: 数据集目录
        augmentation_factor: 每张图片的增强倍数
    """
    # 获取训练集目录
    images_dir = os.path.join(dataset_dir, "images", "train")
    labels_dir = os.path.join(dataset_dir, "labels", "train")
    
    # 检查目录是否存在
    if not os.path.exists(images_dir) or not os.path.exists(labels_dir):
        print(f"数据集目录结构不正确。请确保存在 {images_dir} 和 {labels_dir} 目录。")
        return False
    
    # 获取所有原始图片文件（排除已增强的图片）
    img_files = [f for f in os.listdir(images_dir) 
                 if f.lower().endswith(('.jpg', '.jpeg', '.png', '.bmp', '.tiff', '.tif')) 
                 and not is_augmented_image(f)]
    
    if not img_files:
        print("在图片目录中未找到原始图片文件。")
        return False
    
    print(f"发现 {len(img_files)} 张原始图片，将对每张图进行增强...")
    
    # 对每张原始图片进行增强
    processed_count = 0
    for img_file in img_files:
        img_path = os.path.join(images_dir, img_file)
        lbl_path = os.path.join(labels_dir, os.path.splitext(img_file)[0] + '.txt')
        
        if os.path.exists(lbl_path):
            # 检查是否已经为该原始图片生成了增强图片
            original_name = os.path.splitext(img_file)[0]
            existing_augmented = [f for f in os.listdir(images_dir) 
                                 if f.startswith(original_name + '_aug_') and f.endswith('.jpg')]
            
            if existing_augmented:
                print(f"跳过 {img_file} - 检测到已存在的增强图片")
                continue
            
            augment_single_image_dataset(img_path, lbl_path, dataset_dir, augmentation_factor)
            print(f"已处理: {img_file}")
            processed_count += 1
        else:
            print(f"警告: 未找到标签文件 {lbl_path}")
    
    print(f"成功处理了 {processed_count} 张原始图片")
    return processed_count > 0

# 使用示例
if __name__ == "__main__":
    # 弹窗选择数据集文件夹
    dataset_dir = select_dataset_folder()
    
    if not dataset_dir:
        print("未选择数据集文件夹，程序退出。")
        exit()
    
    print(f"选择的数据集文件夹: {dataset_dir}")
    
    # 检查是否存在data.yaml文件
    data_yaml_path = os.path.join(dataset_dir, 'data.yaml')
    
    if os.path.exists(data_yaml_path):
        # 从data.yaml中读取类别名称
        class_names = read_class_names_from_yaml(data_yaml_path)
        print(f"从data.yaml中读取到的类别: {class_names}")
    else:
        print("未找到data.yaml文件，程序退出。")
        exit()
    
    # 获取训练图片数量（只统计原始图片）
    train_img_dir = os.path.join(dataset_dir, "images", "train")
    if os.path.exists(train_img_dir):
        original_img_files = [f for f in os.listdir(train_img_dir) 
                             if f.lower().endswith(('.jpg', '.jpeg', '.png', '.bmp', '.tiff', '.tif'))
                             and not is_augmented_image(f)]
        print(f"发现 {len(original_img_files)} 张原始图片")
        
        # 检查是否已有增强图片
        augmented_img_files = [f for f in os.listdir(train_img_dir) 
                              if is_augmented_image(f)]
        if augmented_img_files:
            print(f"检测到 {len(augmented_img_files)} 个已增强的图片")
            response = input("是否跳过数据增强步骤？(y/N): ")
            if response.lower() == 'y':
                print("跳过数据增强步骤，直接开始训练...")
            else:
                print("继续进行数据增强...")
                success = process_all_images_for_training(dataset_dir, augmentation_factor=10)
                if not success:
                    print("处理图片失败，程序退出。")
                    exit()
        else:
            success = process_all_images_for_training(dataset_dir, augmentation_factor=10)
            if not success:
                print("处理图片失败，程序退出。")
                exit()
    else:
        print("训练图片目录不存在，程序退出。")
        exit()
    
    # 准备 data.yaml
    data_yaml_path = prepare_data_yaml_for_training(dataset_dir, class_names)
    
    # 开始训练
    model, results = train_yolo_model(data_yaml_path)
    
    print("训练完成！")
    print(f"训练结果保存在: runs/train/full_dataset_training")
    print("模型训练完成，可以使用use.py进行推理测试。")