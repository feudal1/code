#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
统一工具入口：使用 Typer + 装饰器自动发现命令
用法：
    p pdf merge --files file1.pdf file2.pdf
    p memory reset
    p skill-search <关键词>
    p list-all
"""
import sys
import os
from pathlib import Path
from typing import Dict, List, Any, Optional
from difflib import SequenceMatcher
import json

# 添加项目根目录到 Python 路径
PROJECT_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
sys.path.insert(0, PROJECT_ROOT)

import typer
from ptools import get_all_commands, set_main_app_instance, register_all_main_commands, COMMAND_REGISTRY
from ptools.command_search import search_registered_commands

# 导入 cmd_wrappers 模块以注册所有命令包装器
try:
    from ptools.cmd_wrappers import blender_commands
    from ptools.cmd_wrappers import cad_commands
    from ptools.cmd_wrappers import llm_commands
    from ptools.cmd_wrappers import pdf_commands
    from ptools.cmd_wrappers import ue_commands
  
except (ImportError, ModuleNotFoundError) as e:
    print(f"警告：cmd_wrappers 模块导入失败：{e}")

# 显式导入 ptools 的所有导出函数，确保 PyInstaller 能发现
from ptools import (
    get_all_commands,
    set_main_app_instance,
    register_all_main_commands,
    COMMAND_REGISTRY,
    get_command_group,
    get_main_command_registry,
    register_command,
    register_main_command,
    command_group
)

# 创建主应用
app = typer.Typer(
    help="统一工具入口 - 包含 PDF、UE、CAD、Memory、Blender 等多种工具",
    invoke_without_command=True
  
)

# 设置主应用实例引用，以便后续注册命令
set_main_app_instance(app)

# 将所有已注册的命令组添加到主应用
for group_name, group_data in COMMAND_REGISTRY.items():
    app.add_typer(group_data["app"], name=group_name, help=group_data["help"])


@app.command("search")
@app.command("s", hidden=True)
def skill_search_cmd(
    keyword: str = typer.Argument(..., help="搜索关键词"),
    threshold: float = typer.Option(0.5, "--threshold", "-t", help="相似度阈值")
):
    """从已注册的命令中模糊搜索"""
    print(f"\n搜索关键词：{keyword}\n")

    results = search_registered_commands(
        keyword,
        threshold=threshold,
        include_csharp_export=False,
    )
    
    if results:
        print(f"找到 {len(results)} 个匹配结果:\n")
        for i, r in enumerate(results, 1):
            print(f"{i}. [{r['group']}] 相似度：{r['score']}")
            print(f"   命令：{r['name']}")
            print(f"   描述：{r['help']}")
            print(f"   用法：{r['usage']}")
            print('-' * 60)
    else:
        print("未找到相关命令")
    
    print("\n提示：使用 'p <命令组> --help' 查看该组所有可用命令")


@app.command("list")
def list_all_cmd():
    """列出所有已注册的命令"""
    # 从全局注册表获取所有命令
    all_commands = get_all_commands()
    
    # 统计命令总数
    total_commands = 0
    for group_name, group_data in all_commands.items():
        commands = group_data.get("commands", [])
        total_commands += len(commands)
    
    print("=" * 70)
    print("已注册的命令列表")
    print("=" * 70)
    
    for group_name, group_data in sorted(all_commands.items()):
        print(f"\n{group_name}: {group_data.get('help', '')}")
        commands = group_data.get("commands", [])
        if commands:
            for cmd in commands:
                print(f"  - {cmd.get('name', '')}: {cmd.get('help', '')}")
        else:
            print("  (暂无详细命令信息)")
        print(f"  用法：p {group_name} --help")
    
    print("\n" + "=" * 70)
    print(f"共 {len(all_commands)} 个命令组，{total_commands} 个具体命令")
    print("使用 'p <命令组> --help' 查看详细帮助")
    print("使用 'p skill-search <关键词>' 搜索命令")
    print("=" * 70)


# 注册所有直接添加到主应用的命令
register_all_main_commands()

print("\n命令系统初始化完成")
print(f"已加载 {len(get_all_commands())} 个命令组\n")


if __name__ == "__main__":
  app()