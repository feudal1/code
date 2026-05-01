using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using Newtonsoft.Json;

namespace tools
{
    internal sealed class SwCommandBridgeClient
    {
        private const string PipeName = "my_c_sw_command_bridge_v1";

        public (bool Success, string Message) Ping()
        {
            var response = SendRequest(new BridgeRequest { Action = "ping" });
            return (response.Success, response.Message);
        }

        public (bool Success, string Message, List<BridgeCommandInfo> Commands) ListCommands()
        {
            var response = SendRequest(new BridgeRequest { Action = "list_commands" });
            return (response.Success, response.Message, response.Commands ?? new List<BridgeCommandInfo>());
        }

        public (bool Success, string Message) ExecuteCommand(string commandName)
        {
            var response = SendRequest(new BridgeRequest
            {
                Action = "execute_command",
                CommandName = commandName ?? string.Empty
            });
            return (response.Success, response.Message);
        }

        public (bool Success, string Message) CreateVariant(string variantName)
        {
            var response = SendRequest(new BridgeRequest
            {
                Action = "create_variant",
                VariantName = variantName ?? string.Empty
            });
            return (response.Success, response.Message);
        }

        public (bool Success, string Message) CaptureAssemblyEquations()
        {
            var response = SendRequest(new BridgeRequest
            {
                Action = "capture_assembly_equations"
            });
            return (response.Success, response.Message);
        }

        public (bool Success, string Message, List<BridgeActionInfo> Actions) ListActions()
        {
            var response = SendRequest(new BridgeRequest { Action = "list_actions" });
            return (response.Success, response.Message, response.Actions ?? new List<BridgeActionInfo>());
        }

        public (bool Success, string Message) InvokeAction(string actionName, string argument = "")
        {
            var response = SendRequest(new BridgeRequest
            {
                Action = "invoke_action",
                ActionName = actionName ?? string.Empty,
                Argument = argument ?? string.Empty
            });
            return (response.Success, response.Message);
        }

        public (bool Success, string Message, List<BridgeEquationInfo> Equations) ListCurrentVariantEquations(string keyword = "")
        {
            var response = SendRequest(new BridgeRequest
            {
                Action = "list_current_variant_equations",
                Keyword = keyword ?? string.Empty
            });
            return (response.Success, response.Message, response.Equations ?? new List<BridgeEquationInfo>());
        }

        public (bool Success, string Message) UpdateCurrentVariantEquation(string variableName, string equationValue, bool applyNow = true)
        {
            var response = SendRequest(new BridgeRequest
            {
                Action = "update_current_variant_equation",
                EquationVariable = variableName ?? string.Empty,
                EquationValue = equationValue ?? string.Empty,
                ApplyNow = applyNow
            });
            return (response.Success, response.Message);
        }

        private BridgeResponse SendRequest(BridgeRequest request)
        {
            using var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
            pipe.Connect(1500);

            using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true)
            {
                AutoFlush = true
            };
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);

            string payload = JsonConvert.SerializeObject(request);
            writer.WriteLine(payload);

            string? responseLine = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(responseLine))
            {
                return new BridgeResponse
                {
                    Success = false,
                    Message = "插件桥接返回空响应"
                };
            }

            return JsonConvert.DeserializeObject<BridgeResponse>(responseLine) ?? new BridgeResponse
            {
                Success = false,
                Message = "插件桥接响应解析失败"
            };
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

        [JsonProperty("commands")]
        public List<BridgeCommandInfo>? Commands { get; set; }

        [JsonProperty("actions")]
        public List<BridgeActionInfo>? Actions { get; set; }

        [JsonProperty("equations")]
        public List<BridgeEquationInfo>? Equations { get; set; }
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
}
