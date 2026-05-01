using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace tools
{
    internal sealed class ChatTurn
    {
        [JsonProperty("role")]
        public string Role { get; set; } = "";

        [JsonProperty("content")]
        public string Content { get; set; } = "";
    }

    internal sealed class ChatHistoryStore
    {
        private const int DefaultMaxTurns = 20;
        private readonly string _historyFilePath;

        public ChatHistoryStore()
        {
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string dir = Path.Combine(localAppData, "my_c", "ai_chat");
            Directory.CreateDirectory(dir);
            _historyFilePath = Path.Combine(dir, "history.json");
        }

        public string GetHistoryFilePath()
        {
            return _historyFilePath;
        }

        public List<ChatTurn> Load()
        {
            try
            {
                if (!File.Exists(_historyFilePath))
                {
                    return new List<ChatTurn>();
                }

                string json = File.ReadAllText(_historyFilePath, Encoding.UTF8);
                return JsonConvert.DeserializeObject<List<ChatTurn>>(json) ?? new List<ChatTurn>();
            }
            catch
            {
                return new List<ChatTurn>();
            }
        }

        public void Save(List<ChatTurn> turns, int maxTurns = DefaultMaxTurns)
        {
            try
            {
                var safeTurns = (turns ?? new List<ChatTurn>())
                    .Where(t => !string.IsNullOrWhiteSpace(t.Role) && !string.IsNullOrWhiteSpace(t.Content))
                    .ToList();

                if (safeTurns.Count > maxTurns)
                {
                    safeTurns = safeTurns.Skip(safeTurns.Count - maxTurns).ToList();
                }

                string json = JsonConvert.SerializeObject(safeTurns, Formatting.Indented);
                File.WriteAllText(_historyFilePath, json, Encoding.UTF8);
            }
            catch
            {
                // 历史保存失败不影响主流程
            }
        }

        public void Clear()
        {
            Save(new List<ChatTurn>());
        }
    }
}
