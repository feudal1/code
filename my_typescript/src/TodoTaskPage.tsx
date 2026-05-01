import { ChangeEvent, FormEvent, useMemo, useState } from "react";

type TodoTask = {
  id: string;
  title: string;
  note: string;
  imageDataUrl?: string;
  completed: boolean;
  createdAt: number;
};

function toDataUrl(file: File): Promise<string> {
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(String(reader.result ?? ""));
    reader.onerror = () => reject(new Error("读取图片失败"));
    reader.readAsDataURL(file);
  });
}

export default function TodoTaskPage() {
  const [tasks, setTasks] = useState<TodoTask[]>([]);
  const [title, setTitle] = useState("");
  const [note, setNote] = useState("");
  const [imageDataUrl, setImageDataUrl] = useState<string | undefined>(undefined);
  const [imageName, setImageName] = useState("");
  const [isReadingImage, setIsReadingImage] = useState(false);

  const completedCount = useMemo(
    () => tasks.filter((task) => task.completed).length,
    [tasks],
  );

  async function handleImageChange(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    if (!file) {
      setImageDataUrl(undefined);
      setImageName("");
      return;
    }
    if (!file.type.startsWith("image/")) {
      alert("请选择图片文件");
      event.target.value = "";
      return;
    }

    try {
      setIsReadingImage(true);
      const dataUrl = await toDataUrl(file);
      setImageDataUrl(dataUrl);
      setImageName(file.name);
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
            <label htmlFor="todo-image-input">任务图片：</label>
            <input
              id="todo-image-input"
              type="file"
              accept="image/*"
              onChange={handleImageChange}
            />
            {imageName ? (
              <span className="toolbar-muted">已选择：{imageName}</span>
            ) : (
              <span className="toolbar-muted">未选择图片</span>
            )}
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
