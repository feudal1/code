# -*- coding: utf-8 -*-
import pandas as pd
import ezdxf
from ezdxf.enums import TextEntityAlignment
import os
import re
from tkinter import Tk, simpledialog, messagebox, Canvas, Label, Frame, Button, Scrollbar, HORIZONTAL, VERTICAL
from tkinter.filedialog import askopenfilename
import threading
import time
from collections import defaultdict
import rectpack
from rectpack import newPacker, PackingMode, PackingBin, SORT_AREA, GuillotineBssfSas, MaxRectsBl
# ==============================
# 弹窗输入板材尺寸
# ==============================
def input_sheet_size():
    root = Tk()
    root.withdraw()
    width = simpledialog.askinteger("板材尺寸设置", "请输入板材宽度 (mm):", initialvalue=1490, minvalue=100, maxvalue=50000)
    if width is None:
        messagebox.showinfo("提示", "未输入板材宽度,程序将使用默认值 1200mm")
        width = 1200
    height = simpledialog.askinteger("板材尺寸设置", "请输入板材高度 (mm):", initialvalue=12000, minvalue=100, maxvalue=50000)
    if height is None:
        messagebox.showinfo("提示", "未输入板材高度,程序将使用默认值 2400mm")
        height = 2400
    return width, height

SHEET_WIDTH, SHEET_HEIGHT = input_sheet_size()
MARGIN = 7
PART_CLEARANCE = 0.1

def select_input_file():
    root = Tk()
    root.withdraw()
    file_path = askopenfilename(title="选择零件数据文件", filetypes=[("Data files", "*.xlsx *.csv"), ("All files", "*.*")])
    return file_path

def read_data(file_path):
    _, ext = os.path.splitext(file_path)
    if ext.lower() == '.xlsx':
        df = pd.read_excel(file_path, usecols='A:D', header=0)
        df.columns = ['图号', 'OBB长度', 'OBB宽度', '数量']
    elif ext.lower() == '.csv':
        df = pd.read_csv(file_path)
    else:
        raise ValueError("不支持的文件格式")

    required_columns = ['图号', 'OBB长度', 'OBB宽度', '数量']
    missing = [c for c in required_columns if c not in df.columns]
    if missing:
        if ext.lower() == '.csv' and len(df.columns) >= 4:
            df.columns = ['图号', 'OBB长度', 'OBB宽度', '数量'] + list(df.columns[4:])
        else:
            raise ValueError(f"缺少列: {missing}")
    df = df[required_columns].dropna()
    df['OBB长度'] = pd.to_numeric(df['OBB长度'], errors='coerce')
    df['OBB宽度'] = pd.to_numeric(df['OBB宽度'], errors='coerce')
    df['数量'] = pd.to_numeric(df['数量'], errors='coerce')
    df = df.dropna().astype({'OBB长度': int, 'OBB宽度': int, '数量': int})
    return df

file_path = select_input_file()
if not file_path:
    print("未选择文件,程序退出。")
    exit()

try:
    df = read_data(file_path)
    print(f"成功读取数据,共 {len(df)} 行")
except Exception as e:
    print(f"读取文件时出错: {e}")
    exit()

output_dir = os.path.dirname(file_path)

# ==============================
# 可视化类
# ==============================
class PackingVisualizer:
    def __init__(self, sheet_width, sheet_height):
        self.sheet_width = sheet_width
        self.sheet_height = sheet_height
        self.scale = min(0.3, 400 / max(sheet_width, sheet_height))  # 自动缩放比例
        
        # 创建主窗口
        self.root = Tk()
        self.root.title("零件排版实时可视化")
        self.root.geometry("1200x700")
        
        # 创建控制面板
        control_frame = Frame(self.root)
        control_frame.pack(pady=5)
        
        self.start_button = Button(control_frame, text="开始排版", command=self.start_packing)
        self.start_button.pack(side="left", padx=5)
        
        self.pause_button = Button(control_frame, text="暂停", command=self.toggle_pause, state="disabled")
        self.pause_button.pack(side="left", padx=5)
        
        self.resume_button = Button(control_frame, text="继续", command=self.toggle_pause, state="disabled")
        self.resume_button.pack(side="left", padx=5)
        
        # 添加导出DXF按钮
        self.export_dxf_button = Button(control_frame, text="导出DXF", command=self.export_dxf, state="disabled")
        self.export_dxf_button.pack(side="left", padx=5)
        
        # 创建信息标签
        self.info_label = Label(self.root, text="准备开始排版...", font=("Arial", 12))
        self.info_label.pack(pady=5)
        
        # 创建画布和滚动条
        self.canvas_frame = Frame(self.root)
        self.canvas_frame.pack(fill="both", expand=True, padx=10, pady=10)
        
        # 创建水平和垂直滚动条
        self.h_scrollbar = Scrollbar(self.canvas_frame, orient=HORIZONTAL)
        self.h_scrollbar.pack(side="bottom", fill="x")
        
        self.v_scrollbar = Scrollbar(self.canvas_frame, orient=VERTICAL)
        self.v_scrollbar.pack(side="right", fill="y")
        
        # 创建画布
        self.canvas = Canvas(
            self.canvas_frame, 
            bg="white",
            xscrollcommand=self.h_scrollbar.set,
            yscrollcommand=self.v_scrollbar.set
        )
        self.canvas.pack(side="left", fill="both", expand=True)
        
        # 配置滚动条
        self.h_scrollbar.config(command=self.canvas.xview)
        self.v_scrollbar.config(command=self.canvas.yview)
        
        # 初始化画布参数
        self.canvas_items = []  # 存储所有画布项目
        self.bin_canvases = []  # 存储每个板材的画布坐标
        
        # 绘制板材区域
        self.draw_initial_layout()
        
        # 状态变量
        self.paused = False
        self.packing_started = False
        self.bins_data = []  # 存储所有板材数据
        self.packing_completed = False  # 排版是否完成
        self.packer = None  # 用于存储packer对象引用
        
    def draw_initial_layout(self):
        """绘制初始布局"""
        # 设置滚动区域
        self.canvas.config(scrollregion=(0, 0, 2000, 800))
        
    def draw_sheet(self, bin_index):
        """为指定板材绘制边界"""
        # 计算板材位置
        x_offset = bin_index * (self.sheet_width * self.scale + 50)  # 50为板材间距
        y_offset = 50
        
        # 如果该板材索引已经存在，检查是否需要重新创建框架
        if bin_index < len(self.bin_canvases) and self.bin_canvases[bin_index] is not None:
            # 检查框架元素是否还存在
            canvas_info = self.bin_canvases[bin_index]
            framework_exists = True
            for element in canvas_info['framework_elements']:
                if element not in self.canvas_items:
                    framework_exists = False
                    break
            
            # 如果框架元素不存在，重新创建
            if not framework_exists:
                # 删除所有旧元素
                if 'elements' in canvas_info:
                    for element in canvas_info['elements']:
                        if element in self.canvas_items:
                            self.canvas.delete(element)
                            self.canvas_items.remove(element)
                
                # 重新创建框架
                self._create_sheet_framework(bin_index, x_offset, y_offset)
        else:
            # 创建新的板材框架
            self._create_sheet_framework(bin_index, x_offset, y_offset)
        
        # 确保bin_canvases数组足够大
        while len(self.bin_canvases) <= bin_index:
            self.bin_canvases.append(None)
            
        # 更新板材坐标信息（如果还没有初始化）
        if self.bin_canvases[bin_index] is None:
            self.bin_canvases[bin_index] = {
                'x_offset': x_offset,
                'y_offset': y_offset,
                'width': self.sheet_width * self.scale,
                'height': self.sheet_height * self.scale,
                'utilization_text': None,
                'framework_elements': [],  # 专门存储框架元素
                'elements': []  # 跟踪该板材的所有元素
            }
        
        # 更新滚动区域
        total_width = (bin_index + 1) * (self.sheet_width * self.scale + 50) + 100
        self.canvas.config(scrollregion=(0, 0, total_width, 800))
        
        return x_offset, y_offset
    
    def _create_sheet_framework(self, bin_index, x_offset, y_offset):
        """创建板材框架元素"""
        # 绘制板材边界
        rect = self.canvas.create_rectangle(
            x_offset, y_offset,
            x_offset + self.sheet_width * self.scale,
            y_offset + self.sheet_height * self.scale,
            outline="black", width=2
        )
        
        # 添加板材标签
        label = self.canvas.create_text(
            x_offset + (self.sheet_width * self.scale) / 2,
            y_offset - 20,
            text=f"板材 {bin_index + 1}",
            font=("Arial", 12, "bold")
        )
        
        # 添加利用率显示文本（初始为空）
        utilization_text = self.canvas.create_text(
            x_offset + (self.sheet_width * self.scale) / 2,
            y_offset + self.sheet_height * self.scale + 20,
            text="利用率: 0.00%",
            font=("Arial", 10)
        )
        
        # 保存框架元素
        framework_elements = [rect, label, utilization_text]
        
        # 更新板材画布信息
        if bin_index < len(self.bin_canvases) and self.bin_canvases[bin_index] is not None:
            self.bin_canvases[bin_index]['framework_elements'] = framework_elements
            self.bin_canvases[bin_index]['elements'] = framework_elements[:]
            self.bin_canvases[bin_index]['utilization_text'] = utilization_text
        else:
            # 确保数组足够大
            while len(self.bin_canvases) <= bin_index:
                self.bin_canvases.append(None)
                
            self.bin_canvases[bin_index] = {
                'x_offset': x_offset,
                'y_offset': y_offset,
                'width': self.sheet_width * self.scale,
                'height': self.sheet_height * self.scale,
                'utilization_text': utilization_text,
                'framework_elements': framework_elements,
                'elements': framework_elements[:]
            }
        
        self.canvas_items.extend(framework_elements)
        
    def draw_rectangle(self, bin_index, x, y, w, h, drawing_number, color="lightblue"):
        """在指定板材上绘制单个零件"""
        # 确保板材画布存在
        while len(self.bin_canvases) <= bin_index:
            self.draw_sheet(len(self.bin_canvases))
            
        # 获取板材偏移量
        bin_canvas = self.bin_canvases[bin_index]
        x_offset = bin_canvas['x_offset']
        y_offset = bin_canvas['y_offset']
        
        # 绘制矩形
        rect = self.canvas.create_rectangle(
            x_offset + x * self.scale, 
            y_offset + y * self.scale,
            x_offset + (x + w) * self.scale, 
            y_offset + (y + h) * self.scale,
            fill=color, outline="blue", width=1
        )
        
        # 添加零件编号文本
        text = self.canvas.create_text(
            x_offset + (x + w/2) * self.scale,
            y_offset + (y + h/2) * self.scale,
            text=drawing_number,
            font=("Arial", max(6, int(min(w, h) * self.scale / 6)))
        )
        
        # 将新元素添加到板材元素列表中（但不添加到框架元素中）
        self.bin_canvases[bin_index]['elements'].extend([rect, text])
        self.canvas_items.extend([rect, text])
        self.root.update()
        return rect
        
    def update_info(self, info_text):
        """更新信息标签"""
        self.info_label.config(text=info_text)
        self.root.update()
        
    def add_new_bin(self):
        """添加新板材"""
        bin_index = len(self.bins_data)
        self.bins_data.append({
            'rects': [],
            'used_area': 0
        })
        self.draw_sheet(bin_index)
        return bin_index
        
    def toggle_pause(self):
        """切换暂停状态"""
        self.paused = not self.paused
        if self.paused:
            self.pause_button.config(state="disabled")
            self.resume_button.config(state="normal")
            self.update_info("排版已暂停")
        else:
            self.pause_button.config(state="normal")
            self.resume_button.config(state="disabled")
            self.update_info("继续排版...")
            
    def start_packing(self):
        """开始排版"""
        self.start_button.config(state="disabled")
        self.pause_button.config(state="normal")
        self.packing_started = True
        self.update_info("开始排版...")
        
    def wait_for_start(self):
        """等待用户点击开始按钮"""
        while not self.packing_started:
            self.root.update()
            time.sleep(0.1)
            
    def check_pause(self):
        """检查是否需要暂停"""
        while self.paused:
            self.root.update()
            time.sleep(0.1)
            
    def show_completion(self):
        """显示完成信息"""
        self.update_info("排版完成!")
        self.pause_button.config(state="disabled")
        self.resume_button.config(state="disabled")
        self.export_dxf_button.config(state="normal")
        self.packing_completed = True

    def export_dxf(self):
        """导出DXF文件"""
        # 在新线程中执行导出操作，避免阻塞GUI
        self.export_dxf_button.config(state="disabled")
        self.update_info("正在导出DXF文件...")
        
        export_thread = threading.Thread(target=self._export_dxf_thread)
        export_thread.daemon = True
        export_thread.start()

    def _export_dxf_thread(self):
        """在单独线程中执行DXF导出"""
        try:
            if self.packer is None:
                self.root.after(0, lambda: self.update_info("导出失败: 未找到排版数据"))
                self.root.after(0, lambda: self.export_dxf_button.config(state="normal"))
                return
                
            # 获取排版数据
            bins_data = self.packer.bins
            
            # 执行导出操作
            saved_files = create_individual_dxf(bins_data, output_dir, self.sheet_width, self.sheet_height, MARGIN)
            
            # 统计信息
            total_parts = sum(len(b['rects']) for b in bins_data)
            total_bins = len(bins_data)
            total_used_area = sum(b['used_area'] for b in bins_data)
            effective_sheet_area = (self.sheet_width - 2*MARGIN) * (self.sheet_height - 2*MARGIN)
            overall_utilization = total_used_area / (total_bins * effective_sheet_area) * 100 if total_bins > 0 and effective_sheet_area > 0 else 0

            completion_info = (
                f"✅ DXF导出完成！\n"
                f"总共导出板材: {total_bins} 张\n"
                f"总共零件数: {total_parts} 个\n"
                f"整体材料利用率: {overall_utilization:.2f}%\n"
            )
            
            # 在主线程中更新GUI
            self.root.after(0, lambda: self.update_info(completion_info))
            self.root.after(0, lambda: self.export_dxf_button.config(state="normal"))
            
            print(f"DXF文件已保存到: {os.path.dirname(saved_files[0]['file_path']) if saved_files else '未知位置'}")
        except Exception as e:
            error_msg = f"导出失败: {str(e)}"
            print(error_msg)
            self.root.after(0, lambda: self.update_info(error_msg))
            self.root.after(0, lambda: self.export_dxf_button.config(state="normal"))

    def clear_visualization(self, bin_indices=None):
        """清除指定板材的可视化内容，如果不指定则清除所有"""
        if bin_indices is None:
            # 只清除零件，保留板材框架
            for bin_index in range(len(self.bin_canvases)):
                if self.bin_canvases[bin_index] is not None:
                    # 删除除了框架元素之外的所有元素
                    canvas_info = self.bin_canvases[bin_index]
                    if 'elements' in canvas_info and 'framework_elements' in canvas_info:
                        # 找出需要删除的元素（非框架元素）
                        remove_elements = []
                        for element in canvas_info['elements']:
                            if element not in canvas_info['framework_elements']:
                                remove_elements.append(element)
                        
                        # 删除需要移除的元素
                        for element in remove_elements:
                            if element in self.canvas_items:
                                self.canvas.delete(element)
                                self.canvas_items.remove(element)
                        
                        # 更新元素列表，只保留框架元素
                        canvas_info['elements'] = canvas_info['framework_elements'][:]
                        
                    # 重置板材数据
                    if bin_index < len(self.bins_data):
                        self.bins_data[bin_index] = {'rects': [], 'used_area': 0}
                    
                    # 更新利用率显示
                    self.update_bin_utilization(bin_index, 0.0)
        else:
            # 只清除指定板材的可视化内容
            for bin_index in sorted(bin_indices, reverse=True):  # 反向排序避免索引问题
                if bin_index < len(self.bin_canvases) and self.bin_canvases[bin_index] is not None:
                    # 删除除了框架元素之外的所有元素
                    canvas_info = self.bin_canvases[bin_index]
                    if 'elements' in canvas_info and 'framework_elements' in canvas_info:
                        # 找出需要删除的元素（非框架元素）
                        remove_elements = []
                        for element in canvas_info['elements']:
                            if element not in canvas_info['framework_elements']:
                                remove_elements.append(element)
                        
                        # 删除需要移除的元素
                        for element in remove_elements:
                            if element in self.canvas_items:
                                self.canvas.delete(element)
                                self.canvas_items.remove(element)
                        
                        # 更新元素列表，只保留框架元素
                        canvas_info['elements'] = canvas_info['framework_elements'][:]
                        
                    # 重置板材数据
                    if bin_index < len(self.bins_data):
                        self.bins_data[bin_index] = {'rects': [], 'used_area': 0}
                    
                    # 更新利用率显示
                    self.update_bin_utilization(bin_index, 0.0)
            
    def update_bin_utilization(self, bin_index, utilization):
        """更新指定板材的利用率显示"""
        if bin_index < len(self.bin_canvases) and self.bin_canvases[bin_index] is not None:
            bin_canvas = self.bin_canvases[bin_index]
            if bin_canvas['utilization_text']:
                utilization_text = f"利用率: {utilization:.2f}%"
                self.canvas.itemconfig(bin_canvas['utilization_text'], text=utilization_text)
                self.root.update()

# ==============================
# RectPack优化排版算法
# ==============================
class RectPackPacking:
    def __init__(self, width, height, visualizer=None):
        self.width = width
        self.height = height
        self.effective_width = width - 2 * MARGIN
        self.effective_height = height - 2 * MARGIN
        self.bins = []
        self.visualizer = visualizer
        # 使用rectpack创建打包器
        self.rp_packer = newPacker(  
            mode=PackingMode.Offline,  
            bin_algo=PackingBin.BBF,  # Best Bin First  
            pack_algo=MaxRectsBl,     # MaxRects Bottom Left  
            sort_algo=SORT_AREA,      # Sort by area (not SortBy.Area)  
            rotation=True             # Allow rotation  
        )
        
    def pack_with_rectpack(self, parts):  
        """  
        使用rectpack库进行排版  
        """  
        if self.visualizer:  
            self.visualizer.wait_for_start()  
            
        # 展开所有零件（考虑数量）  
        all_parts = []  
        for idx, row in parts.iterrows():  
            drawing_number = str(row['图号'])  
            length, width, qty = row['OBB长度'], row['OBB宽度'], row['数量']  
            
            for i in range(qty):  
                part_id = f"Part_{idx}_{i+1}"  
                all_parts.append({  
                    'index': idx,  
                    'drawing_number': drawing_number,  
                    'length': length,  
                    'width': width,  
                    'id': part_id,  
                    'original_width': length,  # Store original dimensions  
                    'original_height': width  
                })  
                # 添加到rectpack中  
                self.rp_packer.add_rect(length, width, rid=part_id)  
        
        # 添加板材（bins）  
        self.rp_packer.add_bin(self.effective_width, self.effective_height, count=float("inf"))  
        
        # 执行排版  
        self.rp_packer.pack()  
        
        # 转换结果到原有格式  
        total_bins = len(self.rp_packer)  
        for i in range(total_bins):  
            bin_result = self.rp_packer[i]  
            bin_data = {  
                'rects': [],  
                'used_area': 0,  
                'total_area': self.effective_width * self.effective_height,  
                'free_spaces': []  
            }  
            
            for rect in bin_result:  
                # Find the original part info  
                part_info = next((p for p in all_parts if p['id'] == rect.rid), None)  
                
                # Determine if rotated by comparing dimensions  
                rotated = False  
                if part_info:  
                    # If width and height are swapped compared to original, it's rotated  
                    rotated = (rect.width == part_info['original_height'] and   
                            rect.height == part_info['original_width'])  
                
                placed_rect = {  
                    'x': rect.x,  
                    'y': rect.y,  
                    'w': rect.width,  
                    'h': rect.height,  
                    'drawing_number': part_info['drawing_number'] if part_info else '',  
                    'part_id': rect.rid,  
                    'rotated': rotated  
                }  
                bin_data['rects'].append(placed_rect)  
                bin_data['used_area'] += rect.width * rect.height  
                
            self.bins.append(bin_data)  
            
            # 如果有可视化对象，通知添加新板材  
            if self.visualizer:  
                self.visualizer.add_new_bin()  
                
        # 可视化过程  
        if self.visualizer:  
            total_parts = len(all_parts)  
            processed_parts = 0  
            
            for bin_idx, bin_data in enumerate(self.bins):  
                for rect in bin_data['rects']:  
                    processed_parts += 1  
                    
                    # 更新可视化  
                    utilization = (bin_data['used_area'] / bin_data['total_area']) * 100 if bin_data['total_area'] > 0 else 0  
                    self.visualizer.update_bin_utilization(bin_idx, utilization)  
                    
                    match = re.search(r'(\d+-\d+)', rect['drawing_number'])  
                    display_drawing_number = match.group(1) if match else rect['drawing_number']  
                    self.visualizer.draw_rectangle(  
                        bin_idx, rect['x'], rect['y'], rect['w'], rect['h'], display_drawing_number  
                    )  
                    
                    self.visualizer.update_info(  
                        f"板材 {bin_idx + 1}: "  
                        f"已放置 {len(bin_data['rects'])} 个零件, 利用率: {utilization:.2f}%, "  
                        f"进度: {processed_parts}/{total_parts}"  
                    )  
                    
                    # 检查是否需要暂停  
                    self.visualizer.check_pause()  
                    time.sleep(0.01)  # 短暂延迟以便观察                    
    def pack(self, parts):
        """使用rectpack进行排版"""
        self.pack_with_rectpack(parts)

# ==============================
# 创建 DXF 输出
# ==============================
def create_individual_dxf(bins, base_output_dir, sheet_width, sheet_height, margin):
    base_folder_name = "sheet_dxf_files"
    dxf_folder = os.path.join(base_output_dir, base_folder_name)
    counter = 1
    while os.path.exists(dxf_folder):
        dxf_folder = os.path.join(base_output_dir, f"{base_folder_name}_{counter}")
        counter += 1
    os.makedirs(dxf_folder)
    effective_sheet_area = (sheet_width - 2*margin) * (sheet_height - 2*margin)
    saved_files_info = []
    
    # 创建总DXF文档
    total_doc = ezdxf.new('R2010')
    total_msp = total_doc.modelspace()
    
    for bin_idx, bin_data in enumerate(bins):
        bin_utilization = bin_data['used_area'] / effective_sheet_area * 100 if effective_sheet_area > 0 else 0
        doc = ezdxf.new('R2010')
        msp = doc.modelspace()
        
        # 计算当前板材在总图纸中的位置（横向排列）
        sheet_offset_x = bin_idx * (sheet_width + 100)  # 板材之间留100mm间隔
        
        for rect in bin_data['rects']:
            x, y, w, h = rect['x'], rect['y'], rect['w'], rect['h']
            x1 = round(x + margin, 1)
            y1 = round(y + margin, 1)
            x2 = round(x + w + margin, 1)
            y2 = round(y + h + margin, 1)
            
            # 在单个板材DXF中绘制
            msp.add_line((x1, y1), (x2, y1), dxfattribs={'color': 7})
            msp.add_line((x2, y1), (x2, y2), dxfattribs={'color': 7})
            msp.add_line((x2, y2), (x1, y2), dxfattribs={'color': 7})
            msp.add_line((x1, y2), (x1, y1), dxfattribs={'color': 7})
            
            # 在总DXF中绘制（带偏移量）
            total_msp.add_line(
                (x1 + sheet_offset_x, y1), 
                (x2 + sheet_offset_x, y1), 
                dxfattribs={'color': 7}
            )
            total_msp.add_line(
                (x2 + sheet_offset_x, y1), 
                (x2 + sheet_offset_x, y2), 
                dxfattribs={'color': 7}
            )
            total_msp.add_line(
                (x2 + sheet_offset_x, y2), 
                (x1 + sheet_offset_x, y2), 
                dxfattribs={'color': 7}
            )
            total_msp.add_line(
                (x1 + sheet_offset_x, y2), 
                (x1 + sheet_offset_x, y1), 
                dxfattribs={'color': 7}
            )
        
        # 绘制板材边界（单个文件）
        msp.add_line((0, 0), (sheet_width, 0), dxfattribs={'color': 7})
        msp.add_line((sheet_width, 0), (sheet_width, sheet_height), dxfattribs={'color': 7})
        msp.add_line((sheet_width, sheet_height), (0, sheet_height), dxfattribs={'color': 7})
        msp.add_line((0, sheet_height), (0, 0), dxfattribs={'color': 7})
        
        # 绘制板材边界（总文件）
        total_msp.add_line(
            (sheet_offset_x, 0), 
            (sheet_offset_x + sheet_width, 0), 
            dxfattribs={'color': 7}
        )
        total_msp.add_line(
            (sheet_offset_x + sheet_width, 0), 
            (sheet_offset_x + sheet_width, sheet_height), 
            dxfattribs={'color': 7}
        )
        total_msp.add_line(
            (sheet_offset_x + sheet_width, sheet_height), 
            (sheet_offset_x, sheet_height), 
            dxfattribs={'color': 7}
        )
        total_msp.add_line(
            (sheet_offset_x, sheet_height), 
            (sheet_offset_x, 0), 
            dxfattribs={'color': 7}
        )
        
        # 添加零件编号
        for rect in bin_data['rects']:
            x, y, w, h = rect['x'], rect['y'], rect['w'], rect['h']
            match = re.search(r'(\d+-\d+)', rect['drawing_number'])
            display_drawing_number = match.group(1) if match else rect['drawing_number']
            
            # 单个板材文件中的文字
            text = msp.add_text(display_drawing_number, dxfattribs={'height': 15})
            text.set_placement((x + w / 2 + margin, y + h / 2 + margin), align=TextEntityAlignment.MIDDLE_CENTER)
            
            # 总文件中的文字（带偏移量）
            total_text = total_msp.add_text(display_drawing_number, dxfattribs={'height': 15})
            total_text.set_placement(
                (x + w / 2 + margin + sheet_offset_x, y + h / 2 + margin), 
                align=TextEntityAlignment.MIDDLE_CENTER
            )
        
        # 添加板材标识（总文件）
        sheet_label = total_msp.add_text(f"Sheet {bin_idx + 1}", dxfattribs={'height': 20})
        sheet_label.set_placement(
            (sheet_offset_x + sheet_width / 2, sheet_height + 30), 
            align=TextEntityAlignment.MIDDLE_CENTER
        )
        
        # 添加板材利用率（总文件）
        utilization_text = total_msp.add_text(f"Utilization: {bin_utilization:.2f}%", dxfattribs={'height': 15})
        utilization_text.set_placement(
            (sheet_offset_x + sheet_width / 2, sheet_height + 60),
            align=TextEntityAlignment.MIDDLE_CENTER
        )
        
        filename = f"sheet_{bin_idx + 1}_utilization_{bin_utilization:.1f}%.dxf"
        output_file = os.path.join(dxf_folder, filename)
        doc.saveas(output_file)
        saved_files_info.append({
            'file_path': output_file, 
            'utilization': bin_utilization, 
            'part_count': len(bin_data['rects'])
        })
    
    # 保存总DXF文件
    total_filename = f"all_sheets_combined.dxf"
    total_output_file = os.path.join(dxf_folder, total_filename)
    total_doc.saveas(total_output_file)
    saved_files_info.append({
        'file_path': total_output_file,
        'is_combined': True
    })
    
    return saved_files_info

# ==============================
# 主程序（RectPack优化版）
# ==============================
if __name__ == "__main__":
    print(f"当前板材尺寸: {SHEET_WIDTH}mm x {SHEET_HEIGHT}mm")
    print(f"边缘预留: {MARGIN}mm, 零件间距: {PART_CLEARANCE}mm")

    # 创建可视化对象
    visualizer = PackingVisualizer(SHEET_WIDTH, SHEET_HEIGHT)
    
    # 创建支持可视化的RectPack排版对象
    packer = RectPackPacking(SHEET_WIDTH, SHEET_HEIGHT, visualizer)
    
    # 将packer对象引用传递给visualizer
    visualizer.packer = packer

    # 在新线程中执行排版任务，避免阻塞GUI
    def run_packing():
        # 计算原始零件总数（考虑数量列）
        total_original_parts = df['数量'].sum()
        
        # 使用RectPack算法进行排版
        packer.pack(df)

        # 保存结果
        saved_files = create_individual_dxf(packer.bins, output_dir, SHEET_WIDTH, SHEET_HEIGHT, MARGIN)

        # 统计
        total_parts = sum(len(b['rects']) for b in packer.bins)
        total_bins = len(packer.bins)
        total_used_area = sum(b['used_area'] for b in packer.bins)
        effective_sheet_area = (SHEET_WIDTH - 2*MARGIN) * (SHEET_HEIGHT - 2*MARGIN)
        overall_utilization = total_used_area / (total_bins * effective_sheet_area) * 100 if total_bins > 0 and effective_sheet_area > 0 else 0

        completion_info = (
            f"\n✅ 排版完成！\n"
            f"总共使用板材: {total_bins} 张\n"
            f"原始零件种类数: {len(df)} 种\n"
            f"原始零件总数: {total_original_parts} 个\n"
            f"实际放置零件: {total_parts} 个\n"
            f"整体材料利用率: {overall_utilization:.2f}%\n"
        )
        
        for i, b in enumerate(packer.bins):
            util = b['used_area'] / effective_sheet_area * 100 if effective_sheet_area > 0 else 0
            completion_info += f"  板材 {i+1}: {util:.2f}% ({len(b['rects'])} 个零件)\n"
            
        print(completion_info)
        visualizer.update_info(completion_info)
        visualizer.show_completion()

    # 启动排版线程
    packing_thread = threading.Thread(target=run_packing)
    packing_thread.daemon = True
    packing_thread.start()

    # 启动GUI主循环
    visualizer.root.mainloop()