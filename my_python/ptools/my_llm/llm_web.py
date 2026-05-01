"""
LLM/VLM Web 服务 - 支持 iPad 访问和图片上传
"""
from __future__ import annotations
import sys
import os
import json
import base64
from pathlib import Path
from datetime import datetime

# 添加项目根目录到路径
PROJECT_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
if PROJECT_ROOT not in sys.path:
    sys.path.insert(0, PROJECT_ROOT)

from flask import Flask, render_template_string, request, jsonify, send_from_directory
from werkzeug.utils import secure_filename
import http.client
from io import BytesIO
import base64

from file_memory import (
    load_shortterm_memory,
    save_shortterm_memory,
    clear_shortterm_memory,
    save_longterm_memory_log,
    get_sys_prompt_content,
)
# 已禁用工具搜索功能

# Ollama 配置
OLLAMA_HOST = os.getenv("OLLAMA_HOST", "localhost:11434")
DEFAULT_MODEL = os.getenv("OLLAMA_MODEL", "qwen3-vl:8b")

app = Flask(__name__)

# 配置
if 'PROJECT_ROOT' not in globals():
    PROJECT_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
UPLOAD_FOLDER = os.path.join(PROJECT_ROOT, 'ptools', 'my_llm', 'uploads')
ALLOWED_EXTENSIONS = {'png', 'jpg', 'jpeg', 'gif', 'webp'}
MAX_CONTENT_LENGTH = 16 * 1024 * 1024  # 16MB

app.config['UPLOAD_FOLDER'] = UPLOAD_FOLDER
app.config['MAX_CONTENT_LENGTH'] = MAX_CONTENT_LENGTH

# 确保上传目录存在
os.makedirs(UPLOAD_FOLDER, exist_ok=True)

# API Key（仅保留，不使用）
# DASHSCOPE_API_KEY = os.environ.get("DASHSCOPE_API_KEY", "")
# if not DASHSCOPE_API_KEY:
#     try:
#         from .api_key import DASHSCOPE_API_KEY
#     except ImportError:
#         pass

# Ollama 不需要 API Key
print(f"[INFO] 使用 Ollama 本地模型：{DEFAULT_MODEL}")
print(f"[INFO] Ollama 服务地址：{OLLAMA_HOST}")

# 模型（使用 Ollama 默认模型）
model = DEFAULT_MODEL


def allowed_file(filename):
    """检查文件扩展名是否允许"""
    return '.' in filename and filename.rsplit('.', 1)[1].lower() in ALLOWED_EXTENSIONS


def ollama_chat(messages, model=DEFAULT_MODEL, host=OLLAMA_HOST):
    """
    调用 Ollama API 进行对话
    """
    try:
        # 解析主机和端口
        if ':' in host:
            host_name, port = host.split(':')
            port = int(port)
        else:
            host_name = host
            port = 11434
        
        # 创建 HTTP 连接
        conn = http.client.HTTPConnection(host_name, port, timeout=120)
        
        # 构造请求体
        request_body = {
            "model": model,
            "messages": messages,
            "stream": False
        }
        body = json.dumps(request_body).encode('utf-8')
        
        # 发送 POST 请求
        headers = {
            "Content-Type": "application/json"
        }
        
        conn.request(
            "POST",
            "/api/chat",
            body=body,
            headers=headers
        )
        
        # 获取响应
        response = conn.getresponse()
        
        if response.status != 200:
            error_msg = response.read().decode('utf-8')
            raise RuntimeError(f"Ollama API 错误：{response.status} - {error_msg}")
        
        # 读取响应内容
        response_data = response.read().decode('utf-8')
        data = json.loads(response_data)
        content = data.get('message', {}).get('content', '')
        
        conn.close()
        
        return content
        
    except Exception as e:
        print(f"Ollama 调用失败：{e}")
        raise e


def encode_image_to_base64(image_path):
    """将图片编码为 base64"""
    with open(image_path, 'rb') as f:
        image_data = f.read()
        return base64.b64encode(image_data).decode('utf-8')


def vlm_chat_with_image(user_input, image_path=None):
    """
    使用 Ollama VLM 进行对话（支持图片）
    """
    try:
        # 构建消息
        message = {
            "role": "user",
            "content": user_input if user_input else "请分析这张图片"
        }
        
        # 如果有图片，添加到消息中
        if image_path and os.path.exists(image_path):
            try:
                image_base64 = encode_image_to_base64(image_path)
                message["images"] = [image_base64]
                print(f"[INFO] 已加载图片：{image_path}")
            except Exception as e:
                print(f"[警告] 图片加载失败：{e}，将继续使用纯文本模式")
        
        # 构建消息列表
        messages = [message]
        
        # 调用 Ollama API
        content = ollama_chat(messages=messages, model=model)
        
        return content
            
    except Exception as e:
        print(f"VLM 调用失败：{e}")
        raise e


# HTML 模板
HTML_TEMPLATE = """
<!DOCTYPE html>
<html lang="zh-CN">
<head>
    <meta charset="UTF-8">
    <meta name="viewport" content="width=device-width, initial-scale=1.0">
    <title>LLM/VLM 助手</title>
    <style>
        * {
            margin: 0;
            padding: 0;
            box-sizing: border-box;
        }
        
        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen, Ubuntu, Cantarell, sans-serif;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            min-height: 100vh;
            padding: 20px;
        }
        
        .container {
            max-width: 800px;
            margin: 0 auto;
            background: white;
            border-radius: 20px;
            box-shadow: 0 20px 60px rgba(0,0,0,0.3);
            overflow: hidden;
        }
        
        .header {
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            padding: 30px;
            text-align: center;
        }
        
        .header h1 {
            font-size: 2em;
            margin-bottom: 10px;
        }
        
        .header p {
            opacity: 0.9;
        }
        
        .chat-container {
            padding: 30px;
            min-height: 400px;
            max-height: 600px;
            overflow-y: auto;
        }
        
        .message {
            margin-bottom: 20px;
            display: flex;
            align-items: flex-start;
        }
        
        .message.user {
            justify-content: flex-end;
        }
        
        .message.assistant {
            justify-content: flex-start;
        }
        
        .message-content {
            max-width: 70%;
            padding: 15px 20px;
            border-radius: 15px;
            word-wrap: break-word;
        }
        
        /* Markdown 样式 */
        .message-content.markdown {
            line-height: 1.6;
        }
        
        .message-content.markdown p {
            margin-bottom: 0.8em;
        }
        
        .message-content.markdown p:last-child {
            margin-bottom: 0;
        }
        
        .message-content.markdown pre {
            background: #f6f8fa;
            padding: 12px;
            border-radius: 6px;
            overflow-x: auto;
            margin: 10px 0;
            font-family: 'Consolas', 'Monaco', monospace;
            font-size: 0.9em;
        }
        
        .message-content.markdown code {
            background: #f6f8fa;
            padding: 2px 6px;
            border-radius: 3px;
            font-family: 'Consolas', 'Monaco', monospace;
            font-size: 0.9em;
        }
        
        .message-content.markdown pre code {
            background: transparent;
            padding: 0;
        }
        
        .message-content.markdown ul,
        .message-content.markdown ol {
            padding-left: 20px;
            margin: 10px 0;
        }
        
        .message-content.markdown li {
            margin: 5px 0;
        }
        
        .message-content.markdown blockquote {
            border-left: 4px solid #667eea;
            padding-left: 15px;
            margin: 10px 0;
            color: #666;
        }
        
        .message-content.markdown strong {
            font-weight: bold;
            color: #764ba2;
        }
        
        .message-content.markdown h1,
        .message-content.markdown h2,
        .message-content.markdown h3 {
            margin: 15px 0 10px;
            color: #333;
        }
        
        .message-content.markdown h1 {
            font-size: 1.5em;
            border-bottom: 2px solid #eee;
            padding-bottom: 5px;
        }
        
        .message-content.markdown h2 {
            font-size: 1.3em;
        }
        
        .message-content.markdown h3 {
            font-size: 1.1em;
        }
        
        .user .message-content {
            background: #667eea;
            color: white;
            border-bottom-right-radius: 5px;
        }
        
        .assistant .message-content {
            background: #f0f0f0;
            color: #333;
            border-bottom-left-radius: 5px;
        }
        
        .message-image {
            max-width: 300px;
            max-height: 300px;
            border-radius: 10px;
            margin-bottom: 10px;
            display: block;
        }
        
        .input-container {
            padding: 20px;
            background: #f8f9fa;
            border-top: 1px solid #e9ecef;
        }
        
        .upload-area {
            border: 2px dashed #667eea;
            border-radius: 10px;
            padding: 20px;
            text-align: center;
            margin-bottom: 15px;
            cursor: pointer;
            transition: all 0.3s;
        }
        
        .upload-area:hover {
            background: #f0f0ff;
            border-color: #764ba2;
        }
        
        .upload-area.dragover {
            background: #e0e0ff;
            border-color: #764ba2;
        }
        
        .preview-image {
            max-width: 200px;
            max-height: 200px;
            border-radius: 10px;
            margin: 10px auto;
            display: none;
        }
        
        .input-group {
            display: flex;
            gap: 10px;
        }
        
        #userInput {
            flex: 1;
            padding: 15px;
            border: 2px solid #e0e0e0;
            border-radius: 10px;
            font-size: 16px;
            transition: border-color 0.3s;
        }
        
        #userInput:focus {
            outline: none;
            border-color: #667eea;
        }
        
        button {
            padding: 15px 30px;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            border: none;
            border-radius: 10px;
            font-size: 16px;
            cursor: pointer;
            transition: transform 0.2s;
        }
        
        button:hover {
            transform: translateY(-2px);
        }
        
        button:disabled {
            opacity: 0.5;
            cursor: not-allowed;
            transform: none;
        }
        
        .loading {
            display: none;
            text-align: center;
            padding: 20px;
        }
        
        .loading.show {
            display: block;
        }
        
        .spinner {
            border: 4px solid #f3f3f3;
            border-top: 4px solid #667eea;
            border-radius: 50%;
            width: 40px;
            height: 40px;
            animation: spin 1s linear infinite;
            margin: 0 auto 10px;
        }
        
        @keyframes spin {
            0% { transform: rotate(0deg); }
            100% { transform: rotate(360deg); }
        }
        
        .remove-btn {
            background: #ff4444;
            padding: 5px 10px;
            font-size: 12px;
            margin-top: 10px;
        }
        
        @media (max-width: 600px) {
            .message-content {
                max-width: 85%;
            }
            
            .input-group {
                flex-direction: column;
            }
            
            button {
                width: 100%;
            }
        }
    </style>
</head>
<body>
    <div class="container">
        <div class="header">
            <h1>🤖 LLM/VLM 助手</h1>
            <p>支持文本和图片输入的智能助手</p>
        </div>
        
        <div class="chat-container" id="chatContainer">
            <div class="message assistant">
                <div class="message-content">
                    你好！我是你的智能助手，可以回答你的问题或分析图片。有什么我可以帮你的吗？
                </div>
            </div>
        </div>
        
        <div class="loading" id="loading">
            <div class="spinner"></div>
            <p>思考中...</p>
        </div>
        
        <div class="input-container">
            <div class="upload-area" id="uploadArea">
                <p>📷 点击选择图片或拖拽图片到这里</p>
                <input type="file" id="fileInput" accept="image/*" style="display: none;">
                <img id="preview" class="preview-image" alt="预览">
                <button id="removeBtn" class="remove-btn" style="display: none;">移除图片</button>
            </div>
            
            <div class="input-group">
                <input 
                    type="text" 
                    id="userInput" 
                    placeholder="输入你的问题..." 
                    autocomplete="off"
                >
                <button id="sendBtn">发送</button>
            </div>
        </div>
    </div>
    
    <script src="https://cdn.jsdelivr.net/npm/marked/marked.min.js"></script>
    <script>
        const chatContainer = document.getElementById('chatContainer');
        const userInput = document.getElementById('userInput');
        const sendBtn = document.getElementById('sendBtn');
        const uploadArea = document.getElementById('uploadArea');
        const fileInput = document.getElementById('fileInput');
        const preview = document.getElementById('preview');
        const removeBtn = document.getElementById('removeBtn');
        const loading = document.getElementById('loading');
        
        let selectedFile = null;
        
        // 添加消息到聊天界面
        function addMessage(content, isUser, imageUrl = null) {
            const messageDiv = document.createElement('div');
            messageDiv.className = `message ${isUser ? 'user' : 'assistant'}`;
            
            let innerHTML = '';
            
            if (imageUrl) {
                innerHTML += `<img src="${imageUrl}" class="message-image" alt="上传的图片">`;
            }
            
            // 如果是 AI 回复，使用 Markdown 渲染；用户消息显示纯文本
            if (isUser) {
                innerHTML += `<div class="message-content">${content}</div>`;
            } else {
                innerHTML += `<div class="message-content markdown">${marked.parse(content)}</div>`;
            }
            
            messageDiv.innerHTML = innerHTML;
            chatContainer.appendChild(messageDiv);
            chatContainer.scrollTop = chatContainer.scrollHeight;
        }
        
        // 显示加载状态
        function showLoading(show) {
            if (show) {
                loading.classList.add('show');
                sendBtn.disabled = true;
            } else {
                loading.classList.remove('show');
                sendBtn.disabled = false;
            }
        }
        
        // 发送消息
        async function sendMessage() {
            const text = userInput.value.trim();
            
            if (!text && !selectedFile) return;
            
            // 显示用户消息
            if (selectedFile) {
                const reader = new FileReader();
                reader.onload = function(e) {
                    addMessage(text || '请分析这张图片', true, e.target.result);
                };
                reader.readAsDataURL(selectedFile);
            } else {
                addMessage(text, true);
            }
            
            userInput.value = '';
            showLoading(true);
            
            try {
                const formData = new FormData();
                formData.append('text', text);
                
                if (selectedFile) {
                    formData.append('image', selectedFile);
                }
                
                const response = await fetch('/chat', {
                    method: 'POST',
                    body: formData
                });
                
                const data = await response.json();
                
                if (data.success) {
                    addMessage(data.response, false);
                    
                    // 清除选中的文件
                    clearSelection();
                } else {
                    addMessage('错误：' + data.error, false);
                }
            } catch (error) {
                addMessage('请求失败：' + error.message, false);
            } finally {
                showLoading(false);
            }
        }
        
        // 清除文件选择
        function clearSelection() {
            selectedFile = null;
            fileInput.value = '';
            preview.style.display = 'none';
            removeBtn.style.display = 'none';
            uploadArea.querySelector('p').style.display = 'block';
        }
        
        // 事件监听
        sendBtn.addEventListener('click', sendMessage);
        
        userInput.addEventListener('keypress', function(e) {
            if (e.key === 'Enter') {
                sendMessage();
            }
        });
        
        uploadArea.addEventListener('click', function() {
            if (!selectedFile) {
                fileInput.click();
            }
        });
        
        fileInput.addEventListener('change', function(e) {
            const file = e.target.files[0];
            if (file) {
                handleFile(file);
            }
        });
        
        // 拖拽上传
        uploadArea.addEventListener('dragover', function(e) {
            e.preventDefault();
            uploadArea.classList.add('dragover');
        });
        
        uploadArea.addEventListener('dragleave', function() {
            uploadArea.classList.remove('dragover');
        });
        
        uploadArea.addEventListener('drop', function(e) {
            e.preventDefault();
            uploadArea.classList.remove('dragover');
            
            const file = e.dataTransfer.files[0];
            if (file && file.type.startsWith('image/')) {
                handleFile(file);
            }
        });
        
        // 处理文件
        function handleFile(file) {
            selectedFile = file;
            
            const reader = new FileReader();
            reader.onload = function(e) {
                preview.src = e.target.result;
                preview.style.display = 'block';
                removeBtn.style.display = 'block';
                uploadArea.querySelector('p').style.display = 'none';
            };
            reader.readAsDataURL(file);
        }
        
        removeBtn.addEventListener('click', clearSelection);
    </script>
</body>
</html>
"""


@app.route('/')
def index():
    """主页"""
    return render_template_string(HTML_TEMPLATE)


@app.route('/chat', methods=['POST'])
def chat():
    """处理聊天请求"""
    try:
        text = request.form.get('text', '')
        image_file = request.files.get('image')
        
        # 检查是否是清空短期记忆的命令
        if text.strip().lower() == 'clear':
            clear_shortterm_memory()
            return jsonify({
                'success': True,
                'response': '✅ 短期记忆已清空！长期记忆仍然保留。'
            })
        
        image_path = None
        
        # 处理上传的图片
        if image_file and image_file.filename:
            if not allowed_file(image_file.filename):
                return jsonify({
                    'success': False,
                    'error': '不支持的文件格式'
                })
            
            # 保存文件
            filename = secure_filename(image_file.filename)
            timestamp = datetime.now().strftime('%Y%m%d_%H%M%S')
            filename = f"{timestamp}_{filename}"
            image_path = os.path.join(app.config['UPLOAD_FOLDER'], filename)
            image_file.save(image_path)
        
        # 加载短期记忆
        messages = load_shortterm_memory()
        
        # 如果是第一次对话，添加 system prompt
        if len(messages) == 0:
            sys_prompt = {
                "role": "system",
                "content": get_sys_prompt_content()
            }
            messages.append(sys_prompt)
        
        # 构建用户消息
        user_message = {
            "role": "user",
            "content": text if text else "请分析这张图片"
        }
        
        # 如果有图片，添加到消息中
        if image_path and os.path.exists(image_path):
            try:
                image_base64 = encode_image_to_base64(image_path)
                user_message["images"] = [image_base64]
                print(f"[INFO] 已加载图片：{image_path}")
            except Exception as e:
                print(f"[警告] 图片加载失败：{e}，将继续使用纯文本模式")
        
        # 添加用户消息到短期记忆
        messages.append(user_message)
        
        # 保存到长期记忆
        longterm_content = f"User: {text}"
        if image_path:
            longterm_content += f" [图片：{os.path.basename(image_path)}]"
        save_longterm_memory_log(longterm_content)
        
        # 调用 Ollama API
        response_content = ollama_chat(messages=messages, model=model)
        
        # 添加 AI 回复到短期记忆
        assistant_message = {
            "role": "assistant",
            "content": response_content
        }
        messages.append(assistant_message)
        
        # 保存短期记忆
        save_shortterm_memory(messages)
        
        # 保存到长期记忆
        save_longterm_memory_log(f"Assistant: {response_content}")
        
        return jsonify({
            'success': True,
            'response': response_content
        })
        
    except Exception as e:
        return jsonify({
            'success': False,
            'error': str(e)
        })


@app.route('/uploads/<filename>')
def uploaded_file(filename):
    """提供上传文件的访问"""
    return send_from_directory(app.config['UPLOAD_FOLDER'], filename)


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
    
    print("=" * 50)
    print("🚀 LLM/VLM Web 服务已启动!")
    print("=" * 50)
    print(f"📱 本地访问：http://localhost:5000")
    print(f"🌐 局域网访问：http://{local_ip}:5000")
    print(f"💡 iPad 访问：在 Safari 中输入 http://{local_ip}:5000")
    print(f"📁 上传目录：{UPLOAD_FOLDER}")
    print("=" * 50)
    print("按 Ctrl+C 停止服务")
    print("=" * 50)
    
    # 启动服务（host='0.0.0.0'允许外部访问）
    app.run(host='0.0.0.0', port=5000, debug=False)
