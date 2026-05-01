import os
import cv2
import numpy as np
import tkinter as tk
from tkinter import filedialog, Label, Button, Frame
from ultralytics import YOLO
import yaml
import threading
import time
from PIL import Image, ImageTk

try:
    import pygetwindow as gw
    import pyautogui
    HAS_WINDOW_LIBS = True
except ImportError:
    HAS_WINDOW_LIBS = False

# 导入项目中的截图方法
import win32gui
import win32ui
import win32con
import win32api
from ctypes import windll

class YOLODetector:
    def __init__(self):
        self.model = None
        self.model_path = None
        self.running = False
        self.root = None
        self.image_label = None
        self.status_label = None
        self.capture_thread = None
        
    def select_model_file(self):
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

    def load_model(self, model_path):
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

    def detect_objects(self, model, image, conf_threshold=0.5):
        """
        使用模型检测图像中的对象
        
        Args:
            model: YOLO模型
            image: 图像数组
            conf_threshold: 置信度阈值
            
        Returns:
            list: 检测结果列表，每个元素包含类别、边界框坐标和置信度
        """
        # 进行预测
        results = model.predict(
            source=image,
            conf=conf_threshold,
            save=False,
            
        verbose=False
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
        
        return detections

    def draw_detections(self, image, detections):
        """
        在图像上绘制检测结果
        
        Args:
            image: 原始图像
            detections: 检测结果列表
            
        Returns:
            带有检测框的图像
        """
        display_img = image.copy()
        
        # 设置字体
        font = cv2.FONT_HERSHEY_SIMPLEX
        font_scale = 0.6
        font_thickness = 2
        
        for detection in detections:
            x1, y1, x2, y2 = detection['bbox']
            x1, y1, x2, y2 = int(x1), int(y1), int(x2), int(y2)
            
            conf = detection['confidence']
            class_name = detection['class_name']
            
            # 绘制边界框
            cv2.rectangle(display_img, (x1, y1), (x2, y2), (0, 255, 0), 2)
            
            # 添加标签文本
            label = f"{class_name}: {conf:.2f}"
            label_size = cv2.getTextSize(label, font, font_scale, font_thickness)[0]
            cv2.rectangle(display_img, (x1, y1 - label_size[1] - 10), 
                         (x1 + label_size[0], y1), (0, 255, 0), -1)
            cv2.putText(display_img, label, (x1, y1 - 5), font, font_scale, (0, 0, 0), font_thickness)
        
        return display_img

    def capture_window_by_title(self, window_title):
        """
        根据窗口标题截取指定窗口内容，无需激活窗口

        Args:
            window_title: 窗口标题（部分匹配）

        Returns:
            numpy array: 截图的图像数据，如果找不到窗口则返回None
        """
        # 查找窗口句柄
        def enum_windows_callback(hwnd, windows):
            if win32gui.IsWindowVisible(hwnd):
                window_text = win32gui.GetWindowText(hwnd)
                if window_title.lower() in window_text.lower():
                    windows.append(hwnd)
            return True

        windows = []
        win32gui.EnumWindows(enum_windows_callback, windows)

        if not windows:
            print(f"未找到包含 '{window_title}' 的窗口")
            return None

        # 选择第一个匹配的窗口
        hwnd = windows[0]
        print(f"找到窗口: {win32gui.GetWindowText(hwnd)} at ({win32gui.GetWindowRect(hwnd)})")

        # 获取窗口位置和大小
        left, top, right, bottom = win32gui.GetWindowRect(hwnd)
        width = right - left
        height = bottom - top

        # 获取设备上下文
        hwndDC = win32gui.GetWindowDC(hwnd)
        mfcDC = win32ui.CreateDCFromHandle(hwndDC)
        saveDC = mfcDC.CreateCompatibleDC()

        # 创建位图
        saveBitMap = win32ui.CreateBitmap()
        saveBitMap.CreateCompatibleBitmap(mfcDC, width, height)
        saveDC.SelectObject(saveBitMap)

        # 使用 PrintWindow API 截取窗口
        result = windll.user32.PrintWindow(hwnd, saveDC.GetSafeHdc(), 3)
        
        if result:
            # 转换为PIL图像
            bmpinfo = saveBitMap.GetInfo()
            bmpstr = saveBitMap.GetBitmapBits(True)

            im = Image.frombuffer(
                'RGB',
                (bmpinfo['bmWidth'], bmpinfo['bmHeight']),
                bmpstr, 'raw', 'BGRX', 0, 1)

            # 转换为numpy数组 (PIL -> OpenCV)
            image = np.array(im)
            # RGB to BGR
            image = cv2.cvtColor(image, cv2.COLOR_RGB2BGR)

            # 清理资源
            win32gui.DeleteObject(saveBitMap.GetHandle())
            saveDC.DeleteDC()
            mfcDC.DeleteDC()
            win32gui.ReleaseDC(hwnd, hwndDC)
            
            return image
        else:
            # 清理资源
            win32gui.DeleteObject(saveBitMap.GetHandle())
            saveDC.DeleteDC()
            mfcDC.DeleteDC()
            win32gui.ReleaseDC(hwnd, hwndDC)
            print(f"截图窗口失败: {win32gui.GetWindowText(hwnd)}")
            return None

    def capture_sifu_window(self):
        """
        截图名称包含"sifu"的窗口
        
        Returns:
            numpy array: 截图的图像数据，如果找不到窗口则返回None
        """
        if not HAS_WINDOW_LIBS:
            print("缺少必要的库，请安装: pip install pygetwindow pyautogui")
            return None

        # 使用改进的方法截图窗口，不再依赖pygetwindow的screenshot方法
        image = self.capture_window_by_title('sifu')

        return image

    def capture_and_detect(self):
        """
        每隔5秒自动截图检测
        """
        if not self.model:
            print("模型未加载")
            return
        
        while self.running:
            # 截取名称包含"sifu"的窗口
            image = self.capture_sifu_window()
            
            if image is None:
                print("无法获取sifu窗口截图，等待下次尝试...")
               
                continue

            # 进行检测
            detections = self.detect_objects(self.model, image, 0.2)
            
            # 在图像上绘制检测结果
            annotated_image = self.draw_detections(image, detections)
            
            # 调整图像大小以便显示
            height, width = annotated_image.shape[:2]
            max_height = 600
            max_width = 900
            
            if height > max_height or width > max_width:
                scale = min(max_height/height, max_width/width)
                new_width = int(width * scale)
                new_height = int(height * scale)
                annotated_image = cv2.resize(annotated_image, (new_width, new_height))
            
            # 将图像转换为PIL格式并在GUI中显示
            image_rgb = cv2.cvtColor(annotated_image, cv2.COLOR_BGR2RGB)
            image_pil = Image.fromarray(image_rgb)
            image_tk = ImageTk.PhotoImage(image_pil)
            
            # 更新GUI中的图像
            self.image_label.configure(image=image_tk)
            self.image_label.image = image_tk  # 保持引用防止垃圾回收
            
            # 更新状态
            self.status_label.config(text=f"检测完成，发现 {len(detections)} 个对象 - {time.strftime('%Y-%m-%d %H:%M:%S')}")
            
            # 等待5秒
            time.sleep(0.5)

    def start_detection(self):
        """
        开始自动截图检测
        """
        if not self.model:
            print("请先加载模型")
            return
        
        self.running = True
        self.capture_thread = threading.Thread(target=self.capture_and_detect)
        self.capture_thread.daemon = True
        self.capture_thread.start()
        
        print("开始自动截图检测，每5秒一次...")

    def stop_detection(self):
        """
        停止自动截图检测
        """
        self.running = False
        if self.capture_thread:
            self.capture_thread.join(timeout=1)
        
        print("停止自动截图检测")

    def create_gui(self):
        """
        创建GUI界面 - 精简版，只保留显示功能
        """
        self.root = tk.Tk()
        self.root.title("YOLO SIFU窗口截图检测系统")
        self.root.geometry("1000x700")
        
        # 创建主框架
        main_frame = Frame(self.root)
        main_frame.pack(fill=tk.BOTH, expand=True, padx=10, pady=10)
        
        # 图像显示区域
        self.image_label = Label(main_frame)
        self.image_label.pack(pady=10)
        
        # 初始占位图像
        placeholder_img = Image.new('RGB', (800, 600), color='gray')
        placeholder_photo = ImageTk.PhotoImage(placeholder_img)
        self.image_label.config(image=placeholder_photo)
        self.image_label.image = placeholder_photo
        
        # 状态栏
        self.status_label = Label(main_frame, text="就绪", bd=1, relief=tk.SUNKEN, anchor=tk.W)
        self.status_label.pack(side=tk.BOTTOM, fill=tk.X)
        
        # 加载模型并开始检测
        self.root.after(100, self.load_model_and_start)

    def load_model_and_start(self):
        """
        加载模型并自动开始检测
        """
        model_path = self.select_model_file()
        if model_path:
            self.model_path = model_path
            self.model = self.load_model(model_path)
            if self.model:
                print(f"模型已加载: {model_path}")
                self.start_detection()  # 自动开始检测
            else:
                print("模型加载失败")
        else:
            print("未选择模型文件")
            self.root.destroy()  # 关闭窗口

    def run(self):
        """
        运行GUI应用
        """
        self.create_gui()
        self.root.mainloop()

def main():
    """
    主函数
    """
    print("YOLO SIFU窗口截图检测程序")
    print("注意：需要安装 pygetwindow 和 pyautogui 库: pip install pygetwindow pyautogui")
    
    if not HAS_WINDOW_LIBS:
        print("缺少必要的库，请先安装: pip install pygetwindow pyautogui")
        return
    
    detector = YOLODetector()
    detector.run()

if __name__ == "__main__":
    main()