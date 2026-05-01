# main.py  


import sys
import os
sys.path.append(os.getcwd())
from qq.c2c_listener import C2CListener  
from api_key import qq_appid, qq_secret
from llm_server.llm_use import llm_chat




def qq_run():  
    async def message_handler(message):  
  
        user_text = message.content or ""  

        reply_text = llm_chat(user_text)

        await message.reply(content=reply_text)  
    # 必须启用public_messages以接收C2C消息 [1](#1-0)   
    listener = C2CListener()  
    listener.set_handler(message_handler)  # 注册处理函数  
  
    # 请替换为你的appid与secret，或从api_key.py导入  

    print("启动C2C监听...")  
    listener.run(appid=qq_appid, secret=qq_secret)  


