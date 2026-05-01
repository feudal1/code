# dxf_explode_and_export.py
import tkinter as tk
from tkinter import filedialog, messagebox
import ezdxf
import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib.patches import Circle, Arc
import os
import logging

# 配置日志
logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(levelname)s - %(message)s')
logger = logging.getLogger(__name__)

def explode_all_blocks(msp):
    """
    递归分解模型空间中的所有块引用和多段线
    
    Args:
        msp: 模型空间对象
        
    Returns:
        tuple: (分解的块数量, 分解出的实体数量)
    """
    blocks_broken = 0
    exploded_entities = 0
    
    # 多次遍历直到没有更多的INSERT实体和可分解的多段线
    while True:
        # 收集所有需要分解的实体（INSERT和多段线）
        inserts = [entity for entity in msp if entity.dxftype() == 'INSERT']
        polylines = [entity for entity in msp if entity.dxftype() in ['POLYLINE', 'LWPOLYLINE']]
        
        # 如果没有需要分解的实体，则退出循环
        if not inserts and not polylines:
            break
            
        # 分解所有块引用
        for insert in inserts[:]:  # 使用切片复制避免在迭代时修改列表
            try:
                exploded = insert.explode()
                msp.delete_entity(insert)  # 删除已分解的实体
                blocks_broken += 1
                exploded_entities += len(exploded)
                logger.info(f"分解块 '{insert.dxf.name}'，获得 {len(exploded)} 个实体")
            except Exception as e:
                logger.warning(f"分解块时出错: {e}")
                
        # 分解所有多段线
        for polyline in polylines[:]:  # 使用切片复制避免在迭代时修改列表
            try:
                exploded = polyline.explode()
                msp.delete_entity(polyline)  # 删除已分解的实体
                blocks_broken += 1
                exploded_entities += len(exploded)
                logger.info(f"分解多段线，获得 {len(exploded)} 个实体")
            except Exception as e:
                logger.warning(f"分解多段线时出错: {e}")
                
    return blocks_broken, exploded_entities
def change_yellow_to_white(msp):
    """
    将模型空间中所有黄色实体改为白色
    
    Args:
        msp: 模型空间对象
        
    Returns:
        int: 修改的实体数量
    """
    yellow_color_code = 2  # 黄色在DXF中的颜色代码
    white_color_code = 7   # 白色在DXF中的颜色代码
    
    changed_count = 0
    for entity in msp:
        try:
            if hasattr(entity.dxf, 'color') and entity.dxf.color == yellow_color_code:
                entity.dxf.color = white_color_code
                changed_count += 1
        except Exception as e:
            logger.warning(f"修改实体颜色时出错: {e}")
            
    return changed_count

def export_to_pdf(doc, output_path):
    """
    将DXF文档导出为PDF
    
    Args:
        doc: ezdxf文档对象
        output_path: 输出PDF文件路径
        
    Returns:
        bool: 是否成功导出
    """
    try:
        msp = doc.modelspace()
        
        # 创建图形
        fig, ax = plt.subplots(figsize=(12, 12), dpi=150)
        all_x_coords = []
        all_y_coords = []
        
        # DXF颜色映射
        dxf_color_map = {
            0: 'black', 1: 'red', 2: 'yellow', 3: 'green', 4: 'cyan',
            5: 'blue', 6: 'magenta', 7: 'white', 8: '#a5a5a5', 9: '#c0c0c0',
            10: 'red', 11: '#ffaaaa', 12: '#bd0000', 13: '#bd7373', 14: '#800000',
            15: '#ff0000', 16: '#ffff00', 17: '#ffff73', 18: '#bda000', 19: '#bdae73',
            20: '#808000', 21: '#ffff00', 22: '#00ff00', 23: '#aaffaa', 24: '#00bd00',
            25: '#73bd73', 26: '#008000', 27: '#00ff00', 28: '#00ffff', 29: '#aaffff',
            30: '#00bfbf', 31: '#73bfbf', 32: '#008080', 33: '#00ffff', 34: '#0000ff',
            35: '#aaaaff', 36: '#0000bd', 37: '#7373bf', 38: '#000080', 39: '#0000ff',
            40: '#ff00ff', 41: '#ffaaff', 42: '#bd00bd', 43: '#bd73bd', 44: '#800080',
            45: '#ff00ff', 'default': 'black'
        }

        # 绘制实体
        for entity in msp:
            try:
                color = dxf_color_map.get(entity.dxf.color, dxf_color_map['default'])
            except:
                color = dxf_color_map['default']

            linewidth = 0.5

            if entity.dxftype() == 'LINE':
                start = entity.dxf.start
                end = entity.dxf.end
                ax.plot([start.x, end.x], [start.y, end.y], color=color, linewidth=linewidth)
                all_x_coords.extend([start.x, end.x])
                all_y_coords.extend([start.y, end.y])

            elif entity.dxftype() == 'CIRCLE':
                center = entity.dxf.center
                radius = entity.dxf.radius
                circle = Circle((center.x, center.y), radius, fill=False, color=color, linewidth=linewidth)
                ax.add_patch(circle)
                all_x_coords.extend([center.x - radius, center.x + radius])
                all_y_coords.extend([center.y - radius, center.y + radius])

            elif entity.dxftype() == 'ARC':
                center = entity.dxf.center
                radius = entity.dxf.radius
                start_angle = entity.dxf.start_angle
                end_angle = entity.dxf.end_angle
                if start_angle > end_angle:
                    end_angle += 360
                arc = Arc((center.x, center.y), 2*radius, 2*radius, angle=0,
                          theta1=start_angle, theta2=end_angle,
                          color=color, linewidth=linewidth)
                ax.add_patch(arc)
                all_x_coords.extend([center.x - radius, center.x + radius])
                all_y_coords.extend([center.y - radius, center.y + radius])

            elif entity.dxftype() in ['TEXT', 'MTEXT']:
                insert = entity.dxf.insert
                text = entity.dxf.text if entity.dxftype() == 'TEXT' else entity.text
                height = entity.dxf.height if hasattr(entity.dxf, 'height') else 0.5
                font_size = height * 0.3
                ax.text(insert.x, insert.y, text, color=color, fontsize=font_size)
                all_x_coords.append(insert.x)
                all_y_coords.append(insert.y)

        # 设置坐标范围
        if all_x_coords and all_y_coords:
            margin = 10
            ax.set_xlim(min(all_x_coords) - margin, max(all_x_coords) + margin)
            ax.set_ylim(min(all_y_coords) - margin, max(all_y_coords) + margin)
        else:
            ax.set_xlim(-10, 10)
            ax.set_ylim(-10, 10)

        ax.set_aspect('equal', adjustable='box')
        plt.tight_layout()

        # 保存为PDF
        os.makedirs(os.path.dirname(output_path), exist_ok=True)
        plt.savefig(output_path, bbox_inches='tight', pad_inches=0.1, dpi=150, format='pdf')
        plt.close(fig)
        plt.close('all')
        
        return True
        
    except Exception as e:
        logger.error(f"导出PDF时出错: {e}")
        return False

def process_dxf_file(input_path, output_folder):
    """
    处理DXF文件：explode所有实体，将黄色改为白色，并导出为PDF
    
    Args:
        input_path (str): 输入DXF文件路径
        output_folder (str): 输出文件夹路径
        
    Returns:
        dict: 处理结果
    """
    try:
        # 读取DXF文件
        doc = ezdxf.readfile(input_path)
        msp = doc.modelspace()
        
        # 分解所有可分解实体
        blocks_broken, entities_exploded = explode_all_blocks(msp)
        
        # 将黄色对象改为白色
        changed_entities = change_yellow_to_white(msp)
        
        # 准备输出路径
        filename = os.path.basename(input_path)
        name_without_ext = os.path.splitext(filename)[0]
        output_pdf_path = os.path.join(output_folder, f"{name_without_ext}_processed.pdf")
        
        # 导出为PDF
        export_success = export_to_pdf(doc, output_pdf_path)
        
        if export_success:
            return {
                "success": True,
                "message": f"处理完成:\n- 分解了 {blocks_broken} 个块\n- 展开了 {entities_exploded} 个实体\n- 修改了 {changed_entities} 个黄色对象\n- PDF已保存至: {output_pdf_path}",
                "blocks_broken": blocks_broken,
                "entities_exploded": entities_exploded,
                "changed_entities": changed_entities,
                "output_path": output_pdf_path
            }
        else:
            return {
                "success": False,
                "message": "处理完成，但导出PDF时出错"
            }
            
    except Exception as e:
        logger.error(f"处理文件时出错: {e}")
        return {
            "success": False,
            "message": f"处理文件时出错: {str(e)}"
        }

def select_file_and_process():
    """
    弹出文件选择对话框并处理选定的文件
    """
    root = tk.Tk()
    root.withdraw()  # 隐藏主窗口
    
    # 选择DXF文件
    file_path = filedialog.askopenfilename(
        title="选择DXF文件",
        filetypes=[("DXF files", "*.dxf"), ("All files", "*.*")]
    )
    
    if not file_path:
        messagebox.showinfo("取消", "未选择文件")
        return
    
    # 选择输出文件夹
    output_folder = filedialog.askdirectory(title="选择PDF输出文件夹")
    
    if not output_folder:
        messagebox.showinfo("取消", "未选择输出文件夹")
        return
    
    # 处理文件
    result = process_dxf_file(file_path, output_folder)
    
    # 显示结果
    if result["success"]:
        messagebox.showinfo("处理完成", result["message"])
    else:
        messagebox.showerror("处理失败", result["message"])

if __name__ == "__main__":
    select_file_and_process()