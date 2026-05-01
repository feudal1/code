#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
命令包装模块 - 将现有工具集成到 Typer 命令系统
"""

# 导入所有命令模块以触发装饰器注册
try:
    from ptools.cmd_wrappers import blender_commands  # noqa: F401
    from ptools.cmd_wrappers import cad_commands  # noqa: F401
    from ptools.cmd_wrappers import llm_commands  # noqa: F401
    from ptools.cmd_wrappers import pdf_commands  # noqa: F401
    from ptools.cmd_wrappers import ue_commands  # noqa: F401
    from ptools.cmd_wrappers import memory_commands  # noqa: F401
except (ImportError, ModuleNotFoundError) as e:
    print(f"警告：命令模块导入失败：{e}")
