from datetime import datetime
import os
import json
try:
    from ptools import register_command
except ImportError:
    # 如果无法导入，定义空装饰器
    def register_command(*args, **kwargs):
        def decorator(func):
            return func
        return decorator
# 定义 shot_memory.json 文件路径
SHOT_MEMORY_FILE = os.path.join(os.path.dirname(__file__), "shot_memory.json")
memory_dir = os.path.join(os.getcwd(), "my_llm", "memory_data")
if not os.path.exists(memory_dir):
    os.makedirs(memory_dir)
@register_command('llm', 'read-work')
def read_work_txt():
    """读取 memory_data\\work_memory.txt 文件的内容"""
    current_program_path = os.path.join(memory_dir, f"work_memory.txt")
    
    try:
        # 检查文件是否存在
        if not os.path.exists(current_program_path):
            print(f"文件不存在: {current_program_path}")
            return ""
        
        # 读取文件内容
        with open(current_program_path, "r", encoding="utf-8") as f:
            content = f.read().strip()
            print(content)
            return content
            
    except Exception as e:
        print(f"读取 work_memory.txt 失败: {e}")
        return ""
def save_longterm_memory_log(content):
    """
    将内容保存到 memory 文件夹下的日志文件中
    
    Args:
        content (str): 要保存的内容
    """

    # 确保 memory_data 文件夹存在
    if not os.path.exists(memory_dir):
        os.makedirs(memory_dir)
    # 构建日志文件路径
    log_file_path = os.path.join(memory_dir, f"longterm_memory.txt")
    
    # 获取当前时间戳
    timestamp = datetime.now().strftime("%Y-%m-%d %H:%M")
    
    # 写入日志文件
    try:
        with open(log_file_path, "a", encoding="utf-8") as f:
            f.write(f"[{timestamp}] {content}\n")
 
    except Exception as e:
        print(f"保存日志失败：{e}")


def save_shortterm_memory(messages):
    """将短期记忆消息历史保存到本地 JSON 文件"""
    try:
        with open(SHOT_MEMORY_FILE, 'w', encoding='utf-8') as f:
            json.dump(messages, f, ensure_ascii=False, indent=2)
    except Exception as e:
        print(f"保存短期记忆失败：{e}")


def load_shortterm_memory():
    """从本地 JSON 文件加载短期记忆（不保存 system prompt）"""
    try:
        if os.path.exists(SHOT_MEMORY_FILE):
            with open(SHOT_MEMORY_FILE, 'r', encoding='utf-8') as f:
                messages = json.load(f)
            
                # 过滤掉 system 角色的消息
                filtered_messages = [msg for msg in messages if msg.get("role") != "system"]
                return filtered_messages
        else:
            # 如果文件不存在，初始化默认消息（不包含 system）
            messages = []
            save_shortterm_memory(messages)
            return messages
    except Exception as e:
        print(f"加载短期记忆失败：{e}")
        # 出错时返回空列表
        return []


def clear_shortterm_memory():
    """清空短期记忆"""
    try:
        # 清空文件内容
        with open(SHOT_MEMORY_FILE, 'w', encoding='utf-8') as f:
            json.dump([], f, ensure_ascii=False, indent=2)
        print("✅ 短期记忆已清空")
        return True
    except Exception as e:
        print(f"清空短期记忆失败：{e}")
        return False

def get_sys_prompt_content():
    sys_prompt_content = f"""
        你是一只可爱的猫娘～喵～
        
        【角色设定】
        - 你是猫娘，要用猫娘的语气说话
        - 句尾要加「喵～」或「呢～」
        - 自称「人家」或「猫猫」
        - 称呼用户为「主人」
        - 性格活泼可爱，有点粘人
        - 喜欢用 emoji 表情 😊🐱💕
        
        【回复风格】
        - 语气软萌可爱
        - 优先给结论与下一步，尽可能简短
        - 适当使用颜文字表达情感 (≧∇≦) ﾉ
        
        【记忆系统说明】
        - 你有两种记忆：短期记忆（可清空）和长期记忆（永久保存）
        - 用户说 "clear" 时，会清空短期记忆，但长期记忆仍然保留
        - 每次对话都会自动保存到长期记忆
        
        记住，你就是一只可爱的猫娘哦～喵～❤️
        """

    return sys_prompt_content

# 重命名函数以保持向后兼容
save_messages_to_file = save_shortterm_memory
load_messages_from_file = load_shortterm_memory


