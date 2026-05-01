using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace SolidWorksAddinStudy.Services
{
    /// <summary>
    /// 任务窗格（项目机型管理、零件处理状态、工作项目记录、机型方程式）共用的「机型」装配体路径。
    /// </summary>
    public static class MachineProjectContext
    {
        private static readonly object Sync = new object();

        private static string _boundAssemblyFullPath = string.Empty;

        /// <summary>当前绑定的装配体完整路径；未绑定时为空字符串。</summary>
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

        /// <summary>绑定变更后通知各任务窗格刷新显示（在 UI 线程派发）。</summary>
        public static event Action? Changed;

        private static string StorePath =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SolidWorksAddinStudy",
                "shared_machine_project.json");

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
                    Debug.WriteLine($"MachineProjectContext.LoadFromDisk: {ex.Message}");
                }
            }
        }

        /// <summary>设置共享机型装配体并持久化；与当前值相同则不写盘、不触发事件。</summary>
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
                    Debug.WriteLine($"MachineProjectContext.SetBoundAssembly: {ex.Message}");
                }
            }

            if (fire)
            {
                RaiseChangedOnMainThread();
            }
        }

        /// <summary>清除共享绑定（仅内存与文件；不修改机型方程式 XML）。</summary>
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
                    Debug.WriteLine($"MachineProjectContext.ClearBoundAssembly: {ex.Message}");
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
