import { getElectronApi } from "./electronApi";

/** 界面说明用：Electron 为磁盘，纯网页为浏览器存储 */
export function persistMediumShortLabel(): string {
  return getElectronApi() ? "本地磁盘缓存" : "浏览器本地存储";
}

/**
 * Electron：写入 userData/app-cache（主进程落盘）。
 * 非 Electron：仍用 localStorage，便于纯浏览器预览。
 * 从 localStorage 迁出：Electron 下读盘为空时会迁一次并 removeItem。
 */
export async function readAppCache(key: string): Promise<string | null> {
  const api = getElectronApi();
  if (api) {
    let value = await api.readAppCacheKey(key);
    if (value == null) {
      try {
        const legacy = localStorage.getItem(key);
        if (legacy != null) {
          await api.writeAppCacheKey(key, legacy);
          localStorage.removeItem(key);
          value = legacy;
        }
      } catch {
        /* ignore */
      }
    }
    return value;
  }
  try {
    return localStorage.getItem(key);
  } catch {
    return null;
  }
}

export async function writeAppCache(key: string, value: string): Promise<void> {
  const api = getElectronApi();
  if (api) {
    await api.writeAppCacheKey(key, value);
    try {
      localStorage.removeItem(key);
    } catch {
      /* ignore */
    }
    return;
  }
  try {
    localStorage.setItem(key, value);
  } catch {
    /* ignore */
  }
}

export async function removeAppCache(key: string): Promise<void> {
  const api = getElectronApi();
  if (api) {
    await api.removeAppCacheKey(key);
  }
  try {
    localStorage.removeItem(key);
  } catch {
    /* ignore */
  }
}
