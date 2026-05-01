using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace tools
{
    /// <summary>
    /// 供 AI 决策用的极简操作摘要（带【操作】印记），与调试日志分离。
    /// </summary>
    public static class AiOperationBrief
    {
        private const string Tag = "【操作】";
        private const int MaxLineLength = 400;
        private static readonly object FileLock = new object();

        private static string GetBriefFilePath()
        {
            string llmDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "llm");
            if (!Directory.Exists(llmDir))
            {
                Directory.CreateDirectory(llmDir);
            }

            return Path.Combine(llmDir, "ai_operation_brief.txt");
        }

        /// <summary>
        /// 写入一行操作摘要（自动加【操作】前缀），并输出到 Console。
        /// </summary>
        /// <param name="brief">不含【操作】的简短说明，例如：改方程式：变量=…，新值=…</param>
        public static void Log(string brief)
        {
            if (string.IsNullOrWhiteSpace(brief))
            {
                return;
            }

            var core = brief.Trim().Replace("\r", " ").Replace("\n", " ");
            if (core.Length > MaxLineLength)
            {
                core = core.Substring(0, MaxLineLength) + "…";
            }

            string line = Tag + core;
            try
            {
                Console.WriteLine(line);
            }
            catch
            {
                // ignore
            }

            try
            {
                lock (FileLock)
                {
                    File.AppendAllText(GetBriefFilePath(), line + Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // ignore
            }
        }

        /// <summary>
        /// 取最近若干行，用于注入 system prompt。
        /// </summary>
        public static string GetRecentForPrompt(int maxLines)
        {
            if (maxLines <= 0)
            {
                return "";
            }

            try
            {
                var path = GetBriefFilePath();
                if (!File.Exists(path))
                {
                    return "";
                }

                List<string> lines;
                lock (FileLock)
                {
                    lines = File.ReadAllLines(path, Encoding.UTF8)
                        .Where(l => !string.IsNullOrWhiteSpace(l) && l.StartsWith(Tag, StringComparison.Ordinal))
                        .ToList();
                }

                if (lines.Count == 0)
                {
                    return "";
                }

                var tail = lines.Count > maxLines ? lines.Skip(lines.Count - maxLines).ToList() : lines;
                var sb = new StringBuilder();
                sb.AppendLine("=== 最近用户操作摘要（供决策，不含调试细节）===");
                foreach (var l in tail)
                {
                    sb.AppendLine(l.TrimEnd());
                }

                return sb.ToString().TrimEnd();
            }
            catch
            {
                return "";
            }
        }
    }
}
