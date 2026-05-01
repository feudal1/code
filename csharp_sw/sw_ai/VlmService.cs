using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace tools
{
    public class VlmService
    {
        private const string DefaultModel = "qwen-plus-latest";
        private const string ApiUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions";

        private readonly HttpClient _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };

        public async Task<string> CallTextAsync(string prompt, string? model = null, string? systemPrompt = null)
        {
            if (string.IsNullOrWhiteSpace(prompt))
            {
                throw new ArgumentException("提示词不能为空");
            }

            string apiKey = Environment.GetEnvironmentVariable("DASHSCOPE_API_KEY") ?? "";
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Console.Write("请输入 DASHSCOPE_API_KEY: ");
                apiKey = Console.ReadLine()?.Trim() ?? "";
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new ArgumentException("API Key 不能为空");
            }

            var requestBody = new
            {
                model = model ?? DefaultModel,
                messages = new object[]
                {
                    new
                    {
                        role = "system",
                        content = string.IsNullOrWhiteSpace(systemPrompt)
                            ? "你是 CAD 命令执行日志分析助手，回答要简洁、可执行。"
                            : systemPrompt
                    },
                    new
                    {
                        role = "user",
                        content = prompt
                    }
                }
            };

            string jsonBody = JsonConvert.SerializeObject(requestBody);
            using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
            {
                Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Authorization", $"Bearer {apiKey}");

            using var response = await _httpClient.SendAsync(request);
            string responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"API 请求失败 ({response.StatusCode}): {responseText}");
            }

            var obj = JObject.Parse(responseText);
            string? content = obj["choices"]?[0]?["message"]?["content"]?.ToString();
            if (string.IsNullOrWhiteSpace(content))
            {
                return "模型未返回文本内容。";
            }

            return content.Trim();
        }

        internal async Task<string> CallWithHistoryAsync(
            List<ChatTurn> history,
            string userInput,
            string? model = null,
            string? systemPrompt = null)
        {
            if (string.IsNullOrWhiteSpace(userInput))
            {
                throw new ArgumentException("用户输入不能为空");
            }

            string apiKey = Environment.GetEnvironmentVariable("DASHSCOPE_API_KEY") ?? "";
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                Console.Write("请输入 DASHSCOPE_API_KEY: ");
                apiKey = Console.ReadLine()?.Trim() ?? "";
            }

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new ArgumentException("API Key 不能为空");
            }

            var messages = new List<object>
            {
                new
                {
                    role = "system",
                    content = string.IsNullOrWhiteSpace(systemPrompt)
                        ? "你是 CAD 命令执行日志分析助手，回答要简洁、可执行。"
                        : systemPrompt
                }
            };

            foreach (var turn in history ?? new List<ChatTurn>())
            {
                string role = (turn.Role ?? "").Trim().ToLowerInvariant();
                if ((role == "user" || role == "assistant") && !string.IsNullOrWhiteSpace(turn.Content))
                {
                    messages.Add(new
                    {
                        role,
                        content = turn.Content
                    });
                }
            }

            messages.Add(new
            {
                role = "user",
                content = userInput
            });

            var requestBody = new
            {
                model = model ?? DefaultModel,
                messages = messages.ToArray()
            };

            string jsonBody = JsonConvert.SerializeObject(requestBody);
            using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
            {
                Content = new StringContent(jsonBody, Encoding.UTF8, "application/json")
            };
            request.Headers.Add("Authorization", $"Bearer {apiKey}");

            using var response = await _httpClient.SendAsync(request);
            string responseText = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"API 请求失败 ({response.StatusCode}): {responseText}");
            }

            var obj = JObject.Parse(responseText);
            string? content = obj["choices"]?[0]?["message"]?["content"]?.ToString();
            if (string.IsNullOrWhiteSpace(content))
            {
                return "模型未返回文本内容。";
            }

            return content.Trim();
        }
    }
}
