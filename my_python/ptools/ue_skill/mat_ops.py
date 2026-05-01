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




def delete_materials_in_folder():
    """
    删除指定文件夹中的材质文件

    Returns:
        dict: API响应结果
    """
    code = """
import unreal

folder_path = "/Game/Characters/MainChar/W/Meshes"
print(f"正在扫描文件夹: {folder_path}")

asset_registry = unreal.AssetRegistryHelpers.get_asset_registry()
filter = unreal.ARFilter(recursive_paths=True)
filter.package_paths.append(folder_path)
filter.class_names.append("Material")
filter.class_names.append("MaterialInstanceConstant")

assets = asset_registry.get_assets(filter)
print(f"在 {folder_path} 中找到 {len(assets)} 个材质资产")

material_paths = []
for asset_data in assets:
    print(f"找到材质资产: {asset_data.object_path}")
    material_paths.append(str(asset_data.object_path))

deleted = []
for path in material_paths:
    success = unreal.EditorAssetLibrary.delete_asset(path)
    if success:
        print(f"成功删除材质: {path}")
        deleted.append(path)
    else:
        print(f"删除材质失败: {path} - 可能被引用或锁定")

print(f"共删除 {len(deleted)} 个材质资产。")

"""
    return send_python_code_request(code)


if __name__ == "__main__":
    result = delete_materials_in_folder()
    print(result)