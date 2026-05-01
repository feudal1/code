# file: e:\cqh\code\my_ai\screen_base_tools\vlm_grounding.py
import tkinter as tk
from tkinter import filedialog
from vlm_utils import create_output_folder, get_bounding_boxes_from_ai, draw_bounding_boxes
import pyautogui

def select_image_file():
    """弹窗选择图像文件"""
    root = tk.Tk()
    root.withdraw()
    image_path = filedialog.askopenfilename(
        title="选择要检测的图像",
        filetypes=[
            ("图像文件", "*.jpg *.jpeg *.png *.bmp *.webp *.tiff *.tif"),
            ("所有文件", "*.*")
        ]
    )
    root.destroy()
    return image_path

def select_main(prompt=None):
    """主函数 - 文件选择版本"""
    print("=== AI 目标检测工具（文件选择版）===")
    
    image_path = select_image_file()
    if not image_path:
        print("未选择图片，程序退出")
        return
    
    print(f"已选择图片：{image_path}")
    
    try:
        create_output_folder()
        bounding_boxes = get_bounding_boxes_from_ai(image_path,
                                                    prompt=prompt)
        
        if not bounding_boxes:
            print("⚠️  AI 未检测到任何物体")
            return
        
        print(f"✓ 检测到 {len(bounding_boxes)} 个物体")
        output_path = draw_bounding_boxes(image_path, bounding_boxes)
        
        print("=== 检测完成 ===")
        print(f"结果图片：{output_path}")
        
    except Exception as e:
        print(f"❌ 错误：{str(e)}")
def capture_screen():
    """捕获当前屏幕截图"""
    print("正在截取屏幕...")
    return pyautogui.screenshot()

# file: e:\cqh\code\my_ai\screen_base_tools\vlm_grounding.py
def capture_main(prompt=None):
    """
    执行屏幕截图并进行 AI 目标检测
    
    Args:
        prompt (str): 发送给 AI 的提示词
        
    Returns:
        list: 边界框列表（第一个检测到的物体）
    """
    print("=== AI 目标检测工具（屏幕截图版）===")
    
    try:
        # 捕获屏幕
        screenshot = capture_screen()
        
        # 创建输出文件夹
        create_output_folder()
        
        # 调用 AI 获取边界框
        bounding_boxes = get_bounding_boxes_from_ai(
            screenshot, 
            prompt=prompt, 
            is_file_path=False
        )
        
        if not bounding_boxes:
            print("⚠️  AI 未检测到任何物体")
            return None
        
        print(f"✓ 检测到 {len(bounding_boxes)} 个物体")
        
        # 绘制边界框并保存
        output_path = draw_bounding_boxes(
            screenshot, 
            bounding_boxes, 
            is_file_path=False
        )
        
        print("=== 检测完成 ===")
        print(f"结果图片：{output_path}")
        
        # 返回第一个检测到的边界框
        return bounding_boxes[0]
        
    except Exception as e:
        print(f"❌ 错误：{str(e)}")
        return None

if __name__ == "__main__":
    custom_prompt = """
    请找出 MJ.30左边板.dwg。
    格式为：[{"object_name": "物体名称", "bbox": [x_min, y_min, x_max, y_max]}]
    
    """
    
    result_file = select_main(custom_prompt)
    if result_file:
        print(f"成功保存结果：{result_file}")