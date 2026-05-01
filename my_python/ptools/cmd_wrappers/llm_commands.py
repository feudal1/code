#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
LLM 命令包装（触发命令注册）
"""

# 该模块内部使用 @register_command 注册命令；
# 在此处导入仅用于触发装饰器执行。
try:
    from ptools.my_llm import llm_use  # noqa: F401
    from ptools.my_llm import reset_memory  # noqa: F401
except Exception as e:
    print(f"警告：LLM 模块导入失败：{type(e).__name__}: {e}")

