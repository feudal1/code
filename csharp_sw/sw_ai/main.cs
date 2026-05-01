using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace tools
{
    internal class Program
    {
        private const int MaxLogContextLines = 60;
        private const double DirectAutoRunThreshold = 0.75;
        private const double ConfirmRunThreshold = 0.52;
        private const double CandidateDisplayThreshold = 0.30;
        private static readonly TimeSpan CommandCacheTtl = TimeSpan.FromMinutes(10);
        private const int EquationTaskMaxAttempts = 4;
        private static readonly List<string> CurrentTurnActionTrace = new List<string>();

        [STAThread]
        private static async Task Main()
        {
            Console.WriteLine("sw_ai (LLM 模式)");
            Console.WriteLine($"日志目录: {CommandLogStore.GetLogDirectory()}");
            Console.WriteLine("纯文本对话模式：直接说需求即可（例如：创建新机型 名称叫侧推600x1200x450）。");
            Console.WriteLine("退出请直接输入：退出\n");

            var llmService = new VlmService();
            var swBridgeClient = new SwCommandBridgeClient();
            var historyStore = new ChatHistoryStore();
            var chatHistory = historyStore.Load();
            bool autoRunEnabled = true;
            var cachedCommands = new List<BridgeCommandInfo>();
            DateTime cachedAt = DateTime.MinValue;

            try
            {
                var (ok, message) = swBridgeClient.Ping();
                Console.WriteLine(ok
                    ? "SW 桥接状态: 已连接"
                    : $"SW 桥接状态: 未连接 ({message})");
            }
            catch
            {
                Console.WriteLine("SW 桥接状态: 未连接（请先确保 SolidWorks 插件已加载）");
            }
            Console.WriteLine();
            Console.WriteLine($"已加载对话历史 {chatHistory.Count} 条，历史文件: {historyStore.GetHistoryFilePath()}");
            Console.WriteLine();

            while (true)
            {
                Console.Write("你> ");
                string? inputRaw = Console.ReadLine()?.Trim();
                if (string.IsNullOrWhiteSpace(inputRaw))
                {
                    continue;
                }
                string input = inputRaw!;
                ResetTurnActionTrace();

                if (string.Equals(input, "退出", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(input, "结束", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(input, "bye", StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                if (IsAskForHelp(input))
                {
                    ShowHelp();
                    continue;
                }

                if (IsAskClearHistory(input))
                {
                    chatHistory.Clear();
                    historyStore.Clear();
                    Console.WriteLine("已清空对话历史。");
                    continue;
                }

                if (IsAskShowHistory(input))
                {
                    ShowRecentChatHistory(chatHistory);
                    continue;
                }

                if (TryExtractAutoToggle(input, out bool enableAuto))
                {
                    autoRunEnabled = enableAuto;
                    Console.WriteLine(autoRunEnabled ? "自动执行已开启。" : "自动执行已关闭。");
                    continue;
                }

                if (IsAskForCommandList(input))
                {
                    try
                    {
                        PrintActionCall("list_actions");
                        var (okActions, actionMessage, actions) = swBridgeClient.ListActions();
                        PrintActionResult("list_actions", okActions, actionMessage);
                        if (!okActions)
                        {
                            Console.WriteLine($"读取动作列表失败: {actionMessage}");
                            continue;
                        }

                        bool forceRefresh = IsAskForRefreshCommands(input);
                        PrintActionCall("list_commands", forceRefresh ? "forceRefresh=true" : "forceRefresh=false");
                        bool ok = TryGetCommandsWithCache(
                            swBridgeClient,
                            ref cachedCommands,
                            ref cachedAt,
                            forceRefresh,
                            out var commands,
                            out string error);
                        PrintActionResult("list_commands", ok, ok ? $"命令数量 {commands.Count}" : error);
                        if (!ok)
                        {
                            Console.WriteLine($"读取 SW 命令失败: {error}");
                            continue;
                        }

                        await ReplyFromObservationAsync(
                            llmService,
                            chatHistory,
                            historyStore,
                            input,
                            $"查询动作列表成功，通用动作 {actions.Count} 条，命令 {commands.Count} 条，数据来源：{(forceRefresh ? "刷新" : "缓存")}。",
                            BuildActionListText(actions) + Environment.NewLine + BuildCommandListText(commands));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"读取 SW 命令失败: {ex.Message}");
                    }
                    continue;
                }

                if (TryExtractCreateVariantName(input, out string variantNameFromNatural))
                {
                    try
                    {
                        PrintActionCall("create_variant", $"variantName={variantNameFromNatural}");
                        var (ok, message) = swBridgeClient.CreateVariant(variantNameFromNatural);
                        PrintActionResult("create_variant", ok, message);
                        await ReplyFromObservationAsync(
                            llmService,
                            chatHistory,
                            historyStore,
                            input,
                            ok ? $"动作成功：{message}" : $"动作失败：{message}",
                            "");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"创建机型失败: {ex.Message}");
                    }
                    continue;
                }

                if (IsCaptureAssemblyEquationIntent(input))
                {
                    try
                    {
                        PrintActionCall("capture_assembly_equations");
                        var (ok, message) = swBridgeClient.CaptureAssemblyEquations();
                        PrintActionResult("capture_assembly_equations", ok, message);
                        await ReplyFromObservationAsync(
                            llmService,
                            chatHistory,
                            historyStore,
                            input,
                            ok ? $"动作成功：{message}" : $"动作失败：{message}",
                            "");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"采集方程式失败: {ex.Message}");
                    }
                    continue;
                }

                if (IsAskEquationList(input))
                {
                    try
                    {
                        string keyword = ExtractEquationKeyword(input);
                        PrintActionCall("list_current_variant_equations", string.IsNullOrWhiteSpace(keyword) ? "" : $"keyword={keyword}");
                        var (ok, message, equations) = swBridgeClient.ListCurrentVariantEquations(keyword);
                        PrintActionResult("list_current_variant_equations", ok, message);
                        await ReplyFromObservationAsync(
                            llmService,
                            chatHistory,
                            historyStore,
                            input,
                            ok ? $"动作成功：{message}" : $"动作失败：{message}",
                            ok ? BuildEquationListText(equations) : "");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"读取方程式失败: {ex.Message}");
                    }
                    continue;
                }

                var relativeBatchTasks = await TryExtractRelativeBatchEquationTasksHybridAsync(input, llmService, swBridgeClient);
                if (relativeBatchTasks.Count > 0)
                {
                    try
                    {
                        var messages = new List<string>();
                        int successCount = 0;
                        foreach (var task in relativeBatchTasks)
                        {
                            var result = ExecuteRelativeEquationUpdateTaskLoop(
                                input,
                                task.Keyword,
                                task.Delta,
                                swBridgeClient);
                            messages.Add(result.Message);
                            if (result.Success)
                            {
                                successCount++;
                            }
                        }

                        bool allOk = successCount == relativeBatchTasks.Count;
                        string observation = allOk
                            ? $"动作成功：批量相对更新完成（成功 {successCount}/{relativeBatchTasks.Count}）"
                            : $"动作失败：批量相对更新未完全成功（成功 {successCount}/{relativeBatchTasks.Count}）";
                        string extra = string.Join("；", messages);
                        await ReplyFromObservationAsync(
                            llmService,
                            chatHistory,
                            historyStore,
                            input,
                            observation,
                            extra);
                        continue;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"批量修改方程式失败: {ex.Message}");
                        continue;
                    }
                }

                var absoluteBatchTasks = await TryExtractAbsoluteBatchEquationTasksHybridAsync(input, llmService, swBridgeClient);
                if (absoluteBatchTasks.Count > 0)
                {
                    try
                    {
                        var messages = new List<string>();
                        int successCount = 0;
                        foreach (var task in absoluteBatchTasks)
                        {
                            var result = ExecuteEquationUpdateTaskLoopCore(
                                input,
                                task.Keyword,
                                task.TargetValue,
                                swBridgeClient);
                            messages.Add(result.Message);
                            if (result.Success)
                            {
                                successCount++;
                            }
                        }

                        bool allOk = successCount == absoluteBatchTasks.Count;
                        string observation = allOk
                            ? $"动作成功：批量绝对更新完成（成功 {successCount}/{absoluteBatchTasks.Count}）"
                            : $"动作失败：批量绝对更新未完全成功（成功 {successCount}/{absoluteBatchTasks.Count}）";
                        string extra = string.Join("；", messages);
                        await ReplyFromObservationAsync(
                            llmService,
                            chatHistory,
                            historyStore,
                            input,
                            observation,
                            extra);
                        continue;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"批量设置方程式失败: {ex.Message}");
                        continue;
                    }
                }

                var equationIntent = await TryExtractEquationUpdateIntentHybridAsync(input, llmService, swBridgeClient);
                if (equationIntent.IsMatch)
                {
                    try
                    {
                        await ExecuteEquationUpdateTaskLoopAsync(
                            input,
                            equationIntent.EquationKeyword,
                            equationIntent.TargetValue,
                            swBridgeClient,
                            llmService,
                            chatHistory,
                            historyStore);
                        continue;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"修改方程式失败: {ex.Message}");
                        continue;
                    }
                }

                bool gotCommands = false;
                List<BridgeCommandInfo> commandsForAutoRun = new List<BridgeCommandInfo>();
                if (autoRunEnabled)
                {
                    gotCommands = TryGetCommandsWithCache(
                        swBridgeClient,
                        ref cachedCommands,
                        ref cachedAt,
                        forceRefresh: false,
                        out commandsForAutoRun,
                        out _);

                    bool handled = gotCommands &&
                                   await TryAutoExecuteSwCommandWithConfirmAsync(
                                       input,
                                       swBridgeClient,
                                       commandsForAutoRun,
                                       llmService,
                                       chatHistory,
                                       historyStore);
                    if (handled)
                    {
                        continue;
                    }
                }

                // 防止“口头说已执行”：如果看起来像执行意图但未触发动作，先明确告知未执行
                if (LooksLikeExecutionIntent(input))
                {
                    string extraContext = "";
                    if (gotCommands && commandsForAutoRun.Count > 0)
                    {
                        var match = SwCommandMatcher.Match(input, commandsForAutoRun);
                        if (match.BestCommand != null && match.BestScore >= CandidateDisplayThreshold)
                        {
                            extraContext = $"最接近的命令候选是：{match.BestCommand.Name} / {match.BestCommand.LocalizedName}，匹配分 {match.BestScore:F2}。";
                        }
                    }

                    await ReplyFromObservationAsync(
                        llmService,
                        chatHistory,
                        historyStore,
                        input,
                        "尚未执行任何插件动作（没有命中可执行入口）。",
                        extraContext);
                    continue;
                }

                string logContext = CommandLogStore.GetRecentLogLines(MaxLogContextLines);
                string prompt = BuildChatPrompt(input, logContext);

                try
                {
                    Console.WriteLine("AI> ");
                    string answer = await llmService.CallWithHistoryAsync(
                        chatHistory,
                        prompt,
                        systemPrompt: "你是 CAD 智能助手。像同事聊天一样回答，简短直接；需要执行时明确说你已做了什么。");
                    Console.WriteLine(answer);
                    Console.WriteLine();
                    AppendHistory(chatHistory, historyStore, "user", input);
                    AppendHistory(chatHistory, historyStore, "assistant", answer);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"调用 LLM 失败: {ex.Message}");
                }
            }
        }

        private static string BuildChatPrompt(string userInput, string logContext)
        {
            return
$@"你是 CAD 智能助手，你既能正常对话，也能在需要时建议可执行的 CAD 操作。
下面是最近的命令执行日志（JSONL，每行一个 JSON 对象）：
{logContext}

请像同事聊天一样回复：
1) 语气自然，先给结论；
2) 尽量短句，少用分点和术语；
3) 涉及操作时直接说“我来做/你现在做什么”。
4) 严禁虚构执行结果：只有明确收到“系统观察=动作成功”时，才允许说“已执行/已修改”。

用户问题：{userInput}";
        }

        private static void ShowHelp()
        {
            Console.WriteLine("\n你可以直接自然语言输入，例如：");
            Console.WriteLine("  - 创建新机型 名称叫侧推600x1200x450");
            Console.WriteLine("  - 从装配体采集方程式");
            Console.WriteLine("  - 当前有哪些方程式 / 导杆相关方程式有哪些");
            Console.WriteLine("  - 帮我导出装配体钣金展开");
            Console.WriteLine("  - 把当前工程图转dwg");
            Console.WriteLine("  - 列一下你能执行的命令");
            Console.WriteLine("  - 关闭自动执行 / 开启自动执行");
            Console.WriteLine("  - 查看对话历史 / 清空对话历史");
            Console.WriteLine($"自动执行策略：>= {DirectAutoRunThreshold:F2} 直接执行，>= {ConfirmRunThreshold:F2} 先确认");
            Console.WriteLine("退出请输入：退出\n");
        }

        private static async Task<bool> TryAutoExecuteSwCommandWithConfirmAsync(
            string userInput,
            SwCommandBridgeClient swBridgeClient,
            List<BridgeCommandInfo> commands,
            VlmService llmService,
            List<ChatTurn> chatHistory,
            ChatHistoryStore historyStore)
        {
            try
            {
                if (commands == null || commands.Count == 0)
                {
                    return false;
                }

                var match = SwCommandMatcher.Match(userInput, commands);
                if (match.BestCommand == null || match.BestScore < ConfirmRunThreshold)
                {
                    return false;
                }

                var best = match.BestCommand;
                if (match.BestScore >= DirectAutoRunThreshold)
                {
                    PrintActionCall("execute_command", $"command={best.Name}, source=auto, score={match.BestScore:F2}");
                    var (runOkDirect, runMsgDirect) = swBridgeClient.ExecuteCommand(best.Name);
                    PrintActionResult("execute_command", runOkDirect, runMsgDirect);
                    string observationDirect = runOkDirect
                        ? $"已自动执行命令 {best.Name} / {best.LocalizedName}，匹配分 {match.BestScore:F2}。执行结果：{runMsgDirect}"
                        : $"尝试自动执行命令 {best.Name} / {best.LocalizedName}，匹配分 {match.BestScore:F2}。执行失败：{runMsgDirect}";
                    await ReplyFromObservationAsync(llmService, chatHistory, historyStore, userInput, observationDirect, "");
                    return true;
                }

                Console.WriteLine($"匹配到命令: {best.Name} / {best.LocalizedName} (匹配分 {match.BestScore:F2})");
                if (match.Candidates.Count > 1)
                {
                    Console.WriteLine("候选命令:");
                    foreach (var candidate in match.Candidates)
                    {
                        Console.WriteLine($"  - {candidate.Name} / {candidate.LocalizedName}");
                    }
                }
                Console.Write("是否执行该命令? (y/n): ");
                string confirm = (Console.ReadLine() ?? string.Empty).Trim().ToLowerInvariant();
                if (confirm != "y" && confirm != "yes")
                {
                    await ReplyFromObservationAsync(
                        llmService,
                        chatHistory,
                        historyStore,
                        userInput,
                        $"已取消执行命令 {best.Name} / {best.LocalizedName}。",
                        "");
                    return true;
                }

                PrintActionCall("execute_command", $"command={best.Name}, source=confirm, score={match.BestScore:F2}");
                var (runOk, runMsg) = swBridgeClient.ExecuteCommand(best.Name);
                PrintActionResult("execute_command", runOk, runMsg);
                string observation = runOk
                    ? $"已执行命令 {best.Name} / {best.LocalizedName}。执行结果：{runMsg}"
                    : $"执行命令 {best.Name} / {best.LocalizedName} 失败。原因：{runMsg}";
                await ReplyFromObservationAsync(llmService, chatHistory, historyStore, userInput, observation, "");
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static async Task ReplyFromObservationAsync(
            VlmService llmService,
            List<ChatTurn> chatHistory,
            ChatHistoryStore historyStore,
            string userInput,
            string observation,
            string extraContext)
        {
            string traceText = BuildActionTraceSummary();
            if (!string.IsNullOrWhiteSpace(traceText))
            {
                extraContext = string.IsNullOrWhiteSpace(extraContext)
                    ? traceText
                    : $"{extraContext}{Environment.NewLine}{traceText}";
            }

            if (TryBuildDeterministicObservationReply(observation, extraContext, out string deterministicReply))
            {
                Console.WriteLine("AI> ");
                Console.WriteLine(deterministicReply);
                Console.WriteLine();
                AppendHistory(chatHistory, historyStore, "user", userInput);
                AppendHistory(chatHistory, historyStore, "assistant", deterministicReply);
                return;
            }

            try
            {
                string prompt =
$@"用户输入：{userInput}
系统观察：{observation}
附加上下文：{extraContext}

请用口语化中文回复，像同事聊天：
- 先一句话说明结果；
- 再一句话告诉用户下一步；
- 不要模板化，不要长篇解释；
- 如果系统观察是“未执行”，必须明确告诉用户“还没执行”。";

                Console.WriteLine("AI> ");
                string answer = await llmService.CallWithHistoryAsync(
                    chatHistory,
                    prompt,
                    systemPrompt: "你是 SolidWorks 协作助手。说话像同事聊天，简短、直接、有人味，不要官话。");
                Console.WriteLine(answer);
                Console.WriteLine();
                AppendHistory(chatHistory, historyStore, "user", userInput);
                AppendHistory(chatHistory, historyStore, "assistant", answer);
            }
            catch
            {
                Console.WriteLine($"AI> {observation}");
                Console.WriteLine();
            }
        }

        private static bool TryBuildDeterministicObservationReply(
            string observation,
            string extraContext,
            out string reply)
        {
            reply = "";
            string obs = (observation ?? "").Trim();
            string extra = (extraContext ?? "").Trim();

            if (string.IsNullOrWhiteSpace(obs))
            {
                return false;
            }

            // 关键：执行路径优先使用固定模板，避免历史对话污染导致幻觉建议
            if (obs.Contains("尚未执行任何插件动作"))
            {
                reply = string.IsNullOrWhiteSpace(extra)
                    ? "这次还没真正执行任何插件动作，也没有匹配到可靠命令。你可以说得更具体一点，比如“从装配体采集方程式”或“创建新机型 名称叫xxx”。"
                    : $"这次还没真正执行任何插件动作。{extra}";
                return true;
            }

            if (obs.Contains("动作成功：") || obs.Contains("已执行命令") || obs.Contains("已自动执行命令"))
            {
                reply = $"已执行完成。{obs}";
                return true;
            }

            if (obs.Contains("动作失败：") || obs.Contains("执行失败") || obs.Contains("失败"))
            {
                reply = $"执行没成功。{obs}";
                return true;
            }

            return false;
        }

        private static string BuildCommandListText(List<BridgeCommandInfo> commands)
        {
            if (commands == null || commands.Count == 0)
            {
                return "(空)";
            }

            var lines = new List<string>();
            foreach (var cmd in commands)
            {
                lines.Add($"- {cmd.Name} / {cmd.LocalizedName} : {cmd.Tooltip}");
            }
            return string.Join(Environment.NewLine, lines);
        }

        private static string BuildActionListText(List<BridgeActionInfo> actions)
        {
            if (actions == null || actions.Count == 0)
            {
                return "(无通用动作)";
            }

            var lines = new List<string>();
            foreach (var action in actions)
            {
                lines.Add($"- {action.Name} : {action.Description}");
            }
            return string.Join(Environment.NewLine, lines);
        }

        private static string BuildEquationListText(List<BridgeEquationInfo> equations)
        {
            if (equations == null || equations.Count == 0)
            {
                return "(没有匹配到方程式)";
            }

            int take = Math.Min(30, equations.Count);
            var lines = new List<string>();
            for (int i = 0; i < take; i++)
            {
                var e = equations[i];
                lines.Add($"- [{e.Part}] #{e.Index} {e.Variable} = {e.Value}");
            }

            if (equations.Count > take)
            {
                lines.Add($"... 其余 {equations.Count - take} 条省略");
            }

            return string.Join(Environment.NewLine, lines);
        }

        private static bool TryExtractRelativeBatchEquationTasksByRule(
            string input,
            out List<(string Keyword, double Delta)> tasks)
        {
            tasks = new List<(string Keyword, double Delta)>();
            string t = (input ?? "").Trim();
            if (string.IsNullOrWhiteSpace(t))
            {
                return false;
            }

            // 示例：管4长和管3长分别减300 / 把A、B都加20
            Match opMatch = Regex.Match(t, @"(加|减)\s*([+-]?\d+(?:\.\d+)?)");
            if (!opMatch.Success)
            {
                return false;
            }

            if (!double.TryParse(opMatch.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double amount))
            {
                return false;
            }

            double delta = opMatch.Groups[1].Value == "减" ? -Math.Abs(amount) : Math.Abs(amount);
            string before = t.Substring(0, opMatch.Index);
            before = before.Replace("分别", "")
                           .Replace("都", "")
                           .Replace("把", "")
                           .Replace("将", "")
                           .Replace("请", "")
                           .Trim();

            if (string.IsNullOrWhiteSpace(before))
            {
                return false;
            }

            before = Regex.Replace(
                before,
                @"(?<=[长宽高深厚径孔距数值量])(?=[\u4e00-\u9fa5A-Za-z_])",
                "|");

            var pieces = Regex.Split(before, @"和|及|并且|还有|、|,|，|\|")
                .Select(x => NormalizeEquationKeyword(x))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (pieces.Count < 2)
            {
                return false;
            }

            tasks = pieces.Select(x => (x, delta)).ToList();
            return true;
        }

        private static async Task<List<(string Keyword, double Delta)>> TryExtractRelativeBatchEquationTasksHybridAsync(
            string input,
            VlmService llmService,
            SwCommandBridgeClient swBridgeClient)
        {
            var empty = new List<(string Keyword, double Delta)>();
            string t = (input ?? "").Trim();
            if (string.IsNullOrWhiteSpace(t))
            {
                return empty;
            }

            // 1) LLM优先：更灵活地理解自然语言中的“批量相对更新”
            try
            {
                string equationsSnapshot = BuildEquationSnapshotForLlm(swBridgeClient, maxCount: 120);
                string llmPrompt =
$@"请从下面这句中文中提取“批量相对修改方程变量”的任务。
只返回 JSON，不要解释，不要 Markdown。

JSON 格式：
{{
  ""intent"": ""relative_batch_update"" 或 ""other"",
  ""tasks"": [
    {{ ""variable"": ""变量名"", ""op"": ""add"" 或 ""sub"", ""amount"": 数字 }}
  ]
}}

要求：
- 只有当是“两个及以上变量”且表达的是相对变化（加/减）时，intent 才是 relative_batch_update
- 若无法确定，返回 intent=other

当前机型方程式快照（用于你理解变量名）：
{equationsSnapshot}

用户输入：{t}";

                string llmRaw = await llmService.CallTextAsync(
                    llmPrompt,
                    systemPrompt: "你是 CAD 指令解析器，只输出严格 JSON。");

                if (TryParseRelativeBatchTasksJson(llmRaw, out var llmTasks) && llmTasks.Count >= 2)
                {
                    return llmTasks;
                }
            }
            catch
            {
                // LLM解析失败时自动回退规则
            }

            // 2) 规则兜底
            if (TryExtractRelativeBatchEquationTasksByRule(t, out var ruleTasks))
            {
                return ruleTasks;
            }

            return empty;
        }

        private static bool TryParseRelativeBatchTasksJson(string raw, out List<(string Keyword, double Delta)> tasks)
        {
            tasks = new List<(string Keyword, double Delta)>();
            string text = (raw ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');
            if (start < 0 || end <= start)
            {
                return false;
            }

            string json = text.Substring(start, end - start + 1);
            JObject obj;
            try
            {
                obj = JObject.Parse(json);
            }
            catch
            {
                return false;
            }

            string intent = (obj["intent"]?.ToString() ?? "").Trim().ToLowerInvariant();
            if (intent != "relative_batch_update")
            {
                return false;
            }

            if (!(obj["tasks"] is JArray arr) || arr.Count == 0)
            {
                return false;
            }

            foreach (var item in arr)
            {
                string keyword = NormalizeEquationKeyword(item?["variable"]?.ToString() ?? "");
                string op = (item?["op"]?.ToString() ?? "").Trim().ToLowerInvariant();
                if (!double.TryParse(item?["amount"]?.ToString() ?? "", NumberStyles.Float, CultureInfo.InvariantCulture, out double amount))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(keyword))
                {
                    continue;
                }

                double delta = op == "sub" ? -Math.Abs(amount) : Math.Abs(amount);
                tasks.Add((keyword, delta));
            }

            tasks = tasks
                .GroupBy(x => x.Keyword, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            return tasks.Count > 0;
        }

        private static async Task<List<(string Keyword, string TargetValue)>> TryExtractAbsoluteBatchEquationTasksHybridAsync(
            string input,
            VlmService llmService,
            SwCommandBridgeClient swBridgeClient)
        {
            var empty = new List<(string Keyword, string TargetValue)>();
            string t = (input ?? "").Trim();
            if (string.IsNullOrWhiteSpace(t))
            {
                return empty;
            }

            if (TryParseRelativeValueDirective(t, out _))
            {
                return empty;
            }

            try
            {
                string equationsSnapshot = BuildEquationSnapshotForLlm(swBridgeClient, maxCount: 120);
                string llmPrompt =
$@"请从下面这句中文中提取“批量绝对设置方程变量”的任务。
只返回 JSON，不要解释，不要 Markdown。

JSON 格式：
{{
  ""intent"": ""absolute_batch_update"" 或 ""other"",
  ""targetValue"": ""目标值字符串"",
  ""variables"": [""变量名1"", ""变量名2""]
}}

要求：
- 只有当语句明确是“两个及以上变量设置为同一个目标值”时，intent 才是 absolute_batch_update
- 像“都改成588 / 都设置为 1200”属于 absolute_batch_update
- 若无法确定，返回 intent=other

当前机型方程式快照（用于你理解变量名）：
{equationsSnapshot}

用户输入：{t}";

                string llmRaw = await llmService.CallTextAsync(
                    llmPrompt,
                    systemPrompt: "你是 CAD 指令解析器，只输出严格 JSON。");

                if (TryParseAbsoluteBatchTasksJson(llmRaw, out var llmTasks) && llmTasks.Count >= 2)
                {
                    return llmTasks;
                }
            }
            catch
            {
                // LLM解析失败时自动回退规则
            }

            if (TryExtractAbsoluteBatchEquationTasksByRule(t, out var ruleTasks))
            {
                return ruleTasks;
            }

            return empty;
        }

        private static bool TryParseAbsoluteBatchTasksJson(string raw, out List<(string Keyword, string TargetValue)> tasks)
        {
            tasks = new List<(string Keyword, string TargetValue)>();
            string text = (raw ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');
            if (start < 0 || end <= start)
            {
                return false;
            }

            string json = text.Substring(start, end - start + 1);
            JObject obj;
            try
            {
                obj = JObject.Parse(json);
            }
            catch
            {
                return false;
            }

            string intent = (obj["intent"]?.ToString() ?? "").Trim().ToLowerInvariant();
            if (intent != "absolute_batch_update")
            {
                return false;
            }

            string targetValue = NormalizeEquationValueText(obj["targetValue"]?.ToString() ?? "");
            if (string.IsNullOrWhiteSpace(targetValue))
            {
                return false;
            }

            if (!(obj["variables"] is JArray arr) || arr.Count == 0)
            {
                return false;
            }

            foreach (var item in arr)
            {
                string keyword = NormalizeEquationKeyword(item?.ToString() ?? "");
                if (!string.IsNullOrWhiteSpace(keyword))
                {
                    tasks.Add((keyword, targetValue));
                }
            }

            tasks = tasks
                .GroupBy(x => x.Keyword, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            return tasks.Count > 0;
        }

        private static bool TryExtractAbsoluteBatchEquationTasksByRule(string input, out List<(string Keyword, string TargetValue)> tasks)
        {
            tasks = new List<(string Keyword, string TargetValue)>();
            string t = (input ?? "").Trim();
            if (string.IsNullOrWhiteSpace(t))
            {
                return false;
            }

            Match m = Regex.Match(t, @"(.+?)(?:都)?\s*(?:改为|改成|改到|设置为|设为|修改为)\s*([^\s，。；;]+)");
            if (!m.Success)
            {
                return false;
            }

            string before = (m.Groups[1].Value ?? "").Trim()
                .Replace("分别", "")
                .Replace("都", "")
                .Replace("把", "")
                .Replace("将", "")
                .Replace("请", "")
                .Trim();
            string targetValue = NormalizeEquationValueText(m.Groups[2].Value);
            if (string.IsNullOrWhiteSpace(before) || string.IsNullOrWhiteSpace(targetValue))
            {
                return false;
            }

            before = Regex.Replace(
                before,
                @"(?<=[长宽高深厚径孔距数值量])(?=[\u4e00-\u9fa5A-Za-z_])",
                "|");

            var pieces = Regex.Split(before, @"和|及|并且|还有|、|,|，|\|")
                .Select(x => NormalizeEquationKeyword(x))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (pieces.Count < 2)
            {
                return false;
            }

            tasks = pieces.Select(x => (x, targetValue)).ToList();
            return true;
        }

        private static string BuildEquationSnapshotForLlm(SwCommandBridgeClient swBridgeClient, int maxCount)
        {
            try
            {
                PrintActionCall("list_current_variant_equations", "keyword=<empty>, forLlmContext=true");
                var (ok, message, equations) = swBridgeClient.ListCurrentVariantEquations("");
                PrintActionResult("list_current_variant_equations", ok, ok ? $"forLlmContext count={equations?.Count ?? 0}" : message);
                if (!ok || equations == null || equations.Count == 0)
                {
                    return "(当前无法读取方程式快照)";
                }

                int take = Math.Max(1, Math.Min(maxCount, equations.Count));
                var lines = new List<string>(take + 1);
                for (int i = 0; i < take; i++)
                {
                    var e = equations[i];
                    lines.Add($"- [{e.Part}] #{e.Index} {e.Variable} = {e.Value}");
                }

                if (equations.Count > take)
                {
                    lines.Add($"... 其余 {equations.Count - take} 条省略");
                }

                return string.Join(Environment.NewLine, lines);
            }
            catch
            {
                return "(当前无法读取方程式快照)";
            }
        }

        private static (bool Success, string Message) ExecuteRelativeEquationUpdateTaskLoop(
            string userInput,
            string equationKeyword,
            double delta,
            SwCommandBridgeClient swBridgeClient)
        {
            PrintActionCall("list_current_variant_equations", $"keyword={equationKeyword}, mode=relative");
            var (okList, listMessage, equations) = swBridgeClient.ListCurrentVariantEquations(equationKeyword);
            PrintActionResult("list_current_variant_equations", okList, listMessage);
            if (!okList)
            {
                return (false, $"[{equationKeyword}] 查询失败：{listMessage}");
            }

            if (equations.Count == 0)
            {
                PrintActionCall("list_current_variant_equations", "keyword=<empty>, fallback=true, mode=relative");
                var (okFallback, fallbackMessage, allEquations) = swBridgeClient.ListCurrentVariantEquations("");
                PrintActionResult("list_current_variant_equations", okFallback, fallbackMessage);
                if (!okFallback)
                {
                    return (false, $"[{equationKeyword}] 未命中且回退失败：{fallbackMessage}");
                }

                equations = allEquations ?? new List<BridgeEquationInfo>();
                if (equations.Count == 0)
                {
                    return (false, $"[{equationKeyword}] 当前机型没有可修改方程式");
                }
            }

            EquationNumericBoundsFilter valueBounds = EquationNumericBoundsFilter.ParseFromUserText(userInput);
            if (valueBounds.IsActive)
            {
                int beforeCount = equations.Count;
                equations = ApplyEquationNumericBoundsFilter(equations, valueBounds);
                if (equations.Count == 0)
                {
                    return (false,
                        $"[{equationKeyword}] 句中含数值范围条件，但当前机型没有「当前值为数字且满足该条件」的方程式（过滤前 {beforeCount} 条）。");
                }
            }

            var rankedCandidates = equations
                .OrderByDescending(e => ScoreEquationCandidate(e, equationKeyword, userInput))
                .ThenBy(e => e.Part ?? string.Empty)
                .ThenBy(e => e.Index)
                .GroupBy(e => (e.Variable ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            int maxAttempts = Math.Min(EquationTaskMaxAttempts, rankedCandidates.Count);
            string lastFailure = "";
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                var chosen = rankedCandidates[attempt];
                if (!TryParseEquationNumericValue(chosen.Value, out double currentValue))
                {
                    lastFailure = $"变量 {chosen.Variable} 当前值非数字：{chosen.Value}";
                    continue;
                }

                double target = currentValue + delta;
                string targetText = FormatNumericValue(target);
                PrintActionCall(
                    "update_current_variant_equation",
                    $"variable={chosen.Variable}, relativeDelta={FormatNumericValue(delta)}, from={FormatNumericValue(currentValue)}, to={targetText}, applyNow=true, loopAttempt={attempt + 1}/{maxAttempts}");
                var (okUpdate, msgUpdate) = swBridgeClient.UpdateCurrentVariantEquation(chosen.Variable, targetText, applyNow: true);
                PrintActionResult("update_current_variant_equation", okUpdate, msgUpdate);
                if (!okUpdate)
                {
                    lastFailure = msgUpdate;
                    continue;
                }

                if (!IsApplyResultClean(msgUpdate, out string applyIssue))
                {
                    lastFailure = $"应用未完全成功（变量 {chosen.Variable}）：{applyIssue}";
                    continue;
                }

                bool verifyOk = VerifyEquationUpdated(swBridgeClient, chosen.Variable, targetText);
                if (verifyOk)
                {
                    return (true, $"[{equationKeyword}] 已将 {chosen.Variable} 从 {FormatNumericValue(currentValue)} 调整为 {targetText}（Δ={FormatNumericValue(delta)}）");
                }

                lastFailure = $"已执行但校验未通过（变量 {chosen.Variable} 目标 {targetText}）";
            }

            return (false, $"[{equationKeyword}] 重试 {maxAttempts} 次失败：{lastFailure}");
        }

        private static bool TryGetCommandsWithCache(
            SwCommandBridgeClient swBridgeClient,
            ref List<BridgeCommandInfo> cachedCommands,
            ref DateTime cachedAt,
            bool forceRefresh,
            out List<BridgeCommandInfo> commands,
            out string error)
        {
            error = "";
            commands = new List<BridgeCommandInfo>();

            bool cacheValid = cachedCommands.Count > 0 && DateTime.Now - cachedAt < CommandCacheTtl;
            if (!forceRefresh && cacheValid)
            {
                commands = cachedCommands;
                return true;
            }

            var (ok, message, freshCommands) = swBridgeClient.ListCommands();
            if (!ok)
            {
                error = message;
                return false;
            }

            cachedCommands = freshCommands ?? new List<BridgeCommandInfo>();
            cachedAt = DateTime.Now;
            commands = cachedCommands;
            return true;
        }

        private static bool TryExtractCreateVariantName(string input, out string variantName)
        {
            variantName = "";
            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            string text = input.Trim();
            if (!(text.Contains("新机型") || (text.Contains("创建") && text.Contains("机型"))))
            {
                return false;
            }

            // 兼容“创建新机型 名称叫侧推600x1200x450 / 名称是xxx / 叫xxx”等表达
            var patterns = new[]
            {
                @"名称(?:叫|是|为)\s*[:：]?\s*(.+)$",
                @"(?:机型|新机型)\s*(?:叫|是|为)\s*[:：]?\s*(.+)$",
                @"创建(?:一个)?新?机型\s*[:：]?\s*(.+)$"
            };

            foreach (var p in patterns)
            {
                var m = Regex.Match(text, p, RegexOptions.IgnoreCase);
                if (m.Success)
                {
                    string name = (m.Groups[1].Value ?? "").Trim().Trim('\"', '\'', '。', '！', '!');
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        variantName = name;
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsAskForHelp(string input)
        {
            string t = (input ?? "").Trim();
            return t.Contains("帮助") || t.Contains("怎么用") || t.Contains("用法");
        }

        private static bool IsAskForCommandList(string input)
        {
            string t = (input ?? "").Trim();
            return (t.Contains("命令") && (t.Contains("列表") || t.Contains("有哪些") || t.Contains("能执行")))
                   || t.Contains("列一下命令");
        }

        private static bool IsAskForRefreshCommands(string input)
        {
            string t = (input ?? "").Trim();
            return (t.Contains("刷新") || t.Contains("重新"))
                   && t.Contains("命令");
        }

        private static bool TryExtractAutoToggle(string input, out bool enableAuto)
        {
            enableAuto = true;
            string t = (input ?? "").Trim();
            if (t.Contains("关闭自动执行") || t.Contains("不要自动执行"))
            {
                enableAuto = false;
                return true;
            }

            if (t.Contains("开启自动执行") || t.Contains("打开自动执行"))
            {
                enableAuto = true;
                return true;
            }

            return false;
        }

        private static bool IsCaptureAssemblyEquationIntent(string input)
        {
            string t = (input ?? "").Trim();
            if (string.IsNullOrWhiteSpace(t))
            {
                return false;
            }

            bool hasCapture = t.Contains("采集") || t.Contains("收集");
            bool hasEquation = t.Contains("方程") || t.Contains("方程式");
            bool hasAssembly = t.Contains("装配体") || t.Contains("装配");
            return hasCapture && hasEquation && hasAssembly;
        }

        private static bool IsAskEquationList(string input)
        {
            string t = (input ?? "").Trim();
            if (string.IsNullOrWhiteSpace(t))
            {
                return false;
            }

            bool hasEquation = t.Contains("方程") || t.Contains("方程式");
            bool askList = t.Contains("哪些") || t.Contains("列表") || t.Contains("查看") || t.Contains("有哪些") || t.Contains("列出");
            return hasEquation && askList;
        }

        private static string ExtractEquationKeyword(string input)
        {
            string t = (input ?? "").Trim();
            // 常见口语：导杆相关方程式有哪些 / 看一下导杆方程式
            var m = Regex.Match(t, @"([\u4e00-\u9fa5A-Za-z0-9_]+)\s*(相关)?\s*方程");
            if (m.Success)
            {
                string kw = (m.Groups[1].Value ?? "").Trim();
                if (!string.IsNullOrWhiteSpace(kw) &&
                    kw != "当前" && kw != "全部" && kw != "所有")
                {
                    return kw;
                }
            }

            return "";
        }

        private static bool LooksLikeExecutionIntent(string input)
        {
            string t = (input ?? "").Trim();
            if (string.IsNullOrWhiteSpace(t))
            {
                return false;
            }

            string[] verbs =
            {
                "修改", "改成", "设置", "导出", "创建", "新建", "采集", "应用", "删除", "检查", "转换", "打开", "复制"
            };
            bool hasVerb = verbs.Any(v => t.Contains(v));

            // 简单判断：执行意图里常见数值参数（比如“导杆长720”）
            bool hasNumber = Regex.IsMatch(t, @"\d+");
            return hasVerb || hasNumber;
        }

        private static async Task<(bool IsMatch, string EquationKeyword, string TargetValue)> TryExtractEquationUpdateIntentHybridAsync(
            string input,
            VlmService llmService,
            SwCommandBridgeClient swBridgeClient)
        {
            string t = (input ?? "").Trim();
            if (string.IsNullOrWhiteSpace(t))
            {
                return (false, "", "");
            }

            // 1) 优先让 LLM 做结构化抽取，避免句式写死
            try
            {
                string equationsSnapshot = BuildEquationSnapshotForLlm(swBridgeClient, maxCount: 80);
                string llmPrompt =
$@"请从下面这句中文中提取“是否要修改方程变量值”。
只返回 JSON，不要解释，不要 Markdown。

字段要求：
- intent: update_equation 或 other
- variable: 变量关键词（没有就空字符串）
- value: 目标值（没有就空字符串）；相对修改用「加 200」「减 10」或「+200」「-10」这类写法，不要用纯数字当增量除非是绝对目标值
- confidence: 0~1
- 若用户句子里有「大于/小于某数」等阈值，那是筛选当前值的语义，仍把 variable 写成其说的变量关键词即可（阈值由程序从原句解析）

当前机型方程式快照（用于你理解变量名）：
{equationsSnapshot}

用户输入：{t}";

                string llmRaw = await llmService.CallTextAsync(
                    llmPrompt,
                    systemPrompt: "你是 CAD 指令解析器，只输出严格 JSON。");
                if (TryParseEquationUpdateIntentJson(llmRaw, out string vFromLlm, out string valueFromLlm))
                {
                    string equationKeyword = NormalizeEquationKeyword(vFromLlm);
                    string targetValue = NormalizeEquationValueText(valueFromLlm);
                    if (!string.IsNullOrWhiteSpace(equationKeyword) && !string.IsNullOrWhiteSpace(targetValue))
                    {
                        return (true, equationKeyword, targetValue);
                    }
                }
            }
            catch
            {
                // LLM 抽取失败时自动回退规则，不中断主流程
            }

            // 2) 回退规则抽取（兜底）
            bool ok = TryExtractEquationUpdateIntentByRule(t, out string ruleKeyword, out string ruleValue);
            return (ok, ruleKeyword, ruleValue);
        }

        private static bool TryExtractEquationUpdateIntentByRule(string input, out string equationKeyword, out string targetValue)
        {
            equationKeyword = "";
            targetValue = "";
            string t = (input ?? "").Trim();
            if (string.IsNullOrWhiteSpace(t))
            {
                return false;
            }

            bool hasChangeVerb = t.Contains("改为") || t.Contains("改成") || t.Contains("改到") ||
                                 t.Contains("设置为") || t.Contains("设为") || t.Contains("修改为") ||
                                 (t.StartsWith("修改", StringComparison.OrdinalIgnoreCase) && t.Contains("为"));
            if (!hasChangeVerb)
            {
                return false;
            }

            Match m = Regex.Match(t, @"(.+?)(?:改为|改成|改到|设置为|设为|修改为)\s*([^\s，。；;]+)");
            if (!m.Success)
            {
                // 兼容口语：修改滚筒管长为660
                m = Regex.Match(t, @"^修改\s*(.+?)\s*为\s*([^\s，。；;]+)$");
                if (!m.Success)
                {
                    return false;
                }
            }

            equationKeyword = NormalizeEquationKeyword(m.Groups[1].Value);
            targetValue = NormalizeEquationValueText(m.Groups[2].Value);
            return !string.IsNullOrWhiteSpace(equationKeyword) && !string.IsNullOrWhiteSpace(targetValue);
        }

        private static bool TryParseEquationUpdateIntentJson(string raw, out string variable, out string value)
        {
            variable = "";
            value = "";
            string text = (raw ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            int start = text.IndexOf('{');
            int end = text.LastIndexOf('}');
            if (start < 0 || end <= start)
            {
                return false;
            }

            string json = text.Substring(start, end - start + 1);
            JObject obj;
            try
            {
                obj = JObject.Parse(json);
            }
            catch
            {
                return false;
            }

            string intent = (obj["intent"]?.ToString() ?? "").Trim().ToLowerInvariant();
            if (intent != "update_equation")
            {
                return false;
            }

            variable = (obj["variable"]?.ToString() ?? "").Trim();
            value = (obj["value"]?.ToString() ?? "").Trim();
            return true;
        }

        private static string NormalizeEquationValueText(string rawValue)
        {
            return (rawValue ?? "").Trim().Trim('"', '\'', '。', '，', ';', '；');
        }

        private static string NormalizeEquationKeyword(string raw)
        {
            string s = (raw ?? "").Trim();
            if (string.IsNullOrWhiteSpace(s))
            {
                return "";
            }

            string[] trims =
            {
                "把", "将", "请", "给我", "帮我", "参数", "方程", "方程式", "变量", "的", "值"
            };

            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var token in trims)
                {
                    if (s.StartsWith(token, StringComparison.OrdinalIgnoreCase))
                    {
                        s = s.Substring(token.Length).Trim();
                        changed = true;
                    }
                }
            }

            while (s.EndsWith("的", StringComparison.OrdinalIgnoreCase) ||
                   s.EndsWith("值", StringComparison.OrdinalIgnoreCase) ||
                   s.EndsWith("参数", StringComparison.OrdinalIgnoreCase) ||
                   s.EndsWith("方程", StringComparison.OrdinalIgnoreCase) ||
                   s.EndsWith("方程式", StringComparison.OrdinalIgnoreCase))
            {
                if (s.EndsWith("方程式", StringComparison.OrdinalIgnoreCase))
                {
                    s = s.Substring(0, s.Length - 3).Trim();
                }
                else if (s.EndsWith("方程", StringComparison.OrdinalIgnoreCase) || s.EndsWith("参数", StringComparison.OrdinalIgnoreCase))
                {
                    s = s.Substring(0, s.Length - 2).Trim();
                }
                else
                {
                    s = s.Substring(0, s.Length - 1).Trim();
                }
            }

            return s.Trim();
        }

        /// <summary>
        /// 从用户自然语言中解析对方程「当前数值」的区间条件（如「大于600的尺寸」），用于过滤候选，避免误选无关变量。
        /// </summary>
        private sealed class EquationNumericBoundsFilter
        {
            public bool IsActive =>
                StrictLower.HasValue || WeakLower.HasValue || StrictUpper.HasValue || WeakUpper.HasValue;

            /// <summary>要求当前值 &gt; 该数。</summary>
            public double? StrictLower { get; private set; }

            /// <summary>要求当前值 &gt;= 该数。</summary>
            public double? WeakLower { get; private set; }

            /// <summary>要求当前值 &lt; 该数。</summary>
            public double? StrictUpper { get; private set; }

            /// <summary>要求当前值 &lt;= 该数。</summary>
            public double? WeakUpper { get; private set; }

            public static EquationNumericBoundsFilter ParseFromUserText(string? userInput)
            {
                var f = new EquationNumericBoundsFilter();
                string t = userInput ?? "";
                if (string.IsNullOrWhiteSpace(t))
                {
                    return f;
                }

                foreach (Match m in Regex.Matches(t, @"(?:大于|超过|高于)\s*(\d+(?:\.\d+)?)"))
                {
                    f.TightenStrictLower(ParseD(m.Groups[1].Value));
                }

                foreach (Match m in Regex.Matches(t, @"(?:小于|低于)\s*(\d+(?:\.\d+)?)"))
                {
                    f.TightenStrictUpper(ParseD(m.Groups[1].Value));
                }

                foreach (Match m in Regex.Matches(t, @"(?:不小于|至少)\s*(\d+(?:\.\d+)?)"))
                {
                    f.TightenWeakLower(ParseD(m.Groups[1].Value));
                }

                foreach (Match m in Regex.Matches(t, @"(?:不大于|最多)\s*(\d+(?:\.\d+)?)"))
                {
                    f.TightenWeakUpper(ParseD(m.Groups[1].Value));
                }

                foreach (Match m in Regex.Matches(t, @"(?:>=|≥)\s*(\d+(?:\.\d+)?)"))
                {
                    f.TightenWeakLower(ParseD(m.Groups[1].Value));
                }

                foreach (Match m in Regex.Matches(t, @"(?:<=|≤)\s*(\d+(?:\.\d+)?)"))
                {
                    f.TightenWeakUpper(ParseD(m.Groups[1].Value));
                }

                foreach (Match m in Regex.Matches(t, @">\s*(\d+(?:\.\d+)?)"))
                {
                    f.TightenStrictLower(ParseD(m.Groups[1].Value));
                }

                foreach (Match m in Regex.Matches(t, @"<\s*(\d+(?:\.\d+)?)"))
                {
                    f.TightenStrictUpper(ParseD(m.Groups[1].Value));
                }

                foreach (Match m in Regex.Matches(t, @"(\d+(?:\.\d+)?)\s*以上"))
                {
                    f.TightenWeakLower(ParseD(m.Groups[1].Value));
                }

                foreach (Match m in Regex.Matches(t, @"(\d+(?:\.\d+)?)\s*以下"))
                {
                    f.TightenWeakUpper(ParseD(m.Groups[1].Value));
                }

                return f;
            }

            public bool Passes(double v)
            {
                if (!IsActive)
                {
                    return true;
                }

                if (StrictLower.HasValue && v <= StrictLower.Value)
                {
                    return false;
                }

                if (WeakLower.HasValue && v < WeakLower.Value)
                {
                    return false;
                }

                if (StrictUpper.HasValue && v >= StrictUpper.Value)
                {
                    return false;
                }

                if (WeakUpper.HasValue && v > WeakUpper.Value)
                {
                    return false;
                }

                return true;
            }

            private static double ParseD(string s)
            {
                return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
                    ? v
                    : double.NaN;
            }

            private void TightenStrictLower(double n)
            {
                if (double.IsNaN(n))
                {
                    return;
                }

                StrictLower = StrictLower.HasValue ? Math.Max(StrictLower.Value, n) : n;
            }

            private void TightenStrictUpper(double n)
            {
                if (double.IsNaN(n))
                {
                    return;
                }

                StrictUpper = StrictUpper.HasValue ? Math.Min(StrictUpper.Value, n) : n;
            }

            private void TightenWeakLower(double n)
            {
                if (double.IsNaN(n))
                {
                    return;
                }

                WeakLower = WeakLower.HasValue ? Math.Max(WeakLower.Value, n) : n;
            }

            private void TightenWeakUpper(double n)
            {
                if (double.IsNaN(n))
                {
                    return;
                }

                WeakUpper = WeakUpper.HasValue ? Math.Min(WeakUpper.Value, n) : n;
            }
        }

        private static List<BridgeEquationInfo> ApplyEquationNumericBoundsFilter(
            List<BridgeEquationInfo> source,
            EquationNumericBoundsFilter bounds)
        {
            if (source == null || source.Count == 0 || !bounds.IsActive)
            {
                return source?.ToList() ?? new List<BridgeEquationInfo>();
            }

            var list = new List<BridgeEquationInfo>();
            foreach (BridgeEquationInfo e in source)
            {
                if (!TryParseEquationNumericValue(e.Value, out double v))
                {
                    continue;
                }

                if (bounds.Passes(v))
                {
                    list.Add(e);
                }
            }

            return list;
        }

        private static BridgeEquationInfo SelectBestEquationCandidate(
            string equationKeyword,
            string userInput,
            List<BridgeEquationInfo> candidates)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return new BridgeEquationInfo();
            }

            string kw = (equationKeyword ?? "").Trim();
            string input = (userInput ?? "").Trim();
            return candidates
                .OrderByDescending(c => ScoreEquationCandidate(c, kw, input))
                .ThenBy(c => c.Part ?? string.Empty)
                .ThenBy(c => c.Index)
                .First();
        }

        private static double ScoreEquationCandidate(BridgeEquationInfo candidate, string keyword, string userInput)
        {
            if (candidate == null)
            {
                return 0;
            }

            string variable = (candidate.Variable ?? "").Trim();
            string part = (candidate.Part ?? "").Trim();
            string value = (candidate.Value ?? "").Trim();
            string kw = (keyword ?? "").Trim();
            string input = (userInput ?? "").Trim();
            string combined = $"{part} {variable} {value}".ToLowerInvariant();
            string variableLower = variable.ToLowerInvariant();
            string kwLower = kw.ToLowerInvariant();

            double score = 0;
            if (!string.IsNullOrWhiteSpace(kw))
            {
                if (string.Equals(variable, kw, StringComparison.OrdinalIgnoreCase))
                {
                    score += 1.0;
                }
                else if (variableLower.Contains(kwLower))
                {
                    score += 0.78;
                }
                else if (combined.Contains(kwLower))
                {
                    score += 0.50;
                }

                // 关键词轻模糊：兼容“滚筒管长/滚筒长度”这类命名差异
                double kwVarSim = Similarity(kwLower, variableLower);
                if (kwVarSim >= 0.45)
                {
                    score += 0.35 * kwVarSim;
                }

                foreach (string kwToken in BuildCjkTokens(kwLower))
                {
                    if (kwToken.Length >= 2 && variableLower.Contains(kwToken))
                    {
                        score += 0.05;
                    }
                }
            }

            foreach (string token in Regex.Split(input, @"[\s,，。.;；:：()（）/\-_]+")
                         .Where(x => !string.IsNullOrWhiteSpace(x) && x.Length >= 2)
                         .Select(x => x.Trim().ToLowerInvariant())
                         .Distinct())
            {
                if (combined.Contains(token))
                {
                    score += 0.06;
                }
            }

            if (string.Equals(kw, "尺寸", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(kw, "尺度", StringComparison.OrdinalIgnoreCase))
            {
                if (Regex.IsMatch(
                        variableLower,
                        @"(长|宽|高|厚|深|径|距|直径|半径|外长|外宽|外高|内长|内宽|内高|长度|宽度|高度|总长|总宽|总高|外包|外形|轮廓)"))
                {
                    score += 0.48;
                }
            }

            // 轻微偏向变量名更短；若关键词与变量名明显无关，减弱该偏置，避免「尺寸」类泛词误选短名无关变量
            double lengthBias = 1.0 / Math.Max(8.0, variable.Length + 1.0);
            if (!string.IsNullOrWhiteSpace(kw) &&
                !variableLower.Contains(kwLower) &&
                kw.Length >= 2 &&
                !Regex.IsMatch(variableLower, @"(长|宽|高|厚|深|径|距|直径|半径|长度|宽度|高度)"))
            {
                lengthBias *= 0.22;
            }

            score += lengthBias;
            return score;
        }

        private static IEnumerable<string> BuildCjkTokens(string text)
        {
            var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(text))
            {
                return tokens;
            }

            string s = text.Trim();
            foreach (var part in Regex.Split(s, @"[\s,，。.;；:：()（）/\-_]+"))
            {
                string p = (part ?? "").Trim();
                if (p.Length >= 2)
                {
                    tokens.Add(p.ToLowerInvariant());
                }
            }

            if (s.Length >= 4)
            {
                for (int i = 0; i < s.Length - 1; i++)
                {
                    for (int len = 2; len <= 4 && i + len <= s.Length; len++)
                    {
                        tokens.Add(s.Substring(i, len).ToLowerInvariant());
                    }
                }
            }

            return tokens;
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

        private static async Task ExecuteEquationUpdateTaskLoopAsync(
            string userInput,
            string equationKeyword,
            string targetValue,
            SwCommandBridgeClient swBridgeClient,
            VlmService llmService,
            List<ChatTurn> chatHistory,
            ChatHistoryStore historyStore)
        {
            var result = ExecuteEquationUpdateTaskLoopCore(userInput, equationKeyword, targetValue, swBridgeClient);
            await ReplyFromObservationAsync(
                llmService,
                chatHistory,
                historyStore,
                userInput,
                result.Success ? $"动作成功：{result.Message}" : $"动作失败：{result.Message}",
                "");
        }

        private static (bool Success, string Message) ExecuteEquationUpdateTaskLoopCore(
            string userInput,
            string equationKeyword,
            string targetValue,
            SwCommandBridgeClient swBridgeClient)
        {
            PrintActionCall("list_current_variant_equations", $"keyword={equationKeyword}");
            var (okList, listMessage, equations) = swBridgeClient.ListCurrentVariantEquations(equationKeyword);
            PrintActionResult("list_current_variant_equations", okList, listMessage);
            if (!okList)
            {
                return (false, listMessage);
            }

            if (equations.Count == 0)
            {
                PrintActionCall("list_current_variant_equations", "keyword=<empty>, fallback=true");
                var (okFallback, fallbackMessage, allEquations) = swBridgeClient.ListCurrentVariantEquations("");
                PrintActionResult("list_current_variant_equations", okFallback, fallbackMessage);
                if (!okFallback)
                {
                    return (false, $"未找到与「{equationKeyword}」相关的方程式（且回退检索失败：{fallbackMessage}）");
                }

                equations = allEquations ?? new List<BridgeEquationInfo>();
                if (equations.Count == 0)
                {
                    return (false, "当前机型没有可修改的方程式");
                }
            }

            EquationNumericBoundsFilter valueBoundsAbs = EquationNumericBoundsFilter.ParseFromUserText(userInput);
            if (valueBoundsAbs.IsActive)
            {
                int beforeCount = equations.Count;
                equations = ApplyEquationNumericBoundsFilter(equations, valueBoundsAbs);
                if (equations.Count == 0)
                {
                    return (false,
                        $"句中含数值范围条件，但当前机型没有「当前值为数字且满足该条件」的方程式（过滤前 {beforeCount} 条）。");
                }
            }

            var rankedCandidates = equations
                .OrderByDescending(e => ScoreEquationCandidate(e, equationKeyword, userInput))
                .ThenBy(e => e.Part ?? string.Empty)
                .ThenBy(e => e.Index)
                .GroupBy(e => (e.Variable ?? "").Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .ToList();

            int maxAttempts = Math.Min(EquationTaskMaxAttempts, rankedCandidates.Count);
            string lastFailure = "";
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                var chosen = rankedCandidates[attempt];
                string actualTargetValue = targetValue;
                if (TryParseRelativeValueDirective(targetValue, out double relativeDelta))
                {
                    if (!TryParseEquationNumericValue(chosen.Value, out double currentValue))
                    {
                        lastFailure = $"变量 {chosen.Variable} 当前值非数字，无法按相对值计算：{chosen.Value}";
                        continue;
                    }

                    actualTargetValue = FormatNumericValue(currentValue + relativeDelta);
                }

                PrintActionCall(
                    "update_current_variant_equation",
                    $"variable={chosen.Variable}, value={actualTargetValue}, applyNow=true, loopAttempt={attempt + 1}/{maxAttempts}");
                var (okUpdate, msgUpdate) = swBridgeClient.UpdateCurrentVariantEquation(chosen.Variable, actualTargetValue, applyNow: true);
                PrintActionResult("update_current_variant_equation", okUpdate, msgUpdate);
                if (!okUpdate)
                {
                    lastFailure = msgUpdate;
                    continue;
                }

                if (!IsApplyResultClean(msgUpdate, out string applyIssue))
                {
                    lastFailure = $"应用未完全成功（变量 {chosen.Variable}）：{applyIssue}";
                    continue;
                }

                bool verifyOk = VerifyEquationUpdated(swBridgeClient, chosen.Variable, actualTargetValue);
                if (verifyOk)
                {
                    return (true, $"已更新变量 {chosen.Variable} 为 {actualTargetValue}");
                }

                lastFailure = $"已执行但校验未通过（变量 {chosen.Variable} 仍未确认更新为 {actualTargetValue}）";
            }

            string failMessage = string.IsNullOrWhiteSpace(lastFailure)
                ? $"尝试了 {maxAttempts} 次仍未完成方程式更新"
                : $"尝试了 {maxAttempts} 次仍未完成方程式更新，最后一次：{lastFailure}";
            return (false, failMessage);
        }

        private static bool VerifyEquationUpdated(SwCommandBridgeClient swBridgeClient, string variableName, string expectedValue)
        {
            PrintActionCall("list_current_variant_equations", $"keyword={variableName}, verify=true");
            var (ok, _, equations) = swBridgeClient.ListCurrentVariantEquations(variableName);
            PrintActionResult("list_current_variant_equations", ok, ok ? $"verifyCount={equations.Count}" : "verifyFailed");
            if (!ok || equations == null || equations.Count == 0)
            {
                return false;
            }

            string expected = NormalizeEquationValue(expectedValue);
            return equations.Any(e =>
                string.Equals((e.Variable ?? "").Trim(), (variableName ?? "").Trim(), StringComparison.OrdinalIgnoreCase) &&
                NormalizeEquationValue(e.Value) == expected);
        }

        private static bool IsApplyResultClean(string updateMessage, out string issue)
        {
            issue = "";
            string msg = (updateMessage ?? "").Trim();
            if (string.IsNullOrWhiteSpace(msg))
            {
                return true;
            }

            Match m = Regex.Match(msg, @"失败\s*(\d+)\s*条");
            if (!m.Success)
            {
                return true;
            }

            if (!int.TryParse(m.Groups[1].Value, out int failCount))
            {
                return true;
            }

            if (failCount <= 0)
            {
                return true;
            }

            issue = msg;
            return false;
        }

        private static string NormalizeEquationValue(string value)
        {
            return (value ?? "")
                .Trim()
                .Replace(" ", "")
                .Replace("\"", "")
                .Replace("“", "")
                .Replace("”", "")
                .ToLowerInvariant();
        }

        private static bool TryParseEquationNumericValue(string raw, out double value)
        {
            value = 0;
            string s = (raw ?? "").Trim()
                .Replace("\"", "")
                .Replace("“", "")
                .Replace("”", "")
                .Trim();

            Match m = Regex.Match(s, @"[-+]?\d+(?:\.\d+)?");
            if (!m.Success)
            {
                return false;
            }

            return double.TryParse(m.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static bool TryParseRelativeValueDirective(string raw, out double delta)
        {
            delta = 0;
            string text = (raw ?? "").Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            Match m = Regex.Match(text, @"^(加|减)\s*([+-]?\d+(?:\.\d+)?)$");
            if (m.Success)
            {
                if (!double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double amount))
                {
                    return false;
                }

                delta = m.Groups[1].Value == "减" ? -Math.Abs(amount) : Math.Abs(amount);
                return true;
            }

            // LLM 常输出「+200」「-12.5」
            m = Regex.Match(text, @"^\s*([+-])\s*(\d+(?:\.\d+)?)\s*$");
            if (m.Success &&
                double.TryParse(m.Groups[2].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double signedAmt))
            {
                delta = m.Groups[1].Value == "-" ? -signedAmt : signedAmt;
                return true;
            }

            return false;
        }

        private static string FormatNumericValue(double value)
        {
            return value.ToString("0.###############", CultureInfo.InvariantCulture);
        }

        private static bool IsAskClearHistory(string input)
        {
            string t = (input ?? "").Trim();
            return t.Contains("清空") && t.Contains("历史");
        }

        private static bool IsAskShowHistory(string input)
        {
            string t = (input ?? "").Trim();
            return (t.Contains("查看") || t.Contains("显示") || t.Contains("看一下"))
                   && t.Contains("历史");
        }

        private static void ShowRecentChatHistory(List<ChatTurn> chatHistory)
        {
            if (chatHistory == null || chatHistory.Count == 0)
            {
                Console.WriteLine("当前没有对话历史。");
                return;
            }

            int take = Math.Min(10, chatHistory.Count);
            Console.WriteLine($"最近对话历史（{take}条）:");
            for (int i = chatHistory.Count - take; i < chatHistory.Count; i++)
            {
                var t = chatHistory[i];
                string role = string.Equals(t.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? "AI" : "你";
                Console.WriteLine($"{role}: {t.Content}");
            }
            Console.WriteLine();
        }

        private static void AppendHistory(
            List<ChatTurn> chatHistory,
            ChatHistoryStore historyStore,
            string role,
            string content)
        {
            if (string.IsNullOrWhiteSpace(role) || string.IsNullOrWhiteSpace(content))
            {
                return;
            }

            chatHistory.Add(new ChatTurn
            {
                Role = role.Trim().ToLowerInvariant(),
                Content = content.Trim()
            });
            historyStore.Save(chatHistory);
        }

        private static void PrintActionCall(string actionName, string argument = "")
        {
            string line;
            if (string.IsNullOrWhiteSpace(argument))
            {
                line = $"[调用] {actionName}";
            }
            else
            {
                line = $"[调用] {actionName} ({argument})";
            }
            Console.WriteLine(line);
            AddTurnActionTrace(line);
        }

        private static void PrintActionResult(string actionName, bool success, string message)
        {
            string line = $"[回执] {actionName} => {(success ? "成功" : "失败")} | {message}";
            Console.WriteLine(line);
            AddTurnActionTrace(line);
        }

        private static void ResetTurnActionTrace()
        {
            CurrentTurnActionTrace.Clear();
        }

        private static void AddTurnActionTrace(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                return;
            }

            CurrentTurnActionTrace.Add(line.Trim());
            if (CurrentTurnActionTrace.Count > 18)
            {
                CurrentTurnActionTrace.RemoveAt(0);
            }
        }

        private static string BuildActionTraceSummary()
        {
            if (CurrentTurnActionTrace.Count == 0)
            {
                return "";
            }

            return "本次调用链：" + string.Join(" -> ", CurrentTurnActionTrace);
        }
    }
}
