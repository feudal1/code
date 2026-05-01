"""
异步消息处理器 - 从 InfinityFree 获取待处理消息并处理
用户主动触发，非轮询模式
"""
import requests
import json
import time
from datetime import datetime
import os
import sys

# 添加项目路径
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from my_llm.ollama_service import ollama_chat
from my_llm.file_memory import load_shortterm_memory, save_shortterm_memory, get_sys_prompt_content

# InfinityFree 网站配置
# ⚠️ 重要：必须修改为你的实际网站地址
# 示例：BASE_URL = "https://feudal.fwh.is"
BASE_URL = os.getenv("INFINITYFREE_URL", "https://feudal.fwh.is")


class AsyncMessageProcessor:
    """异步消息处理器（手动触发模式）"""
    
    def __init__(self):
        self.session = requests.Session()
        self.processed_count = 0
        self.error_count = 0
        # 设置请求头，模拟浏览器
        self.session.headers.update({
            'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/146.0.0.0 Safari/537.36 Edg/146.0.0.0',
            'Accept': 'text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7',
            'Accept-Language': 'zh-CN,zh;q=0.9,en;q=0.8,en-GB;q=0.7,en-US;q=0.6',
            'Accept-Encoding': 'gzip, deflate, br, zstd',
            'Connection': 'keep-alive',
            'Cookie': '__test=735c6a8f57d7eb243fb7e2f2879f1fd6',
        })
        
        # 加载保存的 Cookie（如果存在）
        self._load_cookie()
    
    def _load_cookie(self):
        """加载保存的 Cookie"""
        try:
            cookie_file = os.path.join(os.path.dirname(__file__), 'infinityfree_cookie.json')
            if os.path.exists(cookie_file):
                with open(cookie_file, 'r', encoding='utf-8') as f:
                    data = json.load(f)
                    cookie_string = data.get('cookie', '')
                    if cookie_string:
                        # 设置 Cookie
                        self.session.headers['Cookie'] = cookie_string
                        print(f"✅ 已加载 Cookie ({len(cookie_string)} 字符)")
                    else:
                        print("⚠️ Cookie 文件为空")
            else:
                print("⚠️ 未找到 Cookie 文件，可能需要先获取 Cookie")
        except Exception as e:
            print(f"⚠️ 加载 Cookie 失败: {e}")
        
    def get_pending_messages(self, limit=50):
        """获取待处理的消息"""
        try:
            url = f"{BASE_URL}/get_pending_messages.php?limit={limit}"
            print(f"📡 正在获取待处理消息...")
            print(f"   URL: {url}")
            
            response = self.session.get(url, timeout=30)
            print(f"   状态码: {response.status_code}")
            print(f"   响应长度: {len(response.text)} 字符")
            
            # 检查响应内容
            if len(response.text) < 100:
                print(f"   响应内容: {response.text[:200]}")
            
            # 检查是否被保护机制拦截
            if 'aes.js' in response.text or '__test' in response.text or 'Javascript' in response.text:
                print(f"⚠️ 检测到 JavaScript 保护机制")
                print(f"   可能需要先通过浏览器访问网站以获取 Cookie")
                return []
            
            response.raise_for_status()
            result = response.json()
            
            if result.get('success'):
                messages = result.get('messages', [])
                count = result.get('count', 0)
                print(f"✅ 获取成功，共 {count} 条待处理消息")
                return messages
            else:
                print(f"❌ 获取消息失败：{result.get('error')}")
                return []
                
        except requests.exceptions.JSONDecodeError as e:
            print(f"❌ JSON 解析错误：{e}")
            print(f"   响应内容前200字符: {response.text[:200] if 'response' in locals() else 'N/A'}")
            return []
        except Exception as e:
            print(f"❌ 网络错误：{e}")
            import traceback
            traceback.print_exc()
            return []
    
    def process_message(self, message):
        """处理单条消息"""
        try:
            msg_id = message['id']
            user_id = message['user_id']
            text = message['message']
            image_base64 = message.get('image')
            
            print(f"\n{'='*60}")
            print(f"📨 处理消息 ID: {msg_id}, 用户：{user_id}")
            print(f"📝 内容：{text[:100]}{'...' if len(text) > 100 else ''}")
            
            # 标记为处理中
            self.update_message_status(msg_id, 'processing')
            
            # 检查是否是 clear 命令
            text_stripped = text.strip().lower()
            print(f"   调试：原始文本='{text}', 处理后='{text_stripped}'")
            
            if text_stripped == 'clear':
                print("🧹 检测到 clear 命令，清空短期记忆")
                from my_llm.file_memory import clear_shortterm_memory
                clear_shortterm_memory()
                
                response_text = "✅ 短期记忆已清空喵～人家重新开始和主人聊天啦！(≧∇≦) ﾉ"
                
                # 提交回复
                success = self.submit_response(msg_id, response_text)
                
                if success:
                    self.processed_count += 1
                    print(f"✅ clear 命令处理完成")
                    return True
                else:
                    print(f"❌ 提交回复失败")
                    self.update_message_status(msg_id, 'failed')
                    return False
            
            # 加载对话历史（限制最大长度，避免上下文爆炸）
            all_messages = load_shortterm_memory()
            
            # 如果是第一次，添加 system prompt
            if len(all_messages) == 0:
                messages = [{
                    "role": "system",
                    "content": get_sys_prompt_content()
                }]
            else:
                # 只保留最近的 10 条对话（5 轮），避免上下文过长
                # system prompt + 最近的消息
                messages = [all_messages[0]]  # system prompt
                messages.extend(all_messages[-10:])  # 最近 10 条
            
            # 构建用户消息
            user_message = {
                "role": "user",
                "content": text
            }
            
            # 如果有图片，解码并处理
            if image_base64 and image_base64.startswith('data:'):
                # 提取 base64 数据
                image_data = image_base64.split(',', 1)[1]
                user_message["images"] = [image_data]
                print(f"🖼️ 包含图片")
            
            messages.append(user_message)
            
            # 调用 Ollama
            model_name = os.getenv("OLLAMA_MODEL", "qwen3-vl:8b")
            print(f"🤖 调用 Ollama ({model_name})...")
            start_time = time.time()
            response_result = ollama_chat(messages=messages, model=model_name)
            elapsed = time.time() - start_time
            print(f"✅ Ollama 响应耗时：{elapsed:.2f}秒")
            
            # 解析 Ollama 响应
            if isinstance(response_result, dict):
                if response_result.get('success'):
                    response_text = response_result.get('content', '')
                else:
                    raise Exception(f"Ollama 调用失败：{response_result.get('error', '未知错误')}")
            else:
                response_text = str(response_result)
            
            if not response_text:
                raise Exception("Ollama 返回空响应")
            
            print(f"💬 回复：{response_text[:200]}{'...' if len(response_text) > 200 else ''}")
            
            # 提交回复
            success = self.submit_response(msg_id, response_text)
            
            if success:
                # 保存到短期记忆（移除图片数据，避免记忆文件过大）
                assistant_message = {"role": "assistant", "content": response_text}
                messages.append(assistant_message)
                
                # 清理消息中的图片数据后再保存
                messages_to_save = []
                for msg in messages:
                    clean_msg = msg.copy()
                    # 移除 images 字段，只保留文本
                    if 'images' in clean_msg:
                        del clean_msg['images']
                        # 在 content 中添加标记，说明有图片
                        if msg['role'] == 'user':
                            clean_msg['content'] = '[图片] ' + clean_msg.get('content', '')
                    messages_to_save.append(clean_msg)
                
                save_shortterm_memory(messages_to_save)
                
                # 检查并清理过大的记忆文件（保留最近 50 条）
                self._cleanup_memory()
                
                self.processed_count += 1
                print(f"✅ 处理完成 (总成功：{self.processed_count})")
                return True
            else:
                print(f"❌ 提交回复失败")
                self.update_message_status(msg_id, 'failed')
                return False
                
        except Exception as e:
            print(f"❌ 处理失败：{e}")
            import traceback
            traceback.print_exc()
            self.error_count += 1
            try:
                self.update_message_status(message['id'], 'failed')
            except:
                pass
            return False
    
    def update_message_status(self, message_id, status):
        """更新消息状态"""
        try:
            url = f"{BASE_URL}/submit_response.php"
            data = {
                'message_id': message_id,
                'status': status,
                'response': '' if status != 'completed' else None
            }
            response = self.session.post(url, data=data, timeout=30)
            return response.json().get('success', False)
        except Exception as e:
            print(f"⚠️ 更新状态失败：{e}")
            return False
    
    def submit_response(self, message_id, response_text):
        """提交处理结果"""
        try:
            url = f"{BASE_URL}/submit_response.php"
            data = {
                'message_id': message_id,
                'response': response_text,
                'status': 'completed'
            }
            response = self.session.post(url, data=data, timeout=30)
            result = response.json()
            return result.get('success', False)
        except Exception as e:
            print(f"❌ 提交回复失败：{e}")
            return False
    
    def check_and_process(self, limit=50):
        """检查并处理所有待处理消息"""
        print("\n" + "=" * 60)
        print("🚀 开始处理消息")
        print("=" * 60)
        print(f"🌐 网站地址：{BASE_URL}")
        print(f"📊 当前统计 - 成功：{self.processed_count}, 失败：{self.error_count}")
        print("=" * 60)
        
        # 获取待处理消息
        messages = self.get_pending_messages(limit=limit)
        
        if not messages:
            print("\n✅ 暂无待处理消息")
            return 0
        
        print(f"\n📥 发现 {len(messages)} 条待处理消息")
        
        success_count = 0
        for i, msg in enumerate(messages, 1):
            print(f"\n[{i}/{len(messages)}]")
            if self.process_message(msg):
                success_count += 1
            time.sleep(0.5)  # 避免过快
        
        print("\n" + "=" * 60)
        print(f"✅ 本次处理完成")
        print(f"📊 成功：{success_count}/{len(messages)}")
        print(f"📊 总计 - 成功：{self.processed_count}, 失败：{self.error_count}")
        print("=" * 60)
        
        return success_count
    
    def _cleanup_memory(self):
        """清理过大的短期记忆文件，保留最近 50 条对话"""
        try:
            all_messages = load_shortterm_memory()
            if len(all_messages) > 52:  # system prompt + 50 条对话 + 1 条缓冲
                # 保留 system prompt + 最近 50 条
                cleaned = [all_messages[0]]  # system prompt
                cleaned.extend(all_messages[-50:])  # 最近 50 条
                save_shortterm_memory(cleaned)
                print(f"🧹 已清理记忆文件，保留 {len(cleaned)} 条")
        except Exception as e:
            print(f"⚠️ 清理记忆失败: {e}")
    
    def show_stats(self):
        """显示统计信息"""
        print("\n" + "=" * 60)
        print("📊 处理统计")
        print("=" * 60)
        print(f"✅ 成功处理：{self.processed_count} 条")
        print(f"❌ 处理失败：{self.error_count} 条")
        if self.processed_count > 0:
            success_rate = self.processed_count / (self.processed_count + self.error_count) * 100
            print(f"📈 成功率：{success_rate:.1f}%")
        print("=" * 60)


def main():
    """主函数 - 交互式命令行"""
    processor = AsyncMessageProcessor()
    
    print("=" * 60)
    print("🤖 InfinityFree 异步消息处理器")
    print("=" * 60)
    print("\n使用说明:")
    print("1. 输入数字执行操作")
    print("2. 输入 'q' 退出程序")
    print("3. 输入 's' 查看统计")
    print()
    
    while True:
        print("\n请选择操作:")
        print("  [1] 检查并处理所有待处理消息")
        print("  [2] 检查并处理前 10 条消息")
        print("  [3] 检查并处理前 20 条消息")
        print("  [s] 查看统计信息")
        print("  [q] 退出")
        
        choice = input("\n你的选择：").strip().lower()
        
        if choice == 'q':
            print("\n👋 处理器已退出")
            break
        elif choice == 's':
            processor.show_stats()
        elif choice == '1':
            processor.check_and_process(limit=50)
        elif choice == '2':
            processor.check_and_process(limit=10)
        elif choice == '3':
            processor.check_and_process(limit=20)
        else:
            print("⚠️ 无效输入，请重新选择")


if __name__ == '__main__':
    main()
