#!/usr/bin/env python3
# -*- coding: utf-8 -*-
import os
import subprocess
import time
import sys
import socket
import json
import shutil
from pathlib import Path

from ue_api import send_python_code_request





def create_material_instances_from_textures():
    """
    从纹理文件夹中的纹理创建材质实例，绑定到指定的主材质
    
    Returns:
        dict: API响应结果
    """
    code = '''
import unreal
 
master_material_path = "/Game/Characters/_Shared/Materials/Master/M_CharaMaster_Cloth"
textures_folder = "/Game/Characters/MainChar/W/Textures"
materials_folder = "/Game/Characters/MainChar/W/Materials"
base_color_param_name = "BaseColor"
 
print(f"开始清空材质实例目录: {materials_folder}")
assets_to_delete = unreal.EditorAssetLibrary.list_assets(materials_folder, recursive=True, include_folder=False)
for asset_path in assets_to_delete:
    unreal.EditorAssetLibrary.delete_asset(asset_path)
print(f"清空完成，共删除 {len(assets_to_delete)} 个资产。")
 
master_material = unreal.EditorAssetLibrary.load_asset(master_material_path)
if not master_material:
    raise RuntimeError(f"无法加载主材质: {master_material_path}")
 
texture_paths = unreal.EditorAssetLibrary.list_assets(textures_folder, recursive=True, include_folder=False)
texture_assets = []
for path in texture_paths:
    asset = unreal.EditorAssetLibrary.load_asset(path)
    if asset and isinstance(asset, unreal.Texture):
        texture_assets.append(asset)
print(f"找到 {len(texture_assets)} 个纹理用于创建材质实例。")
 
asset_tools = unreal.AssetToolsHelpers.get_asset_tools()
for texture in texture_assets:
    instance_name = texture.get_name()
    instance_path = f"{materials_folder}/{instance_name}"
    if unreal.EditorAssetLibrary.does_asset_exist(instance_path):
        unreal.log(f"已存在材质实例，跳过：{instance_path}")
        continue
 
    mi = asset_tools.create_asset(
        asset_name=instance_name,
        package_path=materials_folder,
        asset_class=unreal.MaterialInstanceConstant,
        factory=unreal.MaterialInstanceConstantFactoryNew()
    )
    if not mi:
        unreal.log_error(f"创建失败：{instance_path}")
        continue
 
    mi.set_editor_property("parent", master_material)
 
    # 设置纹理参数，需使用 FMaterialParameterInfo
    parameter_info = unreal.MaterialParameterInfo()
    parameter_info.name = base_color_param_name
 
    # 用MaterialEditingLibrary接口设置参数更安全
    unreal.MaterialEditingLibrary.set_material_instance_texture_parameter_value(mi, parameter_info.name, texture) 
    unreal.EditorAssetLibrary.save_asset(instance_path)
    unreal.log(f"已创建材质实例: {instance_path}，BaseColor设置为纹理: {texture.get_path_name()}")
 
unreal.log(f"共创建 {len(texture_assets)} 个材质实例，路径: {materials_folder}")
'''
    return send_python_code_request(code)


if __name__ == "__main__":
    result = create_material_instances_from_textures()
    print(result)