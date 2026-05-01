#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
将 ptools.py 打包成单文件可执行程序
输出位置：my_python/ptools/bin/ptools.exe
"""
import os
import sys
import subprocess
from pathlib import Path

def build():
    # 当前脚本所在目录：my_python/ptools/make_exe
    current_dir = Path(__file__).parent.absolute()
    
    # ptools 目录：my_python/ptools
    ptools_dir = current_dir.parent
    
    # bin 目录：my_python/ptools/bin
    bin_dir = ptools_dir / "bin"
    
    # ptool.py 路径：my_python/ptools/ptool.py
    ptool_script = ptools_dir / "main.py"
    
    # 图标文件：my_python/ptools/make_exe/favicon.ico
    icon_file = current_dir / "favicon.ico"
    
    print("=" * 70)
    print("开始打包 main.py...")
    print("=" * 70)
    print(f"\n当前目录：{current_dir}")
    print(f"ptools 目录：{ptools_dir}")
    print(f"main.py: {ptool_script}")
    print(f"图标：{icon_file}")
    print(f"输出：{bin_dir}")
    
    # 检查文件是否存在
    if not ptool_script.exists():
        print(f"\n错误：找不到 ptool.py: {ptool_script}")
        sys.exit(1)
    
    if not icon_file.exists():
        print(f"\n错误：找不到图标文件：{icon_file}")
        sys.exit(1)
    
    # 确保 bin 目录存在
    bin_dir.mkdir(exist_ok=True)
    
    print("\n" + "-" * 70)
    
    # PyInstaller 命令
    cmd = [
        sys.executable,
        "-m", "PyInstaller",
        "--onefile",
        "--name", "ptool",
        "--icon", str(icon_file),
        "--distpath", str(bin_dir),
        "--workpath", str(ptools_dir / "build"),
        "--specpath", str(ptools_dir),
        "--clean",
        # 确保包含所有 ptools 子模块
        "--hidden-import", "ptools",
        "--hidden-import", "ptools.command_search",
        "--hidden-import", "ptools.cmd_wrappers",
        "--hidden-import", "ptools.cmd_wrappers.blender_commands",
        "--hidden-import", "ptools.cmd_wrappers.cad_commands",
        "--hidden-import", "ptools.cmd_wrappers.llm_commands",
        "--hidden-import", "ptools.cmd_wrappers.pdf_commands",
        "--hidden-import", "ptools.cmd_wrappers.ue_commands",
        "--collect-all", "ptools",
        str(ptool_script)
    ]
    
    print(f"\n执行命令：{' '.join(cmd)}")
    print("-" * 70)
    
    try:
        # 执行打包
        result = subprocess.run(cmd, check=True, capture_output=True, text=True)
        
        print("\n打包成功!")
        print(f"可执行文件位置：{bin_dir / 'ptool.exe'}")
        
        # 清理临时文件
        build_dir = ptools_dir / "build"
        if build_dir.exists():
            print("\n清理临时文件...")
            import shutil
            shutil.rmtree(build_dir)
        
        spec_file = ptools_dir / "ptool.spec"
        if spec_file.exists():
            spec_file.unlink()
            
        print("=" * 70)
        
    except subprocess.CalledProcessError as e:
        print(f"\n打包失败！错误信息:")
        print(e.stderr)
        sys.exit(1)
    except Exception as e:
        print(f"\n打包失败！错误：{e}")
        sys.exit(1)

if __name__ == "__main__":
    build()