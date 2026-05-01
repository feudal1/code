import sys
import os
sys.path.append(os.path.dirname(os.path.abspath(__file__)))

from blender_api import _send_code_to_exec

def import_pmx(filepath: str = ""):
    """导入PMX文件"""
    if not filepath:
        # 如果没有提供文件路径，使用tkinter选择文件
        import tkinter as tk
        from tkinter import filedialog
        
        root = tk.Tk()
        root.withdraw()  # 隐藏主窗口
        filepath = filedialog.askopenfilename(
            title="选择PMX文件",
            filetypes=[("PMX files", "*.pmx"), ("All files", "*.*")]
        )
        root.destroy()
        
        if not filepath:
            return {"status": "error", "message": "未选择文件"}

    code = f'''import bpy, os
# 确保mmd_tools插件被启用

# 获取3D视图区域
window = bpy.context.window_manager.windows[0]
screen = window.screen
area = next((a for a in screen.areas if a.type == 'VIEW_3D'), None)
region = area.regions[-1] if area else None


# 使用正确的参数导入PMX文件
bpy.ops.cats_importer.import_any_model(
    filepath=r"{filepath}"
    
)

if bpy.context.object:
    bpy.context.object.scale[0] = 100
    bpy.context.object.scale[1] = 100
    bpy.context.object.scale[2] = 100

result = {{"status": "success", "message": "PMX文件导入成功: " + os.path.basename(r"{filepath}")}}'''
    return _send_code_to_exec(code)

if __name__ == "__main__":
    import_pmx()
