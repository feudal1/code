import win32com.client
import pythoncom


def get_all_face():
    """
    获取SolidWorks当前模型中所有面的信息
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

    # 判断文档类型并获取面信息
    doc_type = active_doc.GetType
    
    if doc_type == 1:  # swDocumentTypes_e.swPart
        # 零件文档
        part_doc = active_doc
        
        # 获取所有实体
        bodies = part_doc.GetBodies(0)
       
        if bodies:
            for body in bodies:
                if body:
                    # 获取面集合
                    faces = body.GetFaces()
                    
                    if faces:
                        for face in faces:
                            if face:
                              
                                
                                # 计算面积（转换为mm²）
                                area = round(face.GetArea * 1000000, 2)
                                
                                print(f"面面积 = {area} mm²")
                                
                    

    
    else:
        print("请打开零件或装配体文档")





# 主函数调用示例
if __name__ == "__main__":
    print("获取所有面信息:")
    get_all_face()
   