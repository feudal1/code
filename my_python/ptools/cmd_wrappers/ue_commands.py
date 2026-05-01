#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
UE 工具命令包装
"""
from ptools import register_command


@register_command('ue', 'start')
def ue_start_cmd():
    """启动 Unreal Engine 4 编辑器"""
    try:
        from tools.ue_skill.ue_start import start_ue
        result = start_ue()
        print(result)
    except Exception as e:
       print(f"启动 UE 时出错：{e}")


@register_command('ue', 'import-fbx')
def ue_import_fbx_cmd():
    """导入 FBX 文件到 UE"""
    try:
        from tools.ue_skill.fbx_import import main
        main()
    except Exception as e:
       print(f"导入 FBX 时出错：{e}")
