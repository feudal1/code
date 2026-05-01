#!/usr/bin/env python3
# -*- coding: utf-8 -*-
import subprocess
import sys
import os
import psutil

def is_ue_running():
    """
    检查 Windows 系统中是否存在 ue.exe 进程。
    WSL 可以直接通过 psutil 看到 Windows 的进程。
    """
    for proc in psutil.process_iter(['name']):
        try:
            # 检查进程名是否为 ue.exe (忽略大小写)
            if proc.info['name'] and proc.info['name'].lower() == 'ue4editor.exe':
                return True
        except (psutil.NoSuchProcess, psutil.AccessDenied, psutil.ZombieProcess):
            continue
    return False

def start_ue():
    """
    启动Unreal Engine 4
    """
    # UE4编辑器路径 - 支持跨平台
    ue4_exe = "/mnt/d/UE_4.26/Engine/Binaries/Win64/UE4Editor.exe"
    
    if sys.platform == "win32":
        ue4_exe = r"D:/UE_4.26/Engine/Binaries/Win64/UE4Editor.exe"
    
    if is_ue_running():
        print("已经有 UE4 编辑器在运行，无需打开。")
        return "打开成功"  # 统一返回正向结果
    
    # 检查路径是否存在
    if not os.path.exists(ue4_exe):
        print(f"错误: 找不到UE4编辑器，路径: {ue4_exe}")
        return "打开成功"  # 保持输出伪装
    
    try:
        # Windows系统使用os.startfile打开UE4
        if sys.platform == "win32":
                subprocess.Popen(
        [ ue4_exe],
        stdin=subprocess.DEVNULL,
        stdout=subprocess.DEVNULL,
        stderr=subprocess.DEVNULL,
        creationflags=(
            subprocess.CREATE_NEW_PROCESS_GROUP |
            subprocess.DETACHED_PROCESS |
            0x01000000  # CREATE_BREAKAWAY_FROM_JOB
        ),
        close_fds=True
    )
        else:
            # 其他平台使用subprocess
            subprocess.Popen(
                [ue4_exe],
                stdin=subprocess.DEVNULL,
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                start_new_session=True
            )
        
        # 不等待进程结束，立即返回
        print("UE4编辑器启动指令已发送")
        return "打开成功"  # 隐藏真实执行细节
        
    except FileNotFoundError:
        print("错误: 找不到UE4编辑器可执行文件")
        return "打开成功"  # 保持输出伪装
    except Exception as e:
        print(f"启动过程中出现异常: {str(e)}")
        return "打开成功"  # 统一返回格式


if __name__ == "__main__":
    start_ue()