# dwg_to_dxf_multi_gui.py
import os
import subprocess
import time
import logging
import sys
import tkinter as tk
from tkinter import filedialog, messagebox

# 配置日志
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(levelname)s - %(message)s',
    handlers=[
        logging.StreamHandler()
    ]
)

logger = logging.getLogger(__name__)

def is_valid_dxf(dxf_path):
    """
    检查DXF文件是否有效
    
    Args:
        dxf_path (str): DXF文件路径
        
    Returns:
        bool: 文件是否有效
    """
    try:
        import ezdxf
        doc = ezdxf.readfile(dxf_path)
        return True
    except Exception as e:
        logger.info(f"DXF文件校验失败: {e}")
        return False

def convert_dwg_to_dxf(dwg_file_path):
    """
    使用ODA File Converter将DWG文件转换为DXF文件
    
    Args:
        dwg_file_path (str): DWG文件路径
        
    Returns:
        dict: 转换结果，包含状态和DXF文件路径或错误信息
    """
    start_time = time.time()
    try:
        if not os.path.exists(dwg_file_path):
            return {
                "status": "error",
                "message": f"DWG文件不存在: {dwg_file_path}"
            }

        # 确保输入文件是DWG格式
        if not dwg_file_path.lower().endswith('.dwg'):
            return {
                "status": "error",
                "message": f"输入文件不是DWG格式: {dwg_file_path}"
            }

        dwg_dir = os.path.dirname(dwg_file_path)
        dwg_filename = os.path.basename(dwg_file_path)
        dxf_filename = dwg_filename.replace('.dwg', '.dxf').replace('.DWG', '.dxf')
        dxf_file_path = os.path.join(dwg_dir, dxf_filename)

        output_dir = dwg_dir
        os.makedirs(output_dir, exist_ok=True)

        TEIGHA_PATH = "ODAFileConverter"
        INPUT_FOLDER = dwg_dir
        OUTPUT_FOLDER = output_dir
        OUTVER = "ACAD2018"
        OUTFORMAT = "DXF"
        RECURSIVE = "0"
        AUDIT = "1"
        INPUTFILTER = dwg_filename  # 只处理指定的文件

        cmd = [
            TEIGHA_PATH,
            INPUT_FOLDER,
            OUTPUT_FOLDER,
            OUTVER,
            OUTFORMAT,
            RECURSIVE,
            AUDIT,
            INPUTFILTER
        ]

        logger.info(f"执行命令: {' '.join(cmd)}")

        result = subprocess.run(cmd, shell=True, capture_output=True, text=True, timeout=120)

        if result.returncode == 0:
            if os.path.exists(dxf_file_path) and is_valid_dxf(dxf_file_path):
                response = {
                    "status": "success",
                    "message": f"DWG文件已成功转换为DXF: {dxf_file_path}",
                    "dxf_path": dxf_file_path
                }
                response["processing_time"] = time.time() - start_time
                return response
            else:
                response = {
                    "status": "error",
                    "message": f"生成的DXF文件无效或未创建: {dxf_file_path}"
                }
                response["processing_time"] = time.time() - start_time
                return response
        else:
            response = {
                "status": "error",
                "message": f"ODA转换失败: {result.stderr}"
            }
            response["processing_time"] = time.time() - start_time
            return response

    except subprocess.TimeoutExpired:
        response = {
            "status": "error",
            "message": "转换超时"
        }
        response["processing_time"] = time.time() - start_time
        return response
    except FileNotFoundError:
        response = {
            "status": "error",
            "message": "未找到ODAFileConverter命令，请确保已安装并加入PATH"
        }
        response["processing_time"] = time.time() - start_time
        return response
    except Exception as e:
        response = {
            "status": "error",
            "message": f"转换过程中发生错误: {str(e)}"
        }
        response["processing_time"] = time.time() - start_time
        return response

def select_and_convert_multiple():
    """
    弹出文件选择对话框并转换选定的多个DWG文件
    """
    # 创建根窗口但隐藏它
    root = tk.Tk()
    root.withdraw()
    
    # 弹出多文件选择对话框
    dwg_file_paths = filedialog.askopenfilenames(
        title="选择DWG文件（可多选）",
        filetypes=[("DWG文件", "*.dwg"), ("所有文件", "*.*")]
    )
    
    # 如果用户取消选择，退出程序
    if not dwg_file_paths:
        print("未选择文件，程序退出")
        sys.exit(0)
    
    # 转换统计信息
    total_files = len(dwg_file_paths)
    success_count = 0
    failed_files = []
    
    print(f"开始转换 {total_files} 个DWG文件...")
    
    # 逐个转换文件
    for i, dwg_file_path in enumerate(dwg_file_paths, 1):
        print(f"\n[{i}/{total_files}] 正在转换: {os.path.basename(dwg_file_path)}")
        
        # 检查文件是否存在
        if not os.path.exists(dwg_file_path):
            error_msg = f"文件不存在: {dwg_file_path}"
            logger.error(error_msg)
            failed_files.append((dwg_file_path, error_msg))
            continue
            
        # 检查文件扩展名
        if not dwg_file_path.lower().endswith('.dwg'):
            error_msg = f"文件不是DWG格式: {dwg_file_path}"
            logger.error(error_msg)
            failed_files.append((dwg_file_path, error_msg))
            continue
        
        # 执行转换
        result = convert_dwg_to_dxf(dwg_file_path)
        
        if result["status"] == "success":
            logger.info(result["message"])
            success_count += 1
            print(f"  ✓ 转换成功: {os.path.basename(result['dxf_path'])}")
        else:
            logger.error(result["message"])
            failed_files.append((dwg_file_path, result["message"]))
            print(f"  ✗ 转换失败: {result['message']}")
    
    # 显示转换结果总结
    summary_msg = f"转换完成!\n\n成功: {success_count}/{total_files}"
    
    if failed_files:
        summary_msg += f"\n失败: {len(failed_files)}/{total_files}"
        summary_msg += "\n\n失败文件:"
        for file_path, error in failed_files[:5]:  # 只显示前5个错误
            summary_msg += f"\n- {os.path.basename(file_path)}: {error}"
        if len(failed_files) > 5:
            summary_msg += f"\n... 还有 {len(failed_files) - 5} 个文件"
    
    # 显示结果对话框
    if success_count == total_files:
        messagebox.showinfo("转换完成", summary_msg)
    else:
        messagebox.showwarning("转换完成（部分失败）", summary_msg)
    
    print(f"\n{summary_msg}")
    return success_count, total_files, failed_files

def main():
    select_and_convert_multiple()

if __name__ == "__main__":
    main()