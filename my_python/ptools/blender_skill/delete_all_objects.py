import sys
import os
sys.path.append(os.path.dirname(os.path.abspath(__file__)))

from blender_api import _send_code_to_exec
def delete_all_objects():
    """删除所有对象"""
    code = '''import bpy
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete()
for collection in bpy.data.collections:
    bpy.data.collections.remove(collection)
import bpy
bpy.context.scene.unit_settings.scale_length = 0.01

try:
    for area in bpy.context.screen.areas:
        if area.type == 'VIEW_3D':
            for space in area.spaces:
                if space.type == 'VIEW_3D':
                    space.clip_start = 0.01
                    space.clip_end = 300000
                    break
except Exception as e:
    print(f"设置3D视口裁剪平面时出错: {e}")
result = {"status": "success", "message": "所有对象和集合已删除"}'''

    return _send_code_to_exec(code)


if __name__ == "__main__":
    delete_all_objects()
