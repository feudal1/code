import pandas as pd
import ezdxf
from ezdxf import units

def create_dxf_with_layers(excel_file, output_file='output.dxf'):
    """
    读取Excel中的尺寸数据，按厚度分类创建不同图层的矩形
    
    Args:
        excel_file (str): Excel文件路径
        output_file (str): 输出DXF文件路径
    """
    # 读取Excel数据
    df = pd.read_excel(excel_file)
    
    # 创建DXF文档
    doc = ezdxf.new(dxfversion='R2010')
    doc.units = units.MM
    
    # 创建图层（按厚度分类）
    thicknesses = df['Thickness'].unique()
    colors = [1, 2, 3, 4, 5, 6]  # 不同颜色
    
    for i, thickness in enumerate(thicknesses):
        color = colors[i % len(colors)]
        doc.layers.new(name=f'Thickness_{thickness}', dxfattribs={'color': color})
    
    # 获取模型空间
    msp = doc.modelspace()
    
    # 按行处理数据
    x_position = 0
    for index, row in df.iterrows():
        length = row['Length']
        width = row['Width']
        thickness = row['Thickness']
        
        # 创建矩形
        points = [
            (x_position, 0),
            (x_position + length, 0),
            (x_position + length, width),
            (x_position, width)
        ]
        
        # 在对应厚度的图层上添加矩形
        msp.add_lwpolyline(
            points, 
            close=True, 
            dxfattribs={'layer': f'Thickness_{thickness}'}
        )
        
        # 添加标注文本
        msp.add_text(
            f'{length}×{width}×{thickness}',
            dxfattribs={
                'insert': (x_position, -10),
                'height': 5,
                'layer': f'Thickness_{thickness}'
            }
        )
        
        # 更新下一个矩形的X位置
        x_position += length + 20
    
    # 保存文件
    doc.saveas(output_file)
    print(f"DXF文件已保存为 {output_file}")

# 使用方法
# create_dxf_with_layers('dimensions.xlsx', 'output.dxf')