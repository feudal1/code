import win32com.client
import pythoncom

def get_all_components_names(component, level=0):
    """
    递归获取所有子组件（零件和子装配体）的名称
    :param component: IComponent4 对象
    :param level: 当前层级（用于打印缩进）
    :return: 组件名称列表
    """
    names = []
    indent = "  " * level
    comp_name = component.GetName2  # 获取组件名称（含配置）
    print(f"{indent}- {comp_name}")
    names.append(comp_name)

    # 如果是装配体，则递归获取其子组件
    if component.GetSubComponentCount() > 0:
        sub_comps = component.GetSubComponents()
        for sub_comp in sub_comps:
            names.extend(get_all_components_names(sub_comp, level + 1))
    
    return names

def get_info():
    """
    获取SolidWorks当前装配体中所有子装配体和零件的名称
    """
    # 初始化COM库
    pythoncom.CoInitialize()

    # 连接到SolidWorks应用程序
    try:
        sw_app = win32com.client.Dispatch("SldWorks.Application")
    except Exception as e:
        print("无法连接到 SolidWorks，请确保软件已启动。")
        print(f"错误信息: {e}")
        return

    # 获取活动文档
    active_doc = sw_app.ActiveDoc
    if not active_doc:
        print("没有活动的文档。")
        return

    # 检查是否为装配体
    doc_type = active_doc.GetType
    if doc_type != 2:  # 2 表示装配体 (swDocASSEMBLY)
        print("当前文档不是装配体。")
        return

    # 获取装配体接口
    asm_doc = active_doc  # IAssemblyDoc 接口可直接从 IDocument 转换使用

    # 获取顶层组件
    components = asm_doc.GetComponents(False)  # False 表示不递归，我们自己处理递归

    print("装配体组件结构：")
    all_names = []
    for comp in components:
        all_names.extend(get_all_components_names(comp, level=0))

    print(f"\n共找到 {len(all_names)} 个组件。")

# 主函数调用示例
if __name__ == "__main__":
    get_info()