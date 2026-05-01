"""
SolidWorks 零件导出工具 - 简单命令行版本
调用 HTTP 接口导出当前零件的 DXF 文件
"""
import requests
import sys

def main():
    """简单的导出函数"""
    try:
        print("正在导出当前零件...")
        response = requests.get("http://localhost:5000/part2export", timeout=300)
        result = response.json()
        
        if result["success"]:
            parts = result.get("parts", [])
            print(f"✓ 成功导出 {len(parts)} 个零件:")
            for part in parts:
                print(f"  - {part}")
        else:
            print(f"✗ 导出失败：{result.get('error', '未知错误')}")
            sys.exit(1)
            
    except Exception as e:
        print(f"✗ 错误：{e}")
        sys.exit(1)

if __name__ == "__main__":
    main()
