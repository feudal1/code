import { useCallback, useState } from "react";
import AiChatPage from "./AiChatPage";
import DesignKnowledgeGraphPage from "./DesignKnowledgeGraphPage";
import TodoTaskPage from "./TodoTaskPage";
import WorkflowPage from "./WorkflowPage";

type RootTab = "chat" | "workflow" | "knowledgeGraph" | "todoTask";

export default function App() {
  const [tab, setTab] = useState<RootTab>("chat");

  const goChat = useCallback(() => setTab("chat"), []);
  const goWorkflow = useCallback(() => setTab("workflow"), []);
  const goKnowledgeGraph = useCallback(() => setTab("knowledgeGraph"), []);
  const goTodoTask = useCallback(() => setTab("todoTask"), []);

  return (
    <div className="app-shell">
      <nav className="root-nav" aria-label="主导航">
        <div className="root-brand">工作项目助手</div>
        <div className="root-tabs">
          <button
            type="button"
            className={tab === "chat" ? "root-tab root-tab--active" : "root-tab"}
            onClick={goChat}
          >
            AI 对话
          </button>
          <button
            type="button"
            className={tab === "workflow" ? "root-tab root-tab--active" : "root-tab"}
            onClick={goWorkflow}
          >
            流程管理
          </button>
          <button
            type="button"
            className={tab === "knowledgeGraph" ? "root-tab root-tab--active" : "root-tab"}
            onClick={goKnowledgeGraph}
          >
            设计笔记图谱
          </button>
          <button
            type="button"
            className={tab === "todoTask" ? "root-tab root-tab--active" : "root-tab"}
            onClick={goTodoTask}
          >
            待做任务
          </button>
        </div>
      </nav>

      <main className="root-main">
        {tab === "chat" ? (
          <AiChatPage />
        ) : tab === "workflow" ? (
          <WorkflowPage />
        ) : tab === "knowledgeGraph" ? (
          <DesignKnowledgeGraphPage />
        ) : (
          <TodoTaskPage />
        )}
      </main>
    </div>
  );
}
