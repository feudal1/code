using System;
using System.IO;
using System.Linq;
using System.Text;

namespace tools
{
    internal static class CommandLogStore
    {
        private const string LogFileName = "command-executions.jsonl";

        public static string GetLogDirectory()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "my_c", "command_logs");
        }

        public static string GetLogFilePath()
        {
            return Path.Combine(GetLogDirectory(), LogFileName);
        }

        public static string GetRecentLogLines(int maxLines)
        {
            string file = GetLogFilePath();
            if (!File.Exists(file))
            {
                return "(暂无日志)";
            }

            var allLines = File.ReadAllLines(file, Encoding.UTF8)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToArray();

            if (allLines.Length == 0)
            {
                return "(暂无日志)";
            }

            int take = Math.Min(Math.Max(maxLines, 1), allLines.Length);
            var tailLines = allLines.Skip(allLines.Length - take);
            return string.Join(Environment.NewLine, tailLines);
        }

        public static void ClearLogs()
        {
            string dir = GetLogDirectory();
            Directory.CreateDirectory(dir);
            File.WriteAllText(GetLogFilePath(), string.Empty, Encoding.UTF8);
        }
    }
}
