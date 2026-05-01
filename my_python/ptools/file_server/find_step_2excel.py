import tkinter as tk
from tkinter import filedialog
import pandas as pd
from pathlib import Path
import re

def get_basic_file_info(file_path):
    """
    获取文件的基本信息
    """
    try:
        stat = file_path.stat()
        return {
            '文件大小(字节)': stat.st_size,
            '修改时间': pd.to_datetime(stat.st_mtime, unit='s')
        }
    except Exception as e:
        print(f"获取文件 {file_path} 信息时出错: {e}")
        return {}

def extract_quantity_from_filename(filename):
    """
    从文件名中提取数量信息
    例如: ZSG1-76-4-729-4支.STEP -> 数量为 4
    """
    # 移除扩展名
    name_without_ext = Path(filename).stem
    
    # 查找文件名中的数量模式
    quantity_patterns = [
        r'-([0-9]+)支$',     # 匹配 -4支 这样的模式
        r'-([0-9]+)[个件条]$', # 匹配 -4个, -4件, -4条 等模式
        r'-([0-9]+)$',       # 匹配 -4 这样的模式
    ]
    
    for pattern in quantity_patterns:
        match = re.search(pattern, name_without_ext)
        if match:
            return int(match.group(1))
    
    # 如果没有找到明确的数量标记，返回默认值1
    return 1

def extract_base_name(filename):
    """
    从文件名中提取基本名称（移除数量部分）
    例如: ZSG1-76-4-729-4支.STEP -> ZSG1-76-4-729
    """
    # 移除扩展名
    name_without_ext = Path(filename).stem
    
    # 移除数量部分
    quantity_patterns = [
        r'-[0-9]+支$',     
        r'-[0-9]+[个件条]$',
        r'-[0-9]+$',
    ]
    
    base_name = name_without_ext
    for pattern in quantity_patterns:
        base_name = re.sub(pattern, '', base_name)
        if base_name != name_without_ext:
            break
    
    return base_name

def find_step_files_recursive(folder_path):
    """
    递归查找文件夹及其子文件夹中的所有STEP文件
    """
    step_files = []
    folder_path = Path(folder_path)
    
    # 递归查找所有.step和.stp文件
    step_files.extend(folder_path.rglob('*.step'))
    step_files.extend(folder_path.rglob('*.stp'))
    
    return step_files

def copy_file_content(src_path, dst_path):
    """
    简单的文件内容复制方法
    """
    try:
        with open(src_path, 'rb') as src:
            with open(dst_path, 'wb') as dst:
                while True:
                    chunk = src.read(8192)  # 8KB chunks
                    if not chunk:
                        break
                    dst.write(chunk)
        return True
    except Exception as e:
        print(f"复制文件 {src_path} 到 {dst_path} 时出错: {e}")
        return False

def process_step_files():
    """
    主处理函数 - 提取数量信息到Excel
    """
    # 创建Tk根窗口并隐藏
    root = tk.Tk()
    root.withdraw()
    
    # 选择源文件夹
    source_folder = filedialog.askdirectory(title="选择包含STEP文件的文件夹")
    if not source_folder:
        print("未选择文件夹")
        return
    
    # 在源文件夹中创建输出文件夹
    output_folder = Path(source_folder) / "提取的STEP文件"
    output_folder.mkdir(exist_ok=True)
    
    # 创建Excel数据存储
    excel_data = []
    
    # 递归获取所有STEP文件
    step_files = find_step_files_recursive(source_folder)
    
    if not step_files:
        print("在选定文件夹及其子文件夹中未找到STEP文件")
        return
    
    print(f"找到 {len(step_files)} 个STEP文件")
    
    # 处理每个STEP文件
    for step_file in step_files:
        print(f"处理文件: {step_file.name}")
        
        # 从文件名提取信息
        base_name = extract_base_name(step_file.name)
        quantity = extract_quantity_from_filename(step_file.name)
        
        # 复制文件到统一输出文件夹
        dest_path = output_folder / step_file.name
        # 如果文件名冲突，添加序号
        counter = 1
        original_dest_path = dest_path
        while dest_path.exists():
            name_without_ext = original_dest_path.stem
            new_name = f"{name_without_ext}_{counter}{original_dest_path.suffix}"
            dest_path = output_folder / new_name
            counter += 1
        
        # 使用自定义方法复制文件
        if copy_file_content(step_file, dest_path):
            print(f"  成功复制到: {dest_path}")
        else:
            print(f"  复制失败: {step_file.name}")
            continue
        
        # 获取文件基本信息
        file_info = get_basic_file_info(step_file)
        
        # 添加到Excel数据
        excel_data.append({
            '原路径': str(step_file.relative_to(source_folder)),
            '新文件名': dest_path.name,
            '基本名称': base_name,
            '数量': quantity,
            '文件大小(字节)': file_info.get('文件大小(字节)', 0),
            '修改时间': file_info.get('修改时间', ''),
            '扩展名': step_file.suffix.lower()
        })
    
    # 保存到Excel
    if excel_data:
        df = pd.DataFrame(excel_data)
        excel_path = output_folder / 'step文件清单.xlsx'
        df.to_excel(excel_path, index=False)
        print(f"文件已复制到: {output_folder}")
        print(f"文件清单已保存到: {excel_path}")
        print(f"总共处理了 {len(step_files)} 个文件")
        
        # 显示一些示例
        print("\n示例数据:")
        for i, data in enumerate(excel_data[:3]):
            print(f"  {data['新文件名']} -> 基本名称: {data['基本名称']}, 数量: {data['数量']}")
    else:
        print("未生成有效数据")

if __name__ == "__main__":
    process_step_files()