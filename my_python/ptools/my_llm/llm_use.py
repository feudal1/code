"""
LLM 调用工具模块 - 简化版对话历史
"""
from __future__ import annotations
import sys
import os
import json
sys.path.append(os.getcwd())

import re
import subprocess
from datetime import datetime
from pathlib import Path

DASHSCOPE_API_KEY = os.environ.get("DASHSCOPE_API_KEY", "")  # type: ignore


from .file_memory import (
    save_messages_to_file,
    load_messages_from_file,
    save_longterm_memory_log,
    get_sys_prompt_content,
)
from ptools.command_search import search_registered_commands


PROJECT_ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

import http.client
import json
import typer
from ptools import register_command

model = os.environ.get("ali_vlm", "qwen3.5-flash")


import time

 
def llm_chat(userinput):
    """
    调用 LLM 进行对话（改进版历史管理）
    """
    try:
        if not DASHSCOPE_API_KEY:
            raise RuntimeError(
                "未配置 DASHSCOPE_API_KEY：请在 api_key.py 中提供 DASHSCOPE_API_KEY，"
                "或设置环境变量 DASHSCOPE_API_KEY"
            )

        # 从文件加载消息历史
        messages = load_messages_from_file()
        
        # 每次调用时动态添加 system prompt
        sys_prompt = get_sys_prompt_content()
        
        # 搜索相关命令并添加到 system prompt 中（复用 main.py 的搜索逻辑）
        try:
            results = search_registered_commands(
                userinput,
                threshold=0.3,
                top_k=3,
                include_csharp_export=True,
                project_root_for_csharp=PROJECT_ROOT,
            )
            print(f"搜索结果：{results}")
        except Exception:
            results = []
        if results:
            skills_context = "\n\n***以下是相关的可用命令参考:***\n"
            for r in results:
                skills_context += f"{r['name']}: {r['help']}\n"
                skills_context += f"用法：{r['usage']}\n\n"
            sys_prompt += skills_context
        
        messages_with_system = [{"role": "system", "content": sys_prompt}] + messages
        
        # 用户输入直接添加，不需要加搜索结果
        messages_with_system.append({"role": "user", "content": userinput})
        print(f"用户:{userinput}")
        
        # 调用 LLM（使用原生 http 模块）
        start_time = time.time()
        print(f"LLM 调用中...")
        
        # 准备请求数据
        request_data = {
            "model": model,
            "messages": messages_with_system
        }
        body = json.dumps(request_data).encode('utf-8')
        
        # 创建 HTTPS 连接
        conn = http.client.HTTPSConnection("dashscope.aliyuncs.com", timeout=60)
        
        try:
            conn.request(
                method="POST",
                url="/compatible-mode/v1/chat/completions",
                body=body,
                headers={
                    "Authorization": f"Bearer {DASHSCOPE_API_KEY}",
                    "Content-Type": "application/json"
                }
            )
            
            response = conn.getresponse()
            response_data = response.read().decode('utf-8')
            
            if response.status != 200:
                raise RuntimeError(f"API 请求失败：{response.status} - {response_data}")
            
            result = json.loads(response_data)
            content = result['choices'][0]['message']['content']
            
        finally:
            conn.close()
        
        end_time = time.time()
        elapsed_time_ms = (end_time - start_time) * 1000
        print(f"LLM 调用耗时：{elapsed_time_ms:.0f}毫秒")
        
        print(f"llm 返回：\n{content}")
        
        # 添加助手回复到消息历史（不保存 system）
        messages.append({"role": "assistant", "content": content})

        # 保存更新后的消息历史（不包含 system）
        save_messages_to_file(messages)
        
        
        # 执行 shell 命令并将结果捕获
        shell_results = execute_shell_and_capture_result(content) or ""

        second_result = ""
        if shell_results:
            shell_results = f"<observation>{shell_results}</observation>"
            second_result = llm_chat(shell_results) or ""

        result = content + "\n" + shell_results + "\n" + second_result
        save_longterm_memory_log( result)

        return result
            
    except Exception as e:
        print(f"调用 LLM 失败: {e}")
        return None


def execute_shell_and_capture_result(llm_response):
    """
    执行Shell命令并捕获结果用于上下文（合并版本）
    """
    if not llm_response:
        return ""

    # 直接从 LLM 回复中提取 ```cmd 代码块作为要执行的命令
    # 支持形如：
    # ```cmd
    # dir
    # ```
    pattern = r"```(?:cmd|bat|powershell)?\s*([\s\S]*?)```"
    matches = re.findall(pattern, llm_response, re.IGNORECASE)

    if not matches:
        return ""

    results = []
    for cmd in matches:
        # 取代码块中的第一条非空、非注释行作为要执行的命令
        lines = [l.strip() for l in cmd.splitlines()]
        cmd_line = ""
        for line in lines:
            if not line:
                continue
            if line.lstrip().startswith("#"):
                # 允许在代码块里用 # 注释示例，不执行
                continue
            cmd_line = line
            break

        clean_cmd = cmd_line.strip()
        if not clean_cmd or "del" in clean_cmd.lower():
            continue

        # 执行前让用户确认
        print(f"\n[待执行命令]: {clean_cmd}")
        try:
            confirm = input("\n是否执行此命令？(y/n): ").strip().lower()
            if confirm != 'y' and confirm != 'yes':
                print(f"[已跳过命令]: {clean_cmd}")
                continue
        except (EOFError, KeyboardInterrupt):
            print(f"\n[命令已取消]: {clean_cmd}")
            continue

        # 丢给 shell 统一处理
        result = subprocess.run(
            clean_cmd,
            shell=True,
            capture_output=True
        )
        # 尝试多种编码解码 stdout 和 stderr
        encodings = ['utf-8', 'gbk', 'gb2312', 'latin1']  # 常见编码列表
        decoded_stdout = None
        decoded_stderr = None

        # 解码 stdout
        for encoding in encodings:
            try:
                decoded_stdout = result.stdout.decode(encoding).strip()
                break
            except UnicodeDecodeError:
                continue
        if decoded_stdout is None:
            decoded_stdout = result.stdout.decode('utf-8', errors='ignore').strip()

        # 解码 stderr
        for encoding in encodings:
            try:
                decoded_stderr = result.stderr.decode(encoding).strip()
                break
            except UnicodeDecodeError:
                continue
        if decoded_stderr is None:
            decoded_stderr = result.stderr.decode('utf-8', errors='ignore').strip()

        # 构造结果字符串
        output = f"\n{decoded_stdout}"
        if decoded_stderr:
            output += f"\n{decoded_stderr}"

        results.append(output)

      

    return "\n\n".join(results) +"\n" if results else ""


@register_command("llm", "chat")
def llm_chat_cmd(
    text: str = typer.Argument(..., help="要发送给 LLM 的文本"),
):
    """与 LLM 对话（单轮输入，输出回复）"""
    reply = llm_chat(text)
    if reply is None:
        raise typer.Exit(code=1)
    print(reply)


@register_command("llm", "repl")
def llm_repl_cmd():
    """进入 LLM 交互模式（输入 exit/quit 退出）"""
    while True:
        try:
            user_text = input("你：").strip()
            if not user_text:
                continue
            if user_text.lower() in {"exit", "quit", "q"}:
                return
            reply = llm_chat(user_text)
            if reply is None:
                print("LLM 返回为空/失败")
        except (KeyboardInterrupt, EOFError):
            return
        except Exception as e:
            print("发生错误：", e)
if __name__ == "__main__":
     while True:
        try:
            user_text = input("你：").strip()
            if user_text:
                reply_text = llm_chat(user_text)
           
        except Exception as e:
            print("发生错误：", e)

