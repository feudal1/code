# change_all_text_to_standard_style.py
from pyautocad import Autocad
import pythoncom
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

def explode_all_blocks_in_entities(entities):
    """
    分解选中对象中的所有块引用
    
    Args:
        entities: 实体对象列表
    
    Returns:
        list: 分解后的新实体列表
    """
    exploded_entities = []
    new_entities_count = 0
    
    # 第一步：分解所有块引用
    for i, entity in enumerate(entities):
        try:
            if entity.ObjectName == "AcDbBlockReference":
                print(f"分解块引用 {i+1}: {entity.Name}")
                # 尝试分解块
                exploded_items = entity.Explode()
                new_entities_count += 1
                print(f"  成功分解块引用，产生 {exploded_items.Count if hasattr(exploded_items, 'Count') else '未知'} 个新对象")
            else:
                exploded_entities.append(entity)
        except Exception as e:
            print(f"分解块引用 {i+1} 时出错: {e}")
            # 即使分解失败也保留原始实体
            exploded_entities.append(entity)
    
    if new_entities_count > 0:
        print(f"共分解了 {new_entities_count} 个块引用")
        # 重新获取模型空间中的所有对象
        return get_all_entities_from_model_space(entities[0].Application.Document)
    else:
        return entities

def get_all_entities_from_model_space(doc):
    """
    从模型空间获取所有实体对象
    
    Args:
        doc: AutoCAD文档对象
    
    Returns:
        list: 所有实体对象列表
    """
    try:
        ms = doc.ModelSpace
        entities = []
        for i in range(ms.Count):
            try:
                entity = ms.Item(i)
                entities.append(entity)
            except Exception as e:
                print(f"无法访问模型空间对象 {i}: {e}")
        return entities
    except Exception as e:
        print(f"无法访问模型空间: {e}")
        return []

def set_text_style_to_standard(doc):
    """
    设置文字样式为标准样式
    
    Args:
        doc: AutoCAD文档对象
    """
    try:
        # 获取标准文字样式
        text_styles = doc.TextStyles
        standard_style = None
        
        # 查找Standard样式
        for i in range(text_styles.Count):
            style = text_styles.Item(i)
            if style.Name == "Standard":
                standard_style = style
                break
        
        if standard_style:
            # 设置为当前文字样式
            doc.SetVariable("TEXTSTYLE", "Standard")
            print("已将文字样式设置为Standard")
            return standard_style
        else:
            print("未找到Standard文字样式")
            return None
    except Exception as e:
        print(f"设置文字样式时出错: {e}")
        return None

def change_text_entities_style(entities, standard_style):
    """
    将文字实体的文字样式更改为标准样式
    
    Args:
        entities: 实体对象列表
        standard_style: 标准文字样式对象
    
    Returns:
        int: 成功更改样式的文字对象数量
    """
    changed_count = 0
    
    # 支持的文字实体类型
    text_entity_types = [
        "AcDbText",           # 单行文字
        "AcDbMText",          # 多行文字
        "AcDbDimension",      # 标注
        "AcDbLeader",         # 引线
        "AcDbMLeader"         # 多重引线
    ]
    
    for i, entity in enumerate(entities):
        try:
            # 检查实体类型是否为文字相关类型
            if entity.ObjectName in text_entity_types:
                # 更改文字样式
                if hasattr(entity, 'StyleName'):
                    old_style = entity.StyleName
                    entity.StyleName = "Standard"
                    changed_count += 1
                    print(f"已将对象 {i+1} ({entity.ObjectName}) 的文字样式从 '{old_style}' 更改为 'Standard'")
                
                # 对于标注对象，还需要处理标注文字样式
                elif entity.ObjectName == "AcDbDimension":
                    old_style = entity.DimensionTextStyle
                    entity.DimensionTextStyle = "Standard"
                    changed_count += 1
                    print(f"已将标注对象 {i+1} 的文字样式从 '{old_style}' 更改为 'Standard'")
                    
                # 对于引线对象
                elif entity.ObjectName == "AcDbLeader":
                    try:
                        old_style = entity.DimensionTextStyle
                        entity.DimensionTextStyle = "Standard"
                        changed_count += 1
                        print(f"已将引线对象 {i+1} 的文字样式从 '{old_style}' 更改为 'Standard'")
                    except:
                        # 某些引线可能没有DimensionTextStyle属性
                        pass
                        
        except Exception as e:
            print(f"更改对象 {i+1} ({entity.ObjectName if hasattr(entity, 'ObjectName') else 'Unknown'}) 的文字样式时出错: {e}")
            continue
    
    return changed_count

def process_all_text_in_model_space(doc, standard_style):
    """
    处理模型空间中的所有文字对象
    
    Args:
        doc: AutoCAD文档对象
        standard_style: 标准文字样式对象
    
    Returns:
        int: 成功更改样式的文字对象数量
    """
    changed_count = 0
    
    try:
        ms = doc.ModelSpace
        print(f"处理模型空间中的 {ms.Count} 个对象...")
        
        for i in range(ms.Count):
            try:
                entity = ms.Item(i)
                # 检查实体类型是否为文字相关类型
                text_entity_types = [
                    "AcDbText",           # 单行文字
                    "AcDbMText",          # 多行文字
                    "AcDbDimension",      # 标注
                    "AcDbLeader",         # 引线
                    "AcDbMLeader"         # 多重引线
                ]
                
                if entity.ObjectName in text_entity_types:
                    # 更改文字样式
                    if hasattr(entity, 'StyleName'):
                        old_style = entity.StyleName
                        entity.StyleName = "Standard"
                        changed_count += 1
                        print(f"已将模型空间对象 {i+1} ({entity.ObjectName}) 的文字样式从 '{old_style}' 更改为 'Standard'")
                    
                    # 对于标注对象
                    elif entity.ObjectName == "AcDbDimension":
                        old_style = entity.DimensionTextStyle
                        entity.DimensionTextStyle = "Standard"
                        changed_count += 1
                        print(f"已将模型空间标注对象 {i+1} 的文字样式从 '{old_style}' 更改为 'Standard'")
                        
                    # 对于引线对象
                    elif entity.ObjectName == "AcDbLeader":
                        try:
                            old_style = entity.DimensionTextStyle
                            entity.DimensionTextStyle = "Standard"
                            changed_count += 1
                            print(f"已将模型空间引线对象 {i+1} 的文字样式从 '{old_style}' 更改为 'Standard'")
                        except:
                            # 某些引线可能没有DimensionTextStyle属性
                            pass
                            
            except Exception as e:
                print(f"处理模型空间对象 {i} 时出错: {e}")
                continue
                
    except Exception as e:
        print(f"访问模型空间时出错: {e}")
    
    return changed_count

def main():
    """
    主函数 - 将选中对象或模型空间中的所有文字更改为标准样式
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
        # 设置文字样式为标准样式
        standard_style = set_text_style_to_standard(doc)
        
        if not standard_style:
            print("无法获取标准文字样式，程序退出")
            return
        
        # 获取要处理的对象
        entities = get_selection_or_model_space(acad, doc)
        
        # 在处理文字样式之前，先分解所有块引用
        if entities:
            print(f"检测到 {len(entities)} 个选中对象，开始分解其中的块引用...")
            entities = explode_all_blocks_in_entities(entities)
            print(f"分解完成后共有 {len(entities)} 个对象")
        
        changed_count = 0
        if entities:
            print(f"处理 {len(entities)} 个对象中的文字...")
            changed_count = change_text_entities_style(entities, standard_style)
        else:
            # 如果没有选中对象，则处理整个模型空间
            print("未检测到选择集，处理模型空间中的所有文字对象...")
            changed_count = process_all_text_in_model_space(doc, standard_style)
        
        if changed_count > 0:
            print(f"成功更改 {changed_count} 个文字对象的样式为Standard")
        else:
            print("未找到需要更改文字样式的对象")
            
    except Exception as e:
        print(f"处理对象时出错: {e}")

if __name__ == "__main__":
    main()