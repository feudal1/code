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


def move_textures_to_folder():
    """
    将meshes文件夹中的纹理资产移动到textures文件夹中
    
    Returns:
        dict: API响应结果
    """
    code = """
import unreal

# 路径配置（无结尾斜杠）
textures_folder = "/Game/Characters/MainChar/W/Textures"
meshes_folder = "/Game/Characters/MainChar/W/Meshes"

# 1. 删除 textures_folder 目录下所有资产
print(f"开始删除目录下所有资产: {textures_folder}")
assets_to_delete = unreal.EditorAssetLibrary.list_assets(textures_folder, recursive=True, include_folder=False)

deleted_count = 0
for asset_path in assets_to_delete:
    success = unreal.EditorAssetLibrary.delete_asset(asset_path)
    if success:
        print(f"删除成功: {asset_path}")
        deleted_count += 1
    else:
        print(f"删除失败: {asset_path}（可能被引用或锁定）")
print(f"共删除了 {deleted_count} 个资产。")

# 2. 查找 meshes_folder 中所有纹理资产
print(f"\\n开始查找文件夹中的纹理资产: {meshes_folder}")
asset_registry = unreal.AssetRegistryHelpers.get_asset_registry()

filter = unreal.ARFilter(recursive_paths=True)
filter.package_paths.append(meshes_folder)
filter.class_names.append("Texture2D")
filter.class_names.append("TextureCube")
filter.class_names.append("TextureRenderTarget2D")
filter.class_names.append("TextureRenderTargetCube")
filter.class_names.append("Texture")

texture_assets = asset_registry.get_assets(filter)
print(f"找到 {len(texture_assets)} 个纹理资产。")

# 3. 移动纹理资产到 textures_folder（重命名路径）
moved_count = 0
for asset_data in texture_assets:
    old_path = str(asset_data.object_path)
    asset_name = asset_data.asset_name
    new_path = f"{textures_folder}/{asset_name}"
    if unreal.EditorAssetLibrary.does_asset_exist(new_path):
        print(f"目标路径已存在，跳过: {new_path}")
        continue
    success = unreal.EditorAssetLibrary.rename_asset(old_path, new_path)
    if success:
        print(f"已移动: {old_path} -> {new_path}")
        moved_count += 1
    else:
        print(f"移动失败: {old_path} -> {new_path}（可能被引用或锁定）")

print(f"共移动 {moved_count} 个纹理资产到 {textures_folder}。")
"""
    return send_python_code_request(code)


if __name__ == "__main__":
    result = move_textures_to_folder()
    print(result)