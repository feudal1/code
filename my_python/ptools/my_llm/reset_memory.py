
import typer
from ptools import register_command

from .file_memory import get_sys_prompt_content, save_messages_to_file


@register_command('llm', 'reset')
def reset_history_cmd(
    confirm: bool = typer.Option(False, "--confirm", "-c", help="确认重置，无需交互确认")
):
    """清空对话历史"""
    if confirm or typer.confirm("确定要重置记忆吗？"):
        messages = [{"role": "system", "content": get_sys_prompt_content()}]
        save_messages_to_file(messages)
        print("对话历史已重置")
    else:
        print("操作已取消")
