#!/usr/bin/env python3

import argparse


from ue_api import send_python_code_request

def send_fbx_import_request(host="localhost", port=8070):
    """
    发送FBX导入请求到服务器 - 使用固定路径
    """
    code = """
import unreal
import os

# 检查文件是否存在
fbx_path = "E:/blender/SK_W_MainChar_01.fbx"
print(fbx_path)
destination_path = "/Game/Characters/MainChar/W/Meshes"

# 清空目标文件夹中的所有资产
print(f"正在扫描文件夹: {destination_path}")
asset_registry = unreal.AssetRegistryHelpers.get_asset_registry()
filter = unreal.ARFilter(recursive_paths=True)
filter.package_paths.append(destination_path)

assets = asset_registry.get_assets(filter)
print(f"在 {destination_path} 中找到 {len(assets)} 个资产")

asset_paths = []
for asset_data in assets:
    print(f"找到资产: {asset_data.object_path}")
    asset_paths.append(str(asset_data.object_path))

deleted = []
for path in asset_paths:
    success = unreal.EditorAssetLibrary.delete_asset(path)
    if success:
        print(f"成功删除资产: {path}")
        deleted.append(path)
    else:
        print(f"删除资产失败: {path} - 可能被引用或锁定")

print(f"共删除 {len(deleted)} 个资产。")

if not os.path.exists(fbx_path):
    result = "文件路径无效: " + fbx_path
else:
    # 创建导入任务
    import_task = unreal.AssetImportTask()
    import_task.set_editor_property('filename', fbx_path)
    import_task.set_editor_property('destination_path', destination_path)
    import_task.set_editor_property('save', True)
    import_task.set_editor_property('automated', True)
    import_task.set_editor_property('replace_existing', True)
    
    # 执行导入任务
    asset_tools = unreal.AssetToolsHelpers.get_asset_tools()
    asset_tools.import_asset_tasks([import_task])
    
    # 获取文件名
    filename = os.path.basename(fbx_path)
    result = "成功导入文件: " + filename + " 到 " + destination_path

result
"""
    return send_python_code_request(code, port)


def main():
    """
    主函数，处理命令行参数
    """
    parser = argparse.ArgumentParser(description='UE FBX 导入工具')
    parser.add_argument('command', type=str, choices=['import_fbx'], help='要执行的命令')
    
    args = parser.parse_args()
    
    if args.command == 'import_fbx':
        
        result = send_fbx_import_request()
        print(result)


if __name__ == "__main__":

    send_fbx_import_request()