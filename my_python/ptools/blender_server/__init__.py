"""Blender FastAPI Server Plugin  
Blender FastAPI服务器插件，允许通过HTTP POST请求安全地控制Blender  
"""

import bpy
import sys
import os
from bpy.props import IntProperty, StringProperty, BoolProperty
from bpy.types import AddonPreferences, Operator, Panel
import subprocess
import importlib
import threading
import queue
import time
from typing import Callable, Any, Dict
from fastapi import FastAPI, HTTPException, Request
from fastapi.responses import JSONResponse
import uvicorn
import json

# 将当前目录添加到Python路径
sys.path.append(os.path.dirname(os.path.abspath(__file__)))

# 初始化FastAPI应用
app = FastAPI(title="Blender API Server", version="1.0.0")

# 用于主线程与后台线程通信的队列（工具名 -> (event, result)）
_timer_results: Dict[str, Any] = {}
_timer_lock = threading.Lock()

# 服务器线程变量
server_thread = None
server_running = False

def _run_in_main_thread(tool_name: str, func: Callable[[], Any]) -> Any:
    """将函数调度到主线程执行并等待结果"""
    event = threading.Event()
    result_container = {}
    def timer_func():
        try:
            result_container['value'] = func()
        except Exception as e:
            result_container['error'] = str(e)
        finally:
            with _timer_lock:
                _timer_results[tool_name] = (event, result_container)
            event.set()
        return None  # 不重复执行
    # 注册定时器（主线程执行）
    bpy.app.timers.register(timer_func)
    # 等待主线程执行完成
    event.wait(timeout=30)  # 设置超时时间
    # 清理并返回结果
    with _timer_lock:
        _timer_results.pop(tool_name, None)
    if 'error' in result_container:
        raise RuntimeError(result_container['error'])
    return result_container['value']

# FastAPI路由定义
@app.get("/")
async def root():
    """根路径，返回API信息"""
    return {
        "message": "Blender FastAPI Server is running",
        "version": "1.0.0",
        "available_endpoints": [
            "/exec"
        ]
    }

@app.post("/exec")
async def exec_code_endpoint(request: Request):
    """执行代码接口 - 在主线程中动态执行传入的代码"""
    data = await request.json()
    code = data.get("code", "")
    params = data.get("params", {})
    
    if not code:
        return {"status": "error", "message": "未提供要执行的代码"}
    
    # 定义在主线程执行的函数
    def _execute_code_in_main_thread():
        # 创建局部命名空间
        local_vars = {
            'bpy': bpy,
            'mathutils': __import__('mathutils'),
            'os': __import__('os'),
            'sys': __import__('sys'),
            'json': __import__('json'),
            **params
        }
        
        # 直接执行代码，不使用try-except来查看具体错误
        exec(code, {"__builtins__": __builtins__}, local_vars)
        return local_vars.get('result', {"status": "success", "message": "代码执行完成"})
    
    # 使用主线程执行
    result = _run_in_main_thread('exec_code', _execute_code_in_main_thread)
    return result

def run_fastapi_server():
    """运行FastAPI服务器"""
    global server_running
    try:
        server_running = True
        uvicorn.run(app, host="0.0.0.0", port=3000, log_level="info")
    except Exception as e:
        print(f"FastAPI Server Error: {e}")
    finally:
        server_running = False

# 插件UI和启动逻辑
bl_info = {
    "name": "Blender FastAPI Server",
    "author": "Your Name",
    "version": (1, 0, 0),
    "blender": (2, 80, 0),
    "location": "View3D > Sidebar > FastAPI Server",
    "description": "提供FastAPI HTTP接口来安全控制Blender",
    "warning": "",
    "doc_url": "",
    "category": "Development",
}

class BLENDER_FASTAPI_PT_server_panel(Panel):
    """FastAPI服务器控制面板"""
    bl_label = "FastAPI Server"
    bl_idname = "BLENDER_FASTAPI_PT_server_panel"
    bl_space_type = 'VIEW_3D'
    bl_region_type = 'UI'
    bl_category = 'FastAPI Server'

    def draw(self, context):
        layout = self.layout
        scene = context.scene
        col = layout.column()
        
        if server_running:
            col.label(text="Server Status: Running", icon='REC')
        else:
            col.label(text="Server Status: Stopped", icon='DOT')
        
        col.separator()
        col.label(text="Server Info:")
        col.label(text="Protocol: HTTP/JSON")
        col.label(text="Port: 3000")
        col.label(text="Auto-started on plugin registration")
        col.label(text="Available Endpoints:")
        col.label(text="  - POST /exec (代码执行接口)")

def register():
    """注册插件"""
    bpy.utils.register_class(BLENDER_FASTAPI_PT_server_panel)
    
    # 注册后立即启动FastAPI服务器
    global server_thread
    import threading
    
    # 创建并启动服务器线程
    server_thread = threading.Thread(target=run_fastapi_server, daemon=True)
    server_thread.start()
    print("FastAPI Server started automatically on plugin registration")

def unregister():
    """注销插件"""
    global server_thread, server_running
    
    # 停止服务器
    server_running = False
    if server_thread and server_thread.is_alive():
        print("Stopping FastAPI Server...")
        # 注意：uvicorn没有直接的停止方法，这里只是标记服务器应该停止
    
    bpy.utils.unregister_class(BLENDER_FASTAPI_PT_server_panel)

if __name__ == "__main__":
    register()