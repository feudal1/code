# change_all_text_to_blue.py
from pyautocad import Autocad
import pythoncom
import time

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

def change_text_entities_color(entities):
    """
    将文字实体的颜色更改为蓝色（颜色索引为5）
    
    Args:
        entities: 实体对象列表
    
    Returns:
        int: 成功更改颜色的文字对象数量
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
    
    blue_color_index = 5  # AutoCAD中蓝色的颜色索引
    
    for i, entity in enumerate(entities):
        try:
            # 检查实体类型是否为文字相关类型
            if entity.ObjectName in text_entity_types:
                # 更改颜色
                entity.Color = blue_color_index
                changed_count += 1
                print(f"已将对象 {i+1} ({entity.ObjectName}) 的颜色更改为蓝色")
        except Exception as e:
            print(f"更改对象 {i+1} ({entity.ObjectName if hasattr(entity, 'ObjectName') else 'Unknown'}) 的颜色时出错: {e}")
            continue
    
    return changed_count

def main():
    """
    主函数 - 将模型空间中的所有文字更改为蓝色
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
        # 直接获取模型空间中的所有对象
        print("获取模型空间中的所有对象...")
        entities = get_all_entities_from_model_space(doc)
        
        if entities:
            print(f"检测到 {len(entities)} 个对象")
            
            # 在处理文字颜色之前，先分解所有块引用
            print("开始分解其中的块引用...")
            entities = explode_all_blocks_in_entities(entities)
            print(f"分解完成后共有 {len(entities)} 个对象")
            
            # 对所有实体应用颜色修改
            print("处理所有对象中的文字...")
            changed_count = change_text_entities_color(entities)
            
            if changed_count > 0:
                print(f"成功更改 {changed_count} 个文字对象的颜色为蓝色")
            else:
                print("未找到需要更改颜色的文字对象")
        else:
            print("模型空间中没有对象")
            
    except Exception as e:
        print(f"处理对象时出错: {e}")

if __name__ == "__main__":
    main()