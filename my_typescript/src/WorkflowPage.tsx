import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type ChangeEventHandler,
} from "react";
import {
  CATEGORIES,
  emptyProjectItem,
  type WorkProjectItem,
} from "./types";
import { loadState, saveState } from "./storage";
import { parseWorkProjectsXml, serializeWorkProjectsXml } from "./xmlCodec";

const PLUGIN_XML_RELATIVE = String.raw`SolidWorksAddinStudy\work_projects.xml`;
const PLUGIN_SHARED_JSON = String.raw`SolidWorksAddinStudy\shared_work_project.json`;
const DEFAULT_XML_FILENAME = "work_projects.xml";

function initialLocalState(): {
  projects: WorkProjectItem[];
  selectedIndex: number;
  addressReadMode: boolean;
} {
  const loaded = loadState();
  if (loaded?.projects?.length) {
    let idx = loaded.selectedIndex;
    if (idx < 0 || idx >= loaded.projects.length) idx = 0;
    return {
      projects: loaded.projects,
      selectedIndex: idx,
      addressReadMode: !!loaded.addressReadMode,
    };
  }
  return { projects: [], selectedIndex: -1, addressReadMode: false };
}

async function copyText(text: string): Promise<boolean> {
  try {
    await navigator.clipboard.writeText(text);
    return true;
  } catch {
    try {
      void window.prompt("复制以下路径（Ctrl+C）：", text);
      return true;
    } catch {
      return false;
    }
  }
}

export default function WorkflowPage() {
  const init = useMemo(() => initialLocalState(), []);
  const [projects, setProjects] = useState<WorkProjectItem[]>(init.projects);
  const [selectedIndex, setSelectedIndex] = useState(init.selectedIndex);
  const [addressReadMode, setAddressReadMode] = useState(init.addressReadMode);
  const [newName, setNewName] = useState("");
  const [toast, setToast] = useState<string | null>(null);
  const fileRef = useRef<HTMLInputElement>(null);

  const showToast = useCallback((msg: string) => {
    setToast(msg);
    window.setTimeout(() => setToast(null), 2600);
  }, []);

  useEffect(() => {
    saveState({ projects, selectedIndex, addressReadMode });
  }, [projects, selectedIndex, addressReadMode]);

  const current =
    selectedIndex >= 0 && selectedIndex < projects.length
      ? projects[selectedIndex]
      : null;

  const patchCurrent = useCallback(
    (patch: Partial<WorkProjectItem>) => {
      if (!current || selectedIndex < 0) return;
      setProjects((prev) => {
        const next = [...prev];
        next[selectedIndex] = { ...next[selectedIndex], ...patch };
        return next;
      });
    },
    [current, selectedIndex],
  );

  const syncBoundAssemblyJson = useCallback(() => {
    if (!current?.ProjectAssemblyPath?.trim()) {
      showToast("当前项目未填写装配体路径");
      return;
    }
    const json = JSON.stringify(
      { BoundAssemblyFullPath: current.ProjectAssemblyPath.trim() },
      null,
      2,
    );
    void copyText(json).then((ok) =>
      showToast(ok ? "已复制 shared_work_project.json 内容到剪贴板" : "复制失败"),
    );
  }, [current, showToast]);

  const copyPluginPathsHint = useCallback(() => {
    const hint = `%LOCALAPPDATA%\\${PLUGIN_XML_RELATIVE}\n%LOCALAPPDATA%\\${PLUGIN_SHARED_JSON}`;
    void copyText(hint).then((ok) =>
      showToast(ok ? "已复制插件数据文件路径说明" : "复制失败"),
    );
  }, [showToast]);

  const addProject = () => {
    const name = newName.trim();
    if (!name) {
      showToast("请输入项目名");
      return;
    }
    if (
      projects.some(
        (p) => p.ProjectName.trim().toLowerCase() === name.toLowerCase(),
      )
    ) {
      showToast("该项目已存在");
      return;
    }
    const item = emptyProjectItem(name);
    setProjects((p) => [...p, item]);
    setSelectedIndex(projects.length);
    setNewName("");
    showToast(`已新建项目: ${name}`);
  };

  const removeProject = () => {
    if (!current || selectedIndex < 0) return;
    if (!window.confirm(`确认删除项目「${current.ProjectName}」？`)) return;
    const r = selectedIndex;
    const next = projects.filter((_, i) => i !== r);
    const sel =
      next.length === 0 ? -1 : r >= next.length ? next.length - 1 : r;
    setProjects(next);
    setSelectedIndex(sel);
    showToast("项目已删除");
  };

  const onImportXml: ChangeEventHandler<HTMLInputElement> = async (e) => {
    const f = e.target.files?.[0];
    e.target.value = "";
    if (!f) return;
    try {
      const text = await f.text();
      const list = parseWorkProjectsXml(text);
      if (!list.length) {
        showToast("XML 中未解析到项目");
        return;
      }
      setProjects(list);
      setSelectedIndex(0);
      showToast(`已导入 ${list.length} 个项目`);
    } catch (err) {
      showToast(`导入失败：${err instanceof Error ? err.message : String(err)}`);
    }
  };

  const exportXml = () => {
    try {
      const xml = serializeWorkProjectsXml(projects);
      const blob = new Blob([xml], { type: "application/xml" });
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = DEFAULT_XML_FILENAME;
      a.click();
      URL.revokeObjectURL(url);
      showToast(`已下载 ${DEFAULT_XML_FILENAME}`);
    } catch (err) {
      showToast(`导出失败：${err instanceof Error ? err.message : String(err)}`);
    }
  };

  const pasteAsmFromClipboard = async () => {
    if (!current) return;
    try {
      const t = (await navigator.clipboard.readText()).trim();
      if (!t) {
        showToast("剪贴板为空");
        return;
      }
      patchCurrent({ ProjectAssemblyPath: t });
      showToast("已写入装配体路径");
    } catch {
      showToast("无法读取剪贴板（需浏览器权限）");
    }
  };

  const handlePathActivate = (
    field: keyof WorkProjectItem,
    pathKind: "file" | "folder",
  ) => {
    const raw = current?.[field];
    const path = typeof raw === "string" ? raw.trim() : "";
    if (addressReadMode) {
      if (!path) {
        showToast("请先填写路径");
        return;
      }
      void copyText(path).then((ok) =>
        showToast(ok ? "已复制路径到剪贴板，可在资源管理器地址栏粘贴打开" : "复制失败"),
      );
      return;
    }
    const suggestion =
      pathKind === "file"
        ? "请输入文件完整路径（浏览器无法像插件那样弹出系统文件框）。可从资源管理器地址栏复制。"
        : "请输入文件夹完整路径，可从资源管理器地址栏复制。";
    showToast(suggestion);
    void path;
  };

  const statusRight =
    current == null
      ? "请选择或新建项目"
      : addressReadMode
        ? "地址模式: 读取（点击地址框可复制路径）"
        : "地址模式: 写入（点击地址框查看填写说明）";

  return (
    <div className="workflow-shell">
      <header className="header-panel workflow-toolbar">
        <span>
          <label htmlFor="pn">项目名:</label>
          <input
            id="pn"
            type="text"
            value={newName}
            onChange={(e) => setNewName(e.target.value)}
            onKeyDown={(e) => {
              if (e.key === "Enter") addProject();
            }}
            placeholder="名称"
          />
        </span>
        <button type="button" className="primary" onClick={addProject}>
          新建项目
        </button>
        <button type="button" onClick={removeProject} disabled={!current}>
          删除项目
        </button>
        <button type="button" onClick={() => fileRef.current?.click()}>
          导入 XML（默认插件格式）
        </button>
        <button type="button" onClick={exportXml}>
          导出 {DEFAULT_XML_FILENAME}
        </button>
        <button type="button" onClick={copyPluginPathsHint}>
          复制插件数据路径
        </button>
        <button type="button" onClick={syncBoundAssemblyJson}>
          复制绑定 JSON
        </button>
        <span className="toolbar-muted">{statusRight}</span>
        <input
          ref={fileRef}
          className="hidden-input"
          type="file"
          accept=".xml,application/xml,text/xml"
          onChange={onImportXml}
        />
      </header>

      <div className="main-split">
        <aside className="side-list">
          <ul>
            {projects.map((p, i) => (
              <li
                key={`${p.ProjectName}-${i}`}
                className={i === selectedIndex ? "active" : ""}
                onClick={() => setSelectedIndex(i)}
              >
                {p.ProjectName}
              </li>
            ))}
          </ul>
          {projects.length === 0 ? (
            <p className="toolbar-muted" style={{ padding: "8px 12px", margin: 0 }}>
              暂无项目
            </p>
          ) : null}
        </aside>

        <section className="detail-panel">
          <div className="asm-toolbar">
            <strong style={{ marginRight: 8 }}>项目装配体</strong>
            <input
              type="text"
              placeholder="装配体 .sldasm 完整路径"
              value={current?.ProjectAssemblyPath ?? ""}
              disabled={!current}
              onChange={(e) =>
                current && patchCurrent({ ProjectAssemblyPath: e.target.value })
              }
            />
            <button type="button" disabled={!current} onClick={pasteAsmFromClipboard}>
              从剪贴板写入路径
            </button>
            <button
              type="button"
              disabled={!current || !current.ProjectAssemblyPath.trim()}
              onClick={() => {
                const p = current!.ProjectAssemblyPath.trim();
                void copyText(p).then((ok) =>
                  showToast(ok ? "已复制装配体路径" : "复制失败"),
                );
              }}
            >
              复制路径
            </button>
            <button
              type="button"
              onClick={() => {
                setAddressReadMode((v) => !v);
              }}
            >
              {addressReadMode ? "模式: 读取" : "模式: 写入"}
            </button>
          </div>

          <div className="detail-scroll">
            <p className="toolbar-muted" style={{ marginTop: 0 }}>
              与 SolidWorks 插件共用数据：将导出的 <code>{DEFAULT_XML_FILENAME}</code>{" "}
              放到 <code>%LOCALAPPDATA%\{PLUGIN_XML_RELATIVE}</code>
              （可先备份原文件）。「复制绑定 JSON」对应插件的 shared_work_project.json。
              列表数据会默认保存在浏览器本地。
            </p>
            {CATEGORIES.map((cat) => {
              const pathKey = cat.pathFields.path;
              const followKey = cat.pathFields.followUp;
              const pathVal = current?.[pathKey];
              const followVal = current?.[followKey];
              const pathStr = typeof pathVal === "string" ? pathVal : "";
              const followStr = typeof followVal === "string" ? followVal : "";
              const kind = cat.pathIsFile ? "file" : "folder";

              return (
                <div key={cat.key} className="category-block">
                  <h3>{cat.title}</h3>
                  <div className="field-grid">
                    <label className="caption">地址</label>
                    <input
                      type="text"
                      disabled={!current}
                      value={pathStr}
                      onChange={(e) =>
                        current &&
                        patchCurrent({ [pathKey]: e.target.value } as Partial<WorkProjectItem>)
                      }
                      onClick={() => handlePathActivate(pathKey, kind)}
                      onDoubleClick={() => handlePathActivate(pathKey, kind)}
                      onKeyDown={(ev) => {
                        if (ev.key === "Enter") {
                          ev.preventDefault();
                          handlePathActivate(pathKey, kind);
                        }
                      }}
                    />
                    <label className="caption">跟进</label>
                    <textarea
                      disabled={!current}
                      value={followStr}
                      onChange={(e) =>
                        current &&
                        patchCurrent({ [followKey]: e.target.value } as Partial<WorkProjectItem>)
                      }
                    />
                  </div>
                </div>
              );
            })}
          </div>
        </section>
      </div>

      {toast ? <div className="toast">{toast}</div> : null}
    </div>
  );
}
