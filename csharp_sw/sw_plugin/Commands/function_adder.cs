   using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swpublished;
using SolidWorksTools;
using System;
using System.IO;
using System.Runtime.InteropServices;
using tools;
using System.Diagnostics;
using SolidWorks.Interop.swconst;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Reflection;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
   namespace SolidWorksAddinStudy
{
   
    public partial class AddinStudy 
{
     // 命令注册表，存储所有通过装饰器注册的命令
     private static Dictionary<int, MethodInfo> _commandRegistry = new Dictionary<int, MethodInfo>();
     private static readonly object _commandActionLogLock = new object();
     
     /// <summary>
     /// 初始化命令注册表，扫描当前类中所有标记了 [Command] 特性的方法
     /// </summary>
     private void InitializeCommandRegistry()
     {
         _commandRegistry.Clear();
         var type = this.GetType();
         
        foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
         {
            var attr = method.GetCustomAttribute<SolidWorksAddinStudy.CommandAttribute>();
             if (attr != null)
             {
                 _commandRegistry[attr.Id] = method;
                Debug.WriteLine($"[命令注册] ID:{attr.Id}, 名称:{attr.Name}, 来源:{attr.Source}");
             }
         }
     }
     /// <summary>
        /// 通过用户点击的菜单 id 来执行不同的动作
        /// </summary>
        public void FunctionProxy(string data)
        {
            int commandId = int.Parse(data);

            if (_commandRegistry.TryGetValue(commandId, out var method))
            {
                try
                {
                    ExecuteRegisteredMethod(method);
                }
                catch (Exception ex)
                {
                    swApp?.SendMsgToUser($"执行命令失败：{ex.Message}");
                }
            }
            else
            {
                swApp?.SendMsgToUser($"未找到命令 ID: {commandId}");
            }
        }

        /// <summary>
        /// 按命令名执行插件命令（供 sw_ai / AI 桥接层调用）
        /// </summary>
        public bool ExecuteCommandByName(string commandName)
        {
            return ExecuteCommandByName(commandName, null);
        }

        public bool ExecuteCommandByName(string commandName, CommandSource? source)
        {
            if (string.IsNullOrWhiteSpace(commandName))
            {
                return false;
            }

            var target = _commandRegistry.Values.FirstOrDefault(method =>
            {
                var attr = method.GetCustomAttribute<SolidWorksAddinStudy.CommandAttribute>();
                return attr != null &&
                       (!source.HasValue || attr.Source == source.Value) &&
                       (string.Equals(attr.LocalizedName, commandName, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(attr.Name, commandName, StringComparison.OrdinalIgnoreCase));
            });

            if (target == null)
            {
                return false;
            }

            ExecuteRegisteredMethod(target);
            return true;
        }

        private void ExecuteRegisteredMethod(MethodInfo method)
        {
            var cmdAttr = method.GetCustomAttribute<SolidWorksAddinStudy.CommandAttribute>();
            bool success = false;
            string message = "ok";
            try
            {
                if (cmdAttr != null && cmdAttr.ShowOutputWindow)
                {
                    ShowOutputWindow();
                }

                method.Invoke(this, null);
                success = true;
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                message = tie.InnerException.Message;
                throw tie.InnerException;
            }
            catch
            {
                message = "执行失败";
                throw;
            }
            finally
            {
                WriteCommandActionLog(cmdAttr, method.Name, success, message);
            }
        }

        private void WriteCommandActionLog(SolidWorksAddinStudy.CommandAttribute? cmdAttr, string methodName, bool success, string message)
        {
            try
            {
                var log = new CommandActionLogEntry
                {
                    Timestamp = DateTime.Now,
                    Source = "sw_command_attribute",
                    ActionName = cmdAttr?.Name ?? methodName,
                    LocalizedName = cmdAttr?.LocalizedName ?? methodName,
                    CommandId = cmdAttr?.Id ?? 0,
                    EntryPoint = cmdAttr?.Source.ToString() ?? CommandSource.CommandBar.ToString(),
                    Success = success,
                    Message = message ?? string.Empty,
                    ActiveDocument = GetCurrentDocSnapshotForActionLog()
                };

                string logDir = GetCommandLogDirectoryForActionLog();
                Directory.CreateDirectory(logDir);
                string logFile = Path.Combine(logDir, "command-executions.jsonl");
                string line = JsonConvert.SerializeObject(log);

                lock (_commandActionLogLock)
                {
                    File.AppendAllText(logFile, line + System.Environment.NewLine, Encoding.UTF8);
                }
            }
            catch
            {
                // 日志失败不影响命令执行
            }
        }

        private CommandActionActiveDocumentSnapshot? GetCurrentDocSnapshotForActionLog()
        {
            try
            {
                if (swApp == null)
                {
                    return null;
                }

                var doc = swApp.ActiveDoc as ModelDoc2;
                if (doc == null)
                {
                    return null;
                }

                string typeText = doc.GetType() switch
                {
                    (int)swDocumentTypes_e.swDocPART => "PART",
                    (int)swDocumentTypes_e.swDocASSEMBLY => "ASSEMBLY",
                    (int)swDocumentTypes_e.swDocDRAWING => "DRAWING",
                    _ => "UNKNOWN"
                };

                return new CommandActionActiveDocumentSnapshot
                {
                    Title = doc.GetTitle() ?? string.Empty,
                    Path = doc.GetPathName() ?? string.Empty,
                    Type = typeText
                };
            }
            catch
            {
                return null;
            }
        }

        private static string GetCommandLogDirectoryForActionLog()
        {
            string localAppData = System.Environment.GetFolderPath(System.Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(localAppData, "my_c", "command_logs");
        }

        private sealed class CommandActionLogEntry
        {
            [JsonProperty("timestamp")]
            public DateTime Timestamp { get; set; }

            [JsonProperty("source")]
            public string Source { get; set; } = "";

            [JsonProperty("actionName")]
            public string ActionName { get; set; } = "";

            [JsonProperty("localizedName")]
            public string LocalizedName { get; set; } = "";

            [JsonProperty("commandId")]
            public int CommandId { get; set; }

            [JsonProperty("entryPoint")]
            public string EntryPoint { get; set; } = "";

            [JsonProperty("success")]
            public bool Success { get; set; }

            [JsonProperty("message")]
            public string Message { get; set; } = "";

            [JsonProperty("activeDocument")]
            public CommandActionActiveDocumentSnapshot? ActiveDocument { get; set; }
        }

        private sealed class CommandActionActiveDocumentSnapshot
        {
            [JsonProperty("title")]
            public string Title { get; set; } = "";

            [JsonProperty("path")]
            public string Path { get; set; } = "";

            [JsonProperty("type")]
            public string Type { get; set; } = "";
        }
   private void AddCommandMgr()
        {
            try
            {
                InitializeCommandRegistry();

                var toolbarCommands = _commandRegistry.Values
                    .Where(m => m.GetCustomAttribute<SolidWorksAddinStudy.CommandAttribute>()?.Source == CommandSource.CommandBar)
                    .Select(m => m.GetCustomAttribute<SolidWorksAddinStudy.CommandAttribute>())
                    .Where(attr => attr != null)
                    .ToArray();

                int mainCmdGroupID = 5001;
                int[] mainItemIds = toolbarCommands.Select(a => a.Id).ToArray();

                int cmdGroupErr = 0;
                bool ignorePrevious = false;

                object registryIDs = null;
                bool getDataResult = iCmdMgr?.GetGroupDataFromRegistry(mainCmdGroupID, out registryIDs) ?? false;

                if (getDataResult && registryIDs != null && registryIDs is int[] regIds)
                {
                    if (!CompareIDs(regIds, mainItemIds))
                    {
                        ignorePrevious = true;
                    }
                }

                // 强制忽略之前的配置，确保重新创建所有 CommandTab
                ignorePrevious = true;

                var cmdGroup = iCmdMgr?.CreateCommandGroup2(
                    mainCmdGroupID,
                    "调试工具",
                    "调试控制台控制",
                    "",
                    -1,
                    ignorePrevious,
                    ref cmdGroupErr
                );

                if (cmdGroup == null) return;

                string[] icons = new string[6];
                for (int i = 0; i < 6; i++)
                {
                    icons[i] = "";
                }
                cmdGroup.IconList = icons;

                int menuToolbarOption = (int)(swCommandItemType_e.swMenuItem | swCommandItemType_e.swToolbarItem);

                foreach (var cmdAttr in toolbarCommands)
                {
                    int cmdIndex = cmdGroup.AddCommandItem2(
                        cmdAttr.Name,
                        -1,
                        cmdAttr.Tooltip,
                        cmdAttr.LocalizedName,
                        0,
                        $"FunctionProxy({cmdAttr.Id})",
                        $"EnableFunction({cmdAttr.Id})",
                        cmdAttr.Id,
                        menuToolbarOption
                    );
                }

                cmdGroup.HasToolbar = true;
                cmdGroup.HasMenu = true;
                cmdGroup.Activate();

                // 按文档类型分组命令
                var allDocTypes = new[] { 
                    (int)swDocumentTypes_e.swDocPART, 
                    (int)swDocumentTypes_e.swDocASSEMBLY, 
                    (int)swDocumentTypes_e.swDocDRAWING 
                };

                foreach (var docType in allDocTypes)
                {
                    // 获取该文档类型应该显示的命令
                    var commandsForDocType = toolbarCommands
                        .Where(attr => attr != null && 
                              (attr.DocumentTypes.Contains(0) || attr.DocumentTypes.Contains(docType)))
                        .ToArray();

                    if (commandsForDocType.Length == 0) continue;

                    string docTypeName = docType == (int)swDocumentTypes_e.swDocPART ? "零件" :
                                        docType == (int)swDocumentTypes_e.swDocASSEMBLY ? "装配体" : "工程图";
                    
                    Debug.WriteLine($"[文档类型:{docTypeName}] 命令数：{commandsForDocType.Length}");
                    foreach (var cmd in commandsForDocType)
                    {
                        Debug.WriteLine($"  - {cmd.Name} (ID:{cmd.Id}, 文档类型:[{string.Join(",", cmd.DocumentTypes)}])");
                    }

                    var cmdTab = iCmdMgr?.GetCommandTab(docType, "调试工具");

                    if (cmdTab != null && (!getDataResult || ignorePrevious))
                    {
                        iCmdMgr?.RemoveCommandTab(cmdTab);
                        cmdTab = null;
                    }

                    if (cmdTab == null)
                    {
                        cmdTab = iCmdMgr?.AddCommandTab(docType, "调试工具");

                        var cmdBox = cmdTab?.AddCommandTabBox();

                        List<int> cmdIDs = new List<int>();
                        List<int> showTextType = new List<int>();

                        foreach (var cmdAttr in commandsForDocType)
                        {
                            int cmdIndexInGroup = Array.IndexOf(mainItemIds, cmdAttr.Id);
                            cmdIDs.Add(cmdGroup.get_CommandID(cmdIndexInGroup));
                            showTextType.Add((int)swCommandTabButtonTextDisplay_e.swCommandTabButton_TextBelow);
                        }

                        cmdBox?.AddCommands(cmdIDs.ToArray(), showTextType.ToArray());
                    }
                }
            }
            catch (Exception ex)
            {
                swApp?.SendMsgToUser($"添加按钮失败：{ex.Message}");
            }
        }

}}