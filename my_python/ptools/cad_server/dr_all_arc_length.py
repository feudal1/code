# fixed_modified_dr_arc_length_centerline.py
from pyautocad import Autocad, APoint
import math
import time

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
    print("请选择对象")
    
    try:
        # 先尝试删除可能已存在的临时选择集
        try:
            existing_selection_set = doc.SelectionSets.Item("Temp_Selection_Set")
            existing_selection_set.Delete()
        except:
            # 如果不存在则忽略错误
            pass
        
        # 使用唯一名称避免冲突
        unique_name = f"Temp_Selection_Set_{int(time.time() * 1000) % 10000}"
        
        selection_set = doc.SelectionSets.Add(unique_name)
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

def process_arcs(entities):
    """
    从实体列表中筛选出圆弧对象作为中心线
    """
    arcs = []
    
    for i, entity in enumerate(entities):
        try:
            # 判断是否为圆弧
            if entity.ObjectName == "AcDbArc":
                arcs.append(entity)
                print(f"找到中心弧对象 {len(arcs)}")
            else:
                print(f"对象 {i} 类型: {entity.ObjectName}")
        except Exception as e:
            print(f"检查对象 {i} 时出错: {e}")
            continue
    
    return arcs

def create_concentric_arcs(doc, center_arcs, offset_distance=50):
    """
    基于中心弧创建内外同心圆弧，并分配到对应图层
    
    Args:
        doc: AutoCAD文档对象
        center_arcs: 中心弧对象列表
        offset_distance: 偏移距离
    
    Returns:
        tuple: (outer_arcs, inner_arcs) 外弧和内弧列表
    """
    # 确保内外弧图层存在
    ensure_layer_exists(doc, "外弧")
    ensure_layer_exists(doc, "内弧")
    
    outer_arcs = []
    inner_arcs = []
    
    for i, arc in enumerate(center_arcs):
        try:
            # 获取圆弧属性
            center = arc.Center
            radius = arc.Radius
            start_angle = arc.StartAngle
            end_angle = arc.EndAngle
            
            # 检查半径有效性
            if radius <= 0:
                print(f"第{i+1}条中心弧半径无效: {radius}")
                continue
            
            # 创建外弧（向外偏移）
            outer_radius = radius + offset_distance
            try:
                # 创建新的外弧对象
                outer_center = APoint(center[0], center[1])
                outer_start_angle = start_angle
                outer_end_angle = end_angle
                
                outer_arc = doc.ModelSpace.AddArc(outer_center, outer_radius, outer_start_angle, outer_end_angle)
                # 分配到外弧图层
                outer_arc.Layer = "外弧"
                outer_arcs.append(outer_arc)
            except Exception as e:
                print(f"创建第{i+1}条外弧时出错: {e}")
                continue
            
            # 创建内弧（向内偏移）
            inner_radius = radius - offset_distance
            # 确保内弧半径为正数
            if inner_radius <= 0:
                inner_radius = radius * 0.1  # 如果偏移过大，使用原半径的10%
                print(f"第{i+1}条内弧偏移过大，调整半径为: {inner_radius}")
            
            try:
                # 创建新的内弧对象
                inner_center = APoint(center[0], center[1])
                inner_start_angle = start_angle
                inner_end_angle = end_angle
                
                inner_arc = doc.ModelSpace.AddArc(inner_center, inner_radius, inner_start_angle, inner_end_angle)
                # 分配到内弧图层
                inner_arc.Layer = "内弧"
                inner_arcs.append(inner_arc)
            except Exception as e:
                print(f"创建第{i+1}条内弧时出错: {e}")
                continue
            
            print(f"成功为第{i+1}条中心弧创建内外弧并分配图层")
            
        except Exception as e:
            print(f"为第{i+1}条中心弧创建内外弧时出错: {e}")
            continue
    
    return outer_arcs, inner_arcs

def calculate_arc_properties_from_object(arc):
    """
    直接从Arc对象计算属性
    
    Args:
        arc: AutoCAD Arc对象
    
    Returns:
        dict: 包含弧长、弦长等信息的字典
    """
    try:
        # 获取基本属性
        center = arc.Center
        radius = arc.Radius
        start_angle = arc.StartAngle
        end_angle = arc.EndAngle
        
        # 计算弧长
        if end_angle >= start_angle:
            angle_diff = end_angle - start_angle
        else:
            angle_diff = (2 * math.pi - start_angle) + end_angle
            
        arc_length = radius * angle_diff
        
        # 计算起点和终点坐标
        start_x = center[0] + radius * math.cos(start_angle)
        start_y = center[1] + radius * math.sin(start_angle)
        end_x = center[0] + radius * math.cos(end_angle)
        end_y = center[1] + radius * math.sin(end_angle)
        
        # 计算弦长
        chord_length = math.sqrt((end_x - start_x)**2 + (end_y - start_y)**2)
        
        # 计算矢高（拱高）
        # 弦的中点
        mid_chord_x = (start_x + end_x) / 2
        mid_chord_y = (start_y + end_y) / 2
        
        # 圆心到弦中点的距离
        dist_center_to_chord_mid = math.sqrt((mid_chord_x - center[0])**2 + (mid_chord_y - center[1])**2)
        
        # 矢高 = 半径 - 圆心到弦的距离
        sagitta = radius - dist_center_to_chord_mid
        
        # 计算弧中点角度
        mid_angle = (start_angle + end_angle) / 2
        if end_angle < start_angle:
            mid_angle = (start_angle + end_angle + 2*math.pi) / 2
            if mid_angle > 2*math.pi:
                mid_angle -= 2*math.pi
                
        # 弧中点坐标
        arc_mid_x = center[0] + radius * math.cos(mid_angle)
        arc_mid_y = center[1] + radius * math.sin(mid_angle)
        
        return {
            'arc_length': arc_length,
            'chord_length': chord_length,
            'sagitta': sagitta,
            'start_point': (start_x, start_y),
            'end_point': (end_x, end_y),
            'mid_arc_point': (arc_mid_x, arc_mid_y),
            'center': center,
            'radius': radius,
            'start_angle': start_angle,
            'end_angle': end_angle,
            'mid_angle': mid_angle
        }
    except Exception as e:
        print(f"计算圆弧属性时出错: {e}")
        return None

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

def add_outer_arc_dimensions(doc, outer_arcs):
    """
    为外弧添加弧长标注
    
    Args:
        doc: AutoCAD文档对象
        outer_arcs: 外弧对象列表
    
    Returns:
        list: 成功添加的标注对象列表
    """
    # 确保外弧标注图层存在
    ensure_layer_exists(doc, "外弧标注")
    
    added_dims = []
    
    for i, arc in enumerate(outer_arcs):
        try:
            # 直接从Arc对象计算属性
            props = calculate_arc_properties_from_object(arc)
            if not props:
                continue
                
            # 添加弧长标注
            try:
                # 弧长标注需要圆心点、起点、终点和标注位置点
                center_point = APoint(props['center'][0], props['center'][1])
                start_point = APoint(props['start_point'][0], props['start_point'][1])
                end_point = APoint(props['end_point'][0], props['end_point'][1])
                
                # 标注点放在弧中点外侧更远的位置
                # 计算弧中点并向外延伸更多距离作为标注位置
                offset_distance = max(props['radius'] * 0.3, 10)  # 至少10个单位距离
                mid_offset_x = props['center'][0] + (props['radius'] + offset_distance) * math.cos(props['mid_angle'])
                mid_offset_y = props['center'][1] + (props['radius'] + offset_distance) * math.sin(props['mid_angle'])
                dim_point = APoint(mid_offset_x, mid_offset_y)
                
                # 添加真正的弧长标注
                arc_length_dim = doc.ModelSpace.AddDimArc(center_point, start_point, end_point, dim_point)
                arc_length_value = round(props['arc_length'])
                arc_length_dim.TextOverride = f"外弧长={arc_length_value}"
                # 分配到外弧标注图层
                arc_length_dim.Layer = "外弧标注"
                added_dims.append(arc_length_dim)
                print(f"成功为外弧 {i+1} 添加弧长标注: L={arc_length_value}")
            except Exception as e:
                print(f"为外弧 {i+1} 添加弧长标注时出错: {e}")
                
        except Exception as e:
            print(f"为外弧 {i+1} 添加标注时出错: {e}")
            continue
    
    return added_dims

def add_inner_arc_dimensions(doc, inner_arcs):
    """
    为内弧添加弦长和矢高标注
    
    Args:
        doc: AutoCAD文档对象
        inner_arcs: 内弧对象列表
    
    Returns:
        list: 成功添加的标注对象列表
    """
    added_dims = []
    
    # 确保相关图层存在
    ensure_layer_exists(doc, "矢高")
    ensure_layer_exists(doc, "内弧标注")
    
    for i, arc in enumerate(inner_arcs):
        try:
            # 直接从Arc对象计算属性
            props = calculate_arc_properties_from_object(arc)
            if not props:
                continue
                
            # 1. 添加弦长标注
            try:
                # 在弦下方标注弦长
                # 计算弦中点
                chord_mid_x = (props['start_point'][0] + props['end_point'][0]) / 2
                chord_mid_y = (props['start_point'][1] + props['end_point'][1]) / 2
                
                # 偏移一定距离来放置标注文本
                # 计算垂直于弦的单位向量
                chord_dx = props['end_point'][0] - props['start_point'][0]
                chord_dy = props['end_point'][1] - props['start_point'][1]
                chord_length = math.sqrt(chord_dx**2 + chord_dy**2)
                
                if chord_length > 0:
                    # 单位向量
                    unit_x = chord_dx / chord_length
                    unit_y = chord_dy / chord_length
                    
                    # 垂直向量（逆时针旋转90度）
                    perp_x = -unit_y
                    perp_y = unit_x
                    
                    # 标注点（弦中点向下偏移更远）
                    offset_distance = max(props['radius'] * 0.3, 15)  # 至少10个单位距离
                    dim_x = chord_mid_x + perp_x * offset_distance
                    dim_y = chord_mid_y + perp_y * offset_distance
                    
                    chord_dim_point = APoint(dim_x, dim_y)
                    
                    # 添加弦长标注
                    chord_start = APoint(props['start_point'][0], props['start_point'][1])
                    chord_end = APoint(props['end_point'][0], props['end_point'][1])
                    
                    chord_length_dim = doc.ModelSpace.AddDimAligned(chord_start, chord_end, chord_dim_point)
                    chord_length_value = round(props['chord_length'])
                    chord_length_dim.TextOverride = f"内弦长={chord_length_value}"
                    
                    # 控制文字位置
                    chord_length_dim.TextMovement = 2  # 固定文字位置
                    chord_length_dim.TextPosition = chord_dim_point
                    # 分配到内弧标注图层
                    chord_length_dim.Layer = "内弧标注"
                    
                    added_dims.append(chord_length_dim)
                    print(f"成功为内弧 {i+1} 添加弦长标注: 弦长={chord_length_value}")
            except Exception as e:
                print(f"为内弧 {i+1} 添加弦长标注时出错: {e}")
            
            # 2. 添加矢高标注
            try:
                # 矢高标注放在弦的中垂线上
                chord_mid_x = (props['start_point'][0] + props['end_point'][0]) / 2
                chord_mid_y = (props['start_point'][1] + props['end_point'][1]) / 2
                
                # 圆心到弦中点的向量
                to_center_x = props['center'][0] - chord_mid_x
                to_center_y = props['center'][1] - chord_mid_y
                to_center_length = math.sqrt(to_center_x**2 + to_center_y**2)
                
                if to_center_length > 0:
                    # 单位向量
                    unit_x = to_center_x / to_center_length
                    unit_y = to_center_y / to_center_length
                    
                    # 矢高标注点（放在更精确的中间位置）
                    offset_distance = max(props['radius'] * 0.3, 15)
                    dim_x = chord_mid_x + unit_x * offset_distance
                    dim_y = chord_mid_y - unit_y * offset_distance/2
                    
                    sagitta_dim_point = APoint(dim_x, dim_y)
                    
                    # 矢高起点（弦中点）
                    sagitta_start = APoint(chord_mid_x, chord_mid_y)
                    
                    # 矢高终点（弧中点）
                    sagitta_end = APoint(props['mid_arc_point'][0], props['mid_arc_point'][1])
                    
                    # 添加矢高标注
                    sagitta_dim = doc.ModelSpace.AddDimAligned(sagitta_start, sagitta_end, sagitta_dim_point)
                    sagitta_value = round(props['sagitta'])
                    sagitta_dim.TextOverride = f"内矢高={sagitta_value}"
                    
                    # 设置图层为"矢高"
                    sagitta_dim.Layer = "矢高"
                    
                    # 控制文字位置
                    sagitta_dim.TextMovement = 2  # 固定文字位置
                    sagitta_dim.TextPosition = sagitta_dim_point
                    
                    added_dims.append(sagitta_dim)
                    print(f"成功为内弧 {i+1} 添加矢高标注: 矢高={sagitta_value}")
            except Exception as e:
                print(f"为内弧 {i+1} 添加矢高标注时出错: {e}")
                
        except Exception as e:
            print(f"为内弧 {i+1} 添加标注时出错: {e}")
            continue
    
    return added_dims

def add_inner_arc_radius_dimensions(doc, inner_arcs):
    """
    为内弧添加半径标注
    
    Args:
        doc: AutoCAD文档对象
        inner_arcs: 内弧对象列表
    
    Returns:
        list: 成功添加的标注对象列表
    """
    # 确保内弧半径标注图层存在
    ensure_layer_exists(doc, "内弧半径标注")
    
    added_dims = []
    
    for i, arc in enumerate(inner_arcs):
        try:
            # 直接从Arc对象计算属性
            props = calculate_arc_properties_from_object(arc)
            if not props:
                continue
                
            # 添加半径标注
            try:  
                # 半径标注需要圆心点、弧上一点和引线长度  
                center_point = APoint(props['center'][0], props['center'][1])  
                
                # 在弧上选择一个点作为半径标注的引线点（使用中点）  
                mid_arc_point = APoint(props['mid_arc_point'][0], props['mid_arc_point'][1])  
                
                # 标注点放在从圆心到弧点延长线上的位置  
                radius = props['radius']  
                offset_distance = max(radius * 0.2, 10)  # 至少10个单位距离  
                
                # 添加半径标注 - 使用三个参数  
                radius_dim = doc.ModelSpace.AddDimRadial(center_point, mid_arc_point, offset_distance)  
                radius_value = round(props['radius'])  
                radius_dim.TextOverride = f"内R{radius_value}"  
                # 分配到内弧半径标注图层
                radius_dim.Layer = "内弧半径标注"
                
                added_dims.append(radius_dim)  
                print(f"成功为内弧 {i+1} 添加半径标注: R={radius_value}")  
            except Exception as e:  
                print(f"为内弧 {i+1} 添加半径标注时出错: {e}")
                
        except Exception as e:
            print(f"为内弧 {i+1} 添加标注时出错: {e}")
            continue
    
    return added_dims

def main():
    """
    主函数 - 基于选中的中心弧创建内外弧并添加相应标注
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
        
        # 筛选出圆弧对象作为中心线
        center_arcs = process_arcs(entities)
        
        if not center_arcs:
            print("没有找到中心弧对象")
            return
        
        print(f"找到 {len(center_arcs)} 条中心弧")
        
        # 确保中心弧图层存在并将中心弧移动到该图层
        ensure_layer_exists(doc, "中心弧")
        for arc in center_arcs:
            try:
                arc.Layer = "中心弧"
            except Exception as e:
                print(f"设置中心弧图层时出错: {e}")
        
        # 获取用户输入的偏移距离
        try:
            offset_input = input("请输入偏移距离（默认为50）: ")
            if offset_input.strip() == "":
                offset_distance = 50
            else:
                offset_distance = float(offset_input)
            print(f"使用偏移距离: {offset_distance}")
        except ValueError:
            print("输入无效，使用默认偏移距离50")
            offset_distance = 50
        except KeyboardInterrupt:
            print("用户取消输入，使用默认偏移距离50")
            offset_distance = 50
        
        # 基于中心弧创建内外弧
        outer_arcs, inner_arcs = create_concentric_arcs(doc, center_arcs, offset_distance=offset_distance)
        
        if not outer_arcs and not inner_arcs:
            print("未能创建任何内外弧")
            return
        
        print(f"创建了 {len(outer_arcs)} 条外弧和 {len(inner_arcs)} 条内弧")
        
        # 为外弧添加弧长标注
        outer_dims = add_outer_arc_dimensions(doc, outer_arcs)
        
        # 为内弧添加弦长和矢高标注
        inner_dims = add_inner_arc_dimensions(doc, inner_arcs)
        
        # 为内弧添加半径标注
        radius_dims = add_inner_arc_radius_dimensions(doc, inner_arcs)
        
        total_dims = len(outer_dims) + len(inner_dims) + len(radius_dims)
        if total_dims > 0:
            print(f"成功添加了 {total_dims} 个标注")
        else:
            print("未能添加任何标注")
            
    except Exception as e:
        print(f"处理对象时出错: {e}")
if __name__ == "__main__":
    main()