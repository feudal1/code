import { readAppCache, writeAppCache } from "./appLocalCache";
import type { WorkProjectItem } from "./types";

const KEY = "work_project_flow_state_v1";

export interface PersistedUiState {
  projects: WorkProjectItem[];
  selectedIndex: number;
  addressReadMode: boolean;
}

export async function loadState(): Promise<PersistedUiState | null> {
  try {
    const raw = await readAppCache(KEY);
    if (!raw) return null;
    const data = JSON.parse(raw) as PersistedUiState;
    if (!Array.isArray(data.projects)) return null;
    return data;
  } catch {
    return null;
  }
}

export async function saveState(state: PersistedUiState): Promise<void> {
  try {
    await writeAppCache(KEY, JSON.stringify(state));
  } catch {
    // ignore
  }
}
