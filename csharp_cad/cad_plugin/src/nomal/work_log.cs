using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

namespace share.nomal
{
    /// <summary>
    /// 工作日志条目
    /// </summary>
    public class WorkLogEntry
    {
        public int Id { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Content { get; set; } = "";
        public bool IsCompleted { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? Remark { get; set; }
    }

    /// <summary>
    /// 工作日志管理器
    /// </summary>
    public static class WorkLogManager
    {
        private static readonly string LogFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ctools_worklog.json"
        );

        private static List<WorkLogEntry> LoadLogs()
        {
            if (!File.Exists(LogFilePath))
                return new List<WorkLogEntry>();

            try
            {
                var json = File.ReadAllText(LogFilePath);
                return JsonConvert.DeserializeObject<List<WorkLogEntry>>(json) ?? new List<WorkLogEntry>();
            }
            catch
            {
                return new List<WorkLogEntry>();
            }
        }

        private static void SaveLogs(List<WorkLogEntry> logs)
        {
            var json = JsonConvert.SerializeObject(logs, Formatting.Indented);
            File.WriteAllText(LogFilePath, json);
        }

        /// <summary>
        /// 添加工作日志
        /// </summary>
        public static int AddLog(string content, string? remark = null)
        {
            var logs = LoadLogs();
            var newId = logs.Count > 0 ? logs.Max(l => l.Id) + 1 : 1;

            var entry = new WorkLogEntry
            {
                Id = newId,
                CreatedAt = DateTime.Now,
                Content = content,
                IsCompleted = false,
                Remark = remark
            };

            logs.Add(entry);
            SaveLogs(logs);

            return newId;
        }

        /// <summary>
        /// 获取所有未完成的日志
        /// </summary>
        public static List<WorkLogEntry> GetPendingLogs()
        {
            var logs = LoadLogs();
            return logs.Where(l => !l.IsCompleted)
                       .OrderBy(l => l.CreatedAt)
                       .ToList();
        }

        /// <summary>
        /// 获取所有日志
        /// </summary>
        public static List<WorkLogEntry> GetAllLogs()
        {
            return LoadLogs().OrderByDescending(l => l.CreatedAt).ToList();
        }

        /// <summary>
        /// 设置日志完成状态
        /// </summary>
        public static bool SetComplete(int id, bool completed = true)
        {
            var logs = LoadLogs();
            var log = logs.FirstOrDefault(l => l.Id == id);

            if (log == null)
                return false;

            log.IsCompleted = completed;
            log.CompletedAt = completed ? DateTime.Now : null;
            SaveLogs(logs);

            return true;
        }

        /// <summary>
        /// 删除日志
        /// </summary>
        public static bool DeleteLog(int id)
        {
            var logs = LoadLogs();
            var log = logs.FirstOrDefault(l => l.Id == id);

            if (log == null)
                return false;

            logs.Remove(log);
            SaveLogs(logs);

            return true;
        }
    }
}
