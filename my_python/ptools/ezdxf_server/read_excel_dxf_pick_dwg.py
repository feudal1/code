import os
import shutil
import re
from collections import defaultdict
import subprocess
import time
import logging
import openpyxl
import ezdxf
from flask import Flask, jsonify, request
# 寻找小于Excel编号的最大图纸编号这是在不同文件内找，大小排序是当前dxf文档要进行提取图号，dxf文档名称要提取一个图号，里面内容也要提取图号，dxf文档内的图号随dxf图号增加也会增加
# 初始化日志和Flask应用
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

app = Flask(__name__)

# ==================== 工具函数 ====================

def parse_id(id_str):
    """解析ID为 (prefix, number)"""
    match = re.match(r'^([A-Z]+[A-Z0-9]*)-([0-9]+)$', id_str)
    if match:
        prefix, num = match.groups()
        return prefix, int(num)
    return None, None

def extract_ids_from_dxf(dxf_path):
    """从DXF文件中提取所有符合格式的ID"""
    try:
        doc = ezdxf.readfile(dxf_path)
        msp = doc.modelspace()
        ids = set()

        pattern = r'^[A-Z]+[A-Z0-9]*-[0-9]+$'

        for entity in msp:
            if entity.dxftype() == 'TEXT':
                text = entity.dxf.text
                if text and isinstance(text, str):
                    matches = re.findall(pattern, text)
                    for match in matches:
                        ids.add(match.strip())
            elif entity.dxftype() == 'MTEXT':
                text = entity.dxf.text
                if text and isinstance(text, str):
                    lines = text.split('\n')
                    for line in lines:
                        matches = re.findall(pattern, line)
                        for match in matches:
                            ids.add(match.strip())

        return list(ids)
    except Exception as e:
        logger.error(f"解析DXF失败: {dxf_path}, 错误: {e}")
        return []

def convert_dwg_to_dxf(dwg_file_path):
    """
    使用ODA File Converter将DWG文件转换为DXF文件
    """
    start_time = time.time()
    try:
        if not os.path.exists(dwg_file_path):
            return {
                "status": "error",
                "message": f"DWG文件不存在: {dwg_file_path}"
            }

        dwg_dir = os.path.dirname(dwg_file_path)
        dwg_filename = os.path.basename(dwg_file_path)
        dxf_filename = dwg_filename.replace('.dwg', '.dxf')
        dxf_file_path = dwg_file_path.replace('.dwg', '.dxf')
        if dxf_file_path == dwg_file_path:
            dxf_file_path = f"{dwg_file_path}.dxf"

        output_dir = os.path.dirname(dxf_file_path)
        os.makedirs(output_dir, exist_ok=True)

        cmd = [
            "ODAFileConverter",
            dwg_dir,
            output_dir,
            "ACAD2018",
            "DXF",
            "0",
            "1",
            "*.DWG"
        ]

        result = subprocess.run(cmd, shell=True, capture_output=True, text=True, timeout=120)

        if result.returncode == 0 and os.path.exists(dxf_file_path):
            response = {
                "status": "success",
                "message": f"DWG文件已成功转换为DXF: {dxf_file_path}",
                "dxf_path": dxf_file_path
            }
            return response
        else:
            response = {
                "status": "error",
                "message": f"ODA转换失败: {result.stderr}"
            }
            return response

    except subprocess.TimeoutExpired:
        return {"status": "error", "message": "转换超时"}
    except FileNotFoundError:
        return {"status": "error", "message": "未找到ODAFileConverter命令"}
    except Exception as e:
        return {"status": "error", "message": f"转换过程中发生错误: {str(e)}"}

# ==================== 核心逻辑 ====================

def find_dwg_files_by_excel_data(excel_path, folder_path, output_folder='dxf_output/pick_dxf', column='B', sheet_name=None):
    """
    主逻辑：读取Excel，遍历DWG，转换为DXF，提取文字ID，匹配并导出
    """
    if not os.path.exists(excel_path):
        print(f"Excel文件不存在: {excel_path}")
        return {}
    if not os.path.exists(folder_path):
        print(f"文件夹不存在: {folder_path}")
        return {}

    os.makedirs(output_folder, exist_ok=True)

    # 读取Excel数据
    workbook = openpyxl.load_workbook(excel_path)
    worksheet = workbook[sheet_name] if sheet_name else workbook.active
    search_terms = []
    row = 2
    while True:
        cell_value = worksheet[f'{column}{row}'].value
        if cell_value is None:
            break
        search_terms.append(str(cell_value))
        row += 1

    print(f"从Excel中读取到 {len(search_terms)} 个搜索项: {search_terms}")

    # 获取所有DWG文件
    dwg_files = [f for f in os.listdir(folder_path) if f.lower().endswith('.dwg')]
    dxf_files = []
    # 转换所有DWG为DXF（若尚未转换）
    for dwg_file in dwg_files:
        dwg_path = os.path.join(folder_path, dwg_file)
        dxf_path = dwg_path.replace('.dwg', '.dxf')
        dxf_dir = os.path.dirname(dxf_path)
        os.makedirs(dxf_dir, exist_ok=True)

        # 若 DXF 文件已存在，则跳过转换
        if os.path.exists(dxf_path):
            #print(f"DXF文件已存在，跳过转换: {dxf_path}")
            dxf_files.append(dxf_path)
            continue

        result = convert_dwg_to_dxf(dwg_path)
        if result["status"] == "success":
            dxf_files.append(dxf_path)
        else:
            print(f"转换失败: {dwg_file}")

    # 构建索引：按前缀分组，每组按编号排序
    prefix_map = defaultdict(list)
    id_to_dxf_map = {}  # 新增映射：记录ID到DXF文件的对应关系
    for dxf_path in dxf_files:
        ids = extract_ids_from_dxf(dxf_path)
        for id_str in ids:
            prefix, num = parse_id(id_str)
            if prefix and num is not None:
                prefix_map[prefix].append((id_str, num, dxf_path))
                id_to_dxf_map[id_str] = dxf_path  # 记录每个ID对应的DXF文件

    # 对每个前缀按编号排序
    for prefix in prefix_map:
        prefix_map[prefix].sort(key=lambda x: x[1])  # 按数字排序

    # 匹配Excel项
    results = {}
    for term in search_terms:
        target_prefix, target_num = parse_id(term)
        if not target_prefix or target_num is None:
            print(f"跳过无效ID: {term}")
            continue

        print(f"正在查找: {term} (前缀: {target_prefix}, 编号: {target_num})")
        
        # 查找同前缀的文件
        candidates = prefix_map.get(target_prefix, [])
        
        # 显示该前缀下的一个示例（如果存在）
        if candidates:
            sample_candidate = candidates[0]
            print(f"前缀 '{target_prefix}' 下的一个示例: {sample_candidate[0]} (编号: {sample_candidate[1]})")
        else:
            print(f"前缀 '{target_prefix}' 下没有找到匹配项")

        if not candidates:
            # 没有该前缀 → 随机选一个
            if dxf_files:
                random_dxf = dxf_files[0]  # 简单随机选择第一个
                dest_path = os.path.join(output_folder, os.path.basename(random_dxf))
                shutil.copy2(random_dxf, dest_path)
                results[term] = {
                    "file_path": dest_path,
                    "matched_text": None  # 无匹配文字
                }
                print(f"无匹配前缀，随机选择: {dest_path}")
            else:
                results[term] = {
                    "file_path": None,
                    "matched_text": None
                }
            continue

        # 有对应前缀 → 完全匹配编号
        found = None
        matched_text = None  # 记录匹配到的文字
        
        # 寻找完全匹配的编号
        for id_str, num, dxf_path in candidates:
            if num == target_num:  # 完全匹配
                found = dxf_path
                matched_text = id_str
                break

        if found:
            dest_path = os.path.join(output_folder, os.path.basename(found))
            shutil.copy2(found, dest_path)
            results[term] = {
                "file_path": dest_path,
                "matched_text": matched_text  # 返回匹配到的文字
            }
            print(f"匹配成功: {term} -> {dest_path} (匹配文字: {matched_text})")
        else:
            # 没有完全匹配的 → 随机选一个
            if dxf_files:
                random_dxf = dxf_files[0]
                dest_path = os.path.join(output_folder, os.path.basename(random_dxf))
                shutil.copy2(random_dxf, dest_path)
                results[term] = {
                    "file_path": dest_path,
                    "matched_text": None  # 无精确匹配文字
                }
                print(f"无完全匹配编号，随机选择: {dest_path}")
            else:
                results[term] = {
                    "file_path": None,
                    "matched_text": None
                }

    return results
# ==================== Flask 路由 ====================
from dotenv import load_dotenv
@app.route('/find_dwg/<path:excel_path>/<path:folder_path>')
def find_dwg_api(excel_path, folder_path):
    column = request.args.get('column', 'B')
    sheet_name = request.args.get('sheet_name', None)
    output_folder = request.args.get('output_folder', 'dxf_output/pick_dxf')
    dotenv_path = r'E:\code\apikey\.env'
    load_dotenv(dotenv_path)

    excel_path= os.getenv('find_dwg_excel_path')
    folder_path = os.getenv('find_dwg_folder_path')
     
    results = find_dwg_files_by_excel_data(excel_path, folder_path, output_folder, column, sheet_name)
    return jsonify(results)

@app.route('/routes')
def show_routes():
    routes = []
    for rule in app.url_map.iter_rules():
        endpoint_func = app.view_functions.get(rule.endpoint)
        docstring = endpoint_func.__doc__.strip() if endpoint_func and endpoint_func.__doc__ else "无描述"
        routes.append({
            'endpoint': rule.endpoint,
            'methods': list(rule.methods),
            'rule': str(rule),
            'description': docstring
        })
    return jsonify({'routes': routes})

# ==================== 启动脚本 ====================

if __name__ == '__main__':
    import argparse
    parser = argparse.ArgumentParser()
    parser.add_argument('--port', type=int, default=5001, help='Port to run the server on')
    args = parser.parse_args()
    app.run(host='0.0.0.0', port=args.port, debug=True)