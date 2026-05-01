"""
使用 cloudscraper 绕过 Cloudflare/InfinityFree 保护
需要先安装：pip install cloudscraper
"""
import cloudscraper
import json

def test_with_cloudscraper():
    """使用 cloudscraper 测试连接"""
    print("🔍 使用 cloudscraper 测试连接...")
    
    # 创建 scraper 实例
    scraper = cloudscraper.create_scraper(
        browser={
            'browser': 'chrome',
            'platform': 'windows',
            'desktop': True
        }
    )
    
    try:
        # 尝试获取待处理消息
        url = "https://feudal.fwh.is/get_pending_messages.php?limit=5"
        response = scraper.get(url, timeout=30)
        
        print(f"状态码: {response.status_code}")
        print(f"响应内容前200字符: {response.text[:200]}")
        
        # 尝试解析 JSON
        try:
            data = response.json()
            print(f"✅ JSON 解析成功: {data}")
            return True
        except json.JSONDecodeError:
            print(f"❌ 无法解析为 JSON")
            return False
            
    except Exception as e:
        print(f"❌ 错误: {e}")
        return False

if __name__ == '__main__':
    test_with_cloudscraper()
