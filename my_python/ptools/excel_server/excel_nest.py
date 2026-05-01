import pandas as pd
from itertools import combinations_with_replacement
import sys

def find_top_combinations(parts, max_width=1500, top_n=10):
    """
    找出前N组最优零件组合，使得它们的宽度之和 <= max_width，
    并且使用的总数量最大。
    
    Args:
        parts: [(width, height, count)] 列表
        max_width: 板材宽度，默认1500
        top_n: 返回前N个最优组合，默认10
    
    Returns:
        top_combos: 前N个最优组合列表，每个元素是 
                    (combo, total_width, total_count)
    """
    n = len(parts)
    all_combos = []

    def backtrack(idx, current_combo, current_width, current_count):
        if idx == n:
            if current_width <= max_width:
                all_combos.append((current_combo[:], current_width, current_count))
            return
        
        width, height, count = parts[idx]
        
        # 尝试使用 0 到 count 个当前零件
        for use in range(count + 1):
            new_width = current_width + use * width
            if new_width > max_width:
                break
            new_count = current_count + use
            current_combo.append((idx, use))
            backtrack(idx + 1, current_combo, new_width, new_count)
            current_combo.pop()

    backtrack(0, [], 0, 0)
    
    # 按照使用总数量降序排序，如果数量相同则按宽度升序排序
    all_combos.sort(key=lambda x: (-x[2], x[1]))
    
    # 返回前top_n个组合
    return all_combos[:top_n]

# 主程序
if __name__ == "__main__":
    # 读取 Excel 文件
    file_path = r"E:\code\my_python_server\dxf_output\info\dxf_info_20251023_101604.xlsx"  # 替换为你实际的文件路径
    df = pd.read_excel(file_path)

    # 提取数据
    parts = list(zip(df['宽度'], df['高度'], df['数量']))

    print("正在分析前10组最佳排版组合...")
    top_combos = find_top_combinations(parts, top_n=10)

    for i, (combo, total_width, total_count) in enumerate(top_combos, 1):
        print(f"\n--- 第{i}组最优组合 ---")
        print(f"✅ 组合总宽度: {total_width}")
        print(f"✅ 使用的总数量: {total_count}")
        print("📊 组合详情:")
        
        # 过滤掉数量为0的零件
        non_zero_combo = [(idx, qty) for idx, qty in combo if qty > 0]
        
        if non_zero_combo:
            for idx, qty in non_zero_combo:
                w, h, c = parts[idx]
                # 对长宽数值取整
                print(f"  - 宽度{int(w)}, 高度{int(h)}, 数量{qty} (原数量{c})")
        else:
            print("  - 无零件被使用")