# file: e:\cqh\code\my_ai\screen_base_tools\vlm_utils.py
import base64
import json
import os
import re
import io
from PIL import Image, ImageDraw, ImageFont
from datetime import datetime
from ollama import chat

# ==================== 配置区域 ====================
VL_MODEL = "qwen3-vl:235b-instruct-cloud"
OUTPUT_FOLDER = "output/grounding"
ENABLE_COORD_NORMALIZATION = True

# ==================== 工具函数 ====================

def create_output_folder(folder_path=None):
    """创建输出文件夹，如果不存在的话"""
    path = folder_path or OUTPUT_FOLDER
    if not os.path.exists(path):
        os.makedirs(path)
        print(f"已创建输出文件夹：{path}")
    return path

def image_to_base64(image_path):
    """
    将图片文件转换为 base64 字符串
    
    Args:
        image_path: 图片文件路径
        
    Returns:
        str: base64 编码的图像字符串
    """
    try:
        with open(image_path, "rb") as image_file:
            return base64.b64encode(image_file.read()).decode('utf-8')
    except Exception as e:
        print(f"[错误] 图像转换失败：{e}")
        raise e

def image_obj_to_base64(image_obj):
    """
    将 PIL Image 对象转换为 base64 字符串
    
    Args:
        image_obj: PIL.Image 对象
        
    Returns:
        str: base64 编码的图像字符串
    """
    try:
        buffered = io.BytesIO()
        image_obj.save(buffered, format="PNG")
        return base64.b64encode(buffered.getvalue()).decode('utf-8')
    except Exception as e:
        print(f"[错误] 图像对象转换失败：{e}")
        raise e

def extract_json_from_response(text):
    """
    从 AI 返回的文本中提取 JSON 数据
    
    Args:
        text: AI 返回的文本
    
    Returns:
        list: 解析后的边界框列表
    """
    # 移除 Markdown 代码块标记
    text = re.sub(r'```(?:json)?\s*', '', text)
    text = re.sub(r'```\s*', '', text)
    
    # 尝试查找 JSON 数组模式
    json_pattern = r'\[\s*\{.*?\}\s*\]'
    matches = re.findall(json_pattern, text, re.DOTALL)
    
    if matches:
        try:
            data = json.loads(matches[0])
            return data
        except json.JSONDecodeError as e:
            print(f"JSON 解析错误：{e}")
            pass
    
    # 尝试直接解析整个文本
    try:
        start_idx = text.find('[')
        end_idx = text.rfind(']') + 1
        if start_idx != -1 and end_idx > start_idx:
            json_str = text[start_idx:end_idx]
            data = json.loads(json_str)
            return data
    except json.JSONDecodeError as e:
        print(f"JSON 解析错误：{e}")
        pass
    except Exception as e:
        print(f"其他错误：{e}")
        pass
    
    return []

def get_bounding_boxes_from_ai(image_source, prompt=None, is_file_path=True):
    """
    使用 Ollama VLM 进行目标检测
    
    Args:
        image_source: 图片文件路径 或 PIL.Image 对象
        prompt: 自定义提示词
        is_file_path: True=文件路径，False=PIL 对象
        
    Returns:
        list: 检测到的边界框列表
    """
    if prompt is None:
        prompt = """
        请找出图片中的目标物体。
        格式为：[{"object_name": "物体名", "bbox": [x_min, y_min, x_max, y_max]}]
        坐标请返回 0-1000 的归一化坐标。
        """
    
    # 转换图像为 base64
    try:
        if is_file_path:
            image_base64 = image_to_base64(image_source)
        else:
            image_base64 = image_obj_to_base64(image_source)
    except Exception as e:
        raise Exception(f"图像转换失败：{e}")

    # 构建消息内容
    messages = [
        {
            'role': 'user',
            'content': prompt,
            'images': [image_base64]
        }
    ]
    
    try:
        print(f"[{datetime.now().strftime('%H:%M:%S')}] 正在调用 Ollama 模型 '{VL_MODEL}'...")
        
        response = chat(
            model=VL_MODEL,
            messages=messages,
        )
        
        result_text = response.message.content
        print(f"[{datetime.now().strftime('%H:%M:%S')}] AI 分析完成")
        print(f"AI 返回结果:\n{result_text}")
        
        # 解析返回结果
        bounding_boxes = extract_json_from_response(result_text)
        return bounding_boxes
        
    except Exception as e:
        print(f"[错误] Ollama 调用失败：{e}")
        raise Exception(f"Ollama 调用失败：{str(e)}")

def draw_bounding_boxes(image_source, bounding_boxes, output_path=None, is_file_path=True):
    """
    在图片上绘制边界框
    
    Args:
        image_source: 图片文件路径 或 PIL.Image 对象
        bounding_boxes: 边界框列表
        output_path: 输出路径
        is_file_path: True=文件路径，False=PIL 对象
        
    Returns:
        str: 保存后的图片路径
    """
    # 打开图片
    if is_file_path:
        img = Image.open(image_source)
    else:
        img = image_source
    
    draw = ImageDraw.Draw(img)
    img_width, img_height = img.size
    print(f"图片尺寸：{img_width}x{img_height}")
    
    # 颜色列表
    colors = [
        (255, 0, 0), (0, 255, 0), (0, 0, 255),
        (255, 255, 0), (255, 0, 255), (0, 255, 255),
        (255, 128, 0), (128, 0, 255),
    ]
    
    # 加载字体
    try:
        font = ImageFont.truetype("simhei.ttf", 20)
    except:
        try:
            font = ImageFont.truetype("arial.ttf", 20)
        except:
            font = ImageFont.load_default()
    
    # 绘制每个边界框
    for i, box in enumerate(bounding_boxes):
        try:
            # 获取边界框坐标
            if isinstance(box, dict):
                bbox = box.get('bbox', box.get('bbox_2d', box.get('bounding_box', [])))
                obj_name = box.get('object_name', box.get('name', box.get('label', f'object_{i}')))
            elif isinstance(box, list):
                bbox = box[:4] if len(box) >= 4 else box
                obj_name = box[4] if len(box) > 4 else f'object_{i}'
            else:
                continue
            
            if len(bbox) < 4:
                print(f"跳过无效的 bbox: {bbox}")
                continue
            
            x_min, y_min, x_max, y_max = bbox[:4]
            
            # 坐标归一化处理
            if ENABLE_COORD_NORMALIZATION:
                x_min = int((x_min / 1000) * img_width)
                y_min = int((y_min / 1000) * img_height)
                x_max = int((x_max / 1000) * img_width)
                y_max = int((y_max / 1000) * img_height)
                
                # 确保坐标在图像边界内
                x_min = max(0, min(img_width - 1, x_min))
                y_min = max(0, min(img_height - 1, y_min))
                x_max = max(0, min(img_width - 1, x_max))
                y_max = max(0, min(img_height - 1, y_max))
            else:
                x_min, y_min, x_max, y_max = int(x_min), int(y_min), int(x_max), int(y_max)
            
            # 选择颜色
            color = colors[i % len(colors)]
            
            # 绘制矩形框
            draw.rectangle([x_min, y_min, x_max, y_max], outline=color, width=3)
            
            # 绘制标签
            label = f"{obj_name}"
            try:
                text_bbox = draw.textbbox((x_min, y_min - 25), label, font=font)
                draw.rectangle([x_min, y_min - 25, text_bbox[2], y_min], fill=color)
                draw.text((x_min, y_min - 25), label, fill=(255, 255, 255), font=font)
            except:
                pass
            
            print(f"绘制物体 {i+1}: {obj_name} - [{x_min}, {y_min}, {x_max}, {y_max}]")
            
        except Exception as e:
            print(f"绘制第 {i+1} 个框失败：{e}")
            continue
    
    # 生成输出文件名
    if output_path is None:
        timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
        if is_file_path:
            original_name = os.path.basename(image_source)
            name, ext = os.path.splitext(original_name)
            output_path = os.path.join(OUTPUT_FOLDER, f"{name}_boxed_{timestamp}{ext}")
        else:
            output_path = os.path.join(OUTPUT_FOLDER, f"screen_result_{timestamp}.png")
    
    # 保存图片
    img.save(output_path)
    print(f"图片已保存到：{output_path}")
    
    return output_path