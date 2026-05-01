
import sys
import os
sys.path.append(os.getcwd())
# 导入 Ollama 相关模块
from llama_index.core import VectorStoreIndex, SimpleDirectoryReader, Settings
from llama_index.llms.ollama import Ollama
from llama_index.embeddings.ollama import OllamaEmbedding
from api_key import ollama_llm1,  ollama_embedding_model
# 配置 Ollama 模型
Settings.embed_model = OllamaEmbedding(model_name=ollama_embedding_model)
Settings.llm = Ollama(model=ollama_llm1, request_timeout=60.0)


memory_dir = os.path.join(os.getcwd(), "memory_server", "memory_data")
if not os.path.exists(memory_dir):
    os.makedirs(memory_dir)
documents = SimpleDirectoryReader(f"{memory_dir}").load_data()

def query_document_index(query_text="你的问题"):
    """
    使用 Ollama 模型查询文档索引
    
    Args:
        query_text (str): 查询的问题文本
        data_dir (str): 文档数据目录路径
        
    Returns:
        response: 查询结果响应
    """
    if not os.path.exists(memory_dir):
        os.makedirs(memory_dir)
    try:
        # 加载文档数据
        
        # 创建向量索引
        index = VectorStoreIndex.from_documents(documents)
        
        # 使用 Ollama 模型进行查询
        query_engine = index.as_query_engine()
        response = query_engine.query(query_text)
        text = str(response)
        print(f"查询结果: {response}")
        return text 
        
    except Exception as e:
        print(f"文档查询出错: {e}")
        return f"抱歉，我在查询文档时遇到了问题。{e}"

def retrieve_document_chunks(query_text="你的问题", top_k=1):
    """
    仅检索文档中的相关文本块，不进行问答生成
    
    Args:
        query_text (str): 查询的问题文本
        top_k (int): 返回最相似的文本块数量，默认5个
        
    Returns:
        str: 格式化的检索结果字符串
    """
    if not os.path.exists(memory_dir):
        os.makedirs(memory_dir)
    try:
        # 加载文档数据
        documents = SimpleDirectoryReader("E:/code/my_python_server/memory_server/memory_data").load_data()
        # 创建向量索引
        index = VectorStoreIndex.from_documents(documents)
        
        # 创建检索器
        retriever = index.as_retriever(similarity_top_k=top_k)
        # 执行检索
        nodes = retriever.retrieve(query_text)
        
        # 构建格式化的字符串结果
        result_lines = []
        result_lines.append(f"共检索到 {len(nodes)} 个相关文本块:")
        result_lines.append("")
        
        for i, node in enumerate(nodes, 1):
            result_lines.append(f"--- 文本块 {i} ---")
            result_lines.append(node.text)
            # 添加相似度分数（如果存在）
            if hasattr(node, 'score') and node.score is not None:
                result_lines.append(f"相似度分数: {node.score:.4f}")
            result_lines.append("")
            
        result_str = "\n".join(result_lines)
        print(f"检索完成: {result_str}")
        return result_str
        
    except Exception as e:
        error_msg = f"文档检索出错: {e}"
        print(error_msg)
        return error_msg
# 使用示例
if __name__ == "__main__":
    response = retrieve_document_chunks("我喜欢什么")
