# excel_dxf_selector.py

import os
import shutil
import tkinter as tk
from tkinter import filedialog, messagebox
import pandas as pd

def read_excel_parts(excel_path):
    """
    读取Excel文件中的零件名称
    
    Args:
        excel_path (str): Excel文件路径
        
    Returns:
        set: 包含所有零件名称的集合
    """
    try:
        # 读取Excel文件
        df = pd.read_excel(excel_path)
        
        # 查找包含"零件名"的列
        part_column = None
        for col in df.columns:
            if '零件名' in str(col):
                part_column = col
                break
        
        if part_column is None:
            # 如果没有找到包含"零件名"的列，则使用第一列
            part_column = df.columns[0]
            print(f"未找到明确标记为'零件名'的列，使用第一列'{part_column}'作为零件名列")
        
        # 提取零件名称并清理数据
        parts = set()
        for item in df[part_column].dropna():
            # 清理零件名称，去除空格等
            clean_item = str(item).strip()
            if clean_item:  # 只添加非空项
                parts.add(clean_item)
                
        print(f"从Excel中读取到 {len(parts)} 个零件名称")
        return parts
        
    except Exception as e:
        print(f"读取Excel文件时出错: {e}")
        return set()

def match_and_copy_dxf_files(part_names, dxf_files, output_folder):
    """
    匹配DXF文件并复制到输出文件夹
    
    Args:
        part_names (set): 零件名称集合
        dxf_files (list): DXF文件路径列表
        output_folder (str): 输出文件夹路径
    """
    matched_count = 0
    
    # 创建输出文件夹
    os.makedirs(output_folder, exist_ok=True)
    
    # 遍历所有DXF文件
    for dxf_path in dxf_files:
        filename = os.path.basename(dxf_path)
        name_without_ext = os.path.splitext(filename)[0]
        
        # 检查文件名是否匹配零件名（精确匹配）
        if name_without_ext in part_names:
            # 构造目标路径
            dest_path = os.path.join(output_folder, filename)
            
            try:
                # 复制文件
                shutil.copy2(dxf_path, dest_path)
                print(f"已复制: {filename}")
                matched_count += 1
            except Exception as e:
                print(f"复制文件 {filename} 时出错: {e}")
        else:
            # 模糊匹配：检查零件名是否是文件名的一部分
            for part_name in part_names:
                if part_name in name_without_ext or name_without_ext in part_name:
                    dest_path = os.path.join(output_folder, filename)
                    try:
                        shutil.copy2(dxf_path, dest_path)
                        print(f"已复制(模糊匹配): {filename} (匹配零件名: {part_name})")
                        matched_count += 1
                        break
                    except Exception as e:
                        print(f"复制文件 {filename} 时出错: {e}")
    
    return matched_count

def main():
    root = tk.Tk()
    root.withdraw()  # 隐藏主窗口
    
    try:
        # 选择Excel文件
        print("请选择包含零件名称的Excel文件...")
        excel_path = filedialog.askopenfilename(
            title="选择Excel文件",
            filetypes=[("Excel files", "*.xlsx;*.xls"), ("All files", "*.*")]
        )
        
        if not excel_path:
            print("未选择Excel文件，程序退出")
            return
            
        print(f"选择的Excel文件: {excel_path}")
        
        # 读取零件名称
        part_names = read_excel_parts(excel_path)
        
        if not part_names:
            messagebox.showwarning("警告", "未能从Excel文件中读取到有效的零件名称")
            print("未能从Excel文件中读取到有效的零件名称")
            return
        
        print(f"读取到以下零件名称: {list(part_names)[:10]}{'...' if len(part_names) > 10 else ''}")
        
        # 选择DXF文件（支持多选）
        print("请选择需要筛选的DXF文件（可多选）...")
        dxf_files = filedialog.askopenfilenames(
            title="选择DXF文件（可多选）",
            filetypes=[("DXF files", "*.dxf"), ("All files", "*.*")]
        )
        
        if not dxf_files:
            print("未选择任何DXF文件，程序退出")
            return
            
        print(f"选择了 {len(dxf_files)} 个DXF文件")
        
        # 确定输出文件夹路径
        excel_dir = os.path.dirname(excel_path)
        excel_name = os.path.splitext(os.path.basename(excel_path))[0]
        output_folder = os.path.join(excel_dir, f"{excel_name}_筛选结果")
        
        # 执行匹配和复制操作
        matched_count = match_and_copy_dxf_files(part_names, dxf_files, output_folder)
        
        # 显示结果
        result_msg = f"处理完成!\n\n匹配并复制了 {matched_count} 个DXF文件\n\n输出文件夹: {output_folder}"
        print(result_msg)
        messagebox.showinfo("完成", result_msg)
        
    except Exception as e:
        error_msg = f"程序执行过程中发生错误: {str(e)}"
        print(error_msg)
        messagebox.showerror("错误", error_msg)
    finally:
        root.destroy()

if __name__ == "__main__":
    main()