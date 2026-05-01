
import os
import http
import asyncio
import json
import xmlrpc.client
from urllib.parse import urlparse

# 配置日志

deepseek_model_name=os.environ.get("deepseek_MODEL_NAME", None)
deepseek_api_key=os.environ.get("deepseek_OPENAI_API_KEY", None)
deepseek_api_url=os.environ.get("deepseek_OPENAI_API_URL", None)
zhipu_model_name=os.environ.get("zhipu_MODEL_NAME", None)
zhipu_api_key=os.environ.get("zhipu_OPENAI_API_KEY", None)
zhipu_api_url=os.environ.get("zhipu_OPENAI_API_URL", None)
class Client:
    def __init__(self, api_url, api_key, model_name):
        self.api_key = api_key
        self.api_url = api_url
        self.model_name = model_name

    def create(self, messages, tools=None) -> tuple[str, list]:
        # 解析 URL（去掉协议部分）
        
        parsed=urlparse(f"{self.api_url}/chat/completions")
        host, path =parsed.hostname,parsed.path
        # 创建 HTTP 连接
        conn = http.client.HTTPSConnection(host)

        # 构造请求体
        request_body = {
            "model": self.model_name,
            "messages": messages,
            "tools": tools,
            "temperature": 0.9  # 添加温度参数
        }

        # 发送 POST 请求
        headers = {
            "Content-Type": "application/json",
            "Authorization": f"Bearer {self.api_key}"
        }
        conn.request(
            "POST",
            path,
            body=json.dumps(request_body),
            headers=headers
        )

        # 获取响应
        response = conn.getresponse()
        if response.status != 200:
            raise Exception(f"LLM服务器错误: {response.status} - {response.read().decode('utf-8')}")

        # 读取响应内容
        response_data = response.read().decode('utf-8')
        data = json.loads(response_data)

        # 将响应保存到文件
        with open('formatted_data.json', 'w', encoding='utf-8') as f:
            json.dump(data, f, indent=4, ensure_ascii=False)

        # 关闭连接
        conn.close()

        return data


def tool_call(user_say):
        '''
        
        image_promt={
                "role": "user",
                "content": [
                    {"type": "text", 
                    "text": "帮我............."},
                    {"type": "image_url", 
                                    "image_url": {
                        "url": f"data:image/jpeg;base64,{base64_image}"
                    }},
                ]
            }
        
        '''
        tools = [{
            "type": "function",
            "function": {
                "name": "execute",
                "description": "use freecad's function",
                "parameters": {
                "type": "object",
                "properties": {
                    "function": {
                        "type": "string",
                        "description": """
*返回每行是纯freecad的函数代码的代码块,或者,返回单条纯freecad函数代码,不要有注释*
*如果找不到想自创代码块记得用函数赋值或者函数调用,不要用if这种复杂命令*
**************************单条纯函数代码*********************************     

函数代码:doc=FreeCAD.newDocument()  
描述:此方法可选输入文档名称 
注意:在执行任务前要先确保有Document文档,没有需要创建一个
例子:doc= FreeCAD.newDocument("new_Document")

函数代码:doc.recompute() 
描述:刷新文档

函数代码:FreeCADGui.SendMsgToActiveView("ViewFit")  
描述:视图居中
    
函数代码:doc.addObject("object_type", "name")                                            
描述: 添加一个对象，参数为对象类型和名称，返回对象。
例子:cube = doc.addObject("Part::Box", "Cube")

函数代码:obj.attribute = value 
描述:设置对象的属性值。
注意:此命令需要读取对象的名称来设置属性。
例子:cube.Length = 30  , cube.Width = 30 , cube.Height = 30


函数代码:FreeCADGui.activateWorkbench('workbench_name')
描述:切换工作台,需要输入工作台名称。
例子:FreeCADGui.activateWorkbench("SMWorkbench")



函数代码:FreeCADGui.runCommand('SheetMetal_BaseShape', 0)
描述:创建钣金基础形状。
注意:运行钣金命令前需要确保已经切换到钣金工作台   


函数代码:FreeCADGui.Selection.addSelection('Document_name', 'body_name', 'object_name', x, y, z)
描述:选择某条边，用于后续操作（如添加边线法兰）。
参数说明:
    第一个参数:文档名称（如 'Cube_Document'）。
    第二个参数:对象名称（如 'Body'）。
    第三个参数:具体边的标识（如 'BaseShape.Edge7'）。
    后面的参数:边的坐标信息（如 9, -9, 1）。 float类型。
    例子:FreeCADGui.Selection.addSelection('Cube_Document', 'Body', 'BaseShape.Edge7', 9, -9, 1)

函数代码:FreeCADGui.runCommand('SheetMetal_AddWall', 0)
描述:在已选择的边上创建折弯边。
注意:运行此命令前需要先选择一条边（使用 FreeCADGui.Selection.addSelection）。
例子:FreeCADGui.runCommand('SheetMetal_AddWall', 0)

**************************函数代码块*********************************

以下是一个完整的示例，展示如何创建基础钣金形状并添加折弯边:
FreeCADGui.activateWorkbench("SMWorkbench")
doc = FreeCAD.newDocument("new_Document")
FreeCADGui.runCommand('SheetMetal_BaseShape', 0)
FreeCADGui.Selection.addSelection('new_Document', 'Body', 'BaseShape.Edge7', 9, -9, 1)
FreeCADGui.runCommand('SheetMetal_AddWall', 0)
doc.recompute()
FreeCADGui.SendMsgToActiveView("ViewFit")

以下是一个完整的示例，创建立方体30*50*60：
doc= FreeCAD.newDocument("new_Document")
cube = doc.addObject("Part::Box", "Cube")
cube.Length = 30
cube.Width = 50
cube.Height = 60
doc.recompute()
FreeCADGui.SendMsgToActiveView("ViewFit")

                        """},
               
                },
           
            }}}
        ]
        history_content = '\n'.join(history)
        user_promt={
                "role": "user",
                "content":  f"{user_say}\n*这是历史消息*:{history_content}" 
            }
        
        
       
        

        data= deepseek_client.create([user_promt],tools)
        tool_calls=data["choices"][0]["message"]["tool_calls"]
        assistent_message=data["choices"][0]["message"]
        arguments=tool_calls[0]["function"]["arguments"]
        commands=json.loads(arguments)["function"]
        print("ai调用命令:\n"+commands)
        objects_information= execute_commands(commands)  
      
        tool_promt={
                "role": "tool",
                "content":objects_information,
                "tool_call_id":tool_calls[0]["id"]
            }
        system_promt={
                "role": "system",
                "content":"用中文回答谢谢",
            }
        data= deepseek_client.create([system_promt,assistent_message,tool_promt])
        assitant_response=data["choices"][0]["message"].get("content", "")
        print("assitant:"+assitant_response)
        history.append(assitant_response)

'''
import base64
def get_imageToBase64()->str:
    imagePath=os.path.join(os.path.expanduser('~'), '图片', 'screenshot.jpg')
# 获取屏幕的宽度和高度
    screen_width, screen_height = pyautogui.size()
    screenshot = pyautogui.screenshot(region=(60, 300, screen_width//2, screen_height-500))
    screenshot.save(imagePath)
    # 读取图片并转换为 Base64
    with open(imagePath, "rb") as image_file:
        base64_image = base64.b64encode(image_file.read()).decode("utf-8")
        return base64_image

'''

async def main():

    global history,zhipu_client,deepseek_client
    zhipu_client = Client(zhipu_api_url,zhipu_api_key,zhipu_model_name )
    deepseek_client = Client(deepseek_api_url,deepseek_api_key ,deepseek_model_name)
    history=[]
    #user_say="画一个立方体"
    while True:
        user_say=input("你:")
        tool_call(user_say)  
    

def execute_commands(commands) :
     
      proxy = xmlrpc.client.ServerProxy("http://localhost:8000/",allow_none=True)
      return proxy.execute_commands(commands) 
             
async def main2():      
    commands="""
doc= FreeCAD.newDocument("new_Document")
cube = doc.addObject("Part::Box", "Cube")
cube.Length = 30
cube.Width = 50
cube.Height = 60
doc.recompute()
FreeCADGui.SendMsgToActiveView("ViewFit")

""" 
    execute_commands(commands)
      
 
      
if __name__ == "__main__":
     asyncio.run(main2())