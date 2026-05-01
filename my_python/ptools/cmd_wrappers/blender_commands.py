#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Blender 工具命令包装
"""
from ptools import register_command


@register_command('blender', 'start')
def blender_start_cmd():
    """启动 Blender 程序"""
    try:
        from tools.blender_skill.blender_start import start_blender
        result = start_blender()
        print(result)
    except Exception as e:
        print(f"启动 Blender 时出错：{e}")


@register_command('blender', 'delete-all')
def blender_delete_all_cmd():
    """删除 Blender 场景中的所有对象"""
    try:
        from tools.blender_skill.delete_all_objects import main
        main()
    except Exception as e:
        print(f"删除对象时出错：{e}")


@register_command('blender', 'import-pmx')
def blender_import_pmx_cmd():
    """导入 PMX 模型到 Blender"""
    try:
        from tools.blender_skill.import_pmx import main
        main()
    except Exception as e:
        print(f"导入 PMX 时出错：{e}")


@register_command('blender', 'import-psk')
def blender_import_psk_cmd():
    """导入 PSK 模型到 Blender"""
    try:
        from tools.blender_skill.import_psk import main
        main()
    except Exception as e:
        print(f"导入 PSK 时出错：{e}")
