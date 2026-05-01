import sys
import os
sys.path.append(os.path.dirname(os.path.abspath(__file__)))


from blender_api import _send_code_to_exec


def import_psk():
    """导入PSK文件"""
    code = '''import bpy
try:
    from io_scene_psk_psa.psk.reader import read_psk
    from io_scene_psk_psa.psk.importer import import_psk, PskImportOptions
    options = PskImportOptions()
    options.name = 'ObjectName'
    options.should_import_mesh = True
    options.should_import_skeleton = True
    options.scale = 1.0
    psk = read_psk('E:/blender/SK_W_MainChar_01.psk')
    result_val = import_psk(psk, bpy.context, options)
    result = {"status": "success", "message": f"PSK文件导入成功: {result_val}"}
    import bpy
    import mathutils

    def scale_around_world_origin(obj, factor):
        """绕世界原点 (0,0,0) 对 mesh 顶点进行缩放"""
        if obj.type != 'MESH':
            return
        # 直接修改顶点坐标（绕世界原点）
        for v in obj.data.vertices:
            world_co = obj.matrix_world @ v.co
            scaled_world = world_co * factor
            v.co = obj.matrix_world.inverted() @ scaled_world

    def scale_armature_rest_pose_around_origin(arm_obj, factor):
        import bpy
        """绕世界原点缩放 Armature 的 rest pose（即 edit bones）"""
        if arm_obj.type != 'ARMATURE':
            return
        bpy.context.view_layer.objects.active = arm_obj
        bpy.ops.object.mode_set(mode='EDIT')
        
        for bone in arm_obj.data.edit_bones:
            # 缩放 head 和 tail 绕世界原点
            bone.head = bone.head * factor
            bone.tail = bone.tail * factor
        
        bpy.ops.object.mode_set(mode='OBJECT')

    # === 查找目标和参考物体 ===
    target_obj = None
    reference_obj = None

    for obj in bpy.context.scene.objects:
        if obj.type == 'MESH':
            if 'ObjectName' in obj.name:
                target_obj = obj
            elif reference_obj is None:
                reference_obj = obj
            if target_obj and reference_obj:
                break

    if not target_obj or not reference_obj:
        print("错误: 未同时找到 'ObjectName' 物体和参考物体")
    else:
        # 获取真实世界尺寸（使用 bounding box）
        def get_world_dimensions(obj):
            import bpy
            import mathutils
            bbox = obj.bound_box
            world_bbox = [obj.matrix_world @ mathutils.Vector(co) for co in bbox]
            xs = [v.x for v in world_bbox]
            ys = [v.y for v in world_bbox]
            zs = [v.z for v in world_bbox]
            return (max(xs)-min(xs), max(ys)-min(ys), max(zs)-min(zs))
        
        target_dim = get_world_dimensions(target_obj)
        ref_dim = get_world_dimensions(reference_obj)
        
        if target_dim[2] == 0:
            print("错误: ObjectName 的 Z 尺寸为 0，无法缩放")
        else:
            scale_factor = ref_dim[2] / target_dim[2]
            
            # 1. 绕世界原点缩放 mesh 几何（非 transform）
            scale_around_world_origin(target_obj, scale_factor)
            
            # 2. 查找绑定的 Armature
            armature_obj = None
            for mod in target_obj.modifiers:
                if mod.type == 'ARMATURE' and mod.object:
                    armature_obj = mod.object
                    break
            
            if armature_obj:
                # 3. 绕世界原点缩放骨骼的 rest pose（关键！）
                scale_armature_rest_pose_around_origin(armature_obj, scale_factor)
                print(f"✅ 成功：ObjectName 及其绑定骨架已绕世界原点 (0,0,0) 等比缩放（因子: {scale_factor:.6f})")
            else:
                print(f"✅ 成功：ObjectName mesh 已绕世界原点缩放（因子: {scale_factor:.6f}），无绑定骨架")
except Exception as e:
    result = {"status": "error", "message": f"PSK文件导入失败: {str(e)}"}'''
    return _send_code_to_exec(code)

if __name__ == "__main__":
    import_psk()
