# file: e:\cqh\code\my_ai\screen_base_tools\insertdwg.py
import pyautogui
import time
import sys
import os
sys.path.append(os.getcwd())
from screen_base_tools.vlm_grounding import capture_main

def click_bbox_center(bbox):
    """点击边界框的中点"""
    if bbox is None:
        print("⚠️  边界框为空，无法点击")
        return None
    
    x_min, y_min, x_max, y_max = bbox[:4]
    center_x = (x_min + x_max) // 2
    center_y = (y_min + y_max) // 2
    pyautogui.click(center_x, center_y)
    print(f"已点击中点：({center_x}, {center_y})")
    return (center_x, center_y)


def main():
    pyautogui.click(209, 1053)

    pyautogui.click(102, 82)
    pyautogui.click(102, 82)
    time.sleep(2)
    
    custom_prompt = """
    请找出 M25 触屏控制盒盖板.dwg。
    格式为：[{"object_name": "物体名称", "bbox": [x_min, y_min, x_max, y_max]}]
    """
    
    # 获取边界框（不是文件路径）
    bbox = capture_main(custom_prompt)
    
    # 点击边界框中点
    if bbox:
        click_bbox_center(bbox)
    else:
        print("⚠️  未检测到目标，跳过点击")


if __name__ == "__main__":
    main()