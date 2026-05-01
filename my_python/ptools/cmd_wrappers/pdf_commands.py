#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
PDF 命令包装（触发命令注册）
"""

# 这些模块内部使用 @register_command 注册命令；
# 在此处导入仅用于触发装饰器执行。
try:
    from ptools.pdf_server import merge_pdf  # noqa: F401
except Exception as e:
    print(f"警告：PDF 模块导入失败：{type(e).__name__}: {e}")

