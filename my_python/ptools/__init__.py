#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Typer 命令装饰器：用于自动发现和注册命令
支持两种注册方式：
1. @command_group - 创建命令组（子命令）
2. @register_main_command - 直接注册到主应用（无中间层级）
"""
import typer
from typing import Dict, List, Any

# 全局命令注册表
COMMAND_REGISTRY: Dict[str, Dict[str, Any]] = {}
# 主应用命令注册表
MAIN_COMMAND_REGISTRY: List[Dict[str, Any]] = []
# 主应用实例引用（将在 main.py 中设置）
MAIN_APP_INSTANCE = None


def command_group(name: str, help: str = ""):
    """
    命令组装饰器：标记一个命令分组
    
    :param name: 命令组名称（如 'pdf', 'cad', 'memory'）
    :param help: 命令组帮助信息
    
    使用示例:
        @command_group('pdf', 'PDF 处理工具')
        class PDFCommands:
            pass
        
        @pdf_app.command("merge")
        def merge_cmd(...):
            pass
    """
    # 创建新的 Typer 子应用
    sub_app = typer.Typer()
    
    # 存储到全局注册表
    COMMAND_REGISTRY[name] = {
        "help": help,
        "app": sub_app,
        "commands": []
    }
    
    # 如果主应用实例已设置，将命令组添加到主应用
    if MAIN_APP_INSTANCE is not None:
        MAIN_APP_INSTANCE.add_typer(sub_app, name=name, help=help)
    
    return sub_app


def register_command(group_name: str, name: str = None, **kwargs):
    """
    命令注册装饰器：将函数注册为命令
    如果命令组不存在，会自动创建
    
    :param group_name: 所属的命令组名称
    :param name: 命令名称（默认使用函数名）
    :param kwargs: 其他传递给 @app.command() 的参数
    
    使用示例:
        @register_command('pdf', 'merge')
        def merge_pdf_cmd(...):
            pass
    """
    def decorator(func):
        # 如果命令组不存在，自动创建
        if group_name not in COMMAND_REGISTRY:
            sub_app = typer.Typer()
            COMMAND_REGISTRY[group_name] = {
                "help": f"{group_name} 命令组",
                "app": sub_app,
                "commands": []
            }
        
        sub_app = COMMAND_REGISTRY[group_name]["app"]
        command_name = name or func.__name__.replace('_cmd', '').replace('_', '-')
        
        # 使用 Typer 的 @app.command 装饰器
        command_func = sub_app.command(command_name, **kwargs)(func)
        
        # 记录到注册表
        COMMAND_REGISTRY[group_name]["commands"].append({
            "name": command_name,
            "help": func.__doc__ or "",
            "function": func.__name__
        })
        
        return command_func
    
    return decorator


def register_main_command(name: str = None, **kwargs):
    """
    直接注册到主应用的装饰器（无命令组层级）
    
    :param name: 命令名称（默认使用函数名）
    :param kwargs: 其他传递给 @app.command() 的参数
    
    使用示例:
        @register_main_command('obb')
        def obb_cmd(...):
            pass
    """
    def decorator(func):
        command_name = name or func.__name__.replace('_cmd', '').replace('_', '-')
        
        # 记录到主应用注册表（稍后在 main.py 中统一注册）
        MAIN_COMMAND_REGISTRY.append({
            "name": command_name,
            "help": func.__doc__ or "",
            "function": func
        })
        
        return func
    
    return decorator


def get_all_commands() -> Dict[str, Dict[str, Any]]:
    """获取所有已注册的命令（包括命令组和主应用命令）"""
    result = COMMAND_REGISTRY.copy()
    
    # 添加主应用命令到结果中（用于 help 显示）
    if MAIN_COMMAND_REGISTRY:
        result["[主应用]"] = {
            "help": "主应用直接命令",
            "commands": [{"name": cmd["name"], "help": cmd["help"]} for cmd in MAIN_COMMAND_REGISTRY]
        }
    
    return result


def get_command_group(name: str) -> Dict[str, Any]:
    """获取指定命令组的信息"""
    return COMMAND_REGISTRY.get(name)


def get_main_command_registry() -> List[Dict[str, Any]]:
    """获取主应用命令注册表"""
    return MAIN_COMMAND_REGISTRY.copy()


def set_main_app_instance(app):
    """设置主应用实例引用"""
    global MAIN_APP_INSTANCE
    MAIN_APP_INSTANCE = app


def register_all_main_commands():
    """将所有主应用命令注册到主应用实例"""
    if MAIN_APP_INSTANCE is None:
        raise ValueError("主应用实例未设置，请先调用 set_main_app_instance()")
    
    for cmd_info in MAIN_COMMAND_REGISTRY:
        MAIN_APP_INSTANCE.command(cmd_info["name"], help=cmd_info["help"])(cmd_info["function"])


# 保持向后兼容的导出（如果需要在其他地方使用）
