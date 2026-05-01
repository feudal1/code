from pyautocad import Autocad
import argparse
import os
import sys


def open_cad_doc(doc_path: str) -> None:
    """通过文件路径打开 CAD 文档"""
    acad = Autocad(create_if_not_exists=True)
    
    # 检查文件是否存在
    if not os.path.exists(doc_path):
        print(f"❌ 文件不存在：{doc_path}")
        sys.exit(1)
    
    # 打开文档
    doc = acad.app.Documents.Open(doc_path)
    print(f"✅ 已打开文档：{doc_path}")
    return doc


def main():
    # 创建参数解析器
    parser = argparse.ArgumentParser(
        description="通过命令行打开 CAD 文档",
        formatter_class=argparse.RawDescriptionHelpFormatter
    )
    
    # 添加 doc_path 参数
    parser.add_argument(
        "doc_path",
        type=str,
        help="CAD 文档的完整路径（.dwg 格式）"
    )
    

    
    # 解析参数
    args = parser.parse_args()
    
    # 打开文档
    open_cad_doc(args.doc_path)


if __name__ == "__main__":
    main()