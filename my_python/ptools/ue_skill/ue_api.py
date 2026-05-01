
# -*- coding: utf-8 -*-
import os
import subprocess
import time
import sys
import socket
import json
import shutil
from pathlib import Path
def log(message=""):
    """日志输出"""
    print(message)

def get_local_ip():
    if sys.platform != "win32":
        out = subprocess.check_output(['ip', 'route', 'show', 'default'], text=True)
        return out.split()[2]  # default via <GATEWAY> ...
    else :
        return "localhost"
def send_python_code_request(code=None, port=8070):
    """
    发送Python代码执行请求到UE服务器
    """
    try:
        host=get_local_ip()
       
        # 创建socket连接
        sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        sock.connect((host, port))

        # 准备数据
        payload = {
            "code": code or "unreal.SystemLibrary.print_string(None, 'Hello from remote execution!', True, True, unreal.LinearColor(0,1,0,1), 5.0)"
        }

        # 发送JSON数据
        json_data = json.dumps(payload)
        sock.send(json_data.encode('utf-8'))

        # 接收响应
        response = sock.recv(4096).decode('utf-8')
        log(f"服务器响应: {response}")

        sock.close()
        return {"status": "success", "result": response}

    except Exception as e:
        log(f"发送请求时出错: {str(e)}")
        return {"status": "error", "message": str(e)}
