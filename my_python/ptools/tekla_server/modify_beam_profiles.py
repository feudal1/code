# modify_selected_beams.py
import clr
import sys
import os

# 添加Tekla API DLL路径（根据实际安装路径调整）
tekla_path = r"F:\Program Files\Tekla Structures\2024.0\bin"  # 请根据实际版本调整路径
if tekla_path not in sys.path:
    sys.path.append(tekla_path)

def modify_selected_beams_profile(model, new_profile):
    """
    修改选中梁构件的截面类型
    
    Args:
        model: Tekla Model 实例
        new_profile (str): 新的截面类型名称
    """
    try:
        # 获取选中的对象
        selector = model.GetModelObjectSelector()
        selected_objects = selector.GetSelectedObjects()
        
        modified_count = 0
        
        # 遍历选中的对象
        while selected_objects.MoveNext():
            obj = selected_objects.Current
            
            # 检查是否为梁对象
            if isinstance(obj, Beam):
                identifier_info = obj.Identifier.ID if hasattr(obj, 'Identifier') else '无编号'
                print(f"修改选中的梁构件: {identifier_info}")
                print(f"原截面类型: {obj.Profile.ProfileString}")
                
                # 修改截面类型
                obj.Profile.ProfileString = new_profile
                
                # 应用修改
                if obj.Modify():
                    print(f"  成功将选中梁构件的截面类型修改为: {new_profile}")
                    modified_count += 1
                else:
                    print(f"  修改选中梁构件失败")
                    
        if modified_count == 0:
            print("未找到选中的梁构件")
            
        print(f"总共修改了 {modified_count} 个选中的梁构件")
        return modified_count
            
    except Exception as e:
        print(f"修改选中梁构件截面类型时出错: {e}")
        import traceback
        traceback.print_exc()
        return 0

# 主程序入口
if __name__ == "__main__":
    try:
        # 引用 Tekla Open API 的 DLL
        clr.AddReference('Tekla.Structures')
        clr.AddReference('Tekla.Structures.Model')

        from Tekla.Structures.Model import Model, Beam
        from Tekla.Structures import *
        
        # 连接到 Tekla Model
        model = Model()
        if model.GetConnectionStatus():
            print("模型名称:", model.GetInfo().ModelName)
            print("成功连接到Tekla模型")
            
            # 修改选中梁构件的截面类型
            modify_selected_beams_profile(model, "HEA300")
            
        else:
            print("无法连接到模型")
            
    except Exception as e:
        print(f"Tekla API初始化失败: {e}")
        print("请确保Tekla Structures已正确安装，并检查DLL路径配置")