using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace SolidWorksAddinStudy.Services
{
    /// <summary>
    /// 按装配体路径持久化「零件处理状态」任务窗格中的出图状态（项目制）。
    /// </summary>
    public static class PartStatusProjectStore
    {
        private static readonly object FileLock = new object();

        private static string RootDirectory =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SolidWorksAddinStudy", "part_status_by_assembly");

        private static string LatestProjectPath => Path.Combine(RootDirectory, "latest_project.json");

        private class PersistedProject
        {
            public string AssemblyPath { get; set; } = string.Empty;
            public Dictionary<string, string> PartDrawnByName { get; set; } =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            public List<PartStatusInfo> SnapshotParts { get; set; } = new List<PartStatusInfo>();
        }

        private class LatestProjectRef
        {
            public string AssemblyPath { get; set; } = string.Empty;
        }

        private static string GetStoreFilePath(string assemblyFullPath)
        {
            if (string.IsNullOrWhiteSpace(assemblyFullPath))
            {
                throw new ArgumentException("装配体路径不能为空", nameof(assemblyFullPath));
            }

            string normalized = assemblyFullPath.Trim();
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(normalized));
                string name = BitConverter.ToString(hash, 0, 12).Replace("-", string.Empty) + ".json";
                return Path.Combine(RootDirectory, name);
            }
        }

        private static PersistedProject ReadProject(string assemblyFullPath)
        {
            lock (FileLock)
            {
                string path = GetStoreFilePath(assemblyFullPath);
                if (!File.Exists(path))
                {
                    return new PersistedProject { AssemblyPath = assemblyFullPath.Trim() };
                }

                try
                {
                    string json = File.ReadAllText(path, Encoding.UTF8);
                    PersistedProject? loaded = JsonConvert.DeserializeObject<PersistedProject>(json);
                    if (loaded?.PartDrawnByName == null)
                    {
                        return new PersistedProject { AssemblyPath = assemblyFullPath.Trim() };
                    }

                    var normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (KeyValuePair<string, string> kv in loaded.PartDrawnByName)
                    {
                        if (!string.IsNullOrWhiteSpace(kv.Key))
                        {
                            normalized[kv.Key] = kv.Value ?? "未出图";
                        }
                    }

                    loaded.PartDrawnByName = normalized;
                    return loaded;
                }
                catch
                {
                    return new PersistedProject { AssemblyPath = assemblyFullPath.Trim() };
                }
            }
        }

        private static void WriteProject(PersistedProject project, string assemblyFullPath)
        {
            lock (FileLock)
            {
                Directory.CreateDirectory(RootDirectory);
                string path = GetStoreFilePath(assemblyFullPath);
                project.AssemblyPath = assemblyFullPath.Trim();
                string json = JsonConvert.SerializeObject(project, Formatting.Indented);
                File.WriteAllText(path, json, Encoding.UTF8);
                File.WriteAllText(
                    LatestProjectPath,
                    JsonConvert.SerializeObject(new LatestProjectRef { AssemblyPath = project.AssemblyPath }, Formatting.Indented),
                    Encoding.UTF8);
            }
        }

        /// <summary>将已保存的出图状态合并到当前列表（按零件名称匹配）。</summary>
        public static void MergePersistedDrawn(string assemblyFullPath, List<PartStatusInfo> parts)
        {
            if (string.IsNullOrWhiteSpace(assemblyFullPath) || parts == null || parts.Count == 0)
            {
                return;
            }

            PersistedProject disk = ReadProject(assemblyFullPath);
            foreach (PartStatusInfo p in parts)
            {
                if (p == null || string.IsNullOrWhiteSpace(p.PartName))
                {
                    continue;
                }

                if (disk.PartDrawnByName.TryGetValue(p.PartName, out string saved) && !string.IsNullOrWhiteSpace(saved))
                {
                    p.IsDrawn = saved;
                }
            }
        }

        /// <summary>更新单个零件的出图状态并保存到当前项目文件。</summary>
        public static void SavePartDrawn(string assemblyFullPath, string partName, string isDrawn)
        {
            if (string.IsNullOrWhiteSpace(assemblyFullPath) || string.IsNullOrWhiteSpace(partName))
            {
                return;
            }

            PersistedProject disk = ReadProject(assemblyFullPath);
            disk.PartDrawnByName[partName] = isDrawn ?? "未出图";
            WriteProject(disk, assemblyFullPath);
        }

        /// <summary>保存当前零件状态完整快照（用于重启后直接恢复列表）。</summary>
        public static void SaveSnapshot(string assemblyFullPath, List<PartStatusInfo> parts)
        {
            if (string.IsNullOrWhiteSpace(assemblyFullPath) || parts == null)
            {
                return;
            }

            PersistedProject disk = ReadProject(assemblyFullPath);
            var snapshot = new List<PartStatusInfo>(parts.Count);
            foreach (PartStatusInfo part in parts)
            {
                if (part == null || string.IsNullOrWhiteSpace(part.PartName))
                {
                    continue;
                }

                snapshot.Add(new PartStatusInfo(
                    part.PartName ?? string.Empty,
                    part.PartType ?? string.Empty,
                    part.Dimension ?? string.Empty,
                    part.IsDrawn ?? "未出图",
                    part.Quantity ?? string.Empty));

                disk.PartDrawnByName[part.PartName] = part.IsDrawn ?? "未出图";
            }

            disk.SnapshotParts = snapshot;
            WriteProject(disk, assemblyFullPath);
        }

        /// <summary>读取指定装配体的列表快照。</summary>
        public static List<PartStatusInfo> LoadSnapshot(string assemblyFullPath)
        {
            if (string.IsNullOrWhiteSpace(assemblyFullPath))
            {
                return new List<PartStatusInfo>();
            }

            PersistedProject disk = ReadProject(assemblyFullPath);
            return CloneSnapshot(disk.SnapshotParts);
        }

        /// <summary>读取最近一次保存过的列表快照（跨重启恢复使用）。</summary>
        public static bool TryLoadLatestSnapshot(out string assemblyFullPath, out List<PartStatusInfo> parts)
        {
            assemblyFullPath = string.Empty;
            parts = new List<PartStatusInfo>();
            lock (FileLock)
            {
                try
                {
                    if (!File.Exists(LatestProjectPath))
                    {
                        return false;
                    }

                    string json = File.ReadAllText(LatestProjectPath, Encoding.UTF8);
                    LatestProjectRef? dto = JsonConvert.DeserializeObject<LatestProjectRef>(json);
                    string path = dto?.AssemblyPath?.Trim() ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        return false;
                    }

                    PersistedProject disk = ReadProject(path);
                    List<PartStatusInfo> loaded = CloneSnapshot(disk.SnapshotParts);
                    if (loaded.Count == 0)
                    {
                        return false;
                    }

                    assemblyFullPath = path;
                    parts = loaded;
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        private static List<PartStatusInfo> CloneSnapshot(List<PartStatusInfo> source)
        {
            var cloned = new List<PartStatusInfo>();
            if (source == null || source.Count == 0)
            {
                return cloned;
            }

            foreach (PartStatusInfo p in source)
            {
                if (p == null || string.IsNullOrWhiteSpace(p.PartName))
                {
                    continue;
                }

                cloned.Add(new PartStatusInfo(
                    p.PartName ?? string.Empty,
                    p.PartType ?? string.Empty,
                    p.Dimension ?? string.Empty,
                    p.IsDrawn ?? "未出图",
                    p.Quantity ?? string.Empty));
            }

            return cloned;
        }
    }
}
