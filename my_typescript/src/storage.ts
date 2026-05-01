import type { WorkProjectItem } from "./types";

const KEY = "work_project_flow_state_v1";

export interface PersistedUiState {
  projects: WorkProjectItem[];
  selectedIndex: number;
  addressReadMode: boolean;
}

export function loadState(): PersistedUiState | null {
  try {
    const raw = localStorage.getItem(KEY);
    if (!raw) return null;
    const data = JSON.parse(raw) as PersistedUiState;
    if (!Array.isArray(data.projects)) return null;
    return data;
  } catch {
    return null;
  }
}

export function saveState(state: PersistedUiState): void {
  try {
    localStorage.setItem(KEY, JSON.stringify(state));
  } catch {
    // ignore quota / private mode
  }
}
