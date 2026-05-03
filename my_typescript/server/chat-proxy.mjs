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
const NOTES_DIR = path.resolve(process.cwd(), "相关文档", "设计笔记");
const SRC_DIR = path.resolve(process.cwd(), "src");

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

function sanitizeDesignNoteBasename(name) {
  return String(name ?? "")
    .replace(/[/\\?%*:|"<>]/g, "_")
    .trim()
    .slice(0, 80);
}

function resolveDesignNoteAssetAbs(globId) {
  if (typeof globId !== "string" || !globId.trim()) {
    throw new Error("缺少素材 id");
  }
  const abs = path.resolve(SRC_DIR, globId);
  const normalizedNotes = path.normalize(NOTES_DIR);
  const normalizedAbs = path.normalize(abs);
  const rel = path.relative(normalizedNotes, normalizedAbs);
  if (rel.startsWith("..") || path.isAbsolute(rel)) {
    throw new Error("非法素材路径");
  }
  if (!/\.(txt|png)$/i.test(normalizedAbs)) {
    throw new Error("仅支持 .txt 与 .png");
  }
  return normalizedAbs;
}

function buildManualEdgeId(source, target) {
  return source < target ? `${source}@@${target}` : `${target}@@${source}`;
}

function remapAssetIdInGraphPayload(payload, oldId, newId) {
  if (!payload || typeof payload !== "object") {
    return;
  }
  if (payload.selectedId === oldId) {
    payload.selectedId = newId;
  }
  const limits = payload.assetUseLimits;
  if (limits && typeof limits === "object" && Object.prototype.hasOwnProperty.call(limits, oldId)) {
    const v = limits[oldId];
    delete limits[oldId];
    limits[newId] = v;
  }
  const canvases = payload.canvases;
  if (!Array.isArray(canvases)) {
    return;
  }
  for (const c of canvases) {
    if (c.placedNodes && typeof c.placedNodes === "object" && Object.prototype.hasOwnProperty.call(c.placedNodes, oldId)) {
      c.placedNodes[newId] = c.placedNodes[oldId];
      delete c.placedNodes[oldId];
    }
    if (Array.isArray(c.manualEdges)) {
      const seen = new Set();
      const next = [];
      for (const e of c.manualEdges) {
        if (!e || typeof e !== "object") {
          continue;
        }
        const s = e.source === oldId ? newId : e.source;
        const t = e.target === oldId ? newId : e.target;
        if (typeof s !== "string" || typeof t !== "string" || s === t) {
          continue;
        }
        const id = buildManualEdgeId(s, t);
        if (seen.has(id)) {
          continue;
        }
        seen.add(id);
        next.push({ id, source: s, target: t });
      }
      c.manualEdges = next;
    }
    if (c.groupNames && typeof c.groupNames === "object") {
      const nextGn = {};
      for (const [k, v] of Object.entries(c.groupNames)) {
        const parts = k.split("||").map((p) => (p === oldId ? newId : p)).filter(Boolean);
        if (parts.length < 2) {
          continue;
        }
        const nk = parts.slice().sort((a, b) => a.localeCompare(b, "zh-CN")).join("||");
        nextGn[nk] = typeof v === "string" ? v : String(v ?? "");
      }
      c.groupNames = nextGn;
    }
  }
}

function stripAssetFromGraphPayload(payload, assetId) {
  if (!payload || typeof payload !== "object") {
    return;
  }
  if (payload.selectedId === assetId) {
    payload.selectedId = null;
  }
  const limits = payload.assetUseLimits;
  if (limits && typeof limits === "object" && Object.prototype.hasOwnProperty.call(limits, assetId)) {
    delete limits[assetId];
  }
  const canvases = payload.canvases;
  if (!Array.isArray(canvases)) {
    return;
  }
  for (const c of canvases) {
    if (c.placedNodes && typeof c.placedNodes === "object") {
      delete c.placedNodes[assetId];
    }
    if (Array.isArray(c.manualEdges)) {
      c.manualEdges = c.manualEdges.filter(
        (e) => e && e.source !== assetId && e.target !== assetId,
      );
    }
    if (c.groupNames && typeof c.groupNames === "object") {
      const nextGn = {};
      for (const [k, v] of Object.entries(c.groupNames)) {
        if (k.split("||").includes(assetId)) {
          continue;
        }
        nextGn[k] = v;
      }
      c.groupNames = nextGn;
    }
  }
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

  if (req.method === "POST" && req.url === "/api/design-notes/rename") {
    try {
      const body = await readJsonBody(req);
      const id = typeof body.id === "string" ? body.id.trim() : "";
      const rawNew =
        typeof body.newBaseName === "string"
          ? body.newBaseName
          : typeof body.newTitle === "string"
            ? body.newTitle
            : "";
      const newBaseName = sanitizeDesignNoteBasename(rawNew.replace(/\.(txt|png)$/i, "").trim());
      if (!id) {
        json(res, 400, { error: "缺少 id" });
        return;
      }
      if (!newBaseName) {
        json(res, 400, { error: "新文件名无效" });
        return;
      }
      const oldAbs = resolveDesignNoteAssetAbs(id);
      const ext = path.extname(oldAbs).toLowerCase();
      if (ext !== ".txt" && ext !== ".png") {
        json(res, 400, { error: "不支持的扩展名" });
        return;
      }
      const newAbs = path.join(NOTES_DIR, `${newBaseName}${ext}`);
      const newId = `../相关文档/设计笔记/${newBaseName}${ext}`;
      try {
        await fs.access(oldAbs);
      } catch (accErr) {
        if (accErr && typeof accErr === "object" && "code" in accErr && accErr.code === "ENOENT") {
          json(res, 404, { error: "源文件不存在" });
          return;
        }
        throw accErr;
      }
      if (path.normalize(oldAbs) === path.normalize(newAbs)) {
        json(res, 200, { ok: true, newId });
        return;
      }
      try {
        await fs.access(newAbs);
        json(res, 409, { error: "目标文件已存在" });
        return;
      } catch (exErr) {
        if (!(exErr && typeof exErr === "object" && "code" in exErr && exErr.code === "ENOENT")) {
          throw exErr;
        }
      }
      await fs.rename(oldAbs, newAbs);
      try {
        const raw = await fs.readFile(GRAPH_FILE_PATH, "utf8");
        const payload = raw ? JSON.parse(raw) : {};
        remapAssetIdInGraphPayload(payload, id, newId);
        payload.updatedAt = Date.now();
        await ensureGraphDir();
        await fs.writeFile(GRAPH_FILE_PATH, `${JSON.stringify(payload, null, 2)}\n`, "utf8");
      } catch (graphErr) {
        if (graphErr && typeof graphErr === "object" && "code" in graphErr && graphErr.code === "ENOENT") {
          // 尚无 graph-cache.json，忽略
        } else {
          await fs.rename(newAbs, oldAbs);
          throw graphErr;
        }
      }
      json(res, 200, { ok: true, newId });
    } catch (err) {
      json(res, 500, { error: err instanceof Error ? err.message : String(err) });
    }
    return;
  }

  if (req.method === "POST" && req.url === "/api/design-notes/delete") {
    try {
      const body = await readJsonBody(req);
      const id = typeof body.id === "string" ? body.id.trim() : "";
      if (!id) {
        json(res, 400, { error: "缺少 id" });
        return;
      }
      const absPath = resolveDesignNoteAssetAbs(id);
      try {
        await fs.unlink(absPath);
      } catch (unlinkErr) {
        if (
          unlinkErr &&
          typeof unlinkErr === "object" &&
          "code" in unlinkErr &&
          unlinkErr.code === "ENOENT"
        ) {
          json(res, 404, { error: "文件不存在" });
          return;
        }
        throw unlinkErr;
      }
      try {
        const raw = await fs.readFile(GRAPH_FILE_PATH, "utf8");
        if (raw) {
          const payload = JSON.parse(raw);
          stripAssetFromGraphPayload(payload, id);
          payload.updatedAt = Date.now();
          await ensureGraphDir();
          await fs.writeFile(GRAPH_FILE_PATH, `${JSON.stringify(payload, null, 2)}\n`, "utf8");
        }
      } catch (graphErr) {
        if (
          !(graphErr && typeof graphErr === "object" && "code" in graphErr && graphErr.code === "ENOENT")
        ) {
          console.error("design-notes/delete graph update:", graphErr);
        }
      }
      json(res, 200, { ok: true });
    } catch (err) {
      json(res, 500, { error: err instanceof Error ? err.message : String(err) });
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
