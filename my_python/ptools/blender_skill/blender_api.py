import requests
import socket
import json
import subprocess
import sys

def get_local_ip():
    if sys.platform != "win32":
        out = subprocess.check_output(['ip', 'route', 'show', 'default'], text=True)
        return out.split()[2]  # default via <GATEWAY> ...
    else :
        return "localhost"
     
host = get_local_ip()
BASE_URL = f"http://{host}:3000"

def _send_code_to_exec(code: str, **kwargs):
    """发送代码到exec接口执行"""
    params = {
        "code": code,
        "params": kwargs
    }
    
    url = f"{BASE_URL}/exec"
    print(f"Sending code to exec")
    print(f"Code length: {len(code)} chars")
    

    response = requests.post(url, json=params, timeout=30)
    response.raise_for_status()
    print(f"Response: {response.json()}")
    return response.json()


# ==================== 对外暴露的API函数 ====================




