"""
Automatic Segmentation using Segment Anything Model (SAM)
这个脚本实现自动图像分割，不需要text prompt，仅使用SAM模型
"""
import tkinter as tk
from tkinter import filedialog
import torch
from PIL import Image
import numpy as np
import os
import cv2
from segment_anything import sam_model_registry, SamPredictor, SamAutomaticMaskGenerator
import matplotlib.pyplot as plt
import matplotlib.patches as patches
import random
import base64
from io import BytesIO

# 全局变量用于缓存模型
_cached_sam_model = None
_sam_model_type = "vit_h"  # 可以是 "vit_h", "vit_l", "vit_b"
_sam_checkpoint_url = "https://dl.fbaipublicfiles.com/segment_anything/sam_vit_h_4b8939.pth"  # 根据模型类型变化

def download_sam_model(model_type="vit_h"):
    """
    下载SAM模型权重
    """
    import urllib.request
    
    # 定义不同模型类型的下载链接
    model_urls = {
        "vit_h": "https://dl.fbaipublicfiles.com/segment_anything/sam_vit_h_4b8939.pth",
        "vit_l": "https://dl.fbaipublicfiles.com/segment_anything/sam_vit_l_0b3195.pth",
        "vit_b": "https://dl.fbaipublicfiles.com/segment_anything/sam_vit_b_01ec64.pth"
    }
    
    checkpoint_url = model_urls.get(model_type, model_urls["vit_h"])
    
    # 创建模型目录
    model_dir = os.path.expanduser("~/.cache/segment_anything")
    os.makedirs(model_dir, exist_ok=True)
    
    checkpoint_path = os.path.join(model_dir, os.path.basename(checkpoint_url))
    
    if not os.path.exists(checkpoint_path):
        print(f"正在下载SAM模型: {checkpoint_url}")
        urllib.request.urlretrieve(checkpoint_url, checkpoint_path)
        print(f"SAM模型已保存到: {checkpoint_path}")
    
    return checkpoint_path

def get_sam_model(model_type="vit_h"):
    """
    获取SAM模型，如果已缓存则直接返回，否则加载并缓存
    """
    global _cached_sam_model
    
    if _cached_sam_model is None:
        # 下载或获取本地模型路径
        checkpoint_path = download_sam_model(model_type)
        
        print(f"正在加载SAM模型: {model_type}")
        device = "cuda" if torch.cuda.is_available() else "cpu"
        
        # 加载SAM模型
        sam = sam_model_registry[model_type](checkpoint=checkpoint_path)
        sam.to(device=device)
        
        _cached_sam_model = sam
        print("SAM模型加载完成！")
    else:
        print("使用已缓存的SAM模型")
    
    return _cached_sam_model

def automatic_segmentation(image):
    """
    使用SAM的自动掩码生成器对图像进行自动分割
    这个方法不需要任何text prompt或点/框提示
    """
    sam_model = get_sam_model()
    
    # 将PIL图像转换为numpy数组
    image_np = np.array(image)
    
    # 创建自动掩码生成器
    mask_generator = SamAutomaticMaskGenerator(
        model=sam_model,
        points_per_side=32,  # 控制采样密度
        points_per_batch=64,  # 批处理大小
        pred_iou_thresh=0.88,  # 预测IoU阈值
        stability_score_thresh=0.95,  # 稳定性分数阈值
        stability_score_offset=1.0,  # 稳定性分数偏移
        box_nms_thresh=0.7,  # NMS阈值
        crop_n_layers=0,  # 裁剪层数
        crop_n_points_downscale_factor=1,  # 裁剪点缩小因子
        min_mask_region_area=100,  # 最小掩码区域面积
    )
    
    # 生成掩码
    masks = mask_generator.generate(image_np)
    
    return masks

def visualize_automatic_segmentation_results(image, masks):
    """
    可视化自动分割结果
    """
    image_np = np.array(image)
    fig, ax = plt.subplots(1, figsize=(15, 15))
    ax.imshow(image_np)
    
    # 绘制所有检测到的掩码
    sorted_masks = sorted(masks, key=lambda x: x['area'], reverse=True)
    
    for mask_data in sorted_masks:
        mask = mask_data['segmentation']
        color = np.random.rand(3)
        
        # 创建彩色掩码
        colored_mask = np.zeros((mask.shape[0], mask.shape[1], 4))  # RGBA
        colored_mask[:, :, :3] = color.reshape(1, 1, 3)
        colored_mask[:, :, 3] = mask * 0.3  # 设置透明度
        
        ax.imshow(colored_mask)
        
        # 绘制边界框
        x, y, w, h = mask_data['bbox']
        rect = patches.Rectangle((x, y), w, h, 
                                 linewidth=2, edgecolor='r', facecolor='none')
        ax.add_patch(rect)
    
    ax.axis('off')
    
    # 将matplotlib图形转换为PIL图像
    import io
    buf = io.BytesIO()
    fig.savefig(buf, format='png', bbox_inches='tight', dpi=100, pad_inches=0)
    buf.seek(0)
    pil_image = Image.open(buf)
    
    # 转换RGBA模式为RGB，因为JPEG不支持RGBA
    if pil_image.mode == 'RGBA':
        pil_image = pil_image.convert('RGB')
    
    plt.close(fig)
    return pil_image

def select_image():
    """
    弹窗选择图片
    """
    # 创建根窗口并隐藏它
    root = tk.Tk()
    root.withdraw()
    
    # 弹窗选择图片文件
    image_path = filedialog.askopenfilename(
        title="选择要分割的图片",
        filetypes=[
            ("Image files", "*.jpg *.jpeg *.png *.bmp *.tiff *.gif"),
            ("All files", "*.*")
        ]
    )
    
    if not image_path:
        print("未选择图片，程序退出")
        return None
    
    return image_path

def main():
    """
    主函数，执行自动分割
    """
    # 选择图片
    image_path = select_image()
    
    if image_path is None:
        return

    # 加载图片并转换为RGB格式
    image = Image.open(image_path)
    if image.mode != 'RGB':
        image = image.convert('RGB')

    print(f"选择的图片: {image_path}")

    # 使用SAM进行自动分割
    print("正在使用SAM进行自动分割...")
    masks = automatic_segmentation(image)
    
    print(f"检测到 {len(masks)} 个对象")

    if len(masks) == 0:
        print("没有检测到任何对象")
        return

    # 可视化结果
    print("正在生成可视化结果...")
    result_image = visualize_automatic_segmentation_results(image, masks)

    # 由于结果图像现在已经是RGB模式，可以安全地保存为JPEG
    output_path = "automatic_seg_result.jpg"
    result_image.save(output_path)
    print(f"\n结果已保存到 {output_path}")

    # 显示图片（可选）
    result_image.show()

def detect_objects_with_automatic_seg(base64_image):
    """
    使用自动分割检测并分割图像中的目标对象
    :param base64_image: base64编码的图像字符串
    :return: 分割结果列表，包含边界框、面积和掩码
    """
    import base64
    from io import BytesIO
    
    # 将base64图像转换为PIL图像
    image_bytes = base64.b64decode(base64_image)
    image_buffer = BytesIO(image_bytes)
    image = Image.open(image_buffer)
    
    # 确保图像是RGB格式
    if image.mode != 'RGB':
        image = image.convert('RGB')
    
    # 使用SAM进行自动分割
    masks = automatic_segmentation(image)
    
    if len(masks) == 0:
        return []
    
    # 格式化结果
    segmentation_results = []
    
    for mask_data in masks:
        bbox = mask_data['bbox']
        area = mask_data['area']
        mask = mask_data['segmentation']
        
        segmentation_results.append({
            'bbox': list(bbox),  # 边界框 [x, y, width, height]
            'area': area,        # 掩码面积
            'mask': mask,        # 掩码数组
            'predicted_iou': mask_data.get('predicted_iou', 0)  # 预测IoU
        })
    
    return segmentation_results

if __name__ == "__main__":
    main()