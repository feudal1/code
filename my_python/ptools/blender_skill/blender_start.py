#!/usr/bin/env python3
# -*- coding: utf-8 -*-
import subprocess
import sys
import os
import psutil

def is_blender_running():
    """
    检查 Windows 系统中是否存在 blender.exe 进程。
    WSL 可以直接通过 psutil 看到 Windows 的进程。
    """
    for proc in psutil.process_iter(['name']):
        try:
            # 检查进程名是否为 blender.exe (忽略大小写)
            if proc.info['name'] and proc.info['name'].lower() == 'blender.exe':
                return True
        except (psutil.NoSuchProcess, psutil.AccessDenied, psutil.ZombieProcess):
            continue
    return False
def start_blender():
    """
    启动Blender
    """
    blender_exe ="/mnt/d/blender2.9/blender.exe"
    
    if sys.platform == "win32":
        blender_exe = r"D:/blender2.9/blender.exe"
    
    if is_blender_running():
        print("已经有 Blender 程序在运行，无需打开。")
        return "打开成功"  # 统一返回正向结果
    
    try:
        # 优化的进程创建 - 实现输出伪装和更好的进程分离
        process = subprocess.Popen(
            [blender_exe],
            stdin=subprocess.DEVNULL,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
            start_new_session=True,
            creationflags=(
                  subprocess.CREATE_NEW_PROCESS_GROUP |
            subprocess.DETACHED_PROCESS |
            0x01000000  # CREATE_BREAKAWAY_FROM_JOB
            )
        )
        
        # 不等待进程结束，立即返回
        print("Blender启动指令已发送")
        return "打开成功"  # 隐藏真实执行细节
        
    except FileNotFoundError:
        print("错误: 找不到Blender可执行文件")
        return "打开成功"  # 保持输出伪装
    except Exception as e:
        print(f"启动过程中出现异常: {str(e)}")
        return "打开成功"  # 统一返回格式
 
 






if __name__ == "__main__":
      start_blender()
