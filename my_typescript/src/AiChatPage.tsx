import { useCallback, useEffect, useRef, useState } from "react";

export interface ChatMessage {
  id: string;
  role: "user" | "assistant";
  content: string;
  createdAt: number;
}

const STORAGE_KEY = "work_project_ai_chat_v1";
const CHAT_HISTORY_API_URL = import.meta.env.VITE_CHAT_HISTORY_API_URL?.trim() ?? "";
const AUTO_SYNC_DEBOUNCE_MS = 1000;

type ChatHistoryPayload = {
  version: 1;
  updatedAt: number;
  messages: ChatMessage[];
};

function loadMessages(): ChatMessage[] {
  try {
    const raw = localStorage.getItem(STORAGE_KEY);
    if (!raw) return [];
    const parsed = JSON.parse(raw) as unknown;
    if (!Array.isArray(parsed)) return [];
    return parsed.filter(
      (m): m is ChatMessage =>
        m &&
        typeof m === "object" &&
        typeof (m as ChatMessage).id === "string" &&
        ((m as ChatMessage).role === "user" ||
          (m as ChatMessage).role === "assistant") &&
        typeof (m as ChatMessage).content === "string",
    );
  } catch {
    return [];
  }
}

function saveMessages(messages: ChatMessage[]): void {
  try {
    localStorage.setItem(STORAGE_KEY, JSON.stringify(messages));
  } catch {
    // ignore
  }
}

function getChatHistoryApiUrl(): string {
  if (CHAT_HISTORY_API_URL) {
    return CHAT_HISTORY_API_URL;
  }
  if (!CHAT_API_URL) {
    return "";
  }
  return CHAT_API_URL.replace(/\/api\/chat$/i, "/api/chat-history");
}

const CHAT_API_URL = import.meta.env.VITE_CHAT_API_URL?.trim() ?? "";

function extractAssistantText(data: Record<string, unknown>): string | null {
  if (typeof data.reply === "string" && data.reply.trim()) return data.reply.trim();
  if (typeof data.content === "string" && data.content.trim()) return data.content.trim();
  if (typeof data.message === "string" && data.message.trim()) return data.message.trim();
  const nested = data.message;
  if (
    nested &&
    typeof nested === "object" &&
    typeof (nested as { content?: unknown }).content === "string"
  ) {
    const c = (nested as { content: string }).content.trim();
    if (c) return c;
  }
  return null;
}

function placeholderReply(): string {
  return CHAT_API_URL
    ? "请求后端失败或未返回可读文本，请检查 VITE_CHAT_API_URL 对应服务与响应格式。"
    : "（未配置 VITE_CHAT_API_URL：当前为本地占位。请在 .env.local 填写你自己的对话后端地址；模型密钥放在服务端，不要写进前端。）";
}

async function requestAssistantReply(
  history: ChatMessage[],
  userText: string,
): Promise<string> {
  const payload = {
    messages: [
      ...history.map((m) => ({ role: m.role, content: m.content })),
      { role: "user" as const, content: userText },
    ],
  };
  const res = await fetch(CHAT_API_URL, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(payload),
  });
  const rawText = await res.text();
  if (!res.ok) {
    throw new Error(rawText || `HTTP ${res.status}`);
  }
  try {
    const data = JSON.parse(rawText) as Record<string, unknown>;
    const reply = extractAssistantText(data);
    if (reply) return reply;
  } catch {
    /* 非 JSON，退回原文 */
  }
  const t = rawText.trim();
  if (t) return t;
  throw new Error("后端返回空内容");
}

export default function AiChatPage() {
  const [messages, setMessages] = useState<ChatMessage[]>(() => loadMessages());
  const [draft, setDraft] = useState("");
  const [sending, setSending] = useState(false);
  const [remoteStatus, setRemoteStatus] = useState("未连接项目级存储");
  const bottomRef = useRef<HTMLDivElement>(null);
  const chatHistoryApiUrl = getChatHistoryApiUrl();

  useEffect(() => {
    saveMessages(messages);
  }, [messages]);

  useEffect(() => {
    let cancelled = false;
    async function loadRemoteMessages() {
      if (!chatHistoryApiUrl) {
        setRemoteStatus("未配置对话历史接口（仅本地缓存）");
        return;
      }
      try {
        const res = await fetch(chatHistoryApiUrl);
        const text = await res.text();
        if (!res.ok) {
          throw new Error(text || `HTTP ${res.status}`);
        }
        const payload = JSON.parse(text) as ChatHistoryPayload;
        if (!Array.isArray(payload.messages)) {
          throw new Error("返回数据格式无效");
        }
        const safeMessages = payload.messages.filter(
          (m): m is ChatMessage =>
            m &&
            typeof m === "object" &&
            typeof (m as ChatMessage).id === "string" &&
            ((m as ChatMessage).role === "user" || (m as ChatMessage).role === "assistant") &&
            typeof (m as ChatMessage).content === "string",
        );
        if (cancelled) {
          return;
        }
        setMessages((prev) => {
          if (safeMessages.length === 0 && prev.length > 0) {
            return prev;
          }
          return safeMessages;
        });
        setRemoteStatus(`已从项目文件加载（${new Date().toLocaleTimeString()}）`);
      } catch (error) {
        if (!cancelled) {
          setRemoteStatus(`对话历史加载失败：${error instanceof Error ? error.message : String(error)}`);
        }
      }
    }
    void loadRemoteMessages();
    return () => {
      cancelled = true;
    };
  }, [chatHistoryApiUrl]);

  useEffect(() => {
    if (!chatHistoryApiUrl) {
      return;
    }
    const timer = window.setTimeout(async () => {
      try {
        const payload: ChatHistoryPayload = {
          version: 1,
          updatedAt: Date.now(),
          messages,
        };
        const res = await fetch(chatHistoryApiUrl, {
          method: "PUT",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify(payload),
        });
        const text = await res.text();
        if (!res.ok) {
          throw new Error(text || `HTTP ${res.status}`);
        }
        setRemoteStatus(`已同步到项目文件（${new Date().toLocaleTimeString()}）`);
      } catch (error) {
        setRemoteStatus(`对话历史同步失败：${error instanceof Error ? error.message : String(error)}`);
      }
    }, AUTO_SYNC_DEBOUNCE_MS);
    return () => {
      window.clearTimeout(timer);
    };
  }, [messages, chatHistoryApiUrl]);

  useEffect(() => {
    bottomRef.current?.scrollIntoView({ behavior: "smooth" });
  }, [messages, sending]);

  const send = useCallback(async () => {
    const text = draft.trim();
    if (!text || sending) return;
    const userMsg: ChatMessage = {
      id: `${Date.now()}-u`,
      role: "user",
      content: text,
      createdAt: Date.now(),
    };
    setDraft("");
    setMessages((prev) => [...prev, userMsg]);

    if (!CHAT_API_URL) {
      const assistantMsg: ChatMessage = {
        id: `${Date.now()}-a`,
        role: "assistant",
        content: placeholderReply(),
        createdAt: Date.now(),
      };
      setMessages((prev) => [...prev, assistantMsg]);
      return;
    }

    setSending(true);
    try {
      const replyText = await requestAssistantReply(messages, text);
      const assistantMsg: ChatMessage = {
        id: `${Date.now()}-a`,
        role: "assistant",
        content: replyText,
        createdAt: Date.now(),
      };
      setMessages((prev) => [...prev, assistantMsg]);
    } catch (e) {
      const assistantMsg: ChatMessage = {
        id: `${Date.now()}-a`,
        role: "assistant",
        content: `${placeholderReply()}\n\n详情：${e instanceof Error ? e.message : String(e)}`,
        createdAt: Date.now(),
      };
      setMessages((prev) => [...prev, assistantMsg]);
    } finally {
      setSending(false);
    }
  }, [draft, messages, sending]);

  const clearChat = useCallback(() => {
    if (!messages.length) return;
    if (!window.confirm("清空当前对话记录？")) return;
    setMessages([]);
    localStorage.removeItem(STORAGE_KEY);
  }, [messages.length]);

  return (
    <div className="chat-shell">
      <div className="chat-header-row">
        <p className="chat-intro">
          在此与 AI 对话；SolidWorks 出图流程请在顶部切换到「流程管理」子页编辑。
          {CHAT_API_URL
            ? ` 已配置后端：${CHAT_API_URL}`
            : " 未配置 VITE_CHAT_API_URL 时使用占位回复；密钥请放服务端。"}
          {" "}
          {remoteStatus}
        </p>
        <button type="button" className="chat-clear" onClick={clearChat} disabled={!messages.length}>
          清空对话
        </button>
      </div>

      <div className="chat-thread" role="log" aria-live="polite">
        {messages.length === 0 ? (
          <p className="toolbar-muted chat-empty">暂无消息，在下方输入开始。</p>
        ) : (
          messages.map((m) => (
            <div
              key={m.id}
              className={`chat-bubble chat-bubble--${m.role}`}
            >
              <span className="chat-role">{m.role === "user" ? "你" : "助手"}</span>
              <div className="chat-content">{m.content}</div>
            </div>
          ))
        )}
        <div ref={bottomRef} />
      </div>

      <div className="chat-composer">
        <textarea
          rows={3}
          placeholder="输入消息… Enter 发送，Shift+Enter 换行"
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
          onKeyDown={(e) => {
            if (e.key === "Enter" && !e.shiftKey) {
              e.preventDefault();
              send();
            }
          }}
        />
        <button type="button" className="primary" onClick={() => void send()} disabled={sending}>
          {sending ? "请求中…" : "发送"}
        </button>
      </div>
    </div>
  );
}
