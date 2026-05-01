"""
Ollama LLM/VLM 服务 - 本地模型支持
"""
import base64
import io
from PIL import Image
import http.client
import json
import os
from typing import Optional, List, Dict, Any

# Ollama 默认配置
OLLAMA_HOST = os.getenv("OLLAMA_HOST", "localhost:11434")
DEFAULT_MODEL = os.getenv("OLLAMA_MODEL", "qwen3-vl:8b")


def encode_image_to_base64(image_path: str) -> str:
    """
    将图片文件编码为 base64 字符串
    
    Args:
        image_path: 图片文件路径
        
    Returns:
        base64 编码的图片字符串
    """
    try:
        with open(image_path, 'rb') as f:
            image_data = f.read()
            return base64.b64encode(image_data).decode('utf-8')
    except Exception as e:
        raise Exception(f"图片编码失败：{str(e)}")


def ollama_chat(
    messages: List[Dict[str, Any]],
    model: str = DEFAULT_MODEL,
    host: str = OLLAMA_HOST,
    stream: bool = False
) -> Dict[str, Any]:
    """
    调用 Ollama API 进行对话
    
    Args:
        messages: 消息列表，格式如：
                  [{"role": "user", "content": "你好"}]
                  或包含图片：
                  [{"role": "user", "content": "描述图片", "images": ["base64_string"]}]
        model: 模型名称，默认 qwen-vl:4b
        host: Ollama 服务地址，默认 localhost:11434
        stream: 是否流式输出
        
    Returns:
        包含响应的字典
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
            "stream": stream
        }
        
        # 发送 POST 请求
        headers = {
            "Content-Type": "application/json"
        }
        
        conn.request(
            "POST",
            "/api/chat",
            body=json.dumps(request_body),
            headers=headers
        )
        
        # 获取响应
        response = conn.getresponse()
        
        if response.status != 200:
            error_msg = response.read().decode('utf-8')
            raise Exception(f"Ollama API 错误：{response.status} - {error_msg}")
        
        # 读取响应内容
        response_data = response.read().decode('utf-8')
        
        if stream:
            # 流式响应处理
            result_lines = response_data.strip().split('\n')
            responses = []
            for line in result_lines:
                if line.strip():
                    try:
                        data = json.loads(line)
                        if 'message' in data and 'content' in data['message']:
                            responses.append(data['message']['content'])
                    except json.JSONDecodeError:
                        continue
            final_response = ''.join(responses)
        else:
            # 普通响应
            data = json.loads(response_data)
            final_response = data.get('message', {}).get('content', '')
        
        conn.close()
        
        return {
            "success": True,
            "content": final_response,
            "model": model,
            "raw_response": response_data
        }
        
    except Exception as e:
        return {
            "success": False,
            "error": str(e)
        }


def ollama_vlm_chat(
    user_input: str,
    image_path: Optional[str] = None,
    model: str = DEFAULT_MODEL,
    host: str = OLLAMA_HOST,
    system_prompt: Optional[str] = None
) -> Dict[str, Any]:
    """
    使用 Ollama VLM 进行多模态对话（支持图片）
    
    Args:
        user_input: 用户输入的文本
        image_path: 图片文件路径（可选）
        model: 模型名称，默认 qwen-vl:4b
        host: Ollama 服务地址
        system_prompt: 系统提示词（可选）
        
    Returns:
        包含响应的字典
    """
    try:
        # 构建消息
        message = {
            "role": "user",
            "content": user_input
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
        messages = []
        
        # 如果有 system prompt，添加到系统消息
        if system_prompt:
            messages.append({
                "role": "system",
                "content": system_prompt
            })
        
        messages.append(message)
        
        # 调用 Ollama API
        result = ollama_chat(messages=messages, model=model, host=host)
        
        return result
        
    except Exception as e:
        return {
            "success": False,
            "error": str(e)
        }


def ollama_text_chat(
    user_input: str,
    model: str = DEFAULT_MODEL,
    host: str = OLLAMA_HOST,
    system_prompt: Optional[str] = None,
    conversation_history: Optional[List[Dict[str, str]]] = None
) -> Dict[str, Any]:
    """
    使用 Ollama 进行纯文本对话
    
    Args:
        user_input: 用户输入的文本
        model: 模型名称
        host: Ollama 服务地址
        system_prompt: 系统提示词
        conversation_history: 对话历史（可选）
        
    Returns:
        包含响应的字典
    """
    try:
        # 构建消息列表
        messages = []
        
        # 添加 system prompt
        if system_prompt:
            messages.append({
                "role": "system",
                "content": system_prompt
            })
        
        # 添加对话历史
        if conversation_history:
            messages.extend(conversation_history)
        
        # 添加当前用户输入
        messages.append({
            "role": "user",
            "content": user_input
        })
        
        # 调用 Ollama API
        result = ollama_chat(messages=messages, model=model, host=host)
        
        return result
        
    except Exception as e:
        return {
            "success": False,
            "error": str(e)
        }


def check_ollama_status(host: str = OLLAMA_HOST) -> Dict[str, Any]:
    """
    检查 Ollama 服务状态
    
    Args:
        host: Ollama 服务地址
        
    Returns:
        包含服务状态的字典
    """
    try:
        if ':' in host:
            host_name, port = host.split(':')
            port = int(port)
        else:
            host_name = host
            port = 11434
        
        conn = http.client.HTTPConnection(host_name, port, timeout=5)
        conn.request("GET", "/api/tags")
        response = conn.getresponse()
        
        if response.status == 200:
            data = json.loads(response.read().decode('utf-8'))
            models = data.get('models', [])
            conn.close()
            
            return {
                "status": "online",
                "models": [m.get('name', '') for m in models],
                "model_count": len(models)
            }
        else:
            conn.close()
            return {
                "status": "error",
                "error": f"HTTP {response.status}"
            }
            
    except Exception as e:
        return {
            "status": "offline",
            "error": str(e)
        }


def list_available_models(host: str = OLLAMA_HOST) -> List[str]:
    """
    列出 Ollama 可用的模型
    
    Args:
        host: Ollama 服务地址
        
    Returns:
        模型名称列表
    """
    status = check_ollama_status(host)
    if status.get('status') == 'online':
        return status.get('models', [])
    return []


# 测试函数
if __name__ == "__main__":
    print("=" * 60)
    print("Ollama LLM/VLM 服务测试")
    print("=" * 60)
    
    # 检查服务状态
    print("\n[1] 检查 Ollama 服务状态...")
    status = check_ollama_status()
    print(f"状态：{status.get('status')}")
    if status.get('status') == 'online':
        print(f"可用模型数量：{status.get('model_count')}")
        print(f"可用模型：{', '.join(status.get('models', [])[:5])}")  # 显示前 5 个
    else:
        print(f"错误：{status.get('error')}")
        print("\n请确保 Ollama 服务正在运行！")
        print("启动命令：ollama serve")
    
    # 测试纯文本对话
    print("\n" + "=" * 60)
    print("[2] 测试纯文本对话...")
    print("=" * 60)
    result = ollama_text_chat(
        user_input="你好，请用一句话介绍你自己",
        model=DEFAULT_MODEL
    )
    if result.get('success'):
        print(f"回复：{result.get('content', '')}")
    else:
        print(f"失败：{result.get('error', '')}")
    
    # 测试图片对话（如果有测试图片）
    test_image = "test.jpg"
    if os.path.exists(test_image):
        print("\n" + "=" * 60)
        print(f"[3] 测试图片分析 (图片：{test_image})...")
        print("=" * 60)
        result = ollama_vlm_chat(
            user_input="请描述这张图片",
            image_path=test_image,
            model=DEFAULT_MODEL
        )
        if result.get('success'):
            print(f"回复：{result.get('content', '')}")
        else:
            print(f"失败：{result.get('error', '')}")
    else:
        print(f"\n[跳过] 未找到测试图片：{test_image}")
    
    print("\n" + "=" * 60)
    print("测试完成")
    print("=" * 60)
