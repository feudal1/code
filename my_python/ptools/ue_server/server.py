import unreal
import socket
import select
import threading
from queue import Queue
import os
import json

def execute_unreal_command(code_string):
    """
    执行传入的Python代码字符串
    """
    try:
        # 添加调试输出，完整记录执行的代码
       
        unreal.log(f"执行的代码:\n{code_string}")
        # 使用globals()和locals()提供当前环境的上下文
        # exec执行代码并捕获输出
        local_vars = {'unreal': unreal}
        # 使用compile来预编译代码，可以更好地处理多行代码
        compiled_code = compile(code_string, '<string>', 'exec')
        
        exec(compiled_code, globals(), local_vars)
        return "代码执行成功"
    except SyntaxError as e:
        error_msg = f"语法错误: {str(e)} (文件: {e.filename}, 行号: {e.lineno}, 文本: {e.text})"
        unreal.log_error(error_msg)
        return error_msg
    except Exception as e:
        error_msg = f"执行错误: {str(e)}"
        unreal.log_error(error_msg)
        return error_msg

class SimpleJsonServer:
    def __init__(self):
        self.socket = None
        self.clients = []
        self.should_stop = False
        self.tick_handle = None
        
    def start_server(self, port=8070):
        """启动基于Tick的简单JSON服务器"""
        try:
            # 创建非阻塞socket
            self.socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
            self.socket.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
            self.socket.bind(('', port))
            self.socket.listen(5)
            self.socket.setblocking(False)  # 设置为非阻塞模式
            
            # 注册tick函数
            self.tick_handle = unreal.register_slate_post_tick_callback(
                self.process_requests
            )
            
            unreal.log(f"基于Tick的简单JSON服务器启动在端口 {port}")
            return True
            
        except Exception as e:
            unreal.log_error(f"服务器启动失败: {str(e)}")
            return False
    
    def process_requests(self, delta_time):
        """在UE的tick中处理请求"""
        if self.should_stop:
            self.stop_server()
            return
            
        try:
            # 检查是否有新的连接或数据
            ready, _, error = select.select([self.socket] + self.clients, [], self.clients, 0)
            
            # 处理错误的连接
            for err_sock in error:
                if err_sock in self.clients:
                    self.clients.remove(err_sock)
                    err_sock.close()
            
            for sock in ready:
                if sock == self.socket:
                    # 新连接
                    try:
                        client, addr = self.socket.accept()
                        client.setblocking(False)  # 客户端也设为非阻塞
                        self.clients.append(client)
                        unreal.log(f"新连接来自: {addr}")
                    except:
                        pass  # 非阻塞accept可能失败
                else:
                    # 处理客户端数据
                    try:
                        # 尝试接收完整的JSON数据
                        full_data = b""
                        while True:
                            try:
                                chunk = sock.recv(4096)  # 增加接收缓冲区大小
                                if not chunk:
                                    break
                                full_data += chunk
                                # 如果数据块小于缓冲区大小，可能数据接收完毕
                                if len(chunk) < 4096:
                                    break
                            except BlockingIOError:
                                # 非阻塞socket没有数据可读，跳出循环
                                break
                            except ConnectionResetError:
                                # 连接被重置
                                if sock in self.clients:
                                    self.clients.remove(sock)
                                sock.close()
                                raise
                            except Exception as e:
                                # 其他错误
                                unreal.log_error(f"接收数据时出错: {str(e)}")
                                if sock in self.clients:
                                    self.clients.remove(sock)
                                sock.close()
                                raise
                        
                        # 如果接收到数据
                        if full_data:
                            data_str = full_data.decode('utf-8')
                            self.handle_request(sock, data_str)
                        else:
                            # 客户端断开连接
                            if sock in self.clients:
                                self.clients.remove(sock)
                            sock.close()
                    except BlockingIOError:
                        # 非阻塞socket没有数据可读
                        pass
                    except ConnectionResetError:
                        # 连接被重置
                        if sock in self.clients:
                            self.clients.remove(sock)
                        sock.close()
                    except Exception as e:
                        # 其他错误
                        unreal.log_error(f"处理客户端数据时出错: {str(e)}")
                        if sock in self.clients:
                            self.clients.remove(sock)
                        sock.close()
                        
        except Exception as e:
            unreal.log_warning(f"处理请求时出错: {str(e)}")
    
    def handle_request(self, client_socket, data):
        """处理请求并发送响应 - 纯JSON格式"""
        try:
            # 直接解析JSON数据，不处理HTTP头部
            request_data = json.loads(data)
            code_to_execute = request_data.get('code', '')
            
            if code_to_execute:
                # 执行传入的代码
                result = execute_unreal_command(code_to_execute)
                # 直接返回结果，不使用HTTP响应格式
                client_socket.send(result.encode('utf-8'))
            else:
                error_msg = "错误: 未提供要执行的代码"
                client_socket.send(error_msg.encode('utf-8'))
            
        except json.JSONDecodeError as e:
            error_msg = f"错误: 无效的JSON格式 - {str(e)}"
            unreal.log_error(error_msg)
            try:
                client_socket.send(error_msg.encode('utf-8'))
            except:
                pass
        except Exception as e:
            error_msg = f"处理请求时发生未知错误: {str(e)}"
            unreal.log_error(error_msg)
            try:
                client_socket.send(error_msg.encode('utf-8'))
            except:
                pass
        finally:
            # 关闭连接
            if client_socket in self.clients:
                self.clients.remove(client_socket)
            try:
                client_socket.close()
            except:
                pass
    
    def stop_server(self):
        """停止服务器"""
        self.should_stop = True
        
        # 移除tick回调
        if self.tick_handle:
            unreal.unregister_slate_tick_callback(self.tick_handle)
            self.tick_handle = None
        
        # 关闭所有连接
        for client in self.clients:
            try:
                client.close()
            except:
                pass
        self.clients.clear()
        
        # 关闭服务器socket
        if self.socket:
            try:
                self.socket.close()
            except:
                pass
            self.socket = None
        
        unreal.log("简单JSON服务器已停止")

def start_simple_json_server():
    """启动简单JSON服务器"""
    server = SimpleJsonServer()
    if server.start_server(8070):
        # 保存服务器实例以便后续控制
        if not hasattr(start_simple_json_server, 'server_instance'):
            start_simple_json_server.server_instance = server
        return server
    return None

def stop_simple_json_server():
    """停止简单JSON服务器"""
    if hasattr(start_simple_json_server, 'server_instance'):
        start_simple_json_server.server_instance.stop_server()
        start_simple_json_server.server_instance = None
        return True
    return False

# 使用示例
if __name__ == "__main__":
    # 启动简单JSON服务器
    server = start_simple_json_server()
    if server:
        unreal.log("简单JSON服务器已启动")
    else:
        unreal.log_error("服务器启动失败")