import os
import tkinter as tk
from tkinter import filedialog
import pandas as pd
import shutil
from pathlib import Path
import re

def select_directory(title="选择文件夹"):
    """
    弹窗选择文件夹
    
    Args:
        title (str): 弹窗标题
        
    Returns:
        str: 选择的文件夹路径
    """
    root = tk.Tk()
    root.withdraw()  # 隐藏主窗口
    directory = filedialog.askdirectory(title=title)
    root.destroy()
    return directory

def select_file(filetypes=[("Excel files", "*.xlsx *.xls")], title="选择文件"):
    """
    弹窗选择文件
    
    Args:
        filetypes (list): 可选择的文件类型
        title (str): 弹窗标题
        
    Returns:
        str: 选择的文件路径
    """
    root = tk.Tk()
    root.withdraw()  # 隐藏主窗口
    file_path = filedialog.askopenfilename(
        title=title,
        filetypes=filetypes
    )
    root.destroy()
    return file_path

def extract_drawing_number(filename):
    """
    从文件名中提取图号（由数字和横杠组成）
    
    Args:
        filename (str): 文件名
        
    Returns:
        str: 提取的图号
    """
    # 使用正则表达式查找由数字和横杠组成的图号
    match = re.search(r'[\d-]+', filename)
    if match:
        return match.group()
    return ""

def filter_and_copy_pdfs(pdf_directory, excel_path, output_directory):
    """
    根据Excel中的类别筛选PDF并复制到指定目录
    
    Args:
        pdf_directory (str): PDF文件所在目录
        excel_path (str): Excel文件路径
        output_directory (str): 输出目录
    """
    # 创建输出目录（如果不存在）
    Path(output_directory).mkdir(parents=True, exist_ok=True)
    
    # 读取Excel文件
    df = pd.read_excel(excel_path)
    
    # 假设第一列是文件名或零件号，'类别'列是类别
    filename_column = df.columns[0]
    category_column = '类别'
    
    # 筛选出类别为"组装图"的记录
    assembly_parts = df[df[category_column] == '组装图']
    
    # 提取这些记录的图号
    drawing_numbers = set()
    for _, row in assembly_parts.iterrows():
        filename = str(row[filename_column])
        drawing_number = extract_drawing_number(Path(filename).stem)
        if drawing_number:
            drawing_numbers.add(drawing_number)
    
    print(f"找到 {len(drawing_numbers)} 个组装图的图号")
    print("图号列表:", drawing_numbers)
    
    # 遍历PDF目录中的所有PDF文件
    pdf_files = [f for f in os.listdir(pdf_directory) if f.lower().endswith('.pdf')]
    
    copied_count = 0
    for pdf_file in pdf_files:
        # 从PDF文件名提取图号
        pdf_drawing_number = extract_drawing_number(Path(pdf_file).stem)
        
        # 如果图号匹配，则复制文件
        if pdf_drawing_number in drawing_numbers:
            source_path = os.path.join(pdf_directory, pdf_file)
            dest_path = os.path.join(output_directory, pdf_file)
            
            try:
                shutil.copy2(source_path, dest_path)
                print(f"已复制: {pdf_file}")
                copied_count += 1
            except Exception as e:
                print(f"复制文件 {pdf_file} 失败: {e}")
    
    print(f"总共复制了 {copied_count} 个PDF文件到 {output_directory}")

def main():
    """
    主函数
    """
    print("请选择包含PDF文件的文件夹...")
    pdf_directory = select_directory("选择包含PDF文件的文件夹")
    if not pdf_directory:
        print("未选择文件夹，程序退出")
        return
    
    print(f"已选择PDF文件夹: {pdf_directory}")
    
    print("请选择Excel文件...")
    excel_path = select_file([("Excel files", "*.xlsx *.xls")], "选择Excel文件")
    if not excel_path:
        print("未选择Excel文件，程序退出")
        return
    
    print(f"已选择Excel文件: {excel_path}")
    
    # 设置输出目录
    output_directory = os.path.join(os.getcwd(), "pdf_output", "select_pdf")
    
    print("开始筛选和复制PDF文件...")
    filter_and_copy_pdfs(pdf_directory, excel_path, output_directory)
    
    print("处理完成!")

if __name__ == "__main__":
    main()