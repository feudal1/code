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




def move_and_rename_skeleton():
    """
    将meshes文件夹中的Skeleton资产移动到指定目标文件夹并重命名
    
    Returns:
        dict: API响应结果
    """
    code = '''
import unreal

# 配置路径和目标名称
delete_folder = "/Game/Characters/Skeleton"
mesh_folder = "/Game/Characters/MainChar/W/Meshes"
target_folder = delete_folder
target_name = "Base_skeleton"
target_asset_path = f"{target_folder}/{target_name}"

print(f"开始删除目录下所有Skeleton资产: {delete_folder}")

# 获取资产注册表
asset_registry = unreal.AssetRegistryHelpers.get_asset_registry()

# 删除delete_folder下所有Skeleton资产
delete_filter = unreal.ARFilter(recursive_paths=True)
delete_filter.package_paths.append(delete_folder)
delete_filter.class_names.append("Skeleton")

skeletons_to_delete = asset_registry.get_assets(delete_filter)
deleted_count = 0
for asset_data in skeletons_to_delete:
    asset_path = str(asset_data.object_path)
    success = unreal.EditorAssetLibrary.delete_asset(asset_path)
    if success:
        print(f"已删除: {asset_path}")
        deleted_count += 1
    else:
        print(f"删除失败: {asset_path}（可能被引用或锁定）")
print(f"共删除 {deleted_count} 个Skeleton资产。")

# 扫描mesh_folder的Skeleton资产并移动重命名
print(f"开始扫描骨骼资产: {mesh_folder}")

mesh_filter = unreal.ARFilter(recursive_paths=True)
mesh_filter.package_paths.append(mesh_folder)
mesh_filter.class_names.append("Skeleton")

skeleton_assets = asset_registry.get_assets(mesh_filter)
print(f"在 {mesh_folder} 找到 {len(skeleton_assets)} 个Skeleton资产")

if len(skeleton_assets) == 0:
    print("未找到任何Skeleton资产，脚本结束。")
else:
    if unreal.EditorAssetLibrary.does_asset_exist(target_asset_path):
        print(f"目标路径 {target_asset_path} 已存在Skeleton资产，请先处理或改名，脚本中止。")
    else:
        asset_data = skeleton_assets[0]
        old_path = str(asset_data.object_path)
        print(f"尝试重命名并移动骨骼资产:\\n{old_path} -> {target_asset_path}")
        success = unreal.EditorAssetLibrary.rename_asset(old_path, target_asset_path)
        if success:
            print(f"成功重命名并移动Skeleton资产到：{target_asset_path}")
        else:
            print(f"重命名或移动失败，可能资产被引用或锁定。")
'''
    return send_python_code_request(code)


if __name__ == "__main__":
    result = move_and_rename_skeleton()
    print(result)