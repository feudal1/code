import os
from PyPDF2 import PdfMerger
from datetime import datetime
import logging
import subprocess
import sys
import typer
from typing import Optional, List
from pathlib import Path
from ptools import register_command

logging.getLogger('PyPDF2').setLevel(logging.ERROR)

@register_command('pdf', 'merge')
def merge(
    files: Optional[List[Path]] = typer.Argument(
        None,
        help="要合并的 PDF 文件路径列表（不指定则弹出文件选择对话框）",
        exists=True,
        file_okay=True,
        dir_okay=False,
        readable=True,
    ),
    output: Optional[Path] = typer.Option(
        None,
        "--output",
        "-o",
        help="输出文件路径（默认保存在第一个文件的目录下）"
    ),
    use_dialog: bool = typer.Option(
        False,
        "--dialog",
        "-d",
        help="强制使用文件选择对话框"
    )
):
    """合并 PDF 文件"""
    
    # 如果没有指定文件或者使用了 dialog 选项，弹出文件选择对话框
    if not files or use_dialog:
        import tkinter as tk
        from tkinter import filedialog
        
        root = tk.Tk()
        root.withdraw()
        
        file_paths = filedialog.askopenfilenames(
            title="选择要合并的 PDF 文件",
            filetypes=[("PDF files", "*.pdf"), ("All files", "*.*")]
        )
        
        root.destroy()
        
        if not file_paths:
            print("[黄色]提示：未选择任何文件！[/黄色]")
            raise typer.Exit(code=0)
        
        files = [Path(f) for f in file_paths]
    
    if len(files) < 2:
        print("[黄色]警告：请至少选择两个 PDF 文件进行合并！[/黄色]")
        raise typer.Exit(code=1)
    
    try:
        # 创建 PDF 合并对象
        merger = PdfMerger()
        
        # 添加所有选中的 PDF 文件
        for pdf_file in files:
            merger.append(str(pdf_file))
        
        # 生成时间戳文件名
        timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
        
        # 确定输出路径
        if output:
            output_path = output
        else:
            output_filename = f"merged_{timestamp}.pdf"
            first_file_dir = files[0].parent
            output_path = first_file_dir / output_filename
        
        # 保存合并后的 PDF
        with open(output_path, "wb") as output_file:
            merger.write(output_file)
        
        merger.close()
        
        if sys.platform == "win32":
            # 标准化路径
            safe_path = os.path.normpath(os.path.abspath(output_path))
            # 调用 start 命令打开文件
            subprocess.Popen(
                f'start "" "{safe_path}"',
                shell=True,
                stdin=subprocess.DEVNULL,
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                creationflags=(
                    subprocess.CREATE_NEW_PROCESS_GROUP |
                    subprocess.DETACHED_PROCESS |
                    0x01000000
                )
            )
        
        print(f"[绿色]✓ PDF 合并完成！[/绿色]")
        print(f"[蓝色]保存路径：{output_path}[/蓝色]")
    
    except Exception as e:
        print(f"[红色]错误：合并过程中出现错误：{str(e)}[/红色]")
        raise typer.Exit(code=1)

