import { useEffect, useMemo, useRef, useState, type DragEvent, type PointerEvent as ReactPointerEvent } from "react";

type AssetNode = {
  id: string;
  title: string;
  kind: "txt" | "png";
  content: string;
  url?: string;
};

type ManualEdge = {
  id: string;
  source: string;
  target: string;
};

type NodeSize = "small" | "medium" | "large";

type CanvasData = {
  id: string;
  name: string;
  placedNodes: Record<string, { x: number; y: number }>;
  manualEdges: ManualEdge[];
  nodeSize: NodeSize;
  groupNames: Record<string, string>;
};

type GraphCachePayload = {
  version: 1;
  updatedAt: number;
  activeCanvasId: string;
  selectedId: string | null;
  canvases: CanvasData[];
  /** 每个素材最多可放置到多少个画布（同一画布仍只能放一份）；缺省为 1 */
  assetUseLimits?: Record<string, number>;
};

const STORAGE_KEY = "design-note-canvas-cache-v1";
const LEGACY_CANVAS_LIST_KEY = "design-note-canvas-list-v1";
const LEGACY_EDGE_KEY = "design-note-manual-graph-v1";
const LEGACY_PLACED_KEY = "design-note-placed-nodes-v1";
const STORAGE_SIZE_KEY = "design-note-node-size-v1";
const GRAPH_API_URL = import.meta.env.VITE_GRAPH_API_URL?.trim() ?? "";
const AUTO_SYNC_DEBOUNCE_MS = 1200;

const RAW_TXT = import.meta.glob("../相关文档/设计笔记/*.txt", {
  eager: true,
  query: "?raw",
  import: "default",
}) as Record<string, string>;

const RAW_PNG = import.meta.glob("../相关文档/设计笔记/*.png", {
  eager: true,
  import: "default",
}) as Record<string, string>;

function normalizeTitle(path: string) {
  const file = path.split("/").pop() ?? path;
  return file.replace(/\.(txt|png)$/i, "");
}

function buildAssetNodes() {
  const txtNodes: AssetNode[] = Object.entries(RAW_TXT).map(([path, content]) => ({
    id: path,
    title: normalizeTitle(path),
    kind: "txt",
    content,
  }));
  const pngNodes: AssetNode[] = Object.entries(RAW_PNG).map(([path, url]) => ({
    id: path,
    title: normalizeTitle(path),
    kind: "png",
    content: "",
    url,
  }));
  return [...txtNodes, ...pngNodes].sort((a, b) => a.title.localeCompare(b.title, "zh-CN"));
}

function buildEdgeId(source: string, target: string) {
  return source < target ? `${source}@@${target}` : `${target}@@${source}`;
}

const ASSET_USE_LIMIT_MIN = 1;
const ASSET_USE_LIMIT_MAX = 99;

function countAssetPlacements(allCanvases: CanvasData[], assetId: string) {
  return allCanvases.reduce((n, c) => n + (c.placedNodes[assetId] ? 1 : 0), 0);
}

function getAssetUseLimit(limits: Record<string, number>, assetId: string) {
  const v = limits[assetId];
  if (typeof v !== "number" || !Number.isFinite(v)) {
    return ASSET_USE_LIMIT_MIN;
  }
  return Math.min(ASSET_USE_LIMIT_MAX, Math.max(ASSET_USE_LIMIT_MIN, Math.floor(v)));
}

function sanitizeAssetUseLimits(
  raw: unknown,
  nodeIdSet: Set<string>,
): Record<string, number> {
  if (!raw || typeof raw !== "object") {
    return {};
  }
  const next: Record<string, number> = {};
  for (const [id, val] of Object.entries(raw as Record<string, unknown>)) {
    if (!nodeIdSet.has(id) || typeof val !== "number" || !Number.isFinite(val)) {
      continue;
    }
    const n = Math.floor(val);
    if (n >= ASSET_USE_LIMIT_MIN && n <= ASSET_USE_LIMIT_MAX) {
      next[id] = n;
    }
  }
  return next;
}

function isValidEdge(edge: ManualEdge, nodeIdSet: Set<string>) {
  return nodeIdSet.has(edge.source) && nodeIdSet.has(edge.target) && edge.source !== edge.target;
}

function createCanvas(name: string): CanvasData {
  return {
    id: `canvas-${Date.now()}-${Math.random().toString(36).slice(2, 7)}`,
    name,
    placedNodes: {},
    manualEdges: [],
    nodeSize: "medium",
    groupNames: {},
  };
}

function buildGroupKey(nodeIds: string[]) {
  return nodeIds.slice().sort().join("||");
}

function getGraphApiUrl() {
  if (GRAPH_API_URL) {
    return GRAPH_API_URL;
  }
  const chatApiUrl = import.meta.env.VITE_CHAT_API_URL?.trim() ?? "";
  if (!chatApiUrl) {
    return "";
  }
  return chatApiUrl.replace(/\/api\/chat$/i, "/api/graph");
}

export default function DesignKnowledgeGraphPage() {
  const boardRef = useRef<HTMLDivElement | null>(null);
  const noteItemRefs = useRef<Record<string, HTMLLIElement | null>>({});
  const remoteInitDoneRef = useRef(false);
  const nodes = useMemo(() => buildAssetNodes(), []);
  const [selectedId, setSelectedId] = useState<string | null>(nodes[0]?.id ?? null);
  const [canvases, setCanvases] = useState<CanvasData[]>([]);
  const [activeCanvasId, setActiveCanvasId] = useState<string>("");
  const [pendingSourceId, setPendingSourceId] = useState<string | null>(null);
  const [selectedGroupKey, setSelectedGroupKey] = useState<string>("");
  const [previewImage, setPreviewImage] = useState<{ url: string; title: string } | null>(null);
  const [canvasDialog, setCanvasDialog] = useState<{ mode: "create" | "rename"; value: string } | null>(null);
  const [draggingNode, setDraggingNode] = useState<{ id: string; offsetX: number; offsetY: number } | null>(null);
  const [lastSyncedAt, setLastSyncedAt] = useState<number | null>(null);
  const [remoteSyncStatus, setRemoteSyncStatus] = useState("未同步到项目文件");
  const [assetUseLimits, setAssetUseLimits] = useState<Record<string, number>>({});

  useEffect(() => {
    const nodeIdSet = new Set(nodes.map((item) => item.id));
    try {
      const raw = localStorage.getItem(STORAGE_KEY);
      if (raw) {
        const parsed = JSON.parse(raw) as GraphCachePayload;
        setAssetUseLimits(sanitizeAssetUseLimits(parsed.assetUseLimits, nodeIdSet));
        const safeCanvases = (parsed.canvases ?? [])
          .map((item) => ({
            ...item,
            manualEdges: (item.manualEdges ?? []).filter((edge) => isValidEdge(edge, nodeIdSet)),
            placedNodes: Object.fromEntries(
              Object.entries(item.placedNodes ?? {}).filter(([id]) => nodeIdSet.has(id)),
            ) as Record<string, { x: number; y: number }>,
            nodeSize:
              item.nodeSize === "small" || item.nodeSize === "medium" || item.nodeSize === "large"
                ? item.nodeSize
                : "medium",
            groupNames:
              item.groupNames && typeof item.groupNames === "object"
                ? (item.groupNames as Record<string, string>)
                : {},
          }))
          .filter((item) => item.id && item.name);
        if (safeCanvases.length > 0) {
          setCanvases(safeCanvases);
          const cachedActiveId = safeCanvases.some((item) => item.id === parsed.activeCanvasId)
            ? parsed.activeCanvasId
            : safeCanvases[0].id;
          setActiveCanvasId(cachedActiveId);
          setSelectedId(parsed.selectedId ?? null);
          setLastSyncedAt(typeof parsed.updatedAt === "number" ? parsed.updatedAt : Date.now());
          return;
        }
      }

      const legacyCanvasRaw = localStorage.getItem(LEGACY_CANVAS_LIST_KEY);
      if (legacyCanvasRaw) {
        setAssetUseLimits({});
        const parsed = JSON.parse(legacyCanvasRaw) as CanvasData[];
        const safeCanvases = parsed
          .map((item) => ({
            ...item,
            manualEdges: (item.manualEdges ?? []).filter((edge) => isValidEdge(edge, nodeIdSet)),
            placedNodes: Object.fromEntries(
              Object.entries(item.placedNodes ?? {}).filter(([id]) => nodeIdSet.has(id)),
            ) as Record<string, { x: number; y: number }>,
            nodeSize:
              item.nodeSize === "small" || item.nodeSize === "medium" || item.nodeSize === "large"
                ? item.nodeSize
                : "medium",
            groupNames:
              item.groupNames && typeof item.groupNames === "object"
                ? (item.groupNames as Record<string, string>)
                : {},
          }))
          .filter((item) => item.id && item.name);
        if (safeCanvases.length > 0) {
          const now = Date.now();
          setCanvases(safeCanvases);
          setActiveCanvasId(safeCanvases[0].id);
          setLastSyncedAt(now);
          return;
        }
      }

      setAssetUseLimits({});
      const legacyEdgesRaw = localStorage.getItem(LEGACY_EDGE_KEY);
      const legacyPlacedRaw = localStorage.getItem(LEGACY_PLACED_KEY);
      const legacySizeRaw = localStorage.getItem(STORAGE_SIZE_KEY);
      const legacyEdges = legacyEdgesRaw ? (JSON.parse(legacyEdgesRaw) as ManualEdge[]) : [];
      const legacyPlaced = legacyPlacedRaw
        ? (JSON.parse(legacyPlacedRaw) as Record<string, { x: number; y: number }>)
        : {};
      const first = createCanvas("默认画布");
      first.manualEdges = legacyEdges.filter((item) => isValidEdge(item, nodeIdSet));
      first.placedNodes = Object.fromEntries(
        Object.entries(legacyPlaced).filter(([id]) => nodeIdSet.has(id)),
      ) as Record<string, { x: number; y: number }>;
      first.nodeSize =
        legacySizeRaw === "small" || legacySizeRaw === "medium" || legacySizeRaw === "large"
          ? legacySizeRaw
          : "medium";
      setCanvases([first]);
      setActiveCanvasId(first.id);
      setLastSyncedAt(Date.now());
    } catch {
      setAssetUseLimits({});
      const first = createCanvas("默认画布");
      setCanvases([first]);
      setActiveCanvasId(first.id);
      setLastSyncedAt(Date.now());
    }
  }, [nodes]);

  useEffect(() => {
    if (canvases.length > 0) {
      const payload: GraphCachePayload = {
        version: 1,
        updatedAt: Date.now(),
        activeCanvasId,
        selectedId,
        canvases,
        assetUseLimits,
      };
      localStorage.setItem(STORAGE_KEY, JSON.stringify(payload));
      setLastSyncedAt(payload.updatedAt);
    }
  }, [canvases, activeCanvasId, selectedId, assetUseLimits]);

  const activeCanvas = useMemo(
    () => canvases.find((item) => item.id === activeCanvasId) ?? canvases[0] ?? null,
    [canvases, activeCanvasId],
  );
  const placedNodes = activeCanvas?.placedNodes ?? {};
  const manualEdges = activeCanvas?.manualEdges ?? [];
  const nodeSize = activeCanvas?.nodeSize ?? "medium";
  const groupNames = activeCanvas?.groupNames ?? {};
  const graphApiUrl = getGraphApiUrl();

  const nodeMap = useMemo(() => new Map(nodes.map((item) => [item.id, item])), [nodes]);
  const placedNodeList = useMemo(
    () =>
      Object.entries(placedNodes)
        .map(([id, pos]) => {
          const node = nodeMap.get(id);
          if (!node) {
            return null;
          }
          return { node, pos };
        })
        .filter((item): item is { node: AssetNode; pos: { x: number; y: number } } => item !== null),
    [placedNodes, nodeMap],
  );

  const groups = useMemo(() => {
    const placedIds = new Set(Object.keys(placedNodes));
    const adjacency = new Map<string, Set<string>>();
    for (const edge of manualEdges) {
      if (!placedIds.has(edge.source) || !placedIds.has(edge.target)) {
        continue;
      }
      if (!adjacency.has(edge.source)) adjacency.set(edge.source, new Set());
      if (!adjacency.has(edge.target)) adjacency.set(edge.target, new Set());
      adjacency.get(edge.source)!.add(edge.target);
      adjacency.get(edge.target)!.add(edge.source);
    }
    const visited = new Set<string>();
    const comps: { key: string; nodeIds: string[]; name: string }[] = [];
    const candidates = Array.from(adjacency.keys()).sort((a, b) => a.localeCompare(b, "zh-CN"));
    for (const start of candidates) {
      if (visited.has(start)) continue;
      const stack = [start];
      const nodeIds: string[] = [];
      visited.add(start);
      while (stack.length) {
        const curr = stack.pop()!;
        nodeIds.push(curr);
        const next = adjacency.get(curr);
        if (!next) continue;
        for (const nid of next) {
          if (!visited.has(nid)) {
            visited.add(nid);
            stack.push(nid);
          }
        }
      }
      if (nodeIds.length > 1) {
        const key = buildGroupKey(nodeIds);
        comps.push({ key, nodeIds: nodeIds.sort((a, b) => a.localeCompare(b, "zh-CN")), name: "" });
      }
    }
    comps.sort((a, b) => a.nodeIds[0].localeCompare(b.nodeIds[0], "zh-CN"));
    return comps.map((item, idx) => ({
      ...item,
      name: (groupNames[item.key] || "").trim() || `分组${idx + 1}`,
    }));
  }, [placedNodes, manualEdges, groupNames]);

  const nodeGroupMap = useMemo(() => {
    const map = new Map<string, { key: string; name: string }>();
    for (const group of groups) {
      for (const nodeId of group.nodeIds) {
        map.set(nodeId, { key: group.key, name: group.name });
      }
    }
    return map;
  }, [groups]);

  useEffect(() => {
    if (!groups.length) {
      if (selectedGroupKey) {
        setSelectedGroupKey("");
      }
      return;
    }
    if (!groups.some((item) => item.key === selectedGroupKey)) {
      setSelectedGroupKey(groups[0].key);
    }
  }, [groups, selectedGroupKey]);

  useEffect(() => {
    if (!selectedId) {
      return;
    }
    const el = noteItemRefs.current[selectedId];
    if (!el) {
      return;
    }
    el.scrollIntoView({ block: "center", behavior: "smooth" });
  }, [selectedId]);

  function updateActiveCanvas(updater: (canvas: CanvasData) => CanvasData) {
    if (!activeCanvas) {
      return;
    }
    setCanvases((prev) => prev.map((item) => (item.id === activeCanvas.id ? updater(item) : item)));
  }

  function setPlacedNodes(updater: (prev: Record<string, { x: number; y: number }>) => Record<string, { x: number; y: number }>) {
    updateActiveCanvas((item) => ({ ...item, placedNodes: updater(item.placedNodes) }));
  }

  function setManualEdges(updater: (prev: ManualEdge[]) => ManualEdge[]) {
    updateActiveCanvas((item) => ({ ...item, manualEdges: updater(item.manualEdges) }));
  }

  function setNodeSize(size: NodeSize) {
    updateActiveCanvas((item) => ({ ...item, nodeSize: size }));
  }

  function createNewCanvas(rawName: string) {
    const name = rawName.trim() || `新画布${canvases.length + 1}`;
    const next = createCanvas(name);
    setCanvases((prev) => [...prev, next]);
    setActiveCanvasId(next.id);
    setSelectedId(null);
    setPendingSourceId(null);
  }

  function renameActiveCanvas(rawName: string) {
    const name = rawName.trim();
    if (!name || !activeCanvas) {
      return;
    }
    updateActiveCanvas((item) => ({ ...item, name }));
  }

  function deleteActiveCanvas() {
    if (!activeCanvas) {
      return;
    }
    if (canvases.length <= 1) {
      const fallback = createCanvas("默认画布");
      setCanvases([fallback]);
      setActiveCanvasId(fallback.id);
      setSelectedId(null);
      setPendingSourceId(null);
      return;
    }
    const currentIndex = canvases.findIndex((item) => item.id === activeCanvas.id);
    const nextCanvas = canvases[currentIndex - 1] ?? canvases[currentIndex + 1] ?? canvases[0];
    setCanvases((prev) => prev.filter((item) => item.id !== activeCanvas.id));
    setActiveCanvasId(nextCanvas.id);
    setSelectedId(null);
    setPendingSourceId(null);
  }

  function openCreateCanvasDialog() {
    setCanvasDialog({ mode: "create", value: `新画布${canvases.length + 1}` });
  }

  function openRenameCanvasDialog() {
    if (!activeCanvas) {
      return;
    }
    setCanvasDialog({ mode: "rename", value: activeCanvas.name });
  }

  function submitCanvasDialog() {
    if (!canvasDialog) {
      return;
    }
    if (canvasDialog.mode === "create") {
      createNewCanvas(canvasDialog.value);
    } else {
      renameActiveCanvas(canvasDialog.value);
    }
    setCanvasDialog(null);
  }

  function getNodeDimensions() {
    if (nodeSize === "small") {
      return { width: 110, height: 64 };
    }
    if (nodeSize === "large") {
      return { width: 180, height: 128 };
    }
    return { width: 140, height: 92 };
  }

  function addEdge(sourceId: string, targetId: string) {
    if (!sourceId || !targetId || sourceId === targetId) {
      return;
    }
    const id = buildEdgeId(sourceId, targetId);
    setManualEdges((prev) => {
      if (prev.some((item) => item.id === id)) {
        return prev;
      }
      return [...prev, { id, source: sourceId, target: targetId }];
    });
  }

  function removeEdge(edgeId: string) {
    setManualEdges((prev) => prev.filter((item) => item.id !== edgeId));
  }

  function removePlacedNode(nodeId: string) {
    setPlacedNodes((prev) => {
      const next = { ...prev };
      delete next[nodeId];
      return next;
    });
    setManualEdges((prev) => prev.filter((item) => item.source !== nodeId && item.target !== nodeId));
    if (pendingSourceId === nodeId) {
      setPendingSourceId(null);
    }
    if (selectedId === nodeId) {
      setSelectedId(null);
    }
  }

  function onDragStart(nodeId: string, event: DragEvent<HTMLLIElement>) {
    event.dataTransfer.setData("text/plain", nodeId);
  }

  function onDropToBoard(event: DragEvent<HTMLDivElement>) {
    event.preventDefault();
    const nodeId = event.dataTransfer.getData("text/plain");
    if (!nodeId || !nodeMap.has(nodeId)) {
      return;
    }
    if (placedNodes[nodeId]) {
      return;
    }
    const placedCount = countAssetPlacements(canvases, nodeId);
    const limit = getAssetUseLimit(assetUseLimits, nodeId);
    if (placedCount >= limit) {
      return;
    }
    const { width: nodeWidth, height: nodeHeight } = getNodeDimensions();
    const rect = event.currentTarget.getBoundingClientRect();
    const x = Math.max(16, Math.min(rect.width - nodeWidth - 8, event.clientX - rect.left - nodeWidth / 2));
    const y = Math.max(16, Math.min(rect.height - nodeHeight - 8, event.clientY - rect.top - nodeHeight / 2));
    setPlacedNodes((prev) => ({ ...prev, [nodeId]: { x, y } }));
  }

  function onNodeClick(nodeId: string) {
    setSelectedId(nodeId);
  }

  function onNodeLinkClick(nodeId: string) {
    setSelectedId(nodeId);
    if (!pendingSourceId) {
      setPendingSourceId(nodeId);
      return;
    }
    if (pendingSourceId === nodeId) {
      setPendingSourceId(null);
      return;
    }
    addEdge(pendingSourceId, nodeId);
    setPendingSourceId(null);
  }

  function clearBoard() {
    setPlacedNodes(() => ({}));
    setManualEdges(() => []);
    updateActiveCanvas((item) => ({ ...item, groupNames: {} }));
    setPendingSourceId(null);
    setSelectedGroupKey("");
  }

  function renameSelectedGroup() {
    if (!selectedGroupKey) {
      return;
    }
    const target = groups.find((item) => item.key === selectedGroupKey);
    if (!target) {
      return;
    }
    const nextName = window.prompt("请输入分组名称", target.name);
    if (nextName == null) {
      return;
    }
    const name = nextName.trim();
    if (!name) {
      return;
    }
    updateActiveCanvas((item) => ({
      ...item,
      groupNames: { ...(item.groupNames ?? {}), [selectedGroupKey]: name },
    }));
  }

  function onNodePointerDown(nodeId: string, event: ReactPointerEvent<HTMLDivElement>) {
    if (event.button !== 0) {
      return;
    }
    const target = event.target as HTMLElement;
    if (target.closest(".graph-card-remove") || target.closest(".graph-card-link")) {
      return;
    }
    const rect = event.currentTarget.getBoundingClientRect();
    setDraggingNode({
      id: nodeId,
      offsetX: event.clientX - rect.left,
      offsetY: event.clientY - rect.top,
    });
  }

  useEffect(() => {
    if (!draggingNode) {
      return;
    }
    const activeDrag = draggingNode;
    function onPointerMove(event: PointerEvent) {
      const board = boardRef.current;
      if (!board) {
        return;
      }
      const boardRect = board.getBoundingClientRect();
      const { width: nodeWidth, height: nodeHeight } = getNodeDimensions();
      const x = Math.max(
        16,
        Math.min(boardRect.width - nodeWidth - 8, event.clientX - boardRect.left - activeDrag.offsetX),
      );
      const y = Math.max(
        16,
        Math.min(boardRect.height - nodeHeight - 8, event.clientY - boardRect.top - activeDrag.offsetY),
      );
      setPlacedNodes((prev) => ({ ...prev, [activeDrag.id]: { x, y } }));
    }
    function onPointerUp() {
      setDraggingNode(null);
    }
    window.addEventListener("pointermove", onPointerMove);
    window.addEventListener("pointerup", onPointerUp);
    return () => {
      window.removeEventListener("pointermove", onPointerMove);
      window.removeEventListener("pointerup", onPointerUp);
    };
  }, [draggingNode, nodeSize, activeCanvasId]);

  function buildCachePayload(): GraphCachePayload {
    return {
      version: 1,
      updatedAt: Date.now(),
      activeCanvasId,
      selectedId,
      canvases,
      assetUseLimits,
    };
  }

  function bumpAssetUseLimit(assetId: string, delta: number) {
    const nodeIdSet = new Set(nodes.map((item) => item.id));
    if (!nodeIdSet.has(assetId)) {
      return;
    }
    const used = countAssetPlacements(canvases, assetId);
    const current = getAssetUseLimit(assetUseLimits, assetId);
    const nextVal = Math.min(
      ASSET_USE_LIMIT_MAX,
      Math.max(ASSET_USE_LIMIT_MIN, Math.max(used, current + delta)),
    );
    setAssetUseLimits((prev) => ({ ...prev, [assetId]: nextVal }));
  }

  async function saveToProjectFile(options?: { silent?: boolean }) {
    const silent = options?.silent ?? false;
    if (!graphApiUrl) {
      if (!silent) {
        setRemoteSyncStatus("未配置图谱接口地址");
      }
      return;
    }
    try {
      const payload = buildCachePayload();
      const res = await fetch(graphApiUrl, {
        method: "PUT",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify(payload),
      });
      const text = await res.text();
      if (!res.ok) {
        throw new Error(text || `HTTP ${res.status}`);
      }
      setRemoteSyncStatus(
        silent
          ? `已自动同步（${new Date().toLocaleTimeString()}）`
          : `已保存到项目文件（${new Date().toLocaleTimeString()}）`,
      );
    } catch (error) {
      if (!silent) {
        setRemoteSyncStatus(`保存失败：${error instanceof Error ? error.message : String(error)}`);
      }
    }
  }

  async function loadFromProjectFile(options?: { silent?: boolean }) {
    const silent = options?.silent ?? false;
    if (!graphApiUrl) {
      if (!silent) {
        setRemoteSyncStatus("未配置图谱接口地址");
      }
      return;
    }
    try {
      const res = await fetch(graphApiUrl);
      const text = await res.text();
      if (!res.ok) {
        throw new Error(text || `HTTP ${res.status}`);
      }
      const payload = JSON.parse(text) as GraphCachePayload;
      const nodeIdSet = new Set(nodes.map((item) => item.id));
      setAssetUseLimits(sanitizeAssetUseLimits(payload.assetUseLimits, nodeIdSet));
      const safeCanvases = (payload.canvases ?? [])
        .map((item) => ({
          ...item,
          manualEdges: (item.manualEdges ?? []).filter((edge) => isValidEdge(edge, nodeIdSet)),
          placedNodes: Object.fromEntries(
            Object.entries(item.placedNodes ?? {}).filter(([id]) => nodeIdSet.has(id)),
          ) as Record<string, { x: number; y: number }>,
          nodeSize:
            item.nodeSize === "small" || item.nodeSize === "medium" || item.nodeSize === "large"
              ? item.nodeSize
              : "medium",
          groupNames:
            item.groupNames && typeof item.groupNames === "object"
              ? (item.groupNames as Record<string, string>)
              : {},
        }))
        .filter((item) => item.id && item.name);
      if (!safeCanvases.length) {
        if (!silent) {
          setRemoteSyncStatus("项目文件为空，未导入内容");
        }
        return;
      }
      setCanvases(safeCanvases);
      setActiveCanvasId(
        safeCanvases.some((item) => item.id === payload.activeCanvasId)
          ? payload.activeCanvasId
          : safeCanvases[0].id,
      );
      setSelectedId(payload.selectedId ?? null);
      setLastSyncedAt(payload.updatedAt ?? Date.now());
      setRemoteSyncStatus(
        silent
          ? `已自动加载项目文件（${new Date().toLocaleTimeString()}）`
          : `已从项目文件加载（${new Date().toLocaleTimeString()}）`,
      );
    } catch (error) {
      if (!silent) {
        setRemoteSyncStatus(`加载失败：${error instanceof Error ? error.message : String(error)}`);
      }
    }
  }

  useEffect(() => {
    let cancelled = false;
    async function initRemoteCache() {
      if (!graphApiUrl) {
        remoteInitDoneRef.current = true;
        return;
      }
      await loadFromProjectFile({ silent: true });
      if (!cancelled) {
        remoteInitDoneRef.current = true;
      }
    }
    void initRemoteCache();
    return () => {
      cancelled = true;
    };
  }, [graphApiUrl]);

  useEffect(() => {
    if (!graphApiUrl || !canvases.length || !remoteInitDoneRef.current) {
      return;
    }
    const timer = window.setTimeout(() => {
      void saveToProjectFile({ silent: true });
    }, AUTO_SYNC_DEBOUNCE_MS);
    return () => {
      window.clearTimeout(timer);
    };
  }, [canvases, activeCanvasId, selectedId, assetUseLimits, graphApiUrl]);

  if (!activeCanvas) {
    return null;
  }

  return (
    <div className="graph-shell graph-shell--manual">
      <section className="graph-side-panel">
        <h3>素材列表（png + txt）</h3>
        <ul className="graph-note-list">
          {nodes.map((item) => (
            (() => {
              const placedCount = countAssetPlacements(canvases, item.id);
              const limit = getAssetUseLimit(assetUseLimits, item.id);
              const usedByCurrentCanvas = Boolean(placedNodes[item.id]);
              const usedByOtherCanvas = placedCount > 0 && !usedByCurrentCanvas;
              const quotaFull = placedCount >= limit;
              const canDragFromList = !usedByCurrentCanvas && !quotaFull;
              const isSelected = item.id === selectedId;
              return (
            <li
              key={item.id}
              ref={(el) => {
                noteItemRefs.current[item.id] = el;
              }}
              className={
                quotaFull
                  ? isSelected
                    ? "active graph-note-item--used"
                    : "graph-note-item--used"
                  : isSelected
                    ? "active"
                    : ""
              }
              onClick={() => setSelectedId(item.id)}
              draggable={canDragFromList}
              onDragStart={(event) => {
                if (!canDragFromList) {
                  event.preventDefault();
                  return;
                }
                onDragStart(item.id, event);
              }}
              aria-disabled={!canDragFromList}
            >
              <div className="graph-note-title-row">
                <div className="graph-note-title">
                  [{item.kind.toUpperCase()}] {item.title}
                </div>
                <div
                  className="graph-note-limit-controls"
                  onClick={(e) => e.stopPropagation()}
                  onPointerDown={(e) => e.stopPropagation()}
                  role="group"
                  aria-label="素材使用次数限额"
                >
                  <span className="toolbar-muted graph-note-limit-label">限额</span>
                  <button
                    type="button"
                    className="graph-note-limit-btn"
                    onClick={() => bumpAssetUseLimit(item.id, -1)}
                    disabled={limit <= Math.max(ASSET_USE_LIMIT_MIN, placedCount)}
                    aria-label="减少可用次数"
                  >
                    −
                  </button>
                  <span className="graph-note-limit-value" title="已用画布数 / 可用总次数">
                    {placedCount}/{limit}
                  </span>
                  <button
                    type="button"
                    className="graph-note-limit-btn"
                    onClick={() => bumpAssetUseLimit(item.id, 1)}
                    disabled={limit >= ASSET_USE_LIMIT_MAX}
                    aria-label="增加可用次数"
                  >
                    +
                  </button>
                </div>
              </div>
              {usedByCurrentCanvas ? <div className="toolbar-muted">已在当前画布使用</div> : null}
              {usedByOtherCanvas ? <div className="toolbar-muted">已在其他画布使用（仍可拖到未使用的画布）</div> : null}
              {isSelected ? (
                <div className="graph-note-inline-detail">
                  <h4>{item.title}</h4>
                  <div className="toolbar-muted">类型：{item.kind.toUpperCase()}</div>
                  {item.kind === "txt" ? (
                    <p>{item.content || "该文本为空。"}</p>
                  ) : item.url ? (
                    <img className="graph-preview-image" src={item.url} alt={item.title} />
                  ) : (
                    <p>图片地址读取失败。</p>
                  )}
                </div>
              ) : null}
            </li>
              );
            })()
          ))}
        </ul>
      </section>

      <section className="graph-canvas-panel">
        <div className="graph-panel-title">
          图表画布（拖入素材生成节点）
          <span className="toolbar-muted">
            画布：{activeCanvas.name}，已放置 {placedNodeList.length} 个节点 / 关系 {manualEdges.length} 条（自动保存）
          </span>
          <span className="toolbar-muted">
            本地缓存：{lastSyncedAt ? new Date(lastSyncedAt).toLocaleString() : "未同步"}
          </span>
        </div>
        <div className="graph-board-toolbar">
          <select
            value={activeCanvas.id}
            onChange={(e) => {
              setActiveCanvasId(e.target.value);
              setPendingSourceId(null);
            }}
          >
            {canvases.map((item) => (
              <option key={item.id} value={item.id}>
                {item.name}
              </option>
            ))}
          </select>
          <button type="button" onClick={openCreateCanvasDialog}>
            创建画布
          </button>
          <button type="button" onClick={openRenameCanvasDialog}>
            重命名
          </button>
          <button type="button" onClick={deleteActiveCanvas}>
            删除画布
          </button>
          <button type="button" onClick={clearBoard}>
            清空图表
          </button>
          <select value={nodeSize} onChange={(e) => setNodeSize(e.target.value as NodeSize)}>
            <option value="small">节点小</option>
            <option value="medium">节点中</option>
            <option value="large">节点大</option>
          </select>
          <span className="toolbar-muted">
            {pendingSourceId
              ? "已选起点，请点另一个节点的连线按钮"
              : "拖动左侧文件到画布，点击节点查看详情；点线中间的 × 可删除连线"}
          </span>
          <select
            value={selectedGroupKey}
            onChange={(e) => setSelectedGroupKey(e.target.value)}
            disabled={!groups.length}
          >
            {groups.length ? (
              groups.map((group) => (
                <option key={group.key} value={group.key}>
                  {group.name}（{group.nodeIds.length}）
                </option>
              ))
            ) : (
              <option value="">暂无连线分组</option>
            )}
          </select>
          <button type="button" onClick={renameSelectedGroup} disabled={!groups.length || !selectedGroupKey}>
            重命名分组
          </button>
          <span className="toolbar-muted">{remoteSyncStatus}</span>
        </div>
        <div
          ref={boardRef}
          className="graph-board"
          onDragOver={(event) => event.preventDefault()}
          onDrop={onDropToBoard}
          role="region"
          aria-label="知识图谱画布"
        >
          <svg className="graph-board-lines">
            {manualEdges.map((edge) => {
              const source = placedNodes[edge.source];
              const target = placedNodes[edge.target];
              if (!source || !target) {
                return null;
              }
              const x1 = source.x + getNodeDimensions().width / 2;
              const y1 = source.y + getNodeDimensions().height / 2;
              const x2 = target.x + getNodeDimensions().width / 2;
              const y2 = target.y + getNodeDimensions().height / 2;
              return (
                <g key={edge.id}>
                  <line
                    x1={x1}
                    y1={y1}
                    x2={x2}
                    y2={y2}
                    className="graph-line graph-line--board"
                  />
                  <circle cx={x2} cy={y2} r={4} className="graph-line-dot" />
                </g>
              );
            })}
          </svg>
          {manualEdges.map((edge) => {
            const source = placedNodes[edge.source];
            const target = placedNodes[edge.target];
            if (!source || !target) {
              return null;
            }
            const x1 = source.x + getNodeDimensions().width / 2;
            const y1 = source.y + getNodeDimensions().height / 2;
            const x2 = target.x + getNodeDimensions().width / 2;
            const y2 = target.y + getNodeDimensions().height / 2;
            return (
              <button
                key={`remove-${edge.id}`}
                type="button"
                className="graph-edge-remove"
                style={{ left: `${(x1 + x2) / 2 - 9}px`, top: `${(y1 + y2) / 2 - 9}px` }}
                onClick={() => removeEdge(edge.id)}
                aria-label="删除连线"
              >
                ×
              </button>
            );
          })}
          {placedNodeList.map(({ node, pos }) => (
            <div
              key={node.id}
              className={
                node.id === selectedId
                  ? `graph-card graph-card--${nodeSize} graph-card--active`
                  : pendingSourceId === node.id
                    ? `graph-card graph-card--${nodeSize} graph-card--pending`
                    : `graph-card graph-card--${nodeSize}`
              }
              style={{ left: `${pos.x}px`, top: `${pos.y}px` }}
              onClick={() => onNodeClick(node.id)}
              onDoubleClick={() => {
                if (node.kind === "png" && node.url) {
                  setPreviewImage({ url: node.url, title: node.title });
                }
              }}
              onPointerDown={(event) => onNodePointerDown(node.id, event)}
              role="button"
              tabIndex={0}
              onKeyDown={(event) => {
                if (event.key === "Enter" || event.key === " ") {
                  event.preventDefault();
                  onNodeClick(node.id);
                }
              }}
            >
              <button
                type="button"
                className="graph-card-remove"
                onClick={(event) => {
                  event.stopPropagation();
                  removePlacedNode(node.id);
                }}
                aria-label={`删除节点 ${node.title}`}
              >
                ×
              </button>
              <button
                type="button"
                className="graph-card-link"
                onClick={(event) => {
                  event.stopPropagation();
                  onNodeLinkClick(node.id);
                }}
                aria-label={pendingSourceId === node.id ? `取消起点 ${node.title}` : `以 ${node.title} 作为连线节点`}
              >
                连
              </button>
              {node.kind === "png" && node.url ? <img className="graph-card-thumb" src={node.url} alt={node.title} /> : null}
              {nodeGroupMap.get(node.id) ? <span className="graph-card-group">{nodeGroupMap.get(node.id)!.name}</span> : null}
              <span className="graph-card-kind">{node.kind.toUpperCase()}</span>
              <span className="graph-card-title">{node.title}</span>
            </div>
          ))}
          {placedNodeList.length === 0 ? <div className="graph-board-empty">把左侧文件拖到这里，开始创建图表节点</div> : null}
        </div>
      </section>
      {previewImage ? (
        <div className="graph-image-modal" onClick={() => setPreviewImage(null)} role="dialog" aria-modal="true">
          <div className="graph-image-modal-inner" onClick={(event) => event.stopPropagation()}>
            <button type="button" className="graph-image-close" onClick={() => setPreviewImage(null)}>
              关闭
            </button>
            <img src={previewImage.url} alt={previewImage.title} />
            <div className="toolbar-muted">{previewImage.title}</div>
          </div>
        </div>
      ) : null}
      {canvasDialog ? (
        <div className="graph-image-modal" onClick={() => setCanvasDialog(null)} role="dialog" aria-modal="true">
          <div className="graph-dialog-panel" onClick={(event) => event.stopPropagation()}>
            <h4>{canvasDialog.mode === "create" ? "创建画布" : "重命名画布"}</h4>
            <input
              type="text"
              value={canvasDialog.value}
              onChange={(event) =>
                setCanvasDialog((prev) => (prev ? { ...prev, value: event.target.value } : prev))
              }
              placeholder={canvasDialog.mode === "create" ? "请输入画布名称" : "请输入新的画布名称"}
              autoFocus
              onKeyDown={(event) => {
                if (event.key === "Enter") {
                  event.preventDefault();
                  submitCanvasDialog();
                }
              }}
            />
            <div className="graph-dialog-actions">
              <button type="button" onClick={() => setCanvasDialog(null)}>
                取消
              </button>
              <button type="button" onClick={submitCanvasDialog}>
                确定
              </button>
            </div>
          </div>
        </div>
      ) : null}
    </div>
  );
}
