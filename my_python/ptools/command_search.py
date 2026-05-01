#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
复用的命令搜索逻辑：
- 从 tools.get_all_commands() 获取已注册命令
- 模糊匹配并返回 top_k
"""

from __future__ import annotations

from difflib import SequenceMatcher
from pathlib import Path
from typing import Any, Dict, List, Optional
import json
import subprocess

from ptools import get_all_commands


def _calculate_similarity(query: str, text: str) -> float:
    query_lower = (query or "").lower()
    text_lower = (text or "").lower()
    ratio = SequenceMatcher(None, query_lower, text_lower).ratio()
    if query_lower and query_lower in text_lower:
        ratio = max(ratio, 0.9)
    query_set = set(query_lower)
    text_set = set(text_lower)
    overlap = len(query_set & text_set) / max(len(query_set), 1)
    return 0.6 * ratio + 0.4 * overlap


def _find_csharp_tools_exe(project_root: str) -> Optional[Path]:
    """
    在仓库内定位 my_c#/tools 编译输出的 tools.exe。
    """
    repo_root = Path(project_root).resolve().parent
    bin_dir = repo_root / "my_c#" / "tools" / "bin"
    if not bin_dir.exists():
        return None

    candidates = list(bin_dir.rglob("tools.exe"))
    if not candidates:
        return None

    candidates.sort(key=lambda p: p.stat().st_mtime, reverse=True)
    return candidates[0]


def _load_csharp_commands_via_export(project_root: str) -> List[Dict[str, Any]]:
    """
    调用 C# 导出命令 JSON 并读取返回的命令列表。
    失败则返回空列表。
    """
    exe_path = _find_csharp_tools_exe(project_root)
    if exe_path is None:
        return []

    try:
        result = subprocess.run(
            [str(exe_path), "--export-commands"],
            check=False,
            capture_output=True,
            text=True,
            encoding='gbk',
            errors='replace',
        )
        if result.returncode != 0:
            return []
    except Exception:
        return []

    json_path = exe_path.parent / "csharp_commands.json"
    if not json_path.exists():
        return []

    try:
        data = json.loads(json_path.read_text(encoding="utf-8-sig"))
        if isinstance(data, list):
            return data
    except Exception:
        return []

    return []


def search_registered_commands(
    keyword: str,
    *,
    threshold: float = 0.3,
    top_k: Optional[int] = None,
    include_csharp_export: bool = False,
    project_root_for_csharp: Optional[str] = None,
) -> List[Dict[str, Any]]:
    """
    在已注册命令中模糊搜索，返回结果列表：
    [{group,name,help,score,usage}, ...]
    
    Args:
        keyword: 搜索关键词
        threshold: 相似度阈值，默认 0.3
        top_k: 返回结果数量上限，None 表示返回所有满足阈值的结果
        include_csharp_export: 是否包含 C# 导出命令
        project_root_for_csharp: C# 项目根目录路径
    """
    all_commands = get_all_commands()

    if include_csharp_export and project_root_for_csharp:
        csharp_export = _load_csharp_commands_via_export(project_root_for_csharp)
        if csharp_export:
            # 按 group 字段分组
            cad_cmds = []
            solidworks_cmds = []
            
            for item in csharp_export:
                if not isinstance(item, dict):
                    continue
                group = item.get("group", "csharp")
                name = (item.get("name") or "").strip()
                desc = (item.get("description") or "").strip()
                params = (item.get("parameters") or "").strip()
                if not name:
                    continue
                # 构建帮助文本：包含组名信息以便搜索
                help_parts = []
                if group:
                    help_parts.append(group)
                if desc:
                    help_parts.append(desc)
                if params:
                    help_parts.append(f"参数：{params}")
                help_text = " ".join(help_parts)
                
                cmd_data = {"name": name, "help": help_text}
                
                if group == "cad":
                    cad_cmds.append(cmd_data)
                elif group == "solidworks":
                    solidworks_cmds.append(cmd_data)
            
            # 将分组后的命令添加到 all_commands
            all_commands = dict(all_commands)
            if cad_cmds:
                # 如果已有 cad 命令组，合并；否则创建新的
                if "cad" in all_commands:
                    existing_names = {cmd["name"] for cmd in all_commands["cad"].get("commands", [])}
                    for cmd in cad_cmds:
                        if cmd["name"] not in existing_names:
                            all_commands["cad"]["commands"].append(cmd)
                else:
                    all_commands["cad"] = {
                        "help": "CAD 工具（C# 导出）",
                        "commands": cad_cmds,
                    }
            
            if solidworks_cmds:
                all_commands["solidworks"] = {
                    "help": "SolidWorks 工具（C# 导出）",
                    "commands": solidworks_cmds,
                }

    results: List[Dict[str, Any]] = []
    kw_lower = (keyword or "").lower()
    seen_commands = set()  # 用于去重，避免同一个命令在不同别名组重复出现

    for group_name, group_data in all_commands.items():
        # 计算命令组名称与关键词的相似度
        group_score = _calculate_similarity(keyword, group_name)
        
        for cmd in group_data.get("commands", []):
            cmd_name = cmd.get("name", "") or ""
            cmd_help = cmd.get("help", "") or ""

            # 构建唯一标识符，避免同一命令在不同别名组重复
            cmd_key = f"{cmd_name}"
            if cmd_key in seen_commands:
                continue
            
            score_name = _calculate_similarity(keyword, cmd_name)
            score_help = _calculate_similarity(keyword, cmd_help)
            score_group = _calculate_similarity(keyword, group_name)
            
            # 取三者最高分
            score = max(score_name, score_help, score_group)

            if kw_lower and (kw_lower in cmd_name.lower() or kw_lower in cmd_help.lower() or kw_lower in group_name.lower()):
                score = min(1.0, score + 0.5)

            if score >= threshold:
                seen_commands.add(cmd_key)
                results.append(
                    {
                        "group": group_name,
                        "name": cmd_name,
                        "help": cmd_help,
                        "score": round(score, 3),
                        "usage": f"p {group_name} {cmd_name}",
                    }
                )

    results.sort(key=lambda x: x["score"], reverse=True)
    
    # 如果指定了 top_k，则限制返回数量；否则返回所有满足阈值的结果
    if top_k is not None and top_k > 0:
        return results[:top_k]
    return results

