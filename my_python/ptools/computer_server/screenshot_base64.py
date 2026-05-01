import pyautogui

import os
import base64
from datetime import datetime
import shutil


def clear_directory(directory_path: str) -> bool:
    """清空指定目录中的所有文件和子目录"""
    try:
        if os.path.exists(directory_path):
            # 删除目录中的所有内容
            for filename in os.listdir(directory_path):
                file_path = os.path.join(directory_path, filename)
                try:
                    if os.path.isfile(file_path) or os.path.islink(file_path):
                        os.unlink(file_path)
                    elif os.path.isdir(file_path):
                        shutil.rmtree(file_path)
                except Exception as e:
                    print(f"⚠️ 删除文件 {file_path} 失败: {e}")
                    return False
            print(f"🗑️ 已清空目录: {directory_path}")
        return True
    except Exception as e:
        print(f"❌ 清空目录失败: {e}")
        return False

def take_screenshot() -> str:
    """截取当前屏幕并保存为临时文件"""
    try:
        # 创建临时截图目录
        temp_dir = "output/temp_screenshots"
        
        
        # 生成带时间戳的文件名
        timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
        screenshot_path = os.path.join(temp_dir, f"screenshot_{timestamp}.png")
        
        # 截图并保存
        screenshot = pyautogui.screenshot()
        screenshot.save(screenshot_path)
        
        print(f"✅ 截图已保存: {screenshot_path}")
        return screenshot_path
    except Exception as e:
        print(f"❌ 截图失败: {e}")
        return None

def encode_image(image_path: str) -> str:
    """将图片文件编码为base64字符串"""
    with open(image_path, "rb") as image_file:
        return base64.b64encode(image_file.read()).decode('utf-8')

def screenshot_to_base64() -> str:
    """截取屏幕并返回base64编码的图片字符串（默认清空文件夹）"""
    temp_dir = "output/temp_screenshots"
    os.makedirs(temp_dir, exist_ok=True)
    # 默认清空目录
    clear_directory(temp_dir)
    
    screenshot_path = take_screenshot()
    if screenshot_path:
        return encode_image(screenshot_path)
    return None

# 添加主函数，便于直接运行测试
if __name__ == "__main__":
    print("📸 开始截图...")
    result = screenshot_to_base64()
    if result:
        print(f"✅ 截图base64编码完成，长度: {len(result)} 字符")
    else:
        print("❌ 截图失败")