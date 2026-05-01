"""
启动 LLM Web 服务 - Ollama 本地模型
"""
import sys
import os

# 添加项目根目录到路径
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from my_llm.llm_web import app

if __name__ == '__main__':
    # 获取本机 IP 地址
    import socket
    try:
        s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        s.connect(('8.8.8.8', 80))
        local_ip = s.getsockname()[0]
        s.close()
    except Exception:
        local_ip = '127.0.0.1'
    
    # 检查 Ollama 服务状态
    print("=" * 60)
    print("🔍 检查 Ollama 服务...")
    print("=" * 60)
    
    import http.client
    import json
    
    ollama_host = os.getenv("OLLAMA_HOST", "localhost:11434")
    model_name = os.getenv("OLLAMA_MODEL", "qwen3-vl:8b")
    
    try:
        if ':' in ollama_host:
            host_name, port = ollama_host.split(':')
            port = int(port)
        else:
            host_name = ollama_host
            port = 11434
        
        conn = http.client.HTTPConnection(host_name, port, timeout=5)
        conn.request("GET", "/api/tags")
        response = conn.getresponse()
        
        if response.status == 200:
            data = json.loads(response.read().decode('utf-8'))
            models = data.get('models', [])
            model_names = [m.get('name', '') for m in models]
            
            print(f"✅ Ollama 服务在线")
            print(f"📦 可用模型数量：{len(models)}")
            if models:
                print(f"📋 前 5 个模型：{', '.join(model_names[:5])}")
            
            if model_name not in model_names:
                print(f"⚠️  警告：指定的模型 {model_name} 不在列表中")
                print(f"💡 提示：使用 'ollama pull {model_name}' 下载模型")
        else:
            print(f"❌ Ollama 服务响应异常：HTTP {response.status}")
        
        conn.close()
    except Exception as e:
        print(f"❌ 无法连接到 Ollama 服务：{e}")
        print(f"💡 请确保 Ollama 正在运行：ollama serve")
        print(f"💡 或设置环境变量：set OLLAMA_HOST=your-host:port")
    
    print("=" * 60)
    print("🚀 LLM/VLM Web 服务已启动!")
    print("=" * 60)
    print(f"✅ 服务状态：运行中")
    print(f"🤖 使用模型：{model_name}")
    print(f"🏠 本地访问：http://localhost:5000")
    print(f"🌐 局域网访问：http://{local_ip}:5000")
    print(f"💡 iPad/手机访问：确保设备在同一 WiFi 网络，然后访问 http://{local_ip}:5000")
    print(f"📁 上传目录：{os.path.join(os.path.dirname(__file__), 'uploads')}")
    print("=" * 60)
    print("提示:")
    print("- 支持文本对话")
    print("- 支持图片上传分析 (VLM)")
    print("- 拖拽图片到上传区域即可")
    print("- 按 Ctrl+C 停止服务")
    print("=" * 60)
    
    # 启动服务（host='0.0.0.0'允许外部访问）
    app.run(host='0.0.0.0', port=5000, debug=True, use_reloader=False)
