from pyautocad import Autocad, APoint, aDouble
import pythoncom 


def change_objects_color_by_window( min_point, max_point):
    """
    使用窗口选择更改指定范围内对象的颜色
    
    :param acad: Autocad 实例
    :param min_point: 窗口左下角点 (x, y, z)
    :param max_point: 窗口右上角点 (x, y, z)
    :return: 更改颜色的对象数量
    """
    pythoncom.CoInitialize()
    acad = Autocad()
    try:
    
        # 尝试删除已存在的临时选择集
        acad.doc.SelectionSets.Item("TempSS").Delete()
    except:
        pass
    
    # 创建新的选择集
    selection = acad.doc.SelectionSets.Add("TempSS")
    
    # 准备坐标点
    p1 = acad.aDouble(min_point[0], min_point[1], min_point[2])
    p2 = acad.aDouble(max_point[0], max_point[1], max_point[2])
    
    # 使用窗口选择 - 模式 1 = acSelectionSetWindow
    try:
        selection.Select(1, p1, p2)
    except:
        pass
    
    # 检查是否选中了对象
    if selection.Count == 0:
        selection.Delete()
        return 0
    
    # 更改选中对象的颜色
    changed_count = selection.Count
    for i in range(selection.Count):
        try:
            obj = selection.Item(i)
            obj.delete()
        except Exception as e:
            print(f"更改对象颜色时出错: {e}")
            changed_count -= 1
    
    # 清理选择集
    try:
        selection.Delete()
    except:
        pass
    
    return changed_count

# 使用示例
if __name__ == "__main__":
  
    # 先绘制删除区域边界（可选）
    
    count = change_objects_color_by_window( (0, 0, 0), (100, 100, 0))
    print(f"已删除 {count} 个对象")

