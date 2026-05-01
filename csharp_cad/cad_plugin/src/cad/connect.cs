using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Autodesk.AutoCAD.Interop;
using Autodesk.AutoCAD.Interop.Common;
using com_tools;

namespace cad_tools
{
    class CadConnect
    {
        // 缓存已连接的 AutoCAD 实例
        static private AcadApplication? _cachedAcadApp = null;
        
        /// <summary>
        /// 获取或创建 AutoCAD 实例 (自动检测最佳版本)
        /// </summary>
        static public AcadApplication? GetOrCreateInstance()
        { 
            // 如果已有缓存实例，先验证是否有效
            if (_cachedAcadApp != null)
            {
                try
                {
                    // 尝试访问一个简单的属性来验证连接是否有效
                    var appName = _cachedAcadApp.Name;
                    return _cachedAcadApp;
                }
                catch
                {
                    // 连接已失效，清空缓存
                    _cachedAcadApp = null;
                }
            }
            // 尝试获取所有已安装的 AutoCAD 版本
            var installedVersions = GetInstalledAutoCADVersions();
                    
            if (installedVersions.Count == 0)
            {
                Console.WriteLine("错误：未检测到任何 AutoCAD 版本。");
                return null;
            }
                    
            Console.WriteLine($"[调试] 检测到 {installedVersions.Count} 个 AutoCAD 版本:");
            foreach (var version in installedVersions)
            {
                Console.WriteLine($"  - {version.Key}: {version.Value}");
            }
                    
            // 优先尝试最新版本（倒序遍历）
            var versionKeys = installedVersions.Keys.ToList();
            versionKeys.Reverse();
                    
            foreach (var progId in versionKeys)
            {
                try
                {
                    Console.WriteLine($"\n[调试] 尝试连接：{progId}");
                            
                    // 尝试获取正在运行的实例
                    object comObject = ComHelper.GetActiveObject(progId);
                    Console.WriteLine($"✓ 成功连接到运行中的实例：{progId}");
                    return (AcadApplication)comObject;
                }
                catch (COMException comEx)
                {
                    Console.WriteLine($"  获取运行实例失败：{comEx.Message}");
                            
                    // 尝试创建新实例
                    try
                    {
                        Console.WriteLine($"  尝试创建新实例...");
                        Type? comType = Type.GetTypeFromProgID(progId);
                        if (comType != null)
                        {
                            object? comObject = Activator.CreateInstance(comType);
                            if (comObject != null)
                            {
                                 AcadApplication acadApp = (AcadApplication)comObject;
                                
                                // 关键修复：确保AutoCAD窗口可见
                                acadApp.Visible = true;
                                Console.WriteLine($"✓ 成功创建新实例：{progId}");
                                return (AcadApplication)comObject;
                            }
                            else
                            {
                                Console.WriteLine($"  创建实例返回 null");
                            }
                        }
                        else
                        {
                            Console.WriteLine($"  GetTypeFromProgID 返回 null - 可能未正确注册");
                        }
                    }
                    catch (Exception createEx)
                    {
                        Console.WriteLine($"  创建实例异常：{createEx.GetType().Name} - {createEx.Message}");
                        Console.WriteLine($"  StackTrace: {createEx.StackTrace}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  连接异常：{ex.GetType().Name} - {ex.Message}");
                }
            }
                    
            // 如果所有特定版本都失败，尝试通用 ProgID
            Console.WriteLine("\n[调试] 尝试使用通用 ProgID: AutoCAD.Application");
            try
            {
                object comObject = ComHelper.GetActiveObject("AutoCAD.Application");
                Console.WriteLine($"✓ 成功通过通用 ProgID 连接到运行中的实例");
                return (AcadApplication)comObject;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  通用 ProgID 连接失败：{ex.Message}");
            }
                    
            Console.WriteLine("\n错误：无法连接或创建任何 AutoCAD 实例。");
            Console.WriteLine("建议：请手动启动任意版本的 AutoCAD 后再试。");
            return null;
        }
        
        /// <summary>
        /// 清除缓存的 AutoCAD 实例（用于强制重新连接）
        /// </summary>
        static public void ClearCache()
        {
            _cachedAcadApp = null;
        }
        
        /// <summary>
        /// 从注册表获取所有已安装的 AutoCAD 版本
        /// </summary>
        static private Dictionary<string, string> GetInstalledAutoCADVersions()
        {
            var versions = new Dictionary<string, string>();
            
            // AutoCAD 注册表路径
            string[] registryPaths = new string[]
            {
                @"SOFTWARE\Autodesk\AutoCAD",
                @"SOFTWARE\Wow6432Node\Autodesk\AutoCAD"  // 64 位系统上的 32 位应用
            };
            
            foreach (var regPath in registryPaths)
            {
                try
                {
                    using (var key = Registry.LocalMachine.OpenSubKey(regPath))
                    {
                        if (key != null)
                        {
                            // 获取所有子键（版本号）
                            foreach (var subKeyName in key.GetSubKeyNames())
                            {
                                using (var versionKey = key.OpenSubKey(subKeyName))
                                {
                                    if (versionKey != null)
                                    {
                                        // 尝试获取不同语言的描述
                                        string description = $"AutoCAD {subKeyName}";
                                        
                                        // 尝试读取 ACAD-XXXX:XXX 值来获取更详细信息
                                        var acadValue = versionKey.GetValue("ACAD-XXXX:XXX");
                                        if (acadValue != null)
                                        {
                                            description = acadValue.ToString();
                                        }
                                        
                                        // 构建 ProgID (例如：AutoCAD.Application.24.0)
                                        // 注意：注册表中的子键名可能是 "R24.0" 或 "24.0"，需要统一处理
                                        string versionNumber = subKeyName.StartsWith("R", StringComparison.OrdinalIgnoreCase) 
                                            ? subKeyName.Substring(1) 
                                            : subKeyName;
                                        string progId = $"AutoCAD.Application.{versionNumber}";
                                        
                                        if (!versions.ContainsKey(progId))
                                        {
                                            versions[progId] = description;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[警告] 读取注册表 {regPath} 失败：{ex.Message}");
                }
            }
            
            return versions;
        }
    }
}