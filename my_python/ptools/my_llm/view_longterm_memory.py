"""
查看长期记忆内容
"""
import os
from datetime import datetime

memory_dir = os.path.join(os.getcwd(), "my_llm", "memory_data")
longterm_memory_file = os.path.join(memory_dir, "longterm_memory.txt")

def view_longterm_memory():
    """查看长期记忆内容"""
    if not os.path.exists(longterm_memory_file):
        print("❌ 长期记忆文件不存在")
        return
    
    try:
        with open(longterm_memory_file, 'r', encoding='utf-8') as f:
            content = f.read()
        
        print("=" * 60)
        print("📚 长期记忆内容")
        print("=" * 60)
        print(content)
        print("=" * 60)
        
        # 统计信息
        lines = content.strip().split('\n') if content.strip() else []
        print(f"📊 共 {len(lines)} 条记录")
        
    except Exception as e:
        print(f"❌ 读取失败：{e}")

if __name__ == '__main__':
    view_longterm_memory()
