using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace tools
{
    public class RealtimeOperationRecord
    {
        public DateTime Timestamp { get; set; }
        public string CommandName { get; set; } = "";
        public bool Success { get; set; }
        public string Result { get; set; } = "";
    }

    public static class RealtimeOperationStore
    {
        private static readonly object FileLock = new object();

        private static string GetStoreFilePath()
        {
            string llmDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "llm");
            if (!Directory.Exists(llmDir))
            {
                Directory.CreateDirectory(llmDir);
            }

            return Path.Combine(llmDir, "realtime_operations.jsonl");
        }

        public static void Append(string commandName, bool success, string result)
        {
            try
            {
                var record = new RealtimeOperationRecord
                {
                    Timestamp = DateTime.Now,
                    CommandName = commandName ?? "",
                    Success = success,
                    Result = result ?? ""
                };

                string line = JsonConvert.SerializeObject(record);
                lock (FileLock)
                {
                    File.AppendAllText(GetStoreFilePath(), line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // 记录失败不影响主流程
            }
        }

        public static List<RealtimeOperationRecord> GetRecent(int count)
        {
            try
            {
                if (count <= 0)
                {
                    return new List<RealtimeOperationRecord>();
                }

                var path = GetStoreFilePath();
                if (!File.Exists(path))
                {
                    return new List<RealtimeOperationRecord>();
                }

                lock (FileLock)
                {
                    var lines = File.ReadAllLines(path, Encoding.UTF8);
                    return lines
                        .Where(line => !string.IsNullOrWhiteSpace(line))
                        .Reverse()
                        .Take(count)
                        .Select(line =>
                        {
                            try
                            {
                                return JsonConvert.DeserializeObject<RealtimeOperationRecord>(line);
                            }
                            catch
                            {
                                return null;
                            }
                        })
                        .Where(r => r != null)
                        .Cast<RealtimeOperationRecord>()
                        .Reverse()
                        .ToList();
                }
            }
            catch
            {
                return new List<RealtimeOperationRecord>();
            }
        }
    }
}
