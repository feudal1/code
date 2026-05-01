/// <reference types="vite/client" />

interface ImportMetaEnv {
  /** 对话后端完整 URL，例如 http://127.0.0.1:8787/chat（由你的服务实现 POST） */
  readonly VITE_CHAT_API_URL?: string;
  /** 图谱文件接口完整 URL，例如 http://127.0.0.1:8787/api/graph */
  readonly VITE_GRAPH_API_URL?: string;
}

interface ImportMeta {
  readonly env: ImportMetaEnv;
}
