# circular_radius_offset_cad_input.py
from pyautocad import Autocad, APoint
import math

def get_selection_or_model_space(acad, doc):
    """
    获取用户选择的对象，如果没有选择则遍历模型空间
    """
    # 检查是否已有选择集
    try:
        # 遍历现有的选择集
        for i in range(doc.SelectionSets.Count):
            selection_set = doc.SelectionSets.Item(i)
            if selection_set.Count > 0:
                print(f"使用现有选择集: {selection_set.Count} 个对象")
                # 收集选中的对象
                selection = []
                for j in range(selection_set.Count):
                    try:
                        entity = selection_set.Item(j)
                        selection.append(entity)
                    except Exception as e:
                        print(f"无法访问选中对象 {j}: {e}")
                return selection
    except Exception as e:
        print(f"检查现有选择集时出错: {e}")
    
    # 如果没有现成的选择集，则提示用户选择
    print("请选择圆形对象")
    
    try:
        # 先尝试删除可能已存在的临时选择集
        try:
            existing_selection_set = doc.SelectionSets.Item("Temp_Circle_Selection")
            existing_selection_set.Delete()
        except:
            # 如果不存在则忽略错误
            pass
        
        selection_set = doc.SelectionSets.Add("Temp_Circle_Selection")
        selection_set.SelectOnScreen()
        
        if selection_set.Count > 0:
            print(f"检测到 {selection_set.Count} 个选中对象")
            # 收集选中的对象
            selection = []
            for i in range(selection_set.Count):
                try:
                    entity = selection_set.Item(i)
                    selection.append(entity)
                except Exception as e:
                    print(f"无法访问选中对象 {i}: {e}")
            selection_set.Delete()
            return selection
        else:
            selection_set.Delete()
    except Exception as e:
        print(f"无法获取选择集: {e}")
    
    # 如果没有选择对象，遍历模型空间
    print("未检测到选择集，遍历模型空间...")
    try:
        ms = doc.ModelSpace
        selection = []
        for i in range(ms.Count):
            try:
                entity = ms.Item(i)
                selection.append(entity)
            except Exception as e:
                print(f"无法访问模型空间对象 {i}: {e}")
        return selection
    except Exception as e:
        print(f"无法访问模型空间: {e}")
        return []
def get_user_input_with_direction(acad, doc):
    """
    使用AutoCAD的输入功能获取用户输入的偏移距离和方向
    
    Args:
        acad: Autocad实例
        doc: AutoCAD文档对象
        
    Returns:
        float: 偏移距离（已根据方向调整符号）
    """
    try:
        # 获取偏移距离
        offset_value = acad.doc.Utility.GetReal("请输入内圆偏移距离: ")
        
        return abs(offset_value)  # 只取绝对值，因为我们总是向内偏移创建内圆
        
    except Exception as e:
        print(f"获取用户输入时出错: {e}")
        print("使用默认偏移距离10")
        return 10
def process_circles(entities):
    """
    从实体列表中筛选出圆形对象
    """
    circles = []
    
    for i, entity in enumerate(entities):
        try:
            # 判断是否为圆形
            if entity.ObjectName == "AcDbCircle":
                circles.append(entity)
                print(f"找到圆形对象 {len(circles)}")
            else:
                print(f"对象 {i} 类型: {entity.ObjectName}")
        except Exception as e:
            print(f"检查对象 {i} 时出错: {e}")
            continue
    
    return circles

def ensure_layer_exists(doc, layer_name):
    """
    确保指定图层存在，如果不存在则创建
    
    Args:
        doc: AutoCAD文档对象
        layer_name: 图层名称
    
    Returns:
        layer: 图层对象
    """
    try:
        # 检查图层是否已存在
        layers = doc.Layers
        for i in range(layers.Count):
            if layers.Item(i).Name == layer_name:
                return layers.Item(i)
        
        # 如果不存在则创建新图层
        new_layer = layers.Add(layer_name)
        return new_layer
    except Exception as e:
        print(f"创建或获取图层 {layer_name} 时出错: {e}")
        return None

# 在现有代码基础上，主要修改以下几个函数:

def create_concentric_circles(doc, circles, offset_distance):
    """
    基于选定圆形创建两个偏移后的同心圆（内圆和外圆），并分配到对应图层
    
    Args:
        doc: AutoCAD文档对象
        circles: 園形对象列表
        offset_distance: 偏移距离
    
    Returns:
        tuple: (内圆列表, 外圆列表)
    """
    # 确保图层存在
    ensure_layer_exists(doc, "内圆")
    ensure_layer_exists(doc, "外圆")
    
    inner_circles = []
    outer_circles = []
    
    for i, circle in enumerate(circles):
        try:
            # 获取圆形属性
            center = circle.Center
            radius = circle.Radius
            
            # 检查半径有效性
            if radius <= 0:
                print(f"第{i+1}个圆形半径无效: {radius}")
                continue
            
            # 计算内圆半径（向内偏移）
            inner_radius = radius - offset_distance
            
            # 确保内圆半径为正数
            if inner_radius <= 0:
                print(f"第{i+1}个圆形内偏后半径为负数或零: {inner_radius}")
                continue
            
            # 计算外圆半径（向外偏移）
            outer_radius = radius + offset_distance
            
            # 创建内圆
            new_center = APoint(center[0], center[1])
            inner_circle = doc.ModelSpace.AddCircle(new_center, inner_radius)
            inner_circle.Layer = "内圆"
            inner_circles.append(inner_circle)
            
            # 创建外圆
            outer_circle = doc.ModelSpace.AddCircle(new_center, outer_radius)
            outer_circle.Layer = "外圆"
            outer_circles.append(outer_circle)
            
            print(f"成功为第{i+1}个圆形创建同心圆，内圆半径: {inner_radius:.2f}, 外圆半径: {outer_radius:.2f}")
            
        except Exception as e:
            print(f"为第{i+1}个圆形创建同心圆时出错: {e}")
            continue
    
    return inner_circles, outer_circles

def add_radius_dimensions(doc, inner_circles, outer_circles, original_circles):
    """
    为圆形添加半径标注，内圆标注"内"，外圆标注"外"
    
    Args:
        doc: AutoCAD文档对象
        inner_circles: 内圆对象列表
        outer_circles: 外圆对象列表
        original_circles: 原始圆对象列表
    
    Returns:
        list: 成功添加的标注对象列表
    """
    # 确保图层存在
    ensure_layer_exists(doc, "内圆半径标注")
    ensure_layer_exists(doc, "外圆半径标注")
    ensure_layer_exists(doc, "间距标注")
    ensure_layer_exists(doc, "周长标注")
    
    added_dims = []
    
    for i, (inner_circle, outer_circle, original_circle) in enumerate(zip(inner_circles, outer_circles, original_circles)):
        try:
            # 获取圆形属性
            center = inner_circle.Center
            inner_radius = inner_circle.Radius
            outer_radius = outer_circle.Radius
            original_radius = original_circle.Radius
            
            # 为内圆添加半径标注（保持当前的逻辑）
            offset_distance = -max(inner_radius * 0.1, 5)  # 向内偏移
            point_on_circle = APoint(center[0] + inner_radius + offset_distance, center[1])
            center_point = APoint(center[0], center[1])
            
            radius_dim = doc.ModelSpace.AddDimRadial(center_point, point_on_circle, offset_distance)
            radius_value = round(inner_radius)
            radius_dim.TextOverride = f"内R{radius_value}"
            radius_dim.Layer = "内圆半径标注"
            # 设置字体大小为0.5
            radius_dim.TextHeight = 0.5
            added_dims.append(radius_dim)
            print(f"成功为内圆 {i+1} 添加半径标注: 内R{radius_value}")

            # 添加内外圆之间的距离标注（保持当前的逻辑）
            distance = abs(outer_radius - inner_radius)
            # 在两圆之间中点位置添加线性标注
            mid_radius = (inner_radius + outer_radius) / 2
            start_point = APoint(center[0] + inner_radius, center[1])
            end_point = APoint(center[0] + outer_radius, center[1])
            # 将标注放在圆内
            text_point = APoint(center[0] + mid_radius, center[1] - abs(offset_distance)/2)
            
            distance_dim = doc.ModelSpace.AddDimAligned(start_point, end_point, text_point)
            distance_dim.TextOverride = f"{distance:.0f}"
            distance_dim.Layer = "间距标注"
            # 设置字体大小为0.5
            distance_dim.TextHeight = 0.5
            added_dims.append(distance_dim)
            print(f"成功添加间距标注: {distance:.0f}")
            
            # 使用原来的周长标注代码，只修改字体大小为0.5
            perimeter = 2 * math.pi * outer_radius  
            # 在外圆下方添加周长标注（恢复为原来的逻辑）
            offset_distance_orig = max(inner_radius * 0.2, 10)  # 原来的offset计算方式
            perimeter_text_point = APoint(center[0], center[1] - outer_radius - offset_distance_orig)  
            # 创建文本而不是尺寸标注来显示周长  
            # 使用 MText 替代 Text  
            text_width = offset_distance_orig*15 # 设置文本宽度,根据需要调整  
            perimeter_mtext = doc.ModelSpace.AddMText(perimeter_text_point, text_width, f"外周长:{perimeter:.0f}")  
            perimeter_mtext.Height = 500  # 字体高度改为0.5
            perimeter_mtext.Layer = "周长标注"  
            added_dims.append(perimeter_mtext)  
            print(f"成功添加周长标注: {perimeter:.0f}")
                
        except Exception as e:
            print(f"为圆形 {i+1} 添加标注时出错: {e}")
            continue
    
    return added_dims


def main():
    """
    主函数 - 基于选中的圆形创建两个偏移后的同心圆（内圆和外圆）并添加标注
    """
    # 连接到正在运行的 AutoCAD
    try:
        acad = Autocad(create_if_not_exists=True)
        doc = acad.doc
        print(f"成功连接到 AutoCAD 文档: {doc.Name}")
    except Exception as e:
        print("无法连接到 AutoCAD:", e)
        return
    
    try:
        # 获取要处理的对象
        entities = get_selection_or_model_space(acad, doc)
        
        if not entities:
            print("没有找到任何对象")
            return
        
        print(f"处理 {len(entities)} 个对象")
        
        # 筛选出圆形对象
        original_circles = process_circles(entities)
        
        if not original_circles:
            print("没有找到圆形对象")
            return
        
        print(f"找到 {len(original_circles)} 个圆形")
        
        # 确保中心圆图层存在并将中心圆移动到该图层
        ensure_layer_exists(doc, "中心圆")
        for circle in original_circles:
            try:
                circle.Layer = "中心圆"
            except Exception as e:
                print(f"设置中心圆图层时出错: {e}")
        
        # 使用AutoCAD输入功能获取偏移距离
        try:
            offset_distance = get_user_input_with_direction(acad, doc)
            print(f"使用偏移距离: {offset_distance}")
        except Exception as e:
            print(f"获取用户输入时出错: {e}")
            offset_distance = 10
            print("使用默认偏移距离10")
        
        # 基于选定圆形创建两个偏移后的同心圆（内圆和外圆）
        inner_circles, outer_circles = create_concentric_circles(doc, original_circles, offset_distance)
        
        if not inner_circles:
            print("未能创建任何同心圆")
            return
        
        print(f"创建了 {len(inner_circles)} 个内圆和 {len(outer_circles)} 个外圆")
        
        # 为同心圆添加标注
        all_dims = add_radius_dimensions(doc, inner_circles, outer_circles, original_circles)
        
        if len(all_dims) > 0:
            print(f"成功添加了 {len(all_dims)} 个标注")
        else:
            print("未能添加任何标注")
            
    except Exception as e:
        print(f"处理对象时出错: {e}")

if __name__ == "__main__":
    main()