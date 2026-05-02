import { createServer } from "node:http";
import { promises as fs } from "node:fs";
import path from "node:path";
import dotenv from "dotenv";

// Node 进程不会像 Vite 一样自动读取 .env.local，这里显式加载。
dotenv.config({ path: ".env.local" });
dotenv.config();

const PORT = Number(process.env.PORT || 8787);
const DEEPSEEK_API_KEY = (process.env.DEEPSEEK_API_KEY || "").trim();
const DEEPSEEK_BASE_URL = (
  process.env.DEEPSEEK_BASE_URL || "https://api.deepseek.com"
).trim();
const DEEPSEEK_MODEL = (process.env.DEEPSEEK_MODEL || "deepseek-chat").trim();
const ALLOW_ORIGIN = (process.env.ALLOW_ORIGIN || "*").trim();
const GRAPH_FILE_PATH = path.resolve(process.cwd(), "相关文档/设计笔记/graph-cache.json");
const TODO_FILE_PATH = path.resolve(process.cwd(), "相关文档/设计笔记/todo-cache.json");
const CHAT_HISTORY_FILE_PATH = path.resolve(process.cwd(), "相关文档/设计笔记/chat-history-cache.json");
const WORKFLOW_FILE_PATH = path.resolve(process.cwd(), "相关文档/设计笔记/workflow-cache.json");

function json(res, statusCode, body) {
  res.writeHead(statusCode, {
    "Content-Type": "application/json; charset=utf-8",
    "Access-Control-Allow-Origin": ALLOW_ORIGIN,
    "Access-Control-Allow-Headers": "Content-Type, Authorization",
    "Access-Control-Allow-Methods": "GET, POST, PUT, OPTIONS",
  });
  res.end(JSON.stringify(body));
}

function text(res, statusCode, body) {
  res.writeHead(statusCode, {
    "Content-Type": "text/plain; charset=utf-8",
    "Access-Control-Allow-Origin": ALLOW_ORIGIN,
    "Access-Control-Allow-Headers": "Content-Type, Authorization",
    "Access-Control-Allow-Methods": "GET, POST, PUT, OPTIONS",
  });
  res.end(body);
}

async function ensureGraphDir() {
  await fs.mkdir(path.dirname(GRAPH_FILE_PATH), { recursive: true });
}

async function ensureTodoDir() {
  await fs.mkdir(path.dirname(TODO_FILE_PATH), { recursive: true });
}

async function ensureChatHistoryDir() {
  await fs.mkdir(path.dirname(CHAT_HISTORY_FILE_PATH), { recursive: true });
}

async function ensureWorkflowDir() {
  await fs.mkdir(path.dirname(WORKFLOW_FILE_PATH), { recursive: true });
}

function readJsonBody(req) {
  return new Promise((resolve, reject) => {
    const chunks = [];
    req.on("data", (chunk) => chunks.push(chunk));
    req.on("end", () => {
      try {
        const raw = Buffer.concat(chunks).toString("utf8");
        resolve(raw ? JSON.parse(raw) : {});
      } catch (err) {
        reject(err);
      }
    });
    req.on("error", reject);
  });
}

function normalizeMessages(input) {
  if (!Array.isArray(input)) return [];
  return input
    .filter(
      (item) =>
        item &&
        typeof item === "object" &&
        (item.role === "user" || item.role === "assistant" || item.role === "system") &&
        typeof item.content === "string",
    )
    .map((item) => ({ role: item.role, content: item.content }));
}

const server = createServer(async (req, res) => {
  if (req.method === "OPTIONS") {
    res.writeHead(204, {
      "Access-Control-Allow-Origin": ALLOW_ORIGIN,
      "Access-Control-Allow-Headers": "Content-Type, Authorization",
      "Access-Control-Allow-Methods": "GET, POST, PUT, OPTIONS",
    });
    res.end();
    return;
  }

  if (req.method === "GET" && req.url === "/api/graph") {
    try {
      const content = await fs.readFile(GRAPH_FILE_PATH, "utf8");
      const parsed = content ? JSON.parse(content) : {};
      json(res, 200, parsed);
    } catch (err) {
      if (err && typeof err === "object" && "code" in err && err.code === "ENOENT") {
        json(res, 200, {
          version: 1,
          updatedAt: Date.now(),
          activeCanvasId: "",
          selectedId: null,
          canvases: [],
        });
        return;
      }
      text(res, 500, err instanceof Error ? err.message : String(err));
    }
    return;
  }

  if (req.method === "PUT" && req.url === "/api/graph") {
    try {
      const body = await readJsonBody(req);
      await ensureGraphDir();
      await fs.writeFile(
        GRAPH_FILE_PATH,
        `${JSON.stringify(body, null, 2)}\n`,
        "utf8",
      );
      json(res, 200, { ok: true, path: GRAPH_FILE_PATH });
    } catch (err) {
      text(res, 500, err instanceof Error ? err.message : String(err));
    }
    return;
  }

  if (req.method === "GET" && req.url === "/api/todo") {
    try {
      const content = await fs.readFile(TODO_FILE_PATH, "utf8");
      const parsed = content ? JSON.parse(content) : {};
      json(res, 200, parsed);
    } catch (err) {
      if (err && typeof err === "object" && "code" in err && err.code === "ENOENT") {
        json(res, 200, {
          version: 1,
          updatedAt: Date.now(),
          tasks: [],
        });
        return;
      }
      text(res, 500, err instanceof Error ? err.message : String(err));
    }
    return;
  }

  if (req.method === "PUT" && req.url === "/api/todo") {
    try {
      const body = await readJsonBody(req);
      await ensureTodoDir();
      await fs.writeFile(
        TODO_FILE_PATH,
        `${JSON.stringify(body, null, 2)}\n`,
        "utf8",
      );
      json(res, 200, { ok: true, path: TODO_FILE_PATH });
    } catch (err) {
      text(res, 500, err instanceof Error ? err.message : String(err));
    }
    return;
  }

  if (req.method === "GET" && req.url === "/api/chat-history") {
    try {
      const content = await fs.readFile(CHAT_HISTORY_FILE_PATH, "utf8");
      const parsed = content ? JSON.parse(content) : {};
      json(res, 200, parsed);
    } catch (err) {
      if (err && typeof err === "object" && "code" in err && err.code === "ENOENT") {
        json(res, 200, {
          version: 1,
          updatedAt: Date.now(),
          messages: [],
        });
        return;
      }
      text(res, 500, err instanceof Error ? err.message : String(err));
    }
    return;
  }

  if (req.method === "PUT" && req.url === "/api/chat-history") {
    try {
      const body = await readJsonBody(req);
      await ensureChatHistoryDir();
      await fs.writeFile(
        CHAT_HISTORY_FILE_PATH,
        `${JSON.stringify(body, null, 2)}\n`,
        "utf8",
      );
      json(res, 200, { ok: true, path: CHAT_HISTORY_FILE_PATH });
    } catch (err) {
      text(res, 500, err instanceof Error ? err.message : String(err));
    }
    return;
  }

  if (req.method === "GET" && req.url === "/api/workflow") {
    try {
      const content = await fs.readFile(WORKFLOW_FILE_PATH, "utf8");
      const parsed = content ? JSON.parse(content) : {};
      json(res, 200, parsed);
    } catch (err) {
      if (err && typeof err === "object" && "code" in err && err.code === "ENOENT") {
        json(res, 200, {
          version: 1,
          updatedAt: Date.now(),
          selectedIndex: -1,
          addressReadMode: false,
          projects: [],
        });
        return;
      }
      text(res, 500, err instanceof Error ? err.message : String(err));
    }
    return;
  }

  if (req.method === "PUT" && req.url === "/api/workflow") {
    try {
      const body = await readJsonBody(req);
      await ensureWorkflowDir();
      await fs.writeFile(
        WORKFLOW_FILE_PATH,
        `${JSON.stringify(body, null, 2)}\n`,
        "utf8",
      );
      json(res, 200, { ok: true, path: WORKFLOW_FILE_PATH });
    } catch (err) {
      text(res, 500, err instanceof Error ? err.message : String(err));
    }
    return;
  }

  if (req.method !== "POST" || req.url !== "/api/chat") {
    json(res, 404, { error: "Not Found" });
    return;
  }

  if (!DEEPSEEK_API_KEY) {
    json(res, 500, { error: "Missing DEEPSEEK_API_KEY on server." });
    return;
  }

  try {
    const body = await readJsonBody(req);
    const messages = normalizeMessages(body?.messages);
    if (!messages.length) {
      json(res, 400, { error: "Request must include messages array." });
      return;
    }

    const upstream = await fetch(`${DEEPSEEK_BASE_URL}/chat/completions`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bearer ${DEEPSEEK_API_KEY}`,
      },
      body: JSON.stringify({
        model: DEEPSEEK_MODEL,
        messages,
        stream: false,
      }),
    });

    const raw = await upstream.text();
    if (!upstream.ok) {
      text(res, upstream.status, raw || `DeepSeek HTTP ${upstream.status}`);
      return;
    }

    let content = "";
    try {
      const data = JSON.parse(raw);
      content = data?.choices?.[0]?.message?.content?.toString().trim() || "";
    } catch {
      // 非 JSON 时直接透传
    }

    if (content) {
      json(res, 200, { reply: content });
      return;
    }

    text(res, 200, raw);
  } catch (err) {
    text(res, 500, err instanceof Error ? err.message : String(err));
  }
});

server.listen(PORT, () => {
  console.log(`chat proxy running at http://127.0.0.1:${PORT}/api/chat`);
});
