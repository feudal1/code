using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace tools
{
    internal sealed class SwCommandMatchResult
    {
        public BridgeCommandInfo? BestCommand { get; set; }
        public double BestScore { get; set; }
        public List<BridgeCommandInfo> Candidates { get; set; } = new List<BridgeCommandInfo>();
    }

    internal static class SwCommandMatcher
    {
        // 经验阈值：>=0.52 认为可以自动执行
        public const double AutoRunThreshold = 0.52;

        public static SwCommandMatchResult Match(string userInput, List<BridgeCommandInfo> commands)
        {
            var result = new SwCommandMatchResult();
            if (string.IsNullOrWhiteSpace(userInput) || commands == null || commands.Count == 0)
            {
                return result;
            }

            string input = userInput.Trim();
            var tokens = Tokenize(input);
            var scored = new List<(BridgeCommandInfo Cmd, double Score)>();

            foreach (var cmd in commands)
            {
                double score = ScoreCommand(input, tokens, cmd);
                if (score > 0)
                {
                    scored.Add((cmd, score));
                }
            }

            if (scored.Count == 0)
            {
                return result;
            }

            var ordered = scored.OrderByDescending(x => x.Score).ToList();
            result.BestCommand = ordered[0].Cmd;
            result.BestScore = ordered[0].Score;
            result.Candidates = ordered.Take(3).Select(x => x.Cmd).ToList();
            return result;
        }

        private static double ScoreCommand(string input, List<string> tokens, BridgeCommandInfo cmd)
        {
            string name = (cmd.Name ?? string.Empty).Trim();
            string localizedName = (cmd.LocalizedName ?? string.Empty).Trim();
            string tooltip = (cmd.Tooltip ?? string.Empty).Trim();

            if (EqualsIgnoreCase(input, name) || EqualsIgnoreCase(input, localizedName))
            {
                return 1.0;
            }

            string allText = $"{name} {localizedName} {tooltip}".ToLowerInvariant();
            string inputLower = input.ToLowerInvariant();

            double score = 0;

            // 直接包含最强
            if (allText.Contains(inputLower))
            {
                score += 0.65;
            }

            // 分词命中加权
            if (tokens.Count > 0)
            {
                int hit = tokens.Count(t => t.Length >= 2 && allText.Contains(t.ToLowerInvariant()));
                score += 0.3 * (hit / (double)tokens.Count);
            }

            // 名称相似度（编辑距离）兜底
            double nameSim = Math.Max(
                Similarity(inputLower, name.ToLowerInvariant()),
                Similarity(inputLower, localizedName.ToLowerInvariant()));
            score += 0.25 * nameSim;

            return Math.Min(score, 1.0);
        }

        private static List<string> Tokenize(string text)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return list;
            }

            foreach (var part in Regex.Split(text, @"[\s,，。.;；:：()（）/\-_]+"))
            {
                string p = part.Trim();
                if (p.Length >= 2)
                {
                    list.Add(p);
                }
            }

            // 中文短句再做2-4字切分，提高匹配率
            if (list.Count <= 1 && text.Length >= 4)
            {
                for (int i = 0; i < text.Length - 1; i++)
                {
                    for (int len = 2; len <= 4 && i + len <= text.Length; len++)
                    {
                        string sub = text.Substring(i, len).Trim();
                        if (sub.Length >= 2 && !list.Contains(sub))
                        {
                            list.Add(sub);
                        }
                    }
                }
            }

            return list;
        }

        private static bool EqualsIgnoreCase(string a, string b)
        {
            return string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        private static double Similarity(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1) && string.IsNullOrEmpty(s2))
            {
                return 1.0;
            }

            if (string.IsNullOrEmpty(s1) || string.IsNullOrEmpty(s2))
            {
                return 0.0;
            }

            int dist = LevenshteinDistance(s1, s2);
            int maxLen = Math.Max(s1.Length, s2.Length);
            return maxLen == 0 ? 1.0 : 1.0 - (dist / (double)maxLen);
        }

        private static int LevenshteinDistance(string s, string t)
        {
            int n = s.Length;
            int m = t.Length;
            var d = new int[n + 1, m + 1];

            for (int i = 0; i <= n; i++) d[i, 0] = i;
            for (int j = 0; j <= m; j++) d[0, j] = j;

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = s[i - 1] == t[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }

            return d[n, m];
        }
    }
}
