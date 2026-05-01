import tempfile
from ctypes import create_unicode_buffer, windll, wintypes
import os
import sys





import tempfile
from ctypes import create_unicode_buffer, windll, wintypes
import os
import sys
import asyncio

def speak_text(text: str, voice: str = "zh-CN-XiaoxiaoNeural"):
    """使用edge-tts合成并播放语音（同步版本）"""
    if not text or text.isspace():
        return
   
    if sys.platform != "win32":
        print("TTS仅支持Windows系统")
        return
    
    try:
        import edge_tts
        
        # 创建临时MP3文件
        with tempfile.NamedTemporaryFile(suffix=".mp3", delete=False) as temp_file:
            temp_path = temp_file.name
        
        # 同步方式保存音频
        communicate = edge_tts.Communicate(text, voice)
        asyncio.run(communicate.save(temp_path))
        
        # 使用Windows原生MCI API播放
        _get_short_path_name_w = windll.kernel32.GetShortPathNameW
        _get_short_path_name_w.argtypes = [wintypes.LPCWSTR, wintypes.LPWSTR, wintypes.DWORD]
        _get_short_path_name_w.restype = wintypes.DWORD
        
        def get_short_path_name(long_name: str) -> str:
            output_buf_size = 0
            while True:
                output_buf = create_unicode_buffer(output_buf_size)
                needed = _get_short_path_name_w(long_name, output_buf, output_buf_size)
                if output_buf_size >= needed:
                    return output_buf.value
                output_buf_size = needed
        
        mci_send_string_w = windll.winmm.mciSendStringW
        
        def mci_send(msg: str) -> None:
            result = mci_send_string_w(msg, 0, 0, 0)
            if result != 0:
                print(f"MCI错误 {result}: {msg}")
        
        # 获取短文件名并播放
        mp3_shortname = get_short_path_name(temp_path)
        mci_send("Close All")
        mci_send(f'Open "{mp3_shortname}" Type MPEGVideo Alias theMP3')
        mci_send("Play theMP3 Wait")
        mci_send("Close theMP3")
        
    except Exception as e:
        print(f"TTS播放失败: {e}")
    finally:
        # 立即清理临时文件
        try:
            os.unlink(temp_path)
        except:
            pass