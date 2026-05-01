"""
清理短期记忆文件
"""
import sys
import os

# 添加项目路径
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from my_llm.file_memory import load_shortterm_memory, save_shortterm_memory

def cleanup_memory():
    """清理短期记忆，保留最近 20 条对话"""
    print("=" * 60)
    print("🧹 清理短期记忆文件")
    print("=" * 60)
    
    try:
        all_messages = load_shortterm_memory()
        print(f"\n当前记忆数量: {len(all_messages)} 条")
        
        if len(all_messages) <= 22:  # system + 20 条 + 1 缓冲
            print("✅ 记忆文件大小正常，无需清理")
            return
        
        # 保留 system prompt + 最近 20 条
        cleaned = [all_messages[0]]  # system prompt
        cleaned.extend(all_messages[-20:])  # 最近 20 条
        
        save_shortterm_memory(cleaned)
        print(f"✅ 已清理记忆文件")
        print(f"   清理前: {len(all_messages)} 条")
        print(f"   清理后: {len(cleaned)} 条")
        print(f"   删除了: {len(all_messages) - len(cleaned)} 条")
        
    except Exception as e:
        print(f"❌ 清理失败: {e}")
        import traceback
        traceback.print_exc()

if __name__ == '__main__':
    cleanup_memory()
