# c2c_listener.py  
import botpy  
from botpy.message import C2CMessage  
from typing import Callable, Optional  
  
class C2CListener(botpy.Client):  
    """只负责监听C2C消息并暴露发送方法；业务逻辑由外部handler处理"""  
    def __init__(self, **kwargs):
        intents = botpy.Intents(public_messages=True)  
        super().__init__(intents=intents, **kwargs)  
        
        self.handler: Optional[Callable[[C2CMessage], None]] = None  
  
    def set_handler(self, handler: Callable[[C2CMessage], None]):  
        """注册消息处理回调（由调用端实现）"""  
        self.handler = handler  
  
    async def on_ready(self):  
        print("[C2CListener] 已启动，等待C2C消息...")  
  
    async def on_c2c_message_create(self, message: C2CMessage):  
        """收到C2C消息，交给外部handler处理"""  
        if self.handler:  
            await self.handler(message)  # 将消息交给调用端处理  
  
    async def send(self, openid: str, content: str, msg_id: str = None):  
        """主动发送C2C消息；可传入msg_id以实现回复引用"""  
        return await self.api.post_c2c_message(openid=openid, content=content, msg_id=msg_id)