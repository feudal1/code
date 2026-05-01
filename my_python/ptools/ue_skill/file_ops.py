

import shutil
import subprocess
from pathlib import Path




def build_sifu_mod():
    """
    执行Sifu MOD构建流程
    """


    # ================ 第一步：删除 Characters 中的 _Shared 和 Skeleton ================
    char_dir = Path(r"E:\blender\ue4\character\Saved\Cooked\WindowsNoEditor\new\Content\Characters")

    if (char_dir / "_Shared").exists():
        shutil.rmtree(char_dir / "_Shared")
        print(" 已删除 _Shared 文件夹")

    if (char_dir / "Skeleton").exists():
        shutil.rmtree(char_dir / "Skeleton")
        print(" 已删除 Skeleton 文件夹")

    print()

    # ================ 第二步：复制 Characters 到 pakchunk99-XXX-P\Sifu\Content\Characters ================
    source_dir = char_dir
    target_char_dir = Path(r"E:\blender\pakchunk99-XXX-P\Sifu\Content\Characters")

    if not source_dir.exists():
        print(" 错误：源目录不存在！")
        print(f"  {source_dir}")
        return {"status": "error", "message": f"源目录不存在: {source_dir}"}

    if target_char_dir.exists():
        shutil.rmtree(target_char_dir)
        print(" 已删除旧的目标 Characters 文件夹")

    print("正在复制 Characters 到 Sifu 项目...")
    try:
        shutil.copytree(source_dir, target_char_dir)
        print(" Characters 复制成功")
    except Exception as e:
        print(f" 复制失败：{e}")
        return {"status": "error", "message": f"复制失败: {e}"}

    print()

    # ================ 第三步：调用 UnrealPak 打包生成 .pak 文件 ================
    unreal_pak_exe = Path(r"E:\blender\Sifu-MOD-TOOL\UnrealPak\UnrealPak.exe")
    pak_folder = Path(r"E:\blender\pakchunk99-XXX-P")
    filelist_path = Path(r"E:\blender\Sifu-MOD-TOOL\UnrealPak\filelist.txt")

    if not unreal_pak_exe.exists():
        print(" 错误：UnrealPak.exe 不存在！")
        print(f"  {unreal_pak_exe}")
        return {"status": "error", "message": f"UnrealPak.exe 不存在: {unreal_pak_exe}"}

    # 删除已存在的 .pak 文件
    pak_file = Path(f"{pak_folder}.pak")
    if pak_file.exists():
        print(f"正在删除已存在的 .pak 文件: {pak_file}")
        pak_file.unlink()
        print(" 已删除旧的 .pak 文件")

    # 创建 filelist.txt（与批处理文件相同的方式）
    print("正在创建文件列表...")
    try:
        with open(filelist_path, 'w', encoding='utf-8') as f:
            f.write(f'"{pak_folder}\\*.*" "..\\..\\..\\Characters"')
        print(" 文件列表创建成功")
    except Exception as e:
        print(f" 创建文件列表失败：{e}")
        return {"status": "error", "message": f"创建文件列表失败: {e}"}

    print("正在调用 UnrealPak 打包...")
    try:
        # 直接调用 UnrealPak.exe
        cmd = f'"{unreal_pak_exe}" "{pak_file}" -create="{filelist_path}" -compress'
        print(f"执行命令: {cmd}")

        result = subprocess.run(
            cmd,
            shell=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            text=True,
            encoding='gbk',
            errors='replace'
        )

        if result.returncode != 0:
            print(f" 打包警告！返回码: {result.returncode}")
            print(f"错误信息: {result.stderr}")
            print(f"输出信息: {result.stdout}")
            # 不要立即返回错误，先检查 .pak 文件是否已生成
        else:
            print(" 打包成功！")
            try:
                print(result.stdout)
            except Exception:
                print("(输出包含无法显示的字符)")
    except Exception as e:
        print(f" 打包过程中出现错误：{e}")
        import traceback
        error_detail = traceback.format_exc()
        print(f"详细错误: {error_detail}")
        return {"status": "error", "message": f"打包过程中出现错误: {e}", "detail": error_detail}

    # 检查是否生成了 .pak 文件
    pak_file = Path(f"{pak_folder}.pak")
    if pak_file.exists():
        print(" .pak 文件已生成：")
        print(f"  {pak_file}")
    else:
        print(" 打包失败：未生成 .pak 文件！")
        return {"status": "error", "message": "打包失败：未生成 .pak 文件"}

    print()

    # ================ 第四步：将 .pak 文件复制到游戏 MOD 目录 ================
    target_mod_dir = Path(r"G:\Sifu\Sifu\Content\Paks\~mods")
    target_pak = target_mod_dir / f"{pak_folder.name}.pak"

    # 如果 ~mods 目录不存在，则创建它
    target_mod_dir.mkdir(parents=True, exist_ok=True)
    print(" MOD 目录已准备：")
    print(f"  {target_mod_dir}")

    try:
        shutil.copy2(pak_file, target_pak)
        print(" 已替换 MOD 文件到：")
        print(f"  {target_pak}")
    except Exception as e:
        print(f" 复制 MOD 文件失败：{e}")
        return {"status": "error", "message": f"复制 MOD 文件失败: {e}"}
    try:
        shutil.copy2(pak_file, target_pak)
        print(" 已替换 MOD 文件到：")
        print(f"  {target_pak}")
    except Exception as e:
        print(f" 复制 MOD 文件失败：{e}")
        return {"status": "error", "message": f"复制 MOD 文件失败: {e}"}

    # ================ 第五步：处理签名文件 ================
    sig_filename = f"{pak_folder.name}.sig"
    target_sig_path = target_mod_dir / sig_filename
    source_sig_path = Path(r"G:\Sifu\Sifu\Content\Paks\pakchunk0-WindowsNoEditor.sig")

    if not target_sig_path.exists():
        if source_sig_path.exists():
            try:
                shutil.copy2(source_sig_path, target_sig_path)
                print(" 已复制签名文件到：")
                print(f"  {target_sig_path}")
            except Exception as e:
                print(f" 复制签名文件失败：{e}")
                # 这里不返回错误，因为签名文件可能不是必需的
        else:
            print(f" 源签名文件不存在：{source_sig_path}")
    else:
        print(f" 签名文件已存在：{target_sig_path}")

    return {"status": "success", "result": "MOD build completed and game launched"}


if __name__ == "__main__":
    build_sifu_mod()
