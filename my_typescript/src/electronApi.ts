import type { ElectronApi } from "./electronApiTypes";

declare global {
  interface Window {
    electronAPI?: ElectronApi;
  }
}

export type { ElectronApi, OpenFileDialogOptions, SaveFileDialogOptions } from "./electronApiTypes";

export function isElectron(): boolean {
  return typeof window !== "undefined" && Boolean(window.electronAPI);
}

export function getElectronApi(): ElectronApi | undefined {
  return window.electronAPI;
}
