namespace cad_plugin;
 
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Collections.Generic;
using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using System.Windows.Forms;
 
[ComVisible(true)]
public partial class CadPluginCommands
{
    [ComRegisterFunction]
    public static void RegisterFunction(Type t)
    {
        // 注意：此方法在 regasm 注册时会被调用，此时 AutoCAD 未运行
        // 所以不能使用 Autodesk.AutoCAD.ApplicationServices.Application
        // 注册逻辑已移至 register.bat 脚本中直接操作注册表
    }
 
    [ComUnregisterFunction]
    public static void UnregisterFunction(Type t)
    {
        try
        {
            Microsoft.Win32.RegistryKey localMachine = Microsoft.Win32.Registry.LocalMachine;
            string baseKey = "SOFTWARE\\Autodesk\\AutoCAD";
            Microsoft.Win32.RegistryKey acadKey = localMachine.OpenSubKey(baseKey);
            
            if (acadKey == null)
            {
                MessageBox.Show("未找到 AutoCAD 注册表路径", "卸载提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            
            // 遍历所有版本查找并删除插件注册
            bool found = false;
            foreach (string version in acadKey.GetSubKeyNames())
            {
                string versionPath = $"{baseKey}\\{version}";
                Microsoft.Win32.RegistryKey versionKey = localMachine.OpenSubKey(versionPath);
                
                if (versionKey != null)
                {
                    foreach (string product in versionKey.GetSubKeyNames())
                    {
                        if (product.StartsWith("ACAD-"))
                        {
                            string keyname = $"{versionPath}\\{product}\\Applications\\mycad";
                            try
                            {
                                localMachine.DeleteSubKey(keyname, false);
                                found = true;
                                MessageBox.Show($"插件卸载成功!\n\n删除路径: {keyname}", "卸载成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            }
                            catch { }
                            break;
                        }
                    }
                    versionKey.Close();
                }
                
                if (found) break;
            }
            
            if (!found)
            {
                MessageBox.Show("未找到需要卸载的插件注册项", "卸载提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            
            acadKey.Close();
        }
        catch (System.Exception e)
        {
            MessageBox.Show($"卸载时发生错误:\n{e.Message}", "卸载错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

// 实现 IExtensionApplication 接口，在插件加载时自动初始化
public class PluginInitializer : IExtensionApplication
{
    public void Initialize()
    {
        // 插件加载时自动执行的初始化代码
        Editor ed = Autodesk.AutoCAD.ApplicationServices.Application.DocumentManager.MdiActiveDocument?.Editor;
        if (ed != null)
        {
            ed.WriteMessage("\n========================================\n");
            ed.WriteMessage("CAD 插件已成功加载!\n");
            ed.WriteMessage("可用命令: HELLO\n");
            ed.WriteMessage("========================================\n");
        }
    }

    public void Terminate()
    {
        // 插件卸载时的清理代码
    }
}