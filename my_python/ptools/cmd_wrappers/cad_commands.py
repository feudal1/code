#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
CAD 工具命令包装
"""
from ptools import register_command


@register_command('cad', 'obb')
def cad_obb_cmd():
    """CAD 图形 OBB 包围盒计算和旋转"""
    try:
        from tools.cad_server import obb_and_rotate
        obb_and_rotate.obb_and_rotate_cmd()
    except Exception as e:
       print(f"执行 OBB 命令时出错：{e}")


@register_command('cad', 'box')
def cad_box_cmd():
    """CAD 图形 AABB/OBB 包围盒计算"""
    try:
        from tools.cad_server.OBB_box import main
        main()
    except Exception as e:
       print(f"执行 Box 命令时出错：{e}")
