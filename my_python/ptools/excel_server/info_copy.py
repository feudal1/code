import pandas as pd
import os
import re
from pathlib import Path
import tkinter as tk
from tkinter import filedialog

def merge_excel_data(source_file, target_file, output_file):
    """
    将源Excel文件中的材料信息按零件号匹配到目标Excel文件
    
    Args:
        source_file: 包含完整信息的源Excel文件路径
        target_file: 需要补充信息的目标Excel文件路径
        output_file: 输出文件路径
    """
    # 标准化路径
    source_file = str(Path(source_file).resolve())
    target_file = str(Path(target_file).resolve())
    output_file = str(Path(output_file).resolve())
    
    # 验证源文件和目标文件是否存在
    if not os.path.exists(source_file):
        raise FileNotFoundError(f"源文件不存在: {source_file}")
    if not os.path.exists(target_file):
        raise FileNotFoundError(f"目标文件不存在: {target_file}")
    
    # 读取两个Excel文件，源文件使用灵活读取
    source_df = read_excel_with_flexible_header(source_file)
    
    # 智能读取目标文件
    target_df = read_target_excel_intelligently(target_file)
    
    # 显示两个文件的列名
    print("源文件列名:", source_df.columns.tolist())
    print("目标文件列名:", target_df.columns.tolist())
    
    # 自动检测源文件中的相关列
    # 定义可能的列名映射
    column_mapping = {
        '零件号': ['零件号', '图号', '部件号', 'Part Number', 'PartNo', '图号/零件号'],
        '材料': ['材料', 'Material', '材质', '材料规格'],
    }
    
    # 查找实际存在的列名
    actual_columns = {}
    for standard_name, possible_names in column_mapping.items():
        found = False
        for name in possible_names:
            if name in source_df.columns:
                actual_columns[standard_name] = name
                found = True
                break
        if not found:
            print(f"警告: 未找到 '{standard_name}' 对应的列")
    
    print("映射后的列名:", actual_columns)
    
    # 如果自动检测失败，尝试手动映射（基于列位置）
    if not actual_columns:
        print("尝试基于列位置进行手动映射...")
        actual_columns = manual_column_mapping(source_df)
        print("手动映射后的列名:", actual_columns)
    
    # 检查必需的零件号列是否存在
    if '零件号' not in actual_columns:
        raise KeyError("源文件中未找到零件号相关列")
    
    # 提取需要的列（使用实际列名）
    source_columns_to_extract = [actual_columns['零件号']]
    if '材料' in actual_columns:
        source_columns_to_extract.append(actual_columns['材料'])

    
    source_needed = source_df[source_columns_to_extract]
    
    # 重命名列以匹配标准名称
    rename_dict = {actual_columns[key]: key for key in actual_columns}
    source_needed = source_needed.rename(columns=rename_dict)
    
    # 添加标准化零件号列用于匹配
    source_needed['标准化零件号'] = source_needed['零件号'].apply(extract_part_number_from_filename)
    
    # 处理目标文件，找出包含零件号信息的列并创建标准化零件号列
    # 找到第一列（看起来包含零件号信息）
    first_column = target_df.columns[0]
    print(f"使用第一列 '{first_column}' 提取零件号信息")
    
    # 从第一列提取零件号并创建标准化零件号
    target_df['标准化零件号'] = target_df[first_column].apply(extract_part_number_from_filename)
    
    # 使用标准化零件号合并数据
    result_df = pd.merge(target_df, source_needed, on='标准化零件号', how='left', suffixes=('', '_source'))
    
    # 删除临时的标准化零件号列
    result_df = result_df.drop(['标准化零件号'], axis=1)
    
    # 添加去重逻辑：对于相同零件号且相同材料的记录只保留一条
    if '材料' in result_df.columns:
        # 基于零件号和材料去重，保留第一次出现的记录
        result_df = result_df.drop_duplicates(subset=['零件号', '材料'], keep='first')
        print(f"去重后剩余 {len(result_df)} 条记录")
    
    # 确保输出目录存在
    output_dir = os.path.dirname(output_file)
    if output_dir and not os.path.exists(output_dir):
        os.makedirs(output_dir)
    
    # 保存结果
    result_df.to_excel(output_file, index=False)
    print(f"数据合并完成，已保存到 {output_file}")

def extract_part_number_from_filename(filename):
    """
    从文件名中提取并标准化零件号
    例如: 
    "21085-3000-1001-0100-01-" -> "21085-3000-1001-0100-01"
    "21085-3000-1001-0100-01.stp" -> "21085-3000-1001-0100-01"
    """
    if pd.isna(filename):
        return filename
    
    filename_str = str(filename)
    
    # 使用正则表达式从文件名中提取零件号模式
    # 匹配类似 21085-3000-1000-0100-01 这样的零件号
    match = re.search(r'(\d{5}-\d{4}-\d{4}-\d{4}-\d{2})', filename_str)
    if match:
        return match.group(1)
    
    # 更通用的匹配方式，匹配以数字开头的数字和连字符组合，并确保以数字结尾
    match = re.search(r'^([\d\-]*\d)', filename_str)
    if match:
        return match.group(1)
    
    return filename_str

def read_excel_with_flexible_header(file_path):
    """
    灵活读取Excel文件，尝试不同的header行
    """
    # 首先尝试默认读取
    df = pd.read_excel(file_path)
    
    # 检查是否有很多"Unnamed"列
    unnamed_count = sum(1 for col in df.columns if 'Unnamed' in str(col))
    
    # 如果Unnamed列太多，尝试其他header行
    if unnamed_count > len(df.columns) / 2:
        print(f"检测到文件 {file_path} 可能有标题行问题，尝试不同header行...")
        for header_row in range(0, min(5, len(df))):  # 尝试前5行作为header
            try:
                temp_df = pd.read_excel(file_path, header=header_row)
                unnamed_count_temp = sum(1 for col in temp_df.columns if 'Unnamed' in str(col))
                if unnamed_count_temp < unnamed_count:
                    print(f"使用第 {header_row} 行作为标题行")
                    return temp_df
            except Exception as e:
                continue
    return df

def read_target_excel_intelligently(file_path):
    """
    智能读取目标Excel文件，自动判断正确的标题行
    """
    # 读取前几行用于分析
    dfs = []
    for i in range(3):  # 尝试前3行作为标题行
        try:
            df = pd.read_excel(file_path, header=i)
            dfs.append((i, df))
        except:
            pass
    
    if not dfs:
        # 如果都失败了，使用默认方式读取
        return pd.read_excel(file_path)
    
    # 分析哪个是最合适的标题行
    best_header = 0
    best_score = -1
    
    for header_idx, df in dfs:
        score = 0
        
        if not df.empty:
            # 检查第一列是否像标题（不是具体数据）
            first_col_name = str(df.columns[0])
            
            # 如果第一列名看起来像文件名或包含零件号，则可能是数据行
            if not (('.dxf' in first_col_name) or re.search(r'\d{5}-\d{4}', first_col_name)):
                score += 10
            
            # 检查是否有合理的列名（不像具体数据）
            unnamed_count = sum(1 for col in df.columns if 'Unnamed' in str(col))
            if unnamed_count < len(df.columns) / 2:
                score += 5
                
            # 检查第一行数据是否像文件名（如果是，则说明标题行正确）
            if len(df) > 0:
                first_data_value = str(df.iloc[0, 0]) if not df.empty else ""
                if ('.dxf' in first_data_value) or re.search(r'\d{5}-\d{4}', first_data_value):
                    score += 15
        
        if score > best_score:
            best_score = score
            best_header = header_idx
    
    print(f"智能识别目标文件标题行: 第{best_header}行")
    return pd.read_excel(file_path, header=best_header)

def manual_column_mapping(df):
    """
    当自动检测失败时，基于列位置进行手动映射
    """
    columns = df.columns.tolist()
    mapping = {}
    
    # 如果列数足够，基于位置映射
    if len(columns) >= 2:
        mapping['零件号'] = columns[0]  # 假设第1列是零件号
        mapping['材料'] = columns[1]   # 假设第2列是材料

    return mapping

def select_excel_file(title="选择Excel文件"):
    """
    弹出文件选择窗口选择Excel文件
    """
    root = tk.Tk()
    root.withdraw()  # 隐藏主窗口
    file_path = filedialog.askopenfilename(
        title=title,
        filetypes=[("Excel文件", "*.xlsx *.xls"), ("所有文件", "*.*")]
    )
    root.destroy()
    return file_path

# 获取用户通过弹窗选择的文件路径
print("请选择源Excel文件（包含完整信息的文件）")
source_file = select_excel_file("选择源Excel文件")

print("请选择目标Excel文件（需要补充信息的文件）")
target_file = select_excel_file("选择目标Excel文件")

# 检查用户是否选择了文件
if not source_file or not target_file:
    print("错误: 请确保选择了源文件和目标文件")
    input("按回车键退出...")
else:
    # 自动生成输出文件路径
    # 创建输出目录
    output_dir = Path("excel_output") / "info_copy"
    output_dir.mkdir(parents=True, exist_ok=True)
    
    # 生成输出文件名，使用目标文件名加上时间戳
    target_filename = Path(target_file).stem
    output_file = output_dir / f"{target_filename}_材料合并.xlsx"
    
    # 调用函数
    try:
        merge_excel_data(source_file, target_file, str(output_file))
        print("处理完成！")
        print(f"输出文件保存在: {output_file}")
        input("按回车键退出...")
    except Exception as e:
        print(f"发生错误: {e}")
        import traceback
        traceback.print_exc()
        input("按回车键退出...")