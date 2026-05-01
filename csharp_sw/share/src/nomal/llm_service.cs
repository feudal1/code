using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Net.Http;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace tools
{
    /// <summary>
    /// 通义千问 (DashScope) 服务类
    /// 支持普通 LLM 对话和 VLM 图像分析
    /// </summary>
    public class LlmService
    {
        // 配置项
        private const string DefaultModel = "qwen3.5-flash";
        
        private const string ApiUrl = "https://dashscope.aliyuncs.com/compatible-mode/v1/chat/completions";
        private const string ApiHost = "dashscope.aliyuncs.com";
        private const int ApiPort = 443;
        private const string BridgePipeName = "my_c_llm_bridge_v1";
        
        private HttpClient _httpClient;
        private readonly string _shotMemoryFile;
        private readonly string _logFilePath;
        private readonly string _worksKnowledgeFile;
        private readonly string _runLogFilePath;
        private readonly Func<string>? _getCommandsDescriptionFunc;
        private bool _networkDiagLogged;
        private bool _usingProxy;
        private string _proxyDescription = "none";
        private Process? _bridgeProcess;
        private bool _bridgeLaunchAttempted;

        public LlmService(Func<string>? getCommandsDescriptionFunc = null)
        {
            // net48 下默认 TLS 协议可能不包含 TLS1.2，导致 HTTPS 请求失败
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.DefaultConnectionLimit = Math.Max(ServicePointManager.DefaultConnectionLimit, 20);

            _httpClient = CreateHttpClient();

            // 初始化文件路径
            string llmDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "llm");
            if (!Directory.Exists(llmDir))
            {
                Directory.CreateDirectory(llmDir);
            }

            _shotMemoryFile = Path.Combine(llmDir, "shot_memory.json");
      _logFilePath = Path.Combine(llmDir, "longterm_memory.txt");
            _worksKnowledgeFile = Path.Combine(llmDir, "works_knowledge.txt");
            _runLogFilePath = Path.Combine(llmDir, "run_log.txt");
            _getCommandsDescriptionFunc = getCommandsDescriptionFunc;
            
        
        }

        private HttpClient CreateHttpClient()
        {
            var (proxy, proxyDescription) = BuildProxy();
            _usingProxy = proxy != null;
            _proxyDescription = proxyDescription;

            var handler = new HttpClientHandler
            {
                UseProxy = _usingProxy,
                Proxy = proxy,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            Console.WriteLine($"[调试] HttpClient 代理模式：{(_usingProxy ? "启用" : "禁用")} ({_proxyDescription})");

            return new HttpClient(handler)
            {
                Timeout = TimeSpan.FromMinutes(5)
            };
        }

        private (IWebProxy? Proxy, string Description) BuildProxy()
        {
            // 优先读取常见代理环境变量，兼容公司网络或本机代理软件
            string? proxyText = Environment.GetEnvironmentVariable("DASHSCOPE_PROXY")
                                ?? Environment.GetEnvironmentVariable("HTTPS_PROXY")
                                ?? Environment.GetEnvironmentVariable("https_proxy")
                                ?? Environment.GetEnvironmentVariable("HTTP_PROXY")
                                ?? Environment.GetEnvironmentVariable("http_proxy");
            if (!string.IsNullOrWhiteSpace(proxyText) && Uri.TryCreate(proxyText, UriKind.Absolute, out var proxyUri))
            {
                Console.WriteLine($"[调试] 使用环境变量代理：{proxyUri}");
                return (new WebProxy(proxyUri)
                {
                    BypassProxyOnLocal = true,
                    Credentials = CredentialCache.DefaultCredentials
                }, $"env:{proxyUri}");
            }

            // 回退到系统默认代理（WinINet）
            var systemProxy = WebRequest.GetSystemWebProxy();
            if (systemProxy != null)
            {
                try
                {
                    var testUri = new Uri($"https://{ApiHost}");
                    var detectedProxyUri = systemProxy.GetProxy(testUri);
                    Console.WriteLine($"[调试] 系统代理检测结果：{detectedProxyUri}");
                    // 当返回的 URI 与目标地址一致时，表示该地址实际走直连
                    if (detectedProxyUri != null &&
                        !string.Equals(
                            detectedProxyUri.Authority,
                            testUri.Authority,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        return (systemProxy, $"system:{detectedProxyUri}");
                    }

                    Console.WriteLine("[调试] 系统代理未命中目标地址，改为直连");
                }
                catch
                {
                    // 仅用于调试，不影响主流程
                }
            }
            return (null, "none");
        }

        private async Task LogNetworkDiagnosticsOnceAsync()
        {
            if (_networkDiagLogged)
            {
                return;
            }
            _networkDiagLogged = true;

            try
            {
                var addresses = await Dns.GetHostAddressesAsync(ApiHost);
                Console.WriteLine($"[调试] DNS 解析 {ApiHost}：{string.Join(", ", addresses.Select(a => a.ToString()))}");
                var orderedAddresses = addresses
                    .OrderBy(a => a.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
                    .ToList();

                foreach (var address in orderedAddresses)
                {
                    using var tcp = new TcpClient(address.AddressFamily);
                    var connectTask = tcp.ConnectAsync(address, ApiPort);
                    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5));
                    var completed = await Task.WhenAny(connectTask, timeoutTask);
                    if (completed == connectTask && tcp.Connected)
                    {
                        Console.WriteLine($"[调试] TCP 连通成功：{address}:{ApiPort}");
                        break;
                    }
                    Console.WriteLine($"[调试] TCP 连通超时：{address}:{ApiPort}");
                }

            // 辅助打印系统代理信息，便于排查公司网络
            try
            {
                var systemProxy = WebRequest.GetSystemWebProxy();
                if (systemProxy != null)
                {
                    var proxyUri = systemProxy.GetProxy(new Uri($"https://{ApiHost}"));
                    Console.WriteLine($"[调试] 诊断-系统代理路由：{proxyUri}");
                }
            }
            catch
            {
                // 诊断信息失败时忽略
            }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[调试] 网络预检失败：{ex.GetType().Name} - {ex.Message}");
            }
        }

        /// <summary>
        /// 从本地文件加载消息历史（不包含 system）
        /// </summary>
        private List<ChatMessage> LoadMessagesFromDisk()
        {
            try
            {
                if (File.Exists(_shotMemoryFile))
                {
                    var json = File.ReadAllText(_shotMemoryFile, Encoding.UTF8);
                    var messages = JsonConvert.DeserializeObject<List<ChatMessage>>(json) ?? new List<ChatMessage>();
                    
                    // 只保留合法的 role 值：user, assistant, tool, function
                    var validRoles = new HashSet<string> { "user", "assistant", "tool", "function" };
                    var filteredMessages = messages.Where(m => validRoles.Contains(m.Role)).ToList();
                    
                    // 如果有被过滤的消息，提示用户并重新保存
                    if (filteredMessages.Count < messages.Count)
                    {
                        Console.WriteLine($"[调试] 从短期记忆中过滤掉 {messages.Count - filteredMessages.Count} 条非法 role 的消息");
                        SaveMessagesToDisk(filteredMessages);
                    }
                    
                    return filteredMessages;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"加载消息历史失败：{ex.Message}");
            }
            
            return new List<ChatMessage>();
        }

        /// <summary>
        /// 保存消息历史到本地 JSON 文件（超过 10 条自动截断）
        /// </summary>
        private void SaveMessagesToDisk(List<ChatMessage> messages)
        {
            try
            {
                // 只保留合法的 role 值：user, assistant, tool, function
                var validRoles = new HashSet<string> { "user", "assistant", "tool", "function" };
                var filteredMessages = messages.Where(m => validRoles.Contains(m.Role)).ToList();
                
                // 如果消息超过 10 条，保留最近的 10 条（5 轮对话）
                if (filteredMessages.Count > 10)
                {
                    filteredMessages = filteredMessages.Skip(filteredMessages.Count - 10).ToList();
                    Console.WriteLine($"\n[调试] 消息数量超过 10 条，已截断保留最近 10 条");
                }
                
                var json = JsonConvert.SerializeObject(filteredMessages, Formatting.Indented);
                File.WriteAllText(_shotMemoryFile, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"保存消息历史失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 读取 works_knowledge.txt 文件内容
        /// </summary>
        private string ReadWorksKnowledge()
        {
            try
            {
                if (File.Exists(_worksKnowledgeFile))
                {
                    return File.ReadAllText(_worksKnowledgeFile, Encoding.UTF8).Trim();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"读取 works_knowledge.txt 失败：{ex.Message}");
            }
            
            return "";
        }

        /// <summary>
        /// 执行 search 搜索并获取相关命令（基于相似度匹配）
        /// </summary>
        private string SearchCommands(string keyword, double threshold = 0.3, int? topK = null)
        {
            // 使用委托获取实时命令描述
            if (_getCommandsDescriptionFunc != null)
            {
                try
                {
                    string allContent = _getCommandsDescriptionFunc();
                    Debug.WriteLine($"\n[调试] 命令描述内容长度：{allContent.Length} 字符");
                    Debug.WriteLine($"[调试] 搜索关键词：'{keyword}'");
                    Debug.WriteLine($"[调试] 阈值：{threshold}, topK: {topK}");
                    
                    // 保存命令描述到文件以便调试
                    System.IO.File.WriteAllText("debug_commands.txt", allContent, Encoding.UTF8);
                    Debug.WriteLine($"[调试] 命令描述已保存到 debug_commands.txt");
                    
                    return SearchInContent(allContent, keyword, threshold, topK);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"调用命令描述获取函数失败：{ex.Message}");
                }
            }
            
            return $"未找到与 '{keyword}' 相关的命令";
        }

        /// <summary>
        /// 在内容中搜索相关命令（重构版：以命令块为单位匹配）
        /// </summary>
        private string SearchInContent(string content, string keyword, double threshold, int? topK)
        {
            if (string.IsNullOrWhiteSpace(keyword))
            {
                return content;
            }

            var lines = content.Split('\n');
            var commandBlocks = new List<List<string>>();
            var currentBlock = new List<string>();
            
            // 第一步：解析所有命令块
            foreach (var line in lines)
            {
                if (line.StartsWith("【") && line.Contains("】"))
                {
                    if (currentBlock.Count > 0)
                    {
                        commandBlocks.Add(new List<string>(currentBlock));
                    }
                    currentBlock.Clear();
                    currentBlock.Add(line);
                }
                else if (currentBlock.Count > 0)
                {
                    currentBlock.Add(line);
                }
            }
            
            if (currentBlock.Count > 0)
            {
                commandBlocks.Add(currentBlock);
            }
            
            Debug.WriteLine($"[调试] 解析到 {commandBlocks.Count} 个命令块");
            
            // 第二步：对每个命令块计算匹配分数
            var matchedBlocks = new List<(List<string> Block, double Score)>();
            string keywordLower = keyword.ToLower();
            
            foreach (var block in commandBlocks)
            {
                // 将整个块合并为一个字符串进行匹配
                string blockText = string.Join(" ", block).ToLower();
                
                // 快速检查：如果整个块包含关键词，直接高分匹配
                if (blockText.Contains(keywordLower))
                {
                    matchedBlocks.Add((block, 1.0));
                    continue;
                }
                
                // 分词匹配：将用户输入按空格、标点、中文常见分隔符分割
                // 对于中文，我们还需要按字符级别进行切分
                var separators = new[] { ' ', ',', '.', ',', '.', '、', '(', ')', '（', '）', ':', ':', '\n', '\t' };
                var rawKeywords = keywordLower.Split(separators, StringSplitOptions.RemoveEmptyEntries);
                
                // 如果只有一个长词（可能是中文句子），需要进行字符级分词
                List<string> keywords = new List<string>();
                if (rawKeywords.Length == 1 && rawKeywords[0].Length > 3)
                {
                    string longWord = rawKeywords[0];
                    
                    // 提取有意义的词汇（2-4 个字）
                    for (int i = 0; i < longWord.Length - 1; i++)
                    {
                        // 提取 2-4 个字的词
                        for (int len = 2; len <= 4 && i + len <= longWord.Length; len++)
                        {
                            string subWord = longWord.Substring(i, len);
                            // 避免添加纯标点或重复的词
                            if (!keywords.Contains(subWord) && !string.IsNullOrWhiteSpace(subWord.Trim()))
                            {
                                keywords.Add(subWord);
                            }
                        }
                    }
                        Debug.WriteLine($"[调试] 中文句子分词结果：[{string.Join(", ", keywords.Take(10))}...] (共{keywords.Count}个词)");
                }
                else
                {
                    keywords = rawKeywords.ToList();
                }
                int matchCount = 0;
                int totalKeywords = keywords.Count;
                
                Debug.WriteLine($"[调试] 检查命令块，关键词：[{string.Join(", ", keywords)}]");
                
                foreach (var kw in keywords)
                {
                    if (kw.Length >= 2 && blockText.Contains(kw))
                    {
                        matchCount++;
                        Debug.WriteLine($"[调试]   ✓ 匹配到关键词：{kw}");
                    }
                }
                
                // 计算分词匹配率
                if (totalKeywords > 0)
                {
                    double keywordRatio = (double)matchCount / totalKeywords;
                    
                    Debug.WriteLine($"[调试]   匹配率：{matchCount}/{totalKeywords} = {keywordRatio:F2}");
                    
                    // 如果超过 30% 的关键词匹配，认为匹配成功
                    if (keywordRatio >= 0.3)
                    {
                        matchedBlocks.Add((block, 0.6 + keywordRatio * 0.4));
                        Debug.WriteLine($"[调试]   ✓ 匹配成功，分数：{0.6 + keywordRatio * 0.4:F2}");
                        continue;
                    }
                }
                
                // 最后使用编辑距离作为兜底（逐行检查）
                foreach (var line in block)
                {
                    double score = CalculateLevenshteinRatioSimple(keywordLower, line.ToLower());
                    if (score >= threshold)
                    {
                        matchedBlocks.Add((block, score));
                        break;
                    }
                }
            }
            
            // 第三步：按分数排序
            matchedBlocks.Sort((a, b) => b.Score.CompareTo(a.Score));
            
            if (matchedBlocks.Count == 0)
            {
                return $"未找到与 '{keyword}' 相关的命令";
            }
            
            // 第四步：返回前 topK 个结果
            var resultBlocks = matchedBlocks.Select(b => b.Block);
            if (topK.HasValue)
            {
                resultBlocks = resultBlocks.Take(topK.Value);
            }
            
            var result = resultBlocks.SelectMany(b => b);
            return string.Join("\n", result);
        }

        /// <summary>
        /// 计算单行的相似度（保留用于其他场景）
        /// </summary>
        private double CalculateLineSimilarity(string keyword, string line)
        {
            if (string.IsNullOrEmpty(keyword) || string.IsNullOrEmpty(line))
            {
                return 0.0;
            }

            string keywordLower = keyword.ToLower();
            string lineLower = line.ToLower();

            // 直接包含关键词则返回高分
            if (lineLower.Contains(keywordLower))
            {
                return 0.8;
            }

            // 否则计算编辑距离相似度
            return CalculateLevenshteinRatioSimple(keywordLower, lineLower);
        }

        /// <summary>
        /// 简化的 Levenshtein 距离比率计算
        /// </summary>
        private double CalculateLevenshteinRatioSimple(string s1, string s2)
        {
            if (string.IsNullOrEmpty(s1)) return string.IsNullOrEmpty(s2) ? 1.0 : 0.0;
            if (string.IsNullOrEmpty(s2)) return 0.0;

            int[,] matrix = new int[s1.Length + 1, s2.Length + 1];

            for (int i = 0; i <= s1.Length; i++)
                matrix[i, 0] = i;

            for (int j = 0; j <= s2.Length; j++)
                matrix[0, j] = j;

            for (int i = 1; i <= s1.Length; i++)
            {
                for (int j = 1; j <= s2.Length; j++)
                {
                    int cost = (s1[i - 1] == s2[j - 1]) ? 0 : 1;
                    matrix[i, j] = Math.Min(
                        Math.Min(
                            matrix[i - 1, j] + 1,
                            matrix[i, j - 1] + 1
                        ),
                        matrix[i - 1, j - 1] + cost
                    );
                }
            }

            int distance = matrix[s1.Length, s2.Length];
            int maxLen = Math.Max(s1.Length, s2.Length);
            return maxLen > 0 ? 1.0 - (distance / (double)maxLen) : 1.0;
        }

        /// <summary>
        /// 构建 System Prompt（精简版）
        /// </summary>
        private string BuildSystemPrompt()
        {
            var sysPrompt = new StringBuilder();

            sysPrompt.AppendLine("你是一个猫娘 SolidWorks 自动化助手，可以帮助用户执行各种 SolidWorks 操作。");
            sysPrompt.AppendLine("性格特点：活泼可爱，说话结尾会带'喵'字。");
            sysPrompt.AppendLine("交流方式：使用可爱的语气，每句话结尾都要加上'喵'或'喵~'");
            sysPrompt.AppendLine("");
            sysPrompt.AppendLine("重要规则：");
            sysPrompt.AppendLine("1. 当用户提供可用工具时，你必须优先使用工具调用来完成任务，而不是自由文本回复");
            sysPrompt.AppendLine("2. 如果用户的请求可以通过提供的工具完成，请立即调用相应的工具");
            sysPrompt.AppendLine("3. 只有在无法找到合适工具或需要澄清用户意图时，才可以使用自由文本回复");
            sysPrompt.AppendLine("4. 工具调用格式必须严格按照要求，不要自行创造工具名称");

            var briefOps = AiOperationBrief.GetRecentForPrompt(20);
            if (!string.IsNullOrWhiteSpace(briefOps))
            {
                sysPrompt.AppendLine("");
                sysPrompt.AppendLine(briefOps);
            }

            return sysPrompt.ToString();
        }

        /// <summary>
        /// 读取最近的运行日志（指定字符数）
        /// </summary>
        private string ReadRecentRunLog(int charCount)
        {
            try
            {
                if (File.Exists(_runLogFilePath))
                {
                    var fileInfo = new FileInfo(_runLogFilePath);
                    if (fileInfo.Length == 0)
                    {
                        return "";
                    }
                    
                    using var fs = new FileStream(
                        _runLogFilePath, 
                        FileMode.Open, 
                        FileAccess.Read, 
                        FileShare.ReadWrite,
                        bufferSize: 4096
                    );
                    var buffer = new byte[Math.Min(fileInfo.Length, charCount * 3)]; // 预留空间给多字节字符
                    int bytesRead;
                    int totalBytesRead = 0;
                    
                    // 从文件末尾向前读取
                    while ((bytesRead = fs.Read(buffer, totalBytesRead, buffer.Length - totalBytesRead)) > 0)
                    {
                        totalBytesRead += bytesRead;
                        if (totalBytesRead >= charCount * 2) // 至少读取足够的字节
                        {
                            break;
                        }
                    }
                    
                    if (totalBytesRead == 0)
                    {
                        return "";
                    }
                    
                    // 转换为字符串并取最后 charCount 个字符
                    string content = Encoding.UTF8.GetString(buffer, 0, totalBytesRead);
                    if (content.Length > charCount)
                    {
                        content = content.Substring(content.Length - charCount);
                    }
                    
                    // 清理不完整的行，从第一个完整行开始
                    int firstNewLine = content.IndexOf('\n');
                    if (firstNewLine >= 0 && firstNewLine < content.Length - 1)
                    {
                        content = content.Substring(firstNewLine + 1);
                    }
                    
                    return content.Trim();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"读取运行日志失败：{ex.Message}");
            }
            
            return "";
        }

        /// <summary>
        /// 获取 API Key（从环境变量或用户输入）
        /// </summary>
        private async Task<string> GetApiKeyAsync()
        {
            string apiKey = Environment.GetEnvironmentVariable("DASHSCOPE_API_KEY") ?? "";
            
            if (string.IsNullOrEmpty(apiKey))
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("\n[警告] 未找到 DASHSCOPE_API_KEY 环境变量。");
                Console.ResetColor();
                Console.Write("请输入临时 API Key: ");
                
                // 使用异步方式读取控制台输入
                apiKey = await Task.Run(() => Console.ReadLine()?.Trim() ?? "");
            }

            if (string.IsNullOrEmpty(apiKey)) 
                throw new ArgumentException("API Key 不能为空");
            
            return apiKey;
        }

        /// <summary>
        /// 统一的对话接口（支持文本和图像，不支持 Tool）
        /// </summary>
        public async Task<string> ChatAsync(string userPrompt, string? imagePath = null)
        {
            string apiKey = await GetApiKeyAsync();

            // 先对用户输入进行 search 搜索，只导入相关的命令到上下文
            string searchResult = SearchCommands(userPrompt, threshold: 0.3, topK: 5);
            // 加载历史消息（不包含 system）
            var messages = LoadMessagesFromDisk();
            
            // 构建基础的 system prompt
            var sysPrompt = BuildSystemPrompt();
            
            // 将搜索结果添加到 system prompt 中
            if (!string.IsNullOrEmpty(searchResult))
            {
                sysPrompt += "\n***相关命令:***\n";
                sysPrompt += searchResult;
            }
            
            // 读取最近运行日志并添加到 system prompt 中
            string recentLog = ReadRecentRunLog(500);
            if (!string.IsNullOrEmpty(recentLog))
            {
                sysPrompt += "\n\n=== 最近的运行日志 ===\n";
                sysPrompt += recentLog;
            }
            
            // 构建完整的消息列表
            var messagesWithSystem = new List<ChatMessage>
            {
                new ChatMessage { Role = "system", Content = sysPrompt.ToString() }
            };
            messagesWithSystem.AddRange(messages);
            
     
            
            var startTime = DateTime.Now;
            var fullResponse = !string.IsNullOrEmpty(imagePath) 
                ? await CallStreamingWithImageAsync(messagesWithSystem, userPrompt, imagePath, apiKey, DefaultModel)
                : await CallStreamingAsync(messagesWithSystem, apiKey, DefaultModel);
            
            var elapsedMs = (DateTime.Now - startTime).TotalMilliseconds;
            Console.WriteLine($"\n{(imagePath != null ? "VLM" : "LLM")} 调用耗时：{elapsedMs:F0}毫秒");
            
            // 保存用户输入到消息历史
            messages.Add(new ChatMessage { Role = "user", Content = userPrompt });
            
            // 保存助手回复到消息历史
            messages.Add(new ChatMessage { Role = "assistant", Content = fullResponse });
            
            SaveMessagesToDisk(messages);
            
            // 保存长期记忆
            var memoryContent = imagePath != null ? $"[图像分析]{imagePath}\n{fullResponse}" : fullResponse;
            SaveLongTermMemoryLog(memoryContent);
            
            return fullResponse;
        }

        /// <summary>
        /// 带 Tool 调用的对话接口（优化版：只传递搜索后的工具）
        /// </summary>
        public async Task<(string Response, List<ToolCall>? ToolCalls)> ChatWithToolsAsync(
            string userPrompt, 
            List<ToolDefinition> allTools)
        {
            string apiKey = await GetApiKeyAsync();
        
            // 先对用户输入进行 search 搜索，获取相关命令名称
            string searchResult = SearchCommands(userPrompt, threshold: 0.3, topK: 5);
                    
            // 从搜索结果中提取匹配的命令名，过滤工具列表
            var filteredTools = FilterToolsBySearchResult(allTools, searchResult);
                    
            // 加载历史消息
            var messages = LoadMessagesFromDisk();
                    
            // 构建 system prompt（不包含命令列表）
            var sysPrompt = BuildSystemPrompt();
            
            // 构建消息列表（用于发送给 LLM）
            var messagesForLLM = new List<ChatMessage>
            {
                new ChatMessage { Role = "system", Content = sysPrompt.ToString() }
            };
            
            // 添加历史消息
            messagesForLLM.AddRange(messages);
                    
            var startTime = DateTime.Now;
            var (response, toolCalls) = await CallStreamingWithToolsAsync(
                messagesForLLM, 
                userPrompt, 
                filteredTools,  // 只传递筛选后的工具
                apiKey, 
                DefaultModel,
                forceToolCall: true  // 强制要求工具调用
            );
                    
            var elapsedMs = (DateTime.Now - startTime).TotalMilliseconds;
            Console.WriteLine($"\nLLM Tool 调用耗时：{elapsedMs:F0}毫秒");
                    
            // 保存用户输入到消息历史
            messages.Add(new ChatMessage { Role = "user", Content = userPrompt });
                    
            // 如果有回复内容，保存助手回复
            if (!string.IsNullOrEmpty(response))
            {
                messages.Add(new ChatMessage { Role = "assistant", Content = response });
            }
            else if (toolCalls != null && toolCalls.Count > 0)
            {
                // 保存 Tool 调用到消息历史
                foreach (var toolCall in toolCalls)
                {
                    messages.Add(new ChatMessage 
                    { 
                        Role = "assistant", 
                        Content = "",
                        ToolCallId = toolCall.Id,
                        ToolCalls = toolCalls
                    });
                }
            }
                    
            SaveMessagesToDisk(messages);
            SaveLongTermMemoryLog(response ?? $"ToolCall: {JsonConvert.SerializeObject(toolCalls)}");
                    
            return (response ?? "", toolCalls);
        }
        
        /// <summary>
        /// 根据搜索结果过滤工具列表
        /// </summary>
        private List<ToolDefinition> FilterToolsBySearchResult(List<ToolDefinition> allTools, string searchResult)
        {
            // 如果搜索未找到结果，返回全部工具，并给出提示
            if (searchResult.StartsWith("未找到"))
            {
                Console.WriteLine($"\n[调试] 未找到匹配的命令，返回全部 {allTools.Count} 个工具");
                        
                // 添加一个特殊的工具来提示 AI
                var hintTool = new ToolDefinition
                {
                    Type = "function",
                    Function = new FunctionDefinition
                    {
                        Name = "no_matching_command_found",
                        Description = "提示：未在命令库中找到与用户问题直接相关的命令。请根据用户的实际需求，从可用工具中选择最接近的功能，或者询问用户更具体的需求。",
                        Parameters = new FunctionParameters
                        {
                            Properties = new Dictionary<string, PropertyDefinition>
                            {
                                ["reason"] = new PropertyDefinition 
                                { 
                                    Type = "string", 
                                    Description = "说明为什么现有工具无法满足用户需求" 
                                }
                            },
                            Required = new List<string>()
                        }
                    }
                };
                        
                // 将提示工具添加到全部工具前面
                var result = new List<ToolDefinition> { hintTool };
                result.AddRange(allTools);
                return result;
            }
                    
            // 解析搜索结果中的命令名
            var matchedCommandNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var lines = searchResult.Split('\n');
                    
            foreach (var line in lines)
            {
                // 匹配格式：【分组】命令名
                if (line.StartsWith("【") && line.Contains("】"))
                {
                    int closeBracketIndex = line.IndexOf('】');
                    if (closeBracketIndex > 0 && closeBracketIndex < line.Length - 1)
                    {
                        string commandName = line.Substring(closeBracketIndex + 1).Trim();
                        // 去掉异步标记
                        int asyncIndex = commandName.IndexOf(" (异步)");
                        if (asyncIndex >= 0)
                        {
                            commandName = commandName.Substring(0, asyncIndex).Trim();
                        }
                                
                        if (!string.IsNullOrEmpty(commandName))
                        {
                            matchedCommandNames.Add(commandName.ToLower());
                        }
                    }
                }
            }
                    
            Console.WriteLine($"\n[调试] 从搜索结果中提取到 {matchedCommandNames.Count} 个命令名：{string.Join(", ", matchedCommandNames)}");
                    
            // 过滤工具列表，只保留匹配的命令
            var filteredTools = allTools
                .Where(t => 
                {
                    string funcName = t.Function.Name;
                    // 去掉 execute_前缀进行比较
                    if (funcName.StartsWith("execute_"))
                    {
                        funcName = funcName.Substring(8);
                    }
                    return matchedCommandNames.Contains(funcName.ToLower());
                })
                .ToList();
                    
            Console.WriteLine($"\n[调试] 过滤后剩余 {filteredTools.Count} 个工具");
            return filteredTools;
        }

        /// <summary>
        /// 执行流式调用（纯文本 LLM）
        /// </summary>
        private async Task<string> CallStreamingCoreAsync(object requestBodyObj, string apiKey)
        {
            if (string.IsNullOrEmpty(apiKey)) 
                throw new ArgumentException("API Key 不能为空");

            var fullResponse = new StringBuilder();
            bool hasOutputStarted = false;

            string jsonBody = JsonConvert.SerializeObject(requestBodyObj);

            // SolidWorks 进程禁网时优先通过本地桥接进程转发
            if (ShouldPreferBridge())
            {
                var bridgeResult = await TryCallLocalBridgeAsync(jsonBody, apiKey);
                if (bridgeResult.Success)
                {
                    if (!string.IsNullOrWhiteSpace(bridgeResult.Content))
                    {
                        Console.WriteLine("\n[调试] 已通过本地桥接返回结果。");
                        Console.Write(bridgeResult.Content);
                        Console.Out.Flush();
                    }
                    return bridgeResult.Content ?? "";
                }
                if (!string.IsNullOrWhiteSpace(bridgeResult.Error))
                {
                    Console.WriteLine($"[调试] 本地桥接不可用，改走当前进程网络：{bridgeResult.Error}");
                }
            }

            try
            {
                using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                
                Console.WriteLine($"\n[调试] 请求体 JSON 长度：{jsonBody.Length} 字符");
                Console.WriteLine($"[调试] HttpClient 状态：{(_httpClient != null ? "已初始化" : "未初始化")}");
                Console.WriteLine($"[调试] HttpClient Timeout: {_httpClient?.Timeout.TotalSeconds} 秒");
                Console.WriteLine($"[调试] 当前代理配置：{(_usingProxy ? "启用" : "禁用")} ({_proxyDescription})");
                await LogNetworkDiagnosticsOnceAsync();
                
                // 设置 Header
                _httpClient!.DefaultRequestHeaders.Clear();
                _httpClient!.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
                
                Console.WriteLine($"[调试] Authorization Header 已设置");

                // 发送请求
                var request = new HttpRequestMessage
                {
                    Method = HttpMethod.Post,
                    RequestUri = new Uri(ApiUrl),
                    Content = content
                };
                
                Console.WriteLine($"\n[调试] 正在发送请求到：{ApiUrl}");
                Console.WriteLine($"[调试] 请求方法：{request.Method}");
                Console.WriteLine($"[调试] API Key 前缀：{apiKey.Substring(0, Math.Min(10, apiKey.Length))}...");
                
                HttpResponseMessage? response = null;
                try
                {
                    Console.WriteLine($"[调试] 即将调用 SendAsync...");
                    response = await _httpClient.SendAsync(
                        request,
                        HttpCompletionOption.ResponseHeadersRead
                    );
                    Console.WriteLine($"[调试] SendAsync 返回，response 为 {(response == null ? "null" : "非 null")}");
                    
                    if (response != null)
                    {
                        Console.WriteLine($"[调试] 响应状态码：{response.StatusCode}");
                        Console.WriteLine($"[调试] 响应是否成功：{response.IsSuccessStatusCode}");
                    }
                }
                catch (HttpRequestException httpEx)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\n[严重错误] HTTP 请求异常：{httpEx.Message}");
                    Console.ResetColor();
                    Console.WriteLine($"[调试] 异常类型：{httpEx.GetType().FullName}");
                    if (httpEx.InnerException != null)
                    {
                        Console.WriteLine($"[调试] 内部异常：{httpEx.InnerException.Message}");
                        Console.WriteLine($"[调试] 内部异常类型：{httpEx.InnerException.GetType().FullName}");
                    }

                    try
                    {
                        // 代理连通性问题下自动降级为直连后重试一次
                        if (_usingProxy)
                        {
                            Console.WriteLine("[调试] 检测到请求失败，尝试禁用代理后重试一次...");
                            _httpClient.Dispose();
                            _usingProxy = false;
                            _proxyDescription = "fallback:none";
                            var retryHandler = new HttpClientHandler
                            {
                                UseProxy = false,
                                Proxy = null,
                                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
                            };
                            _httpClient = new HttpClient(retryHandler)
                            {
                                Timeout = TimeSpan.FromMinutes(5)
                            };

                            _httpClient.DefaultRequestHeaders.Clear();
                            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

                            using var retryContent = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                            using var retryRequest = new HttpRequestMessage
                            {
                                Method = HttpMethod.Post,
                                RequestUri = new Uri(ApiUrl),
                                Content = retryContent
                            };

                            response = await _httpClient.SendAsync(
                                retryRequest,
                                HttpCompletionOption.ResponseHeadersRead
                            );
                            Console.WriteLine($"[调试] 直连重试成功，response 为 {(response == null ? "null" : "非 null")}");
                        }
                        else
                        {
                            // 直连失败时，再尝试系统代理（覆盖公司网络 / PAC 场景）
                            Console.WriteLine("[调试] 直连失败，尝试切换系统代理后重试一次...");
                            var systemProxy = WebRequest.GetSystemWebProxy();
                            if (systemProxy == null)
                            {
                                throw;
                            }

                            _httpClient.Dispose();
                            _usingProxy = true;
                            _proxyDescription = "fallback:system";
                            var retryHandler = new HttpClientHandler
                            {
                                UseProxy = true,
                                Proxy = systemProxy,
                                DefaultProxyCredentials = CredentialCache.DefaultCredentials,
                                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
                            };
                            _httpClient = new HttpClient(retryHandler)
                            {
                                Timeout = TimeSpan.FromMinutes(5)
                            };

                            _httpClient.DefaultRequestHeaders.Clear();
                            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

                            using var retryContent = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                            using var retryRequest = new HttpRequestMessage
                            {
                                Method = HttpMethod.Post,
                                RequestUri = new Uri(ApiUrl),
                                Content = retryContent
                            };

                            response = await _httpClient.SendAsync(
                                retryRequest,
                                HttpCompletionOption.ResponseHeadersRead
                            );
                            Console.WriteLine($"[调试] 系统代理重试成功，response 为 {(response == null ? "null" : "非 null")}");
                        }
                    }
                    catch (Exception retryEx)
                    {
                        Console.WriteLine($"[调试] HttpClient 重试链路失败：{retryEx.GetType().Name} - {retryEx.Message}");
                        return await CallHttpWebRequestFallbackAsync(jsonBody, apiKey);
                    }
                }
                catch (TaskCanceledException cancelEx)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\n[严重错误] 请求被取消或超时：{cancelEx.Message}");
                    Console.ResetColor();
                    Console.WriteLine($"[调试] 是否因超时取消：{cancelEx.InnerException is TimeoutException}");
                    throw;
                }
                catch (Exception sendEx)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\n[严重错误] 发送请求时出错：{sendEx.Message}");
                    Console.ResetColor();
                    Console.WriteLine($"[调试] 异常类型：{sendEx.GetType().FullName}");
                    if (sendEx.InnerException != null)
                    {
                        Console.WriteLine($"[调试] 内部异常：{sendEx.InnerException.Message}");
                        Console.WriteLine($"[调试] 内部异常类型：{sendEx.InnerException.GetType().FullName}");
                    }
                    throw;
                }
                
                // 处理非 200 状态码
                if (response == null)
                {
                    throw new HttpRequestException("API 请求失败：响应为 null");
                }
                
                if (!response.IsSuccessStatusCode)
                {
                    string errorBody;
                    try
                    {
                        errorBody = await response.Content.ReadAsStringAsync();
                    }
                    catch (Exception readEx)
                    {
                        errorBody = $"无法读取错误内容：{readEx.Message}";
                    }
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\n[严重错误] API 请求失败 ({response.StatusCode}): {errorBody}");
                    Console.ResetColor();
                    throw new HttpRequestException($"API 请求失败 ({response.StatusCode}): {errorBody}");
                }

                // 读取流式响应
                using var stream = await response.Content.ReadAsStreamAsync();
                var buffer = new byte[4096];
                var sbLine = new StringBuilder();
                int bytesRead;

                Console.WriteLine();

                while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    string textChunk = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    
                    foreach (char c in textChunk)
                    {
                        if (c == '\n')
                        {
                            string line = sbLine.ToString().Trim();
                            sbLine.Clear();

                            if (string.IsNullOrEmpty(line)) continue;

                            if (line.StartsWith("data: "))
                            {
                                string dataJson = line.Substring(6).Trim();

                                if (dataJson == "[DONE]")
                                {
                                    goto EndStream;
                                }

                                try
                                {
                                    var jObject = JObject.Parse(dataJson);
                                    var root = jObject;

                                    var errorToken = root["error"];
                                    if (errorToken != null && errorToken["message"] != null)
                                    {
                                        string errMsg = errorToken["message"]!.ToString();
                                        throw new Exception($"流式传输中发生错误：{errMsg}");
                                    }

                                    if (root["choices"] is JArray choices && choices.Count > 0)
                                    {
                                        var choiceToken = choices[0];
                                        var contentToken = choiceToken?["delta"]?["content"];
                                        if (contentToken != null)
                                        {
                                            string chunk = contentToken.ToString();
                                            if (!string.IsNullOrEmpty(chunk))
                                            {
                                                fullResponse.Append(chunk);
                                                Console.Write(chunk);
                                                Console.Out.Flush();
                                                hasOutputStarted = true;
                                            }
                                        }
                                    }
                                }
                                catch (JsonException)
                                {
                                    // 忽略解析错误
                                }
                            }
                        }
                        else
                        {
                            sbLine.Append(c);
                        }
                    }
                }

EndStream:
                if (!hasOutputStarted)
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine("\n[提示] 模型未返回任何文本内容。");
                    Console.ResetColor();
                }

                return fullResponse.ToString();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n\n[严重错误] 调用失败：{ex.Message}");
                Console.ResetColor();
                throw;
            }
        }

        private async Task<BridgeResponse> TryCallLocalBridgeAsync(string jsonBody, string apiKey)
        {
            try
            {
                await EnsureBridgeProcessAsync();

                using var client = new NamedPipeClientStream(
                    ".",
                    BridgePipeName,
                    PipeDirection.InOut,
                    PipeOptions.Asynchronous);

                await Task.Run(() => client.Connect(10000));

                using var writer = new StreamWriter(client, new UTF8Encoding(false), 4096, leaveOpen: true)
                {
                    AutoFlush = true
                };
                using var reader = new StreamReader(client, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);

                var req = new BridgeRequest
                {
                    ApiUrl = ApiUrl,
                    ApiKey = apiKey,
                    JsonBody = jsonBody
                };

                string reqJson = JsonConvert.SerializeObject(req);
                await writer.WriteLineAsync(reqJson);

                var readTask = reader.ReadLineAsync();
                var completed = await Task.WhenAny(readTask, Task.Delay(15000));
                if (completed != readTask)
                {
                    return new BridgeResponse
                    {
                        Success = false,
                        Error = "桥接响应超时（15秒）"
                    };
                }

                var respJson = await readTask;
                if (string.IsNullOrWhiteSpace(respJson))
                {
                    return new BridgeResponse
                    {
                        Success = false,
                        Error = "桥接返回空响应"
                    };
                }

                var resp = JsonConvert.DeserializeObject<BridgeResponse>(respJson);
                if (resp == null)
                {
                    return new BridgeResponse
                    {
                        Success = false,
                        Error = "桥接响应反序列化失败"
                    };
                }
                return resp;
            }
            catch (TimeoutException)
            {
                return new BridgeResponse
                {
                    Success = false,
                    Error = $"无法连接本地桥接（Pipe: {BridgePipeName}）。请确认 sw_ai.exe 或 llm_bridge.exe 已启动且未被安全策略拦截。"
                };
            }
            catch (Exception ex)
            {
                return new BridgeResponse
                {
                    Success = false,
                    Error = $"桥接调用异常：{ex.GetType().Name} - {ex.Message}"
                };
            }
        }

        private async Task EnsureBridgeProcessAsync()
        {
            if (!ShouldPreferBridge())
            {
                return;
            }

            if (_bridgeProcess != null && !_bridgeProcess.HasExited)
            {
                return;
            }

            if (_bridgeLaunchAttempted)
            {
                return;
            }

            _bridgeLaunchAttempted = true;
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                var candidates = BuildBridgeExecutableCandidates(baseDir).ToList();

                string? bridgePath = candidates.FirstOrDefault(File.Exists);
                if (string.IsNullOrWhiteSpace(bridgePath))
                {
                    Console.WriteLine("[调试] 未找到桥接可执行文件（llm_bridge.exe/sw_ai.exe），将继续使用当前进程网络。");
                    Console.WriteLine($"[调试] 桥接搜索基目录：{baseDir}");
                    return;
                }

                var startInfo = new ProcessStartInfo
                {
                    FileName = bridgePath,
                    WorkingDirectory = Path.GetDirectoryName(bridgePath) ?? baseDir,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                _bridgeProcess = Process.Start(startInfo);
                Console.WriteLine($"[调试] 已尝试启动本地桥接：{bridgePath}");
                await Task.Delay(1200);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[调试] 自动启动桥接失败：{ex.GetType().Name} - {ex.Message}");
            }
        }

        private static IEnumerable<string> BuildBridgeExecutableCandidates(string baseDir)
        {
            var candidateNames = new[] { "llm_bridge.exe", "sw_ai.exe" };
            var searchDirs = new List<string>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddDir(string? dir)
            {
                if (string.IsNullOrWhiteSpace(dir))
                {
                    return;
                }

                try
                {
                    string full = Path.GetFullPath(dir);
                    if (visited.Add(full))
                    {
                        searchDirs.Add(full);
                    }
                }
                catch
                {
                    // 忽略无效路径
                }
            }

            var dirInfo = new DirectoryInfo(baseDir);
            for (int i = 0; i < 8 && dirInfo != null; i++)
            {
                AddDir(Path.Combine(dirInfo.FullName, "ctools", "bin", "Debug", "net48"));
                AddDir(Path.Combine(dirInfo.FullName, "ctools", "bin", "Release", "net48"));
                AddDir(Path.Combine(dirInfo.FullName, "ctools", "bin", "Debug", "net9.0-windows"));
                AddDir(Path.Combine(dirInfo.FullName, "ctools", "bin", "Release", "net9.0-windows"));
                dirInfo = dirInfo.Parent;
            }

            // 再回退到宿主目录，避免优先命中被复制到插件目录中的旧版本 sw_ai.exe
            AddDir(baseDir);
            AddDir(Environment.CurrentDirectory);

            try
            {
                AddDir(Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName));
            }
            catch
            {
                // 某些宿主环境下 MainModule 可能受限
            }

            dirInfo = new DirectoryInfo(baseDir);
            for (int i = 0; i < 8 && dirInfo != null; i++)
            {
                AddDir(dirInfo.FullName);
                dirInfo = dirInfo.Parent;
            }

            foreach (var dir in searchDirs)
            {
                foreach (var fileName in candidateNames)
                {
                    yield return Path.Combine(dir, fileName);
                }
            }
        }

        private static bool ShouldPreferBridge()
        {
            var processName = Process.GetCurrentProcess().ProcessName ?? "";
            if (processName.IndexOf("SLDWORKS", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }

            // 可通过环境变量手动启用桥接，便于调试
            var env = Environment.GetEnvironmentVariable("LLM_USE_LOCAL_BRIDGE");
            return string.Equals(env, "1", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(env, "true", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// HttpClient 在宿主环境不可用时，使用 HttpWebRequest 兜底（非流式）
        /// </summary>
        private async Task<string> CallHttpWebRequestFallbackAsync(string jsonBody, string apiKey)
        {
            Console.WriteLine("[调试] 启用 HttpWebRequest 兜底请求（非流式）...");

            string fallbackBody = jsonBody;
            try
            {
                var bodyObj = JObject.Parse(jsonBody);
                bodyObj["stream"] = false;
                fallbackBody = bodyObj.ToString(Formatting.None);
            }
            catch
            {
                // 若请求体无法解析，继续使用原始请求体
            }

            var errors = new List<string>();

            foreach (bool useProxy in new[] { true, false })
            {
                try
                {
                    string mode = useProxy ? "system-proxy" : "direct";
                    Console.WriteLine($"[调试] HttpWebRequest 兜底尝试模式：{mode}");
                    return await SendHttpWebRequestOnceAsync(fallbackBody, apiKey, useProxy);
                }
                catch (WebException webEx)
                {
                    string detail = await BuildWebExceptionDetailAsync(webEx);
                    errors.Add($"{(useProxy ? "system-proxy" : "direct")} => {detail}");
                    Console.WriteLine($"[调试] HttpWebRequest {(useProxy ? "代理" : "直连")}尝试失败：{detail}");
                }
                catch (Exception ex)
                {
                    string detail = $"{ex.GetType().Name}: {ex.Message}";
                    errors.Add($"{(useProxy ? "system-proxy" : "direct")} => {detail}");
                    Console.WriteLine($"[调试] HttpWebRequest {(useProxy ? "代理" : "直连")}尝试失败：{detail}");
                }
            }

            throw new HttpRequestException($"HttpWebRequest 兜底失败：{string.Join(" | ", errors)}");
        }

        private async Task<string> SendHttpWebRequestOnceAsync(string requestBody, string apiKey, bool useSystemProxy)
        {
            var request = (HttpWebRequest)WebRequest.Create(ApiUrl);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Accept = "application/json";
            request.Timeout = 300000;
            request.ReadWriteTimeout = 300000;
            request.Proxy = useSystemProxy ? WebRequest.GetSystemWebProxy() : null;
            if (request.Proxy != null)
            {
                request.Proxy.Credentials = CredentialCache.DefaultCredentials;
            }
            request.Headers[HttpRequestHeader.Authorization] = $"Bearer {apiKey}";

            var payload = Encoding.UTF8.GetBytes(requestBody);
            using (var reqStream = await request.GetRequestStreamAsync())
            {
                await reqStream.WriteAsync(payload, 0, payload.Length);
            }

            using var response = (HttpWebResponse)await request.GetResponseAsync();
            using var respStream = response.GetResponseStream();
            using var reader = new StreamReader(respStream ?? Stream.Null, Encoding.UTF8);
            string responseText = await reader.ReadToEndAsync();

            if (response.StatusCode != HttpStatusCode.OK)
            {
                throw new HttpRequestException($"API 请求失败 ({response.StatusCode}): {responseText}");
            }

            var root = JObject.Parse(responseText);
            var errorToken = root["error"];
            if (errorToken != null)
            {
                throw new HttpRequestException($"API 错误：{errorToken}");
            }

            string content = root["choices"]?[0]?["message"]?["content"]?.ToString() ?? "";
            if (string.IsNullOrWhiteSpace(content))
            {
                Console.WriteLine("[提示] 兜底请求成功，但未返回文本内容。");
            }
            return content;
        }

        private async Task<string> BuildWebExceptionDetailAsync(WebException webEx)
        {
            var parts = new List<string>
            {
                $"Status={webEx.Status}",
                webEx.Message
            };

            if (webEx.InnerException != null)
            {
                parts.Add($"Inner={webEx.InnerException.GetType().Name}:{webEx.InnerException.Message}");
            }

            if (webEx.InnerException is SocketException socketEx)
            {
                parts.Add($"SocketError={socketEx.SocketErrorCode}({(int)socketEx.SocketErrorCode})");
            }

            if (webEx.Response is HttpWebResponse errResp)
            {
                using var errStream = errResp.GetResponseStream();
                using var errReader = new StreamReader(errStream ?? Stream.Null, Encoding.UTF8);
                string errBody = await errReader.ReadToEndAsync();
                parts.Add($"HTTP={(int)errResp.StatusCode} {errResp.StatusCode}");
                if (!string.IsNullOrWhiteSpace(errBody))
                {
                    parts.Add($"Body={errBody}");
                }
            }

            return string.Join("; ", parts);
        }

        /// <summary>
        /// 执行流式调用（纯文本 LLM）
        /// </summary>
        public async Task<string> CallStreamingAsync(List<ChatMessage> messages, string apiKey, string model)
        {
            var requestBody = new
            {
                model = model,
                stream = true,
                messages = messages.Select(m => new
                {
                    role = m.Role,
                    content = m.Content
                })
            };
            
            return await CallStreamingCoreAsync(requestBody, apiKey);
        }

        /// <summary>
        /// 执行流式调用（带图像 VLM）
        /// </summary>
        public async Task<string> CallStreamingWithImageAsync(List<ChatMessage> messages, string userPrompt, string? imagePath, string apiKey, string? model = null)
        {
            model ??= DefaultModel;
            
            // 如果 imagePath 为 null 或空，直接返回
            if (string.IsNullOrEmpty(imagePath))
            {
                throw new ArgumentException("imagePath 不能为空", nameof(imagePath));
            }
            
            // 准备图片数据
            string ext = Path.GetExtension(imagePath).ToLower();
            string mimeType = ext switch
            {
                ".png" => "image/png",
                ".jpg" => "image/jpeg",
                ".jpeg" => "image/jpeg",
                ".webp" => "image/webp",
                _ => "image/png"
            };

            byte[] imageBytes;
            using (var fs = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true))
            {
                var ms = new MemoryStream();
                await fs.CopyToAsync(ms);
                imageBytes = ms.ToArray();
            }
            string base64Image = Convert.ToBase64String(imageBytes);
            string dataUrl = $"data:{mimeType};base64,{base64Image}";

            // 构建请求体（OpenAI 兼容格式，带图像）
            var messagesList = new List<object>();
            foreach (var m in messages)
            {
                messagesList.Add(new { role = m.Role, content = m.Content });
            }
            messagesList.Add(new
            {
                role = "user",
                content = new object[]
                {
                    new { type = "text", text = userPrompt },
                    new { type = "image_url", image_url = new { url = dataUrl } }
                }
            });

            var requestBody = new
            {
                model = model,
                stream = true,
                messages = messagesList.ToArray()
            };
            
            return await CallStreamingCoreAsync(requestBody, apiKey);
        }

        /// <summary>
        /// 执行流式调用（带 Tool）
        /// </summary>
        public async Task<(string Response, List<ToolCall>? ToolCalls)> CallStreamingWithToolsAsync(
            List<ChatMessage> messages, 
            string userPrompt, 
            List<ToolDefinition> tools, 
            string apiKey, 
            string model,
            bool forceToolCall = false)
        {
            // 添加用户消息
            var messagesWithUser = new List<object>();
            foreach (var m in messages)
            {
                messagesWithUser.Add(new { role = m.Role, content = m.Content });
            }
            messagesWithUser.Add(new { role = "user", content = userPrompt });

            // 构建请求体
            object requestBody;
            if (forceToolCall && tools.Count > 0)
            {
                // 强制要求工具调用时，使用 dynamic 对象添加 tool_choice
                var reqDict = new Dictionary<string, object>
                {
                    ["model"] = model,
                    ["stream"] = false,
                    ["messages"] = messagesWithUser.ToArray(),
                    ["tools"] = tools.Select(t => 
                    {
                        var parametersObj = t.Function.Parameters;
                        if (parametersObj is FunctionParameters fp && 
                            fp.Properties != null && fp.Properties.Count == 0)
                        {
                            parametersObj = new { type = "object", properties = new object() };
                        }
                        
                        return new
                        {
                            type = t.Type,
                            function = new
                            {
                                name = t.Function.Name,
                                description = t.Function.Description,
                                parameters = parametersObj
                            }
                        };
                    }).ToArray(),
                    ["tool_choice"] = "required"  // 强制要求工具调用
                };
                requestBody = reqDict;
            }
            else
            {
                // 正常情况下的请求体
                requestBody = new
                {
                    model = model,
                    stream = false,
                    messages = messagesWithUser.ToArray(),
                    tools = tools.Select(t => 
                    {
                        var parametersObj = t.Function.Parameters;
                        if (parametersObj is FunctionParameters fp && 
                            fp.Properties != null && fp.Properties.Count == 0)
                        {
                            parametersObj = new { type = "object", properties = new object() };
                        }
                        
                        return new
                        {
                            type = t.Type,
                            function = new
                            {
                                name = t.Function.Name,
                                description = t.Function.Description,
                                parameters = parametersObj
                            }
                        };
                    }).ToArray()
                };
            }
            
            // 【调试断点】在这里检查请求体内容
            string jsonBody = JsonConvert.SerializeObject(requestBody);
            System.Diagnostics.Debug.WriteLine($"\n【调试】Tool 调用请求 JSON:");
            System.Diagnostics.Debug.WriteLine(jsonBody);
            System.IO.File.WriteAllText("debug_tool_request.json", jsonBody, Encoding.UTF8);
            System.Diagnostics.Debug.WriteLine($"[调试] 请求 JSON 已保存到 debug_tool_request.json");
            
            // 打印关键调试信息
            Console.WriteLine($"\n[调试] 工具调用配置:");
            Console.WriteLine($"  - 强制工具调用: {forceToolCall}");
            Console.WriteLine($"  - 可用工具数量: {tools.Count}");
            if (forceToolCall)
            {
                Console.WriteLine($"  - tool_choice: required");
            }
            
            using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            
            _httpClient!.DefaultRequestHeaders.Clear();
            _httpClient!.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
            
            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri(ApiUrl),
                Content = content
            };
            
            Console.WriteLine($"\n[调试] 发送 Tool 调用请求到：{ApiUrl}");
            Console.WriteLine($"[调试] 工具数量：{tools.Count}");
            Console.WriteLine($"[调试] 消息数量：{messagesWithUser.Count}");
            Console.WriteLine($"[调试] 是否强制工具调用：{forceToolCall}");
            
            // 【调试断点】发送请求前
            var httpRequest = new HttpRequestMessage
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri(ApiUrl),
                Content = content
            };
            
            var response = await _httpClient.SendAsync(httpRequest);
            response.EnsureSuccessStatusCode();
            
            var responseJson = await response.Content.ReadAsStringAsync();
            var result = JObject.Parse(responseJson);
            
            var choices = result["choices"] as JArray;
            if (choices == null || choices.Count == 0)
            {
                return ("", null);
            }
            
            var message = choices[0]["message"];
            if (message == null)
            {
                return ("", null);
            }
            var toolCalls = message["tool_calls"] as JArray;
            
            if (toolCalls != null && toolCalls.Count > 0)
            {
                var calls = toolCalls.ToObject<List<ToolCall>>()!;
                Console.WriteLine($"\n[调试] 检测到 {calls.Count} 个 Tool 调用");
                return ("", calls);
            }
            
            var contentToken = message["content"];
            string responseText = contentToken?.ToString() ?? "";
            
            // 如果强制要求工具调用但没有返回工具调用，记录警告
            if (forceToolCall && string.IsNullOrEmpty(responseText) == false)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"\n[警告] 强制工具调用模式下，LLM 返回了文本而非工具调用: {responseText}");
                Console.ResetColor();
            }
            
            return (responseText, null);
        }

        /// <summary>
        /// 保存长期记忆日志
        /// </summary>
        private void SaveLongTermMemoryLog(string content)
        {
            try
            {
                
                var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
                File.AppendAllText(_logFilePath, $"[{timestamp}] {content}\n", Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"保存日志失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 清空对话历史
        /// </summary>
        public void ClearHistory()
        {
            try
            {
                if (File.Exists(_shotMemoryFile))
                {
                    File.Delete(_shotMemoryFile);
                    Console.WriteLine("对话历史已清空");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"清空历史失败：{ex.Message}");
            }
        }
    }

    /// <summary>
    /// Tool 定义（用于 Function Calling）
    /// </summary>
    public class ToolDefinition
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "function";
        
        [JsonProperty("function")]
        public FunctionDefinition Function { get; set; }
    }
    
    /// <summary>
    /// 函数定义
    /// </summary>
    public class FunctionDefinition
    {
        [JsonProperty("name")]
        public string Name { get; set; }
        
        [JsonProperty("description")]
        public string Description { get; set; }
        
        [JsonProperty("parameters")]
        public object Parameters { get; set; }
    }
    
    /// <summary>
    /// 函数参数定义
    /// </summary>
    public class FunctionParameters
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "object";
        
        [JsonProperty("properties")]
        public Dictionary<string, PropertyDefinition> Properties { get; set; } = new Dictionary<string, PropertyDefinition>();
        
        [JsonProperty("required")]
        public List<string> Required { get; set; } = new List<string>();
    }
    
    /// <summary>
    /// 属性定义
    /// </summary>
    public class PropertyDefinition
    {
        [JsonProperty("type")]
        public string Type { get; set; } = "string";
        
        [JsonProperty("description")]
        public string Description { get; set; }
    }
    
    /// <summary>
    /// Tool 调用响应
    /// </summary>
    public class ToolCall
    {
        [JsonProperty("id")]
        public string Id { get; set; }
        
        [JsonProperty("type")]
        public string Type { get; set; } = "function";
        
        [JsonProperty("function")]
        public FunctionCall Function { get; set; }
    }
    
    /// <summary>
    /// 函数调用
    /// </summary>
    public class FunctionCall
    {
        [JsonProperty("name")]
        public string Name { get; set; }
        
        [JsonProperty("arguments")]
        public string Arguments { get; set; }
    }
    
    /// <summary>
    /// 聊天消息类
    /// </summary>
    public class ChatMessage
    {
        [JsonProperty("role")]
        public string Role { get; set; } = "";
        
        [JsonProperty("content")]
        public string Content { get; set; } = "";
        
        // Tool 调用相关字段
        [JsonProperty("tool_call_id", NullValueHandling = NullValueHandling.Ignore)]
        public string? ToolCallId { get; set; }
        
        [JsonProperty("tool_calls", NullValueHandling = NullValueHandling.Ignore)]
        public List<ToolCall>? ToolCalls { get; set; }
    }

    /// <summary>
    /// 本地桥接请求
    /// </summary>
    public class BridgeRequest
    {
        [JsonProperty("api_url")]
        public string ApiUrl { get; set; } = "";

        [JsonProperty("api_key")]
        public string ApiKey { get; set; } = "";

        [JsonProperty("json_body")]
        public string JsonBody { get; set; } = "";
    }

    /// <summary>
    /// 本地桥接响应
    /// </summary>
    public class BridgeResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("content")]
        public string? Content { get; set; }

        [JsonProperty("error")]
        public string? Error { get; set; }
    }
}
