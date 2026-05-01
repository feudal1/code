using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace SolidWorksAddinStudy.Services
{
    /// <summary>
    /// 项目相关任务窗格（工作项目记录、零件处理状态）共用的装配体路径。
    /// </summary>
    public static class WorkProjectContext
    {
        private static readonly object Sync = new object();
        private static string _boundAssemblyFullPath = string.Empty;

        public static string BoundAssemblyFullPath
        {
            get
            {
                lock (Sync)
                {
                    return _boundAssemblyFullPath ?? string.Empty;
                }
            }
        }

        public static event Action? Changed;

        private static string StorePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SolidWorksAddinStudy",
                "shared_work_project.json");

        public static void LoadFromDisk()
        {
            lock (Sync)
            {
                _boundAssemblyFullPath = string.Empty;
                try
                {
                    if (!File.Exists(StorePath))
                    {
                        return;
                    }

                    string json = File.ReadAllText(StorePath, Encoding.UTF8);
                    var dto = JsonConvert.DeserializeObject<PersistDto>(json);
                    string p = dto?.BoundAssemblyFullPath?.Trim() ?? string.Empty;
                    if (!string.IsNullOrEmpty(p))
                    {
                        _boundAssemblyFullPath = p;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"WorkProjectContext.LoadFromDisk: {ex.Message}");
                }
            }
        }

        public static void SetBoundAssembly(string fullPath, bool notifyListeners = true)
        {
            string normalized = (fullPath ?? string.Empty).Trim();
            bool fire;
            lock (Sync)
            {
                if (string.Equals(_boundAssemblyFullPath, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                _boundAssemblyFullPath = normalized;
                fire = notifyListeners;
                try
                {
                    string dir = Path.GetDirectoryName(StorePath);
                    if (!string.IsNullOrEmpty(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    var dto = new PersistDto { BoundAssemblyFullPath = normalized };
                    File.WriteAllText(StorePath, JsonConvert.SerializeObject(dto, Formatting.Indented), Encoding.UTF8);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"WorkProjectContext.SetBoundAssembly: {ex.Message}");
                }
            }

            if (fire)
            {
                RaiseChangedOnMainThread();
            }
        }

        public static void ClearBoundAssembly()
        {
            bool fire;
            lock (Sync)
            {
                if (string.IsNullOrEmpty(_boundAssemblyFullPath))
                {
                    return;
                }

                _boundAssemblyFullPath = string.Empty;
                fire = true;
                try
                {
                    if (File.Exists(StorePath))
                    {
                        File.Delete(StorePath);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"WorkProjectContext.ClearBoundAssembly: {ex.Message}");
                }
            }

            if (fire)
            {
                RaiseChangedOnMainThread();
            }
        }

        private static void RaiseChangedOnMainThread()
        {
            Action? handler = Changed;
            if (handler == null)
            {
                return;
            }

            System.Threading.SynchronizationContext? ctx = AddinStudy.GetMainSynchronizationContext();
            if (ctx != null)
            {
                ctx.Post(_ => handler(), null);
            }
            else
            {
                handler();
            }
        }

        private sealed class PersistDto
        {
            public string BoundAssemblyFullPath { get; set; } = string.Empty;
        }
    }
}
