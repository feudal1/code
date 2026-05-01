class StartRPCServerCommand:
 
    def GetResources(self):
        return {"MenuText": "Start RPC Server"}

    def Activated(self):
        from xmlrpc.server import SimpleXMLRPCServer
        import queue
        from PySide2.QtCore import QTimer
        import threading
       
        # 定义一个类来封装API方法
        class FreeCADRPC:
            """RPC server for FreeCAD"""



            def execute_commands(self,commands):
                    def task():
        
                      exec(commands, globals())
                    rpc_request_queue.put(task)
                    return   rpc_response_queue.get()
            
         
        # GUI task queue
        rpc_request_queue = queue.Queue()
        rpc_response_queue = queue.Queue()
        
        # 启动 RPC 服务器
        host = "localhost"
        port = 8000
        print(f"Starting RPC server at {host}:{port}...")
        rpc_server_instance = SimpleXMLRPCServer(
            (host, port), allow_none=True, logRequests=False
        )

        # 创建 FreeCADRPC 实例并注册到服务器
     
        rpc_server_instance.register_instance(FreeCADRPC())

        # 启动 RPC 服务器线程
        def server_loop():
            rpc_server_instance.serve_forever()

        rpc_server_thread = threading.Thread(target=server_loop, daemon=True)
        rpc_server_thread.start()

        # 处理 GUI 任务
        def process_gui_tasks():
            while not rpc_request_queue.empty():
                task = rpc_request_queue.get_nowait()
                res = task()
                rpc_response_queue.put(res)
            QTimer.singleShot(500, process_gui_tasks)

        QTimer.singleShot(500, process_gui_tasks)

    def IsActive(self):
        return True


# 注册并运行命令
Gui.addCommand("a_Server", StartRPCServerCommand())
Gui.runCommand("a_Server")