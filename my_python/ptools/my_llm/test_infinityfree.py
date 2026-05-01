"""
测试与 InfinityFree 网站的连接
"""
import requests
import sys
import os

# 添加项目路径
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from my_llm.async_processor import AsyncMessageProcessor

def test_connection():
    """测试数据库连接和 API 接口"""
    print("=" * 60)
    print("🔍 测试 InfinityFree 连接")
    print("=" * 60)
    
    processor = AsyncMessageProcessor()
    
    # 测试 1: 获取待处理消息
    print("\n[测试 1] 获取待处理消息...")
    try:
        messages = processor.get_pending_messages(limit=5)
        print(f"✅ 成功！获取到 {len(messages)} 条消息")
        if messages:
            print(f"   第一条消息 ID: {messages[0]['id']}")
            print(f"   用户: {messages[0]['user_id']}")
            print(f"   内容: {messages[0]['message'][:50]}...")
    except Exception as e:
        print(f"❌ 失败: {e}")
        return False
    
    # 测试 2: 检查配置
    print("\n[测试 2] 检查配置...")
    print(f"   BASE_URL: https://feudal.fwh.is")
    
    # 测试 3: 尝试访问网站首页
    print("\n[测试 3] 访问网站首页...")
    try:
        response = requests.get("https://feudal.fwh.is", timeout=10)
        if response.status_code == 200:
            print(f"✅ 网站可访问 (状态码: {response.status_code})")
        else:
            print(f"⚠️ 网站返回状态码: {response.status_code}")
    except Exception as e:
        print(f"❌ 无法访问网站: {e}")
        return False
    
    print("\n" + "=" * 60)
    print("✅ 所有测试通过！可以开始使用处理器了")
    print("=" * 60)
    return True

if __name__ == '__main__':
    success = test_connection()
    if not success:
        print("\n⚠️ 测试失败，请检查:")
        print("1. 网络连接是否正常")
        print("2. 网站地址是否正确")
        print("3. InfinityFree 服务是否正常运行")
        sys.exit(1)
