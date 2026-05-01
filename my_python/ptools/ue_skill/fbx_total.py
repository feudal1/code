from fbx_import import send_fbx_import_request
from mat_instance import create_material_instances_from_textures
from mat_ops import delete_materials_in_folder
from skeleton_ops import move_and_rename_skeleton
from texture_ops import move_textures_to_folder

def import_fbx_and_create_material_instances():
    send_fbx_import_request()
    delete_materials_in_folder()
    move_and_rename_skeleton()
    move_textures_to_folder()
    create_material_instances_from_textures()
  

    return "导入fbx并创建材质实例完成"

if __name__=="__main__":
    import_fbx_and_create_material_instances()
