# change_text_size.py
from pyautocad import Autocad, APoint

def format_decimal(value):
    """
    智能格式化数值:整数保留0位,非整数保留1位小数
    
    :param value: 要格式化的数值
    :return: 格式化后的数值(整数或保留1位小数的浮点数)
    """
    if value == int(value):
        return int(value)
    else:
        return round(value, 1)

def get_selected_texts(acad):
    """
    获取用户选择的文本对象(TEXT和MTEXT)
    """
    try:
        # 使用 PyAutoCAD 的 get_selection 方法
        selection = acad.get_selection("请选择文本对象")
        
        texts = []
        for i in range(selection.Count):
            try:
                obj = selection.Item(i)
                # 检查是否为文本对象
                if obj.ObjectName in ["AcDbText", "AcDbMText"]:
                    texts.append(obj)
            except Exception as e:
                print(f"无法访问选中对象 {i}: {e}")
        
        return texts
    except Exception as e:
        print(f"获取选择集时出错: {e}")
        return []

def get_current_text_height(texts):
    """
    获取当前选中文本的高度(取第一个作为参考)
    """
    if texts:
        try:
            # TEXT和MTEXT获取高度的方式不同
            if texts[0].ObjectName == "AcDbText":
                current_height = texts[0].Height
            else:  # MTEXT
                current_height = texts[0].TextHeight
            return format_decimal(current_height)
        except Exception as e:
            print(f"获取当前文字高度时出错: {e}")
    return 2.5

def get_text_height_from_cad(acad, current_height):
    """
    通过CAD命令行获取用户输入的文字高度
    """
    try:
        # 显示提示信息
        acad.prompt(f"当前文字高度: {current_height}\n")
        
        # GetReal 只接受一个参数(提示字符串)
        prompt = f"请输入新的文字高度 <{current_height}>: "
        result = acad.doc.Utility.GetReal(prompt)
        return result
    except Exception as e:
        print(f"从CAD获取输入时出错: {e}")
        return None

def modify_text_properties(acad, texts, text_height=None):
    """
    修改文本的属性(文字高度)
    
    :param acad: Autocad 实例
    :param texts: 文本对象列表
    :param text_height: 新的文字高度(可选)
    """
    modified_count = 0
    
    for i, text_obj in enumerate(texts):
        try:
            if text_height is not None:
                # 格式化文字高度
                formatted_height = format_decimal(text_height)
                
                # TEXT和MTEXT设置高度的方式不同
                if text_obj.ObjectName == "AcDbText":
                    text_obj.Height = formatted_height
                else:  # MTEXT
                    text_obj.TextHeight = formatted_height
                
                print(f"已修改文本 {i+1} 的文字高度为 {formatted_height}")
            
            modified_count += 1
        except Exception as e:
            print(f"修改文本 {i+1} 时出错: {e}")
    
    return modified_count

def main():
    """
    主函数
    """
    try:
        # 连接到正在运行的 AutoCAD
        acad = Autocad(create_if_not_exists=True)
        print(f"成功连接到 AutoCAD 文档: {acad.doc.Name}")
        # 使用系统变量修改当前标注样式的属性
        acad.doc.SetVariable("DIMDEC", 1)    # 主单位精度1位小数(0.0格式)
        acad.doc.SetVariable("DIMTXT", 100)   # 文字高度50

    except Exception as e:
        print(f"无法连接到 AutoCAD: {e}")
        return
    
    try:
        # 获取选中的文本对象
        texts = get_selected_texts(acad)
        
        if not texts:
            print("没有找到任何文本对象")
            return
        
        print(f"找到 {len(texts)} 个文本对象")
        
        # 获取当前文字高度
        current_height = get_current_text_height(texts)
        print(f"当前文字高度: {current_height}")
        
        # 通过CAD命令行获取输入
        text_height = get_text_height_from_cad(acad, current_height)
        
        if text_height is None:
            print("用户取消了操作或输入无效")
            return
        
        # 使用智能格式化(整数保留0位,非整数保留1位小数)
        text_height = format_decimal(text_height)
        print(f"设置新的文字高度为: {text_height}")
        
        # 修改文本的文字高度
        modified_count = modify_text_properties(acad, texts, text_height=text_height)
        
        print(f"成功修改了 {modified_count} 个文本的文字高度")
        
        # 刷新视图
        try:
            acad.doc.Regen(1)  # acAllViewports = 1
        except Exception as regen_error:
            print(f"视图刷新失败: {regen_error}")
            
    except Exception as e:
        print(f"处理对象时出错: {e}")

if __name__ == "__main__":
    main()