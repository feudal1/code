from pynput import mouse, keyboard
import os
import uuid
import pyautogui
from datetime import datetime
from PIL import ImageDraw, ImageFont
import threading

# 程序启动时生成一个 UUID4
session_uuid = str(uuid.uuid4())

# 获取当前日期，用于文件夹命名
current_date = datetime.now().strftime("%Y%m%d")

# 设置输出目录：output/recoder_user/{date}/{uuid}/
output_dir = os.path.join("output", "recoder_user", current_date, session_uuid)

# 确保目录存在
os.makedirs(output_dir, exist_ok=True)

# 红圈配置
CIRCLE_RADIUS = 30
CIRCLE_COLOR = "red"
CIRCLE_WIDTH = 3

# 锁防止多线程同时写入
write_lock = threading.Lock()

def save_screenshot(event_type, x=None, y=None, extra_info=""):
    with write_lock:
        current_time = datetime.now().strftime("%H%M%S_%f")[:12]  # 精确到毫秒避免重名
        if x is not None and y is not None:
            filename = f"{event_type}_{current_time}_{x}_{y}.png"
        else:
            filename = f"{event_type}_{current_time}.png"
        path = os.path.join(output_dir, filename)

        screenshot = pyautogui.screenshot()
        draw = ImageDraw.Draw(screenshot)

        if x is not None and y is not None:
            # 鼠标点击/滚轮：画红圈
            draw.ellipse(
                [x - CIRCLE_RADIUS, y - CIRCLE_RADIUS,
                 x + CIRCLE_RADIUS, y + CIRCLE_RADIUS],
                outline=CIRCLE_COLOR,
                width=CIRCLE_WIDTH
            )
        elif extra_info:
            # 键盘事件：在左上角写按键信息
            try:
                # 尝试加载默认字体（部分系统可能无 Arial）
                font = ImageFont.truetype("arial.ttf", 100)
            except:
                font = None
            draw.text((100, 100), f"Key: {extra_info}", fill="red", font=font)

        screenshot.save(path)
        print(f"[{datetime.now().strftime('%H:%M:%S.%f')[:-3]}] {event_type} -> {path}")

def on_click(x, y, button, pressed):
    if pressed:
        save_screenshot("click", x, y, str(button))
        print(f"  [{button}] Click at ({x}, {y})")

def on_scroll(x, y, dx, dy):
    direction = "up" if dy > 0 else "down"
    save_screenshot(f"scroll_{direction}", x, y)
    print(f"  Scroll {direction} at ({x}, {y})")

def on_press(key):
    try:
        key_name = key.char  # 可打印字符
    except AttributeError:
        key_name = str(key).replace('Key.', '')  # 特殊键如 ctrl, enter 等
    save_screenshot("key", extra_info=key_name)
    print(f"  Key pressed: {key_name}")

print(f"Session UUID: {session_uuid}")
print(f"日期：{current_date}")
print(f"截图将保存到：{os.path.abspath(output_dir)}")
print("监听中... 按 Ctrl+C 停止")

# 启动鼠标和键盘监听器（非阻塞方式需用 join）
mouse_listener = mouse.Listener(on_click=on_click, on_scroll=on_scroll)
keyboard_listener = keyboard.Listener(on_press=on_press)

with mouse_listener, keyboard_listener:
    mouse_listener.join()