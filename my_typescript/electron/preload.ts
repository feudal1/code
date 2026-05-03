import { contextBridge, ipcRenderer } from "electron";
import type { ElectronApi } from "../src/electronApiTypes";

const api: ElectronApi = {
  readTextFile: (filePath) => ipcRenderer.invoke("fs:readText", filePath),
  writeTextFile: (filePath, content) =>
    ipcRenderer.invoke("fs:writeText", filePath, content),
  openFile: (options) => ipcRenderer.invoke("dialog:openFile", options ?? {}),
  openFiles: (options) =>
    ipcRenderer.invoke("dialog:openFiles", options ?? {}),
  saveFile: (options) => ipcRenderer.invoke("dialog:saveFile", options ?? {}),
  getPath: (name) => ipcRenderer.invoke("app:getPath", name),
  openPath: (filePath) => ipcRenderer.invoke("shell:openPath", filePath),
  showItemInFolder: (fullPath) =>
    ipcRenderer.invoke("shell:showItemInFolder", fullPath),
  readAppCacheKey: (key) => ipcRenderer.invoke("app-cache:read", key),
  writeAppCacheKey: (key, content) =>
    ipcRenderer.invoke("app-cache:write", key, content),
  removeAppCacheKey: (key) => ipcRenderer.invoke("app-cache:remove", key),
};

contextBridge.exposeInMainWorld("electronAPI", api);
