import { app, BrowserWindow, ipcMain, dialog, shell } from "electron";
import fs from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));

process.env.APP_ROOT = path.join(__dirname, "..");

const RENDERER_DIST = path.join(process.env.APP_ROOT, "dist");
const VITE_DEV_SERVER_URL = process.env["VITE_DEV_SERVER_URL"];

function resolveSafePath(requested: string): string {
  const resolved = path.resolve(requested);
  return resolved;
}

async function ensureParentDir(filePath: string): Promise<void> {
  await fs.mkdir(path.dirname(filePath), { recursive: true });
}

const APP_CACHE_SUBDIR = "app-cache";

function appCacheFilePath(key: string): string {
  const safe = key.replace(/[^a-zA-Z0-9._-]/g, "_").slice(0, 180) || "key";
  return path.join(app.getPath("userData"), APP_CACHE_SUBDIR, `${safe}.json`);
}

function createWindow(): void {
  const win = new BrowserWindow({
    width: 1280,
    height: 800,
    webPreferences: {
      preload: path.join(__dirname, "preload.mjs"),
      contextIsolation: true,
      nodeIntegration: false,
      sandbox: false,
    },
  });

  if (VITE_DEV_SERVER_URL) {
    void win.loadURL(VITE_DEV_SERVER_URL);
  } else {
    void win.loadFile(path.join(RENDERER_DIST, "index.html"));
  }
}

app.whenReady().then(() => {
  createWindow();
  app.on("activate", () => {
    if (BrowserWindow.getAllWindows().length === 0) createWindow();
  });
});

app.on("window-all-closed", () => {
  if (process.platform !== "darwin") app.quit();
});

ipcMain.handle("fs:readText", async (_e, filePath: string) => {
  const p = resolveSafePath(String(filePath));
  return await fs.readFile(p, "utf8");
});

ipcMain.handle("fs:writeText", async (_e, filePath: string, content: string) => {
  const p = resolveSafePath(String(filePath));
  await ensureParentDir(p);
  await fs.writeFile(p, String(content), "utf8");
});

ipcMain.handle("dialog:openFile", async (_e, options: object) => {
  const res = await dialog.showOpenDialog({
    properties: ["openFile"],
    ...(options as Electron.OpenDialogOptions),
  });
  if (res.canceled || !res.filePaths[0]) return null;
  return res.filePaths[0];
});

ipcMain.handle("dialog:openFiles", async (_e, options: object) => {
  const res = await dialog.showOpenDialog({
    properties: ["openFile", "multiSelections"],
    ...(options as Electron.OpenDialogOptions),
  });
  if (res.canceled) return [];
  return res.filePaths;
});

ipcMain.handle("dialog:saveFile", async (_e, options: object) => {
  const res = await dialog.showSaveDialog(
    options as Electron.SaveDialogOptions,
  );
  if (res.canceled || !res.filePath) return null;
  return res.filePath;
});

ipcMain.handle("app:getPath", (_e, name: Parameters<typeof app.getPath>[0]) => {
  return app.getPath(name);
});

ipcMain.handle("shell:openPath", async (_e, targetPath: string) => {
  const err = await shell.openPath(resolveSafePath(String(targetPath)));
  return err || null;
});

ipcMain.handle("shell:showItemInFolder", (_e, fullPath: string) => {
  shell.showItemInFolder(resolveSafePath(String(fullPath)));
});

ipcMain.handle("app-cache:read", async (_e, key: string) => {
  const filePath = appCacheFilePath(String(key));
  try {
    return await fs.readFile(filePath, "utf8");
  } catch (e) {
    if ((e as NodeJS.ErrnoException).code === "ENOENT") {
      return null;
    }
    throw e;
  }
});

ipcMain.handle("app-cache:write", async (_e, key: string, content: string) => {
  const filePath = appCacheFilePath(String(key));
  await ensureParentDir(filePath);
  await fs.writeFile(filePath, String(content), "utf8");
});

ipcMain.handle("app-cache:remove", async (_e, key: string) => {
  const filePath = appCacheFilePath(String(key));
  try {
    await fs.unlink(filePath);
  } catch (e) {
    if ((e as NodeJS.ErrnoException).code !== "ENOENT") {
      throw e;
    }
  }
});
