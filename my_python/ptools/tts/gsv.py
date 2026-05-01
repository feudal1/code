from gsv_tts import TTS

tts = TTS()

# 将 GPT 模型权重从指定路径加载到内存中，这里加载默认模型。
tts.load_gpt_model()

# 将 SoVITS 模型权重从指定路径加载到内存中，这里加载默认模型。
tts.load_sovits_model()


def speak_text(text):
    """
    生成TTS音频并播放（使用预设的音色和风格）
    
    Args:
        text (str): 目标生成文本
    """
    if not text.strip():
        return
    generator = tts.infer_stream(
        spk_audio_path="ptools\\tts\\bu.mp3",
        prompt_audio_path="ptools\\tts\\hez.ogg",
        prompt_audio_text="貴方さまの手を、取ってしまうと思うのです。",
        text=text,
        debug=True,
    )

    for audio in generator:
        audio.play()


    tts.audio_queue.wait()


  
if __name__ == "__main__":
    speak_text("""


数学の勉強を忘れ ないでくださいね。それとも、他のことで忙しいのでしょうか？
    
    
    """)
