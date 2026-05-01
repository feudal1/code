using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Newtonsoft.Json;

namespace SolidWorksAddinStudy
{
    internal sealed class CommandBridgeServer : IDisposable
    {
        private readonly string _pipeName;
        private readonly Func<BridgeRequest, BridgeResponse> _requestHandler;
        private Thread? _workerThread;
        private volatile bool _running;

        public CommandBridgeServer(string pipeName, Func<BridgeRequest, BridgeResponse> requestHandler)
        {
            _pipeName = pipeName;
            _requestHandler = requestHandler;
        }

        public void Start()
        {
            if (_running)
            {
                return;
            }

            _running = true;
            _workerThread = new Thread(ListenLoop)
            {
                IsBackground = true,
                Name = "SW-CommandBridge"
            };
            _workerThread.Start();
        }

        public void Stop()
        {
            if (!_running)
            {
                return;
            }

            _running = false;
            TryWakeListener();
        }

        private void ListenLoop()
        {
            while (_running)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(
                        _pipeName,
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Message,
                        PipeOptions.None);

                    pipe.WaitForConnection();
                    if (!_running)
                    {
                        break;
                    }

                    using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
                    using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true)
                    {
                        AutoFlush = true
                    };

                    string? line = reader.ReadLine();
                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    var request = JsonConvert.DeserializeObject<BridgeRequest>(line) ?? new BridgeRequest();
                    BridgeResponse response;
                    try
                    {
                        response = _requestHandler(request);
                    }
                    catch (Exception ex)
                    {
                        response = BridgeResponse.Fail($"处理请求异常: {ex.Message}");
                    }

                    writer.WriteLine(JsonConvert.SerializeObject(response));
                }
                catch (IOException)
                {
                    // 客户端断连时忽略，继续监听
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"命令桥接监听异常: {ex.Message}");
                }
            }
        }

        private void TryWakeListener()
        {
            try
            {
                using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
                client.Connect(300);
                using var writer = new StreamWriter(client, new UTF8Encoding(false), 1024, true)
                {
                    AutoFlush = true
                };
                writer.WriteLine("{}");
            }
            catch
            {
                // 唤醒失败可忽略
            }
        }

        public void Dispose()
        {
            Stop();
        }
    }

    internal sealed class BridgeRequest
    {
        [JsonProperty("action")]
        public string Action { get; set; } = "";

        [JsonProperty("actionName")]
        public string ActionName { get; set; } = "";

        [JsonProperty("commandName")]
        public string CommandName { get; set; } = "";

        [JsonProperty("variantName")]
        public string VariantName { get; set; } = "";

        [JsonProperty("argument")]
        public string Argument { get; set; } = "";

        [JsonProperty("keyword")]
        public string Keyword { get; set; } = "";

        [JsonProperty("equationVariable")]
        public string EquationVariable { get; set; } = "";

        [JsonProperty("equationValue")]
        public string EquationValue { get; set; } = "";

        [JsonProperty("applyNow")]
        public bool ApplyNow { get; set; } = true;
    }

    internal sealed class BridgeResponse
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("message")]
        public string Message { get; set; } = "";

        [JsonProperty("commands", NullValueHandling = NullValueHandling.Ignore)]
        public List<BridgeCommandInfo>? Commands { get; set; }

        [JsonProperty("actions", NullValueHandling = NullValueHandling.Ignore)]
        public List<BridgeActionInfo>? Actions { get; set; }

        [JsonProperty("equations", NullValueHandling = NullValueHandling.Ignore)]
        public List<BridgeEquationInfo>? Equations { get; set; }

        public static BridgeResponse Ok(string message)
        {
            return new BridgeResponse { Success = true, Message = message };
        }

        public static BridgeResponse Fail(string message)
        {
            return new BridgeResponse { Success = false, Message = message };
        }
    }

    internal sealed class BridgeCommandInfo
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("localizedName")]
        public string LocalizedName { get; set; } = "";

        [JsonProperty("tooltip")]
        public string Tooltip { get; set; } = "";
    }

    internal sealed class BridgeActionInfo
    {
        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("description")]
        public string Description { get; set; } = "";
    }

    internal sealed class BridgeEquationInfo
    {
        [JsonProperty("part")]
        public string Part { get; set; } = "";

        [JsonProperty("index")]
        public int Index { get; set; }

        [JsonProperty("variable")]
        public string Variable { get; set; } = "";

        [JsonProperty("value")]
        public string Value { get; set; } = "";
    }

    public partial class AddinStudy
    {
        private const string CommandBridgePipeName = "my_c_sw_command_bridge_v1";
        private static CommandBridgeServer? _commandBridgeServer;

        private void StartCommandBridgeServer()
        {
            try
            {
                if (_commandBridgeServer != null)
                {
                    return;
                }

                _commandBridgeServer = new CommandBridgeServer(CommandBridgePipeName, HandleBridgeRequest);
                _commandBridgeServer.Start();
                Debug.WriteLine($"命令桥接已启动: {CommandBridgePipeName}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"命令桥接启动失败: {ex.Message}");
            }
        }

        private void StopCommandBridgeServer()
        {
            try
            {
                _commandBridgeServer?.Stop();
                _commandBridgeServer = null;
                Debug.WriteLine("命令桥接已停止");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"命令桥接停止失败: {ex.Message}");
            }
        }

        private BridgeResponse HandleBridgeRequest(BridgeRequest request)
        {
            return RunOnMainThread(() => HandleBridgeRequestCore(request));
        }

        private BridgeResponse HandleBridgeRequestCore(BridgeRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Action))
            {
                return BridgeResponse.Fail("请求缺少 action");
            }

            string action = request.Action.Trim().ToLowerInvariant();
            switch (action)
            {
                case "ping":
                    return BridgeResponse.Ok("pong");
                case "list_commands":
                    return new BridgeResponse
                    {
                        Success = true,
                        Message = "ok",
                        Commands = GetBridgeCommandsSnapshot()
                    };
                case "list_actions":
                    return new BridgeResponse
                    {
                        Success = true,
                        Message = "ok",
                        Actions = GetBridgeActionsSnapshot()
                    };
                case "execute_command":
                    if (string.IsNullOrWhiteSpace(request.CommandName))
                    {
                        return BridgeResponse.Fail("commandName 不能为空");
                    }

                    bool ok = ExecuteCommandByName(request.CommandName.Trim());
                    return ok
                        ? BridgeResponse.Ok($"已执行命令: {request.CommandName}")
                        : BridgeResponse.Fail($"未找到命令: {request.CommandName}");
                case "create_variant":
                    {
                        if (string.IsNullOrWhiteSpace(request.VariantName))
                        {
                            return BridgeResponse.Fail("variantName 不能为空");
                        }

                        var control = GetEquationModelTaskPaneControl();
                        if (control == null)
                        {
                            return BridgeResponse.Fail("机型方程式任务窗格未初始化");
                        }

                        bool createOk = control.TryCreateVariantFromAi(request.VariantName.Trim(), out string createMessage);
                        return createOk ? BridgeResponse.Ok(createMessage) : BridgeResponse.Fail(createMessage);
                    }
                case "capture_assembly_equations":
                    {
                        var control = GetEquationModelTaskPaneControl();
                        if (control == null)
                        {
                            return BridgeResponse.Fail("机型方程式任务窗格未初始化");
                        }

                        bool captureOk = control.TryCaptureAssemblyEquationsFromAi(out string captureMessage);
                        return captureOk ? BridgeResponse.Ok(captureMessage) : BridgeResponse.Fail(captureMessage);
                    }
                case "invoke_action":
                    return InvokeAction(request);
                case "list_current_variant_equations":
                    return ListCurrentVariantEquations(request.Keyword);
                case "update_current_variant_equation":
                    return UpdateCurrentVariantEquation(request);
                default:
                    return BridgeResponse.Fail($"未知 action: {request.Action}");
            }
        }

        private BridgeResponse InvokeAction(BridgeRequest request)
        {
            string actionName = (request.ActionName ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(actionName))
            {
                return BridgeResponse.Fail("actionName 不能为空");
            }

            // 优先处理任务窗格动作
            if (string.Equals(actionName, "create_variant", StringComparison.OrdinalIgnoreCase))
            {
                var control = GetEquationModelTaskPaneControl();
                if (control == null)
                {
                    return BridgeResponse.Fail("机型方程式任务窗格未初始化");
                }

                string variantName = (request.Argument ?? request.VariantName ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(variantName))
                {
                    return BridgeResponse.Fail("create_variant 需要机型名称参数");
                }

                bool ok = control.TryCreateVariantFromAi(variantName, out string msg);
                return ok ? BridgeResponse.Ok(msg) : BridgeResponse.Fail(msg);
            }

            if (string.Equals(actionName, "capture_assembly_equations", StringComparison.OrdinalIgnoreCase))
            {
                var control = GetEquationModelTaskPaneControl();
                if (control == null)
                {
                    return BridgeResponse.Fail("机型方程式任务窗格未初始化");
                }

                bool ok = control.TryCaptureAssemblyEquationsFromAi(out string msg);
                return ok ? BridgeResponse.Ok(msg) : BridgeResponse.Fail(msg);
            }

            if (string.Equals(actionName, "list_current_variant_equations", StringComparison.OrdinalIgnoreCase))
            {
                string keyword = (request.Argument ?? request.Keyword ?? string.Empty).Trim();
                return ListCurrentVariantEquations(keyword);
            }

            if (string.Equals(actionName, "update_current_variant_equation", StringComparison.OrdinalIgnoreCase))
            {
                return UpdateCurrentVariantEquation(request);
            }

            // 回退：把 actionName 当作命令名执行（支持 Name / LocalizedName）
            bool commandOk = ExecuteCommandByName(actionName);
            if (commandOk)
            {
                return BridgeResponse.Ok($"已执行命令: {actionName}");
            }

            return BridgeResponse.Fail($"未找到可执行动作: {actionName}");
        }

        private List<BridgeCommandInfo> GetBridgeCommandsSnapshot()
        {
            return _commandRegistry.Values
                .Select(method =>
                {
                    var attr = method.GetCustomAttribute<CommandAttribute>();
                    if (attr == null)
                    {
                        return null;
                    }

                    return new BridgeCommandInfo
                    {
                        Id = attr.Id,
                        Name = attr.Name ?? string.Empty,
                        LocalizedName = attr.LocalizedName ?? string.Empty,
                        Tooltip = attr.Tooltip ?? string.Empty
                    };
                })
                .Where(x => x != null)
                .GroupBy(x => x!.Id)
                .Select(g => g.First()!)
                .OrderBy(x => x.Id)
                .ToList();
        }

        private List<BridgeActionInfo> GetBridgeActionsSnapshot()
        {
            var result = new List<BridgeActionInfo>
            {
                new BridgeActionInfo
                {
                    Name = "create_variant",
                    Description = "新建机型（参数: argument=机型名称）"
                },
                new BridgeActionInfo
                {
                    Name = "capture_assembly_equations",
                    Description = "从装配体采集方程式到当前机型"
                },
                new BridgeActionInfo
                {
                    Name = "list_current_variant_equations",
                    Description = "查看当前机型方程式（可选参数: argument=关键词）"
                },
                new BridgeActionInfo
                {
                    Name = "update_current_variant_equation",
                    Description = "更新当前机型方程值（参数: equationVariable, equationValue, applyNow）"
                }
            };

            var commandActions = _commandRegistry.Values
                .Select(method => method.GetCustomAttribute<CommandAttribute>())
                .Where(attr => attr != null)
                .Select(attr => new BridgeActionInfo
                {
                    Name = attr!.LocalizedName ?? attr.Name ?? string.Empty,
                    Description = attr.Tooltip ?? "执行插件命令"
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Name))
                .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First());

            result.AddRange(commandActions);
            return result;
        }

        private BridgeResponse ListCurrentVariantEquations(string keyword)
        {
            var control = GetEquationModelTaskPaneControl();
            if (control == null)
            {
                return BridgeResponse.Fail("机型方程式任务窗格未初始化");
            }

            bool ok = control.TryGetCurrentVariantEquationsFromAi(keyword, out var equations, out string message);
            if (!ok)
            {
                return BridgeResponse.Fail(message);
            }

            var data = equations.Select(e => new BridgeEquationInfo
            {
                Part = e.PartDisplayLabel ?? "",
                Index = e.EquationIndex + 1,
                Variable = e.VariableName ?? "",
                Value = e.ValueExpression ?? ""
            }).ToList();

            return new BridgeResponse
            {
                Success = true,
                Message = message,
                Equations = data
            };
        }

        private BridgeResponse UpdateCurrentVariantEquation(BridgeRequest request)
        {
            var control = GetEquationModelTaskPaneControl();
            if (control == null)
            {
                return BridgeResponse.Fail("机型方程式任务窗格未初始化");
            }

            string variableName = (request.EquationVariable ?? string.Empty).Trim();
            string newValue = (request.EquationValue ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(variableName) && !string.IsNullOrWhiteSpace(request.Argument))
            {
                ParseEquationArgument(request.Argument, out variableName, out newValue);
            }

            if (string.IsNullOrWhiteSpace(variableName))
            {
                return BridgeResponse.Fail("equationVariable 不能为空");
            }

            if (string.IsNullOrWhiteSpace(newValue))
            {
                return BridgeResponse.Fail("equationValue 不能为空");
            }

            bool ok = control.TryUpdateCurrentVariantEquationFromAi(variableName, newValue, request.ApplyNow, out string message);
            return ok ? BridgeResponse.Ok(message) : BridgeResponse.Fail(message);
        }

        private static void ParseEquationArgument(string argument, out string variableName, out string value)
        {
            variableName = "";
            value = "";
            string text = (argument ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            int idx = text.IndexOf('=');
            if (idx <= 0 || idx >= text.Length - 1)
            {
                return;
            }

            variableName = text.Substring(0, idx).Trim();
            value = text.Substring(idx + 1).Trim();
        }

        private BridgeResponse RunOnMainThread(Func<BridgeResponse> action)
        {
            var mainContext = GetMainSynchronizationContext();
            if (mainContext == null || SynchronizationContext.Current == mainContext)
            {
                return action();
            }

            BridgeResponse? result = null;
            Exception? dispatchException = null;
            using var done = new ManualResetEventSlim(false);

            mainContext.Post(_ =>
            {
                try
                {
                    result = action();
                }
                catch (Exception ex)
                {
                    dispatchException = ex;
                }
                finally
                {
                    done.Set();
                }
            }, null);

            if (!done.Wait(TimeSpan.FromSeconds(120)))
            {
                return BridgeResponse.Fail("命令执行超时");
            }

            if (dispatchException != null)
            {
                return BridgeResponse.Fail(dispatchException.Message);
            }

            return result ?? BridgeResponse.Fail("未知错误");
        }
    }
}
