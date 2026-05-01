"""
手动获取 InfinityFree Cookie
使用方法：
1. 在浏览器中访问 https://feudal.fwh.is/get_pending_messages.php?limit=1
2. 打开开发者工具 (F12) -> Network 标签
3. 刷新页面，找到 get_pending_messages.php 请求
4. 右键 -> Copy -> Copy as cURL
5. 粘贴到下面，提取 Cookie
6. 运行此脚本保存 Cookie
"""
import json
import os

# 从浏览器复制的 Cookie（示例格式）
# 你需要从浏览器中获取真实的 Cookie 值
COOKIE_STRING = ""  # 在这里粘贴你的 Cookie，例如: "__test=abc123; other=value"

def save_cookie():
    """保存 Cookie 到文件"""
    if not COOKIE_STRING:
        print("⚠️ 请先从浏览器获取 Cookie")
        print("\n操作步骤：")
        print("1. 在浏览器访问: https://feudal.fwh.is/get_pending_messages.php?limit=1")
        print("2. 按 F12 打开开发者工具")
        print("3. 切换到 Network 标签")
        print("4. 刷新页面")
        print("5. 找到 get_pending_messages.php 请求")
        print("6. 点击该请求，查看 Headers")
        print("7. 找到 Request Headers 中的 Cookie 字段")
        print("8. 复制 Cookie 的值")
        print("9. 编辑此文件，将 Cookie 值填入 COOKIE_STRING 变量")
        print("10. 重新运行此脚本")
        return False
    
    # 保存 Cookie
    cookie_file = os.path.join(os.path.dirname(__file__), 'infinityfree_cookie.json')
    with open(cookie_file, 'w', encoding='utf-8') as f:
        json.dump({'cookie': COOKIE_STRING}, f)
    
    print(f"✅ Cookie 已保存到: {cookie_file}")
    print(f"   Cookie 长度: {len(COOKIE_STRING)} 字符")
    return True

if __name__ == '__main__':
    save_cookie()
