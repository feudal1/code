import { ClipboardEvent, FormEvent, useEffect, useMemo, useState } from "react";

type TodoTask = {
  id: string;
  title: string;
  note: string;
  imageDataUrl?: string;
  completed: boolean;
  createdAt: number;
};

const TODO_TASKS_STORAGE_KEY = "todo_task_page_tasks_v1";
const TODO_API_URL = import.meta.env.VITE_TODO_API_URL?.trim() ?? "";
const AUTO_SYNC_DEBOUNCE_MS = 1000;

type TodoCachePayload = {
  version: 1;
  updatedAt: number;
  tasks: TodoTask[];
};

function loadTodoTasks(): TodoTask[] {
  try {
    const raw = localStorage.getItem(TODO_TASKS_STORAGE_KEY);
    if (!raw) {
      return [];
    }
    const parsed = JSON.parse(raw) as unknown;
    if (!Array.isArray(parsed)) {
      return [];
    }
    return parsed.filter((item): item is TodoTask => {
      if (!item || typeof item !== "object") {
        return false;
      }
      const task = item as Partial<TodoTask>;
      return (
        typeof task.id === "string" &&
        typeof task.title === "string" &&
        typeof task.note === "string" &&
        typeof task.completed === "boolean" &&
        typeof task.createdAt === "number" &&
        (typeof task.imageDataUrl === "string" || typeof task.imageDataUrl === "undefined")
      );
    });
  } catch {
    return [];
  }
}

function getTodoApiUrl(): string {
  if (TODO_API_URL) {
    return TODO_API_URL;
  }
  const chatApiUrl = import.meta.env.VITE_CHAT_API_URL?.trim() ?? "";
  if (!chatApiUrl) {
    return "";
  }
  return chatApiUrl.replace(/\/api\/chat$/i, "/api/todo");
}

function toDataUrl(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result ?? ""));
    reader.onerror = () => reject(new Error("读取图片失败"));
    reader.readAsDataURL(file);
  });
}

export default function TodoTaskPage() {
  const [tasks, setTasks] = useState<TodoTask[]>(() => loadTodoTasks());
  const [title, setTitle] = useState("");
  const [note, setNote] = useState("");
  const [imageDataUrl, setImageDataUrl] = useState<string | undefined>(undefined);
  const [imageName, setImageName] = useState("");
  const [isReadingImage, setIsReadingImage] = useState(false);
  const [remoteStatus, setRemoteStatus] = useState("未连接项目级存储");
  const todoApiUrl = getTodoApiUrl();

  const completedCount = useMemo(
    () => tasks.filter((task) => task.completed).length,
    [tasks],
  );

  useEffect(() => {
    try {
      localStorage.setItem(TODO_TASKS_STORAGE_KEY, JSON.stringify(tasks));
    } catch {
      // ignore quota / private mode
    }
  }, [tasks]);

  useEffect(() => {
    let cancelled = false;
    async function loadRemoteTasks() {
      if (!todoApiUrl) {
        setRemoteStatus("未配置待办接口地址（仅本地缓存）");
        return;
      }
      try {
        const res = await fetch(todoApiUrl);
        const text = await res.text();
        if (!res.ok) {
          throw new Error(text || `HTTP ${res.status}`);
        }
        const payload = JSON.parse(text) as TodoCachePayload;
        if (!Array.isArray(payload.tasks)) {
          throw new Error("返回数据格式无效");
        }
        if (cancelled) {
          return;
        }
        const safeTasks = payload.tasks.filter((item): item is TodoTask => {
          if (!item || typeof item !== "object") {
            return false;
          }
          const task = item as Partial<TodoTask>;
          return (
            typeof task.id === "string" &&
            typeof task.title === "string" &&
            typeof task.note === "string" &&
            typeof task.completed === "boolean" &&
            typeof task.createdAt === "number" &&
            (typeof task.imageDataUrl === "string" || typeof task.imageDataUrl === "undefined")
          );
        });
        setTasks((prev) => {
          if (safeTasks.length === 0 && prev.length > 0) {
            return prev;
          }
          return safeTasks;
        });
        setRemoteStatus(`已从项目文件加载（${new Date().toLocaleTimeString()}）`);
      } catch (error) {
        if (!cancelled) {
          setRemoteStatus(`项目级加载失败：${error instanceof Error ? error.message : String(error)}`);
        }
      }
    }
    void loadRemoteTasks();
    return () => {
      cancelled = true;
    };
  }, [todoApiUrl]);

  useEffect(() => {
    if (!todoApiUrl) {
      return;
    }
    const timer = window.setTimeout(async () => {
      try {
        const payload: TodoCachePayload = {
          version: 1,
          updatedAt: Date.now(),
          tasks,
        };
        const res = await fetch(todoApiUrl, {
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
        setRemoteStatus(`项目级同步失败：${error instanceof Error ? error.message : String(error)}`);
      }
    }, AUTO_SYNC_DEBOUNCE_MS);
    return () => {
      window.clearTimeout(timer);
    };
  }, [tasks, todoApiUrl]);

  async function handleImagePaste(event: ClipboardEvent<HTMLTextAreaElement>) {
    const imageItem = Array.from(event.clipboardData.items).find((item) =>
      item.type.startsWith("image/"),
    );
    if (!imageItem) {
      return;
    }
    const file = imageItem.getAsFile();
    if (!file) {
      return;
    }
    event.preventDefault();
    try {
      setIsReadingImage(true);
      const dataUrl = await toDataUrl(file);
      setImageDataUrl(dataUrl);
      setImageName(file.name || "粘贴图片");
    } catch {
      alert("图片读取失败，请重试");
    } finally {
      setIsReadingImage(false);
    }
  }

  function handleCreateTask(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const trimmedTitle = title.trim();
    if (!trimmedTitle) {
      return;
    }

    const nextTask: TodoTask = {
      id: `${Date.now()}-${Math.random().toString(16).slice(2)}`,
      title: trimmedTitle,
      note: note.trim(),
      imageDataUrl,
      completed: false,
      createdAt: Date.now(),
    };

    setTasks((prev) => [nextTask, ...prev]);
    setTitle("");
    setNote("");
    setImageDataUrl(undefined);
    setImageName("");
  }

  function toggleCompleted(taskId: string) {
    setTasks((prev) =>
      prev.map((task) =>
        task.id === taskId ? { ...task, completed: !task.completed } : task,
      ),
    );
  }

  return (
    <div className="todo-shell">
      <section className="todo-create-panel">
        <h2>待做任务管理</h2>
        <p className="toolbar-muted">
          共 {tasks.length} 项，已完成 {completedCount} 项
        </p>
        <p className="toolbar-muted">{remoteStatus}</p>
        <form className="todo-form" onSubmit={handleCreateTask}>
          <input
            type="text"
            placeholder="任务标题（必填）"
            value={title}
            onChange={(event) => setTitle(event.target.value)}
          />
          <textarea
            placeholder="任务说明（可选）"
            value={note}
            onChange={(event) => setNote(event.target.value)}
          />

          <div className="todo-image-row">
            <label htmlFor="todo-image-paste">任务图片（在下方文本框粘贴）：</label>
            <textarea
              id="todo-image-paste"
              className="todo-paste-box"
              placeholder="点击这里后，按 Ctrl+V 粘贴图片"
              onPaste={handleImagePaste}
            />
            {imageName ? (
              <span className="toolbar-muted">已粘贴：{imageName}</span>
            ) : (
              <span className="toolbar-muted">未粘贴图片</span>
            )}
            <button
              type="button"
              onClick={() => {
                setImageDataUrl(undefined);
                setImageName("");
              }}
              disabled={!imageDataUrl}
            >
              清空图片
            </button>
          </div>

          {imageDataUrl ? (
            <img className="todo-image-preview" src={imageDataUrl} alt="任务图片预览" />
          ) : null}

          <button className="primary" type="submit" disabled={!title.trim() || isReadingImage}>
            {isReadingImage ? "读取图片中..." : "新建任务"}
          </button>
        </form>
      </section>

      <section className="todo-list-panel">
        <h3>任务列表</h3>
        {tasks.length === 0 ? (
          <p className="toolbar-muted">还没有任务，先新建一个吧。</p>
        ) : (
          <ul className="todo-list">
            {tasks.map((task) => (
              <li
                key={task.id}
                className={task.completed ? "todo-item todo-item--completed" : "todo-item"}
              >
                <label className="todo-item-head">
                  <input
                    type="checkbox"
                    checked={task.completed}
                    onChange={() => toggleCompleted(task.id)}
                  />
                  <span>{task.title}</span>
                </label>

                {task.note ? <p className="todo-item-note">{task.note}</p> : null}

                {task.imageDataUrl ? (
                  <img className="todo-item-image" src={task.imageDataUrl} alt={task.title} />
                ) : null}

                <span className="toolbar-muted">
                  {new Date(task.createdAt).toLocaleString()}
                </span>
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}
