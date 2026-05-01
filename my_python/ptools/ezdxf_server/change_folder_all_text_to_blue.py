import os
import ezdxf
from ezdxf import units

def process_dxf_files(folder_path):
    """
    处理文件夹中的所有DXF文件，explode块并把文字变成蓝色
    
    Args:
        folder_path (str): 包含DXF文件的文件夹路径
    """
    # 检查文件夹是否存在
    if not os.path.exists(folder_path):
        print(f"文件夹不存在: {folder_path}")
        return
    
    # 获取所有DXF文件
    dxf_files = [f for f in os.listdir(folder_path) if f.lower().endswith('.dxf')]
    
    if not dxf_files:
        print(f"文件夹中没有找到DXF文件: {folder_path}")
        return
    
    print(f"找到 {len(dxf_files)} 个DXF文件")
    
    for dxf_file in dxf_files:
        file_path = os.path.join(folder_path, dxf_file)
        print(f"\n处理文件: {dxf_file}")
        
        try:
            # 读取DXF文件
            doc = ezdxf.readfile(file_path)
            msp = doc.modelspace()
            
            # 第一步：explode所有块
            print("  开始explode块...")
            exploded_count = explode_blocks_in_dxf(doc)
            print(f"  explode了 {exploded_count} 个块")
            
            # 第二步：将所有文字对象变为蓝色
            print("  开始更改文字颜色...")
            changed_count = change_text_color_to_blue(msp)
            print(f"  更改了 {changed_count} 个文字对象的颜色为蓝色")
            
            # 保存文件（可以保存为新文件或覆盖原文件）
            output_path = file_path.replace('.dxf', '_processed.dxf')
            doc.saveas(output_path)
            print(f"  文件已保存为: {output_path}")
            
        except Exception as e:
            print(f"  处理文件 {dxf_file} 时出错: {e}")

def explode_blocks_in_dxf(doc):
    """
    在DXF文档中explode所有块引用
    
    Args:
        doc: ezdxf文档对象
    
    Returns:
        int: explode的块数量
    """
    msp = doc.modelspace()
    exploded_count = 0
    
    # 收集所有INSERT实体（块引用）
    inserts = [entity for entity in msp if entity.dxftype() == 'INSERT']
    
    # 逐个explode块引用
    for insert in inserts:
        try:
            # Explode块引用
            msp.explode(insert)
            exploded_count += 1
        except Exception as e:
            print(f"    explode块 {insert.dxf.name} 时出错: {e}")
    
    return exploded_count

def change_text_color_to_blue(msp):
    """
    将模型空间中的所有文字对象颜色更改为蓝色
    
    Args:
        msp: 模型空间对象
    
    Returns:
        int: 更改颜色的文字对象数量
    """
    changed_count = 0
    
    # 支持的文字实体类型
    text_entity_types = ['TEXT', 'MTEXT', 'DIMENSION', 'LEADER']
    
    # 遍历所有实体
    for entity in msp:
        if entity.dxftype() in text_entity_types:
            try:
                # 设置颜色为蓝色（AutoCAD颜色索引5）
                entity.dxf.color = 5
                changed_count += 1
            except Exception as e:
                print(f"    更改 {entity.dxftype()} 颜色时出错: {e}")
    
    return changed_count

def main():
    """
    主函数 - 处理指定文件夹中的所有DXF文件
    """
    # 指定包含DXF文件的文件夹路径
    folder_path = input("请输入包含DXF文件的文件夹路径: ").strip()
    
    if not folder_path:
        folder_path = "."  # 默认当前目录
    
    process_dxf_files(folder_path)

if __name__ == "__main__":
    main()