import pandas as pd
import os
import shutil
import re
from tkinter import filedialog, Tk, messagebox
import tkinter as tk

def sanitize_filename(filename):
    """
    清理文件名，规范化处理特殊字符
    """
    # 去除首尾空格
    filename = filename.strip()
    # 将连续空格替换为单个下划线
    filename = re.sub(r'\s+', '_', filename)
    return filename

def normalize_filename(filename):
    """
    标准化文件名以提高匹配成功率
    """
    # 去除扩展名
    base_name = os.path.splitext(filename)[0]
    ext = os.path.splitext(filename)[1]
    
    # 清理文件名
    clean_name = sanitize_filename(base_name)
    
    # 移除或替换特殊字符
    clean_name = re.sub(r'[<>:"/\\|?*]', '', clean_name)
    
    return clean_name + ext

def create_file_mapping(source_dxf_folder):
    """
    创建文件映射以支持模糊匹配
    """
    file_mapping = {}
    dxf_files = [f for f in os.listdir(source_dxf_folder) if f.lower().endswith('.dxf')]
    
    for filename in dxf_files:
        base_name = os.path.splitext(filename)[0]
        
        # 添加原始名称映射
        file_mapping[base_name] = filename
        
        # 添加清理后的名称映射
        clean_name = sanitize_filename(base_name)
        file_mapping[clean_name] = filename
        
        # 添加去除空格的名称映射
        no_space_name = base_name.replace(' ', '')
        file_mapping[no_space_name] = filename
        
        # 添加去除括号和空格的名称映射
        no_bracket_name = re.sub(r'[()]', '', base_name).replace(' ', '')
        file_mapping[no_bracket_name] = filename
    
    return file_mapping

def find_dxf_file(part_number, file_mapping):
    """
    在文件映射中查找匹配的DXF文件
    """
    # 尝试多种匹配方式
    search_names = [
        part_number,
        sanitize_filename(part_number),
        part_number.replace(' ', ''),
        re.sub(r'[()]', '', part_number).replace(' ', '')
    ]
    
    for name in search_names:
        if name in file_mapping:
            return file_mapping[name]
    
    # 如果精确匹配失败，尝试模糊匹配
    for key, value in file_mapping.items():
        if part_number in key or key in part_number:
            return value
    
    return None

def add_quantity_to_filename(filename, quantity):
    """
    将数量信息添加到文件名中
    
    Args:
        filename (str): 原始文件名
        quantity (int/str): 数量信息
    
    Returns:
        str: 添加数量信息后的新文件名
    """
    # 分离文件名和扩展名
    name, ext = os.path.splitext(filename)
    
    # 添加数量信息到文件名
    new_filename = f"{name}数量{quantity}{ext}"
    
    return new_filename

def select_files_and_process():
    """
    弹出窗口选择文件夹和Excel文件，处理DXF文件分类
    """
    # 隐藏主窗口
    root = Tk()
    root.withdraw()
    
    # 选择Excel文件
    excel_file = filedialog.askopenfilename(
        title="选择包含零件信息的Excel文件",
        filetypes=[("Excel files", "*.xlsx *.xls")]
    )
    
    if not excel_file:
        messagebox.showwarning("警告", "未选择Excel文件")
        return
    
    # 选择源DXF文件所在文件夹
    source_dxf_folder = filedialog.askdirectory(
        title="选择包含DXF文件的源文件夹"
    )
    
    if not source_dxf_folder:
        messagebox.showwarning("警告", "未选择源DXF文件夹")
        return
    
    try:
        # 处理文件，目标文件夹就在DXF文件夹下
        process_dxf_files(excel_file, source_dxf_folder)
        messagebox.showinfo("完成", "DXF文件分类完成！")
    except Exception as e:
        messagebox.showerror("错误", f"处理过程中出现错误：{str(e)}")

def process_dxf_files(excel_file, source_dxf_folder):
    """
    读取Excel数据并按材料和厚度分类DXF文件，同时在文件名中添加数量信息
    
    Parameters:
    excel_file (str): Excel文件路径
    source_dxf_folder (str): 源DXF文件夹路径（同时也是目标文件夹的父目录）
    """
    # 读取Excel文件
    df = pd.read_excel(excel_file)
    
    # 确保必要的列存在
    required_columns = ['零件号', '材料', '板厚']
    # 检查是否有数量列
    quantity_column = None
    if '数量' in df.columns:
        quantity_column = '数量'
    elif 'Qty' in df.columns:
        quantity_column = 'Qty'
    elif 'qty' in df.columns:
        quantity_column = 'qty'
    
    if not all(col in df.columns for col in required_columns):
        raise ValueError(f"Excel文件必须包含以下列: {', '.join(required_columns)}")
    
    # 创建文件映射以支持模糊匹配
    file_mapping = create_file_mapping(source_dxf_folder)
    
    # 统计信息
    total_parts = len(df)
    processed_parts = 0
    missing_files = 0
    
    # 处理每一行数据
    for index, row in df.iterrows():
        part_number = str(row['零件号'])
        material = str(row['材料'])
        thickness = str(row['板厚'])
        
        # 获取数量信息（如果存在）
        quantity = None
        if quantity_column:
            quantity = row[quantity_column]
        
        # 查找匹配的DXF文件
        dxf_filename = find_dxf_file(part_number, file_mapping)
        
        if not dxf_filename:
            print(f"警告: 零件 {part_number} 的DXF文件不存在")
            missing_files += 1
            continue
        
        # 构造源文件路径
        source_path = os.path.join(source_dxf_folder, dxf_filename)
        
        # 检查源文件是否存在（双重检查）
        if not os.path.exists(source_path):
            print(f"警告: 零件 {part_number} 的DXF文件不存在: {source_path}")
            missing_files += 1
            continue
        
        # 如果有数量信息，则修改文件名
        target_filename = dxf_filename
        if quantity is not None and quantity != '':
            target_filename = add_quantity_to_filename(dxf_filename, quantity)
        
        # 创建目标文件夹路径 (材料+厚度)，在DXF文件夹下
        folder_name = f"{material}+{thickness}"
        target_dir = os.path.join(source_dxf_folder, folder_name)
        
        # 如果文件夹不存在则创建
        os.makedirs(target_dir, exist_ok=True)
        
        # 构造目标文件路径
        target_path = os.path.join(target_dir, target_filename)
        
        # 复制文件
        try:
            shutil.copy2(source_path, target_path)
            if target_filename != dxf_filename:
                print(f"已复制并重命名: {dxf_filename} -> {target_filename} 到 {folder_name}")
            else:
                print(f"已复制: {dxf_filename} -> {folder_name}")
            processed_parts += 1
        except Exception as e:
            print(f"复制文件时出错 {dxf_filename}: {str(e)}")
    
    # 输出处理统计信息
    print(f"\n处理完成统计:")
    print(f"总零件数: {total_parts}")
    print(f"成功处理: {processed_parts}")
    print(f"缺失文件: {missing_files}")

if __name__ == "__main__":
    select_files_and_process()