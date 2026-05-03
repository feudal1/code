"use strict";
const electron = require("electron");
const api = {
  readTextFile: (filePath) => electron.ipcRenderer.invoke("fs:readText", filePath),
  writeTextFile: (filePath, content) => electron.ipcRenderer.invoke("fs:writeText", filePath, content),
  openFile: (options) => electron.ipcRenderer.invoke("dialog:openFile", options ?? {}),
  openFiles: (options) => electron.ipcRenderer.invoke("dialog:openFiles", options ?? {}),
  saveFile: (options) => electron.ipcRenderer.invoke("dialog:saveFile", options ?? {}),
  getPath: (name) => electron.ipcRenderer.invoke("app:getPath", name),
  openPath: (filePath) => electron.ipcRenderer.invoke("shell:openPath", filePath),
  showItemInFolder: (fullPath) => electron.ipcRenderer.invoke("shell:showItemInFolder", fullPath),
  readAppCacheKey: (key) => electron.ipcRenderer.invoke("app-cache:read", key),
  writeAppCacheKey: (key, content) => electron.ipcRenderer.invoke("app-cache:write", key, content),
  removeAppCacheKey: (key) => electron.ipcRenderer.invoke("app-cache:remove", key)
};
electron.contextBridge.exposeInMainWorld("electronAPI", api);
