export type FileFilter = { name: string; extensions: string[] };

export type OpenFileDialogOptions = {
  title?: string;
  defaultPath?: string;
  filters?: FileFilter[];
};

export type SaveFileDialogOptions = {
  title?: string;
  defaultPath?: string;
  filters?: FileFilter[];
  defaultFilename?: string;
};

export type AppPathName =
  | "home"
  | "appData"
  | "userData"
  | "sessionData"
  | "temp"
  | "exe"
  | "module"
  | "desktop"
  | "documents"
  | "downloads"
  | "music"
  | "pictures"
  | "videos"
  | "recent"
  | "logs"
  | "crashDumps";

/** 预加载脚本通过 contextBridge 暴露给渲染进程的 API */
export interface ElectronApi {
  readTextFile: (filePath: string) => Promise<string>;
  writeTextFile: (filePath: string, content: string) => Promise<void>;
  openFile: (options?: OpenFileDialogOptions) => Promise<string | null>;
  openFiles: (options?: OpenFileDialogOptions) => Promise<string[]>;
  saveFile: (options?: SaveFileDialogOptions) => Promise<string | null>;
  getPath: (name: AppPathName) => Promise<string>;
  openPath: (filePath: string) => Promise<string | null>;
  showItemInFolder: (fullPath: string) => Promise<void>;
  /** 应用数据目录下 app-cache，与浏览器存储隔离 */
  readAppCacheKey: (key: string) => Promise<string | null>;
  writeAppCacheKey: (key: string, content: string) => Promise<void>;
  removeAppCacheKey: (key: string) => Promise<void>;
}
