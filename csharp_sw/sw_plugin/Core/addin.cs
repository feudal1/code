using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swpublished;
using SolidWorksTools;
using System;
using System.IO;
using System.Runtime.InteropServices;
using tools;
using System.Diagnostics;
using SolidWorks.Interop.swconst;
using SolidWorksAddinStudy.Services;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace SolidWorksAddinStudy
{
    [Guid("D9C5D3A4-3B9F-4ACF-BC19-6D77D39C47CD"), ComVisible(true)]
    [SwAddin(
    Description = "SolidWorksAddinStudy description",
    Title = "SolidWorksAddinStudy",
    LoadAtStartup = true
    )]
    public partial class AddinStudy : ISwAddin
    {
        
        private static SldWorks? swApp;
        private static ICommandManager? iCmdMgr;
        private static int addinCookieID;
        private static bool consoleOpened = false;
        private static ConsoleOutputForm? consoleForm;
        private static System.Threading.SynchronizationContext? mainSynchronizationContext;
        private static EscCancelMessageFilter? escCancelMessageFilter;
        
        // 任务窗格相关
        private static ITaskpaneView? partStatusTaskPaneView;
        private static PartStatusControl? partStatusTaskPaneControl;
        private static ITaskpaneView? equationModelTaskPaneView;
        private static EquationModelTaskPaneControl? equationModelTaskPaneControl;

        /// <summary>最近一次「导出选中子装配体为 SLDPRT」成功后的文件完整路径。</summary>
        public static string? LastExportedSubAsmSldprtPath { get; set; }

        /// <summary>
        /// 显示输出窗口
        /// </summary>
        public static void ShowOutputWindow()
        {
            if (consoleForm == null || consoleForm.IsDisposed)
            {
                consoleForm = new ConsoleOutputForm();
               // 设置窗口置顶
                consoleForm.Show();
                consoleForm.StartIntercept();
            }
            else
            {
                if (consoleForm.WindowState == FormWindowState.Minimized)
                {
                    consoleForm.WindowState = FormWindowState.Normal;
                }
              
                consoleForm.BringToFront();
            }
        }

        /// <summary>
        /// 关闭输出窗口
        /// </summary>
        public static void CloseOutputWindow()
        {
            if (consoleForm != null && !consoleForm.IsDisposed)
            {
                consoleForm.StopIntercept();
                consoleForm.Close();
                consoleForm = null;
            }
        }

        [DllImport("kernel32.dll")]
        private static extern bool AllocConsole();

        [DllImport("kernel32.dll")]
        private static extern bool FreeConsole();
        
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();
        
        [DllImport("user32.dll")]
        private static extern IntPtr GetSystemMenu(IntPtr hWnd, bool bRevert);
        
        [DllImport("user32.dll")]
        private static extern bool RemoveMenu(IntPtr hMenu, uint uPosition, uint uFlags);
        
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);
        
        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;
        private const uint SC_CLOSE = 0xF060;
        private const uint MF_BYCOMMAND = 0x00000000;

        public bool ConnectToSW(object ThisSW, int Cookie)
        {
            swApp = (SldWorks)ThisSW;
            mainSynchronizationContext = System.Threading.SynchronizationContext.Current;
            addinCookieID = Cookie;
            swApp.SetAddinCallbackInfo(0, this, addinCookieID);
            iCmdMgr = swApp.GetCommandManager(addinCookieID);
            
            Debug.WriteLine("插件已加载...");
            
            // 初始化全局 SolidWorks 上下文
            var swModel = (ModelDoc2)swApp.ActiveDoc;
   
            
            // 初始化命令注册表
            InitializeCommandRegistry();
            

 
            AddCommandMgr();
            ShowWelcomeImage();
            
            PopupMenuInitialize();
            
            // 初始化任务窗格
            InitializeTaskPane();

            if (escCancelMessageFilter == null)
            {
                escCancelMessageFilter = new EscCancelMessageFilter(swApp);
                Application.AddMessageFilter(escCancelMessageFilter);
            }

            StartCommandBridgeServer();

            return true;
        }
/// <summary>
        /// 获取程序集构建时间
        /// </summary>
        private DateTime GetBuildTime()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            string filePath = assembly.Location;
            return new FileInfo(filePath).LastWriteTime;
        }
        
        private void ShowWelcomeImage()
        {
            try
        {
              string pluginDir = Path.GetDirectoryName(typeof(AddinStudy).Assembly.Location);
            string imagePath = Path.Combine(pluginDir, "welcome.png");
                if (!File.Exists(imagePath))
            {
                    System.Windows.Forms.MessageBox.Show($"图片文件不存在：{imagePath}");
                return;
                }
              

                using (Image img = Image.FromFile(imagePath))
            {
                // 获取构建时间
                DateTime buildTime = GetBuildTime();
                string versionText = $"版本: {buildTime:yyyy-MM-dd HH:mm}";
                
                Form form = new Form
                {
                    Text = versionText,
                    Size = new Size(img.Width + 40, img.Height + 90),
                    StartPosition = FormStartPosition.CenterScreen,
                    TopMost = true,
                    FormBorderStyle = FormBorderStyle.FixedDialog,
                    MaximizeBox = false,
                    MinimizeBox = false,
               
                };
                
                PictureBox pictureBox = new PictureBox
                {
                    Image = img,
                    SizeMode = PictureBoxSizeMode.AutoSize,
                    Location = new Point(10, 0)
                };
                
             
          
                
                Button okButton = new Button
                {
                    Text = "清空所有工程文件？ (5)",
                    DialogResult = DialogResult.OK,
                    Location = new Point((form.Width - 200) / 2, form.Height - 80),
                    Size = new Size(200, 30)
                };
                
                form.Controls.Add(pictureBox);
             
                form.Controls.Add(okButton);
                form.AcceptButton = okButton;
                      System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer
                {
                    Interval = 1000
                };
                int countdown = 5;
                timer.Tick += (s, e) =>
                {
                    countdown--;
                    if (countdown <= 0)
                    {
                        timer.Stop();
                        form.Close();
                    }
                    else
                    {
                        okButton.Text = $"清空所有工程文件？  ({countdown})";
                    }
                };
                timer.Start();
                form.ShowDialog();
            }
        }
        catch (Exception ex)
        {
                System.Windows.Forms.MessageBox.Show($"显示图片失败：{ex.Message}");
        }
    }
        public bool DisconnectFromSW()
        {
            Debug.WriteLine("插件已卸载");
            StopCommandBridgeServer();

            // 清理任务窗格
            CleanupTaskPane();
            if (escCancelMessageFilter != null)
            {
                Application.RemoveMessageFilter(escCancelMessageFilter);
                escCancelMessageFilter = null;
            }
        
        
            return true;
        }


        /// <summary>
        /// 决定此命令在该环境下是否可用
        /// </summary>
        public int EnableFunction(string data)
        {
            return 1;
        }

       


  

        /// <summary>
        /// 比较 ID 集
        /// </summary>
        private bool CompareIDs(int[] storedIDs, int[] addinIDs)
        {
            List<int> storedList = new List<int>(storedIDs);
            List<int> addinList = new List<int>(addinIDs);

            addinList.Sort();
            storedList.Sort();

            if (addinList.Count != storedList.Count)
            {
                return false;
            }

            for (int i = 0; i < addinList.Count; i++)
            {
                if (addinList[i] != storedList[i])
                {
                    return false;
                }
            }
            return true;
        }

 #region SolidWorks Registration
 
        [ComRegisterFunctionAttribute]
        public static void RegisterFunction(Type t)
        {
            #region Get Custom Attribute: SwAddinAttribute
 
            SwAddinAttribute SWattr = null;
            Type type = typeof(AddinStudy );
 
            foreach (System.Attribute attr in type.GetCustomAttributes(false))
            {
                if (attr is SwAddinAttribute)
                {
                    SWattr = attr as SwAddinAttribute;
                    break;
                }
            }
 
            #endregion Get Custom Attribute: SwAddinAttribute
 
            try
            {
                Microsoft.Win32.RegistryKey hklm = Microsoft.Win32.Registry.LocalMachine;
                Microsoft.Win32.RegistryKey hkcu = Microsoft.Win32.Registry.CurrentUser;
 
                string keyname = "SOFTWARE\\SolidWorks\\Addins\\{" + t.GUID.ToString() + "}";
                Microsoft.Win32.RegistryKey addinkey = hklm.CreateSubKey(keyname);
                addinkey.SetValue(null, 0);
 
                addinkey.SetValue("Description", SWattr.Description);
                addinkey.SetValue("Title", SWattr.Title);
 
                keyname = "Software\\SolidWorks\\AddInsStartup\\{" + t.GUID.ToString() + "}";
                addinkey = hkcu.CreateSubKey(keyname);
                addinkey.SetValue(null, Convert.ToInt32(SWattr.LoadAtStartup), Microsoft.Win32.RegistryValueKind.DWord);
            }
            catch (System.NullReferenceException nl)
            {
                Debug.WriteLine("There was a problem registering this dll: SWattr is null. \n\"" + nl.Message + "\"");
                
            }
            catch (System.Exception e)
            {
                Debug.WriteLine(e.Message);
 
            }
        }
 
        [ComUnregisterFunctionAttribute]
        public static void UnregisterFunction(Type t)
        {
            try
            {
                Microsoft.Win32.RegistryKey hklm = Microsoft.Win32.Registry.LocalMachine;
                Microsoft.Win32.RegistryKey hkcu = Microsoft.Win32.Registry.CurrentUser;
 
                string keyname = "SOFTWARE\\SolidWorks\\Addins\\{" + t.GUID.ToString() + "}";
                hklm.DeleteSubKey(keyname);
 
                keyname = "Software\\SolidWorks\\AddInsStartup\\{" + t.GUID.ToString() + "}";
                hkcu.DeleteSubKey(keyname);
            }
            catch (System.NullReferenceException nl)
            {
                Debug.WriteLine("There was a problem unregistering this dll: " + nl.Message);
                
            }
            catch (System.Exception e)
            {
                Debug.WriteLine("There was a problem unregistering this dll: " + e.Message);
                
            }
        }
 
        #endregion SolidWorks Registration

        /// <summary>
        /// 初始化任务窗格
        /// </summary>
        private void InitializeTaskPane()
        {
            try
            {
                if (swApp == null) return;
                
                // 如果已经存在任务窗格，先清理
                CleanupTaskPane();

                MachineProjectContext.LoadFromDisk();

                partStatusTaskPaneView = swApp.CreateTaskpaneView2("", "零件状态");
                if (partStatusTaskPaneView != null)
                {
                    partStatusTaskPaneControl = new PartStatusControl(swApp);
                    partStatusTaskPaneView.DisplayWindowFromHandlex64(partStatusTaskPaneControl.Handle.ToInt64());
                    Debug.WriteLine("零件状态任务窗格已初始化（随当前装配体）");
                }

                equationModelTaskPaneView = swApp.CreateTaskpaneView2("", "机型管理");
                if (equationModelTaskPaneView != null)
                {
                    equationModelTaskPaneControl = new EquationModelTaskPaneControl();
                    equationModelTaskPaneView.DisplayWindowFromHandlex64(equationModelTaskPaneControl.Handle.ToInt64());
                    Debug.WriteLine("机型管理任务窗格已初始化");
                }

            }
            catch (Exception ex)
            {
                Debug.WriteLine($"初始化任务窗格失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 清理任务窗格
        /// </summary>
        private void CleanupTaskPane()
        {
            try
            {
                if (partStatusTaskPaneControl != null)
                {
                    partStatusTaskPaneControl.Dispose();
                    partStatusTaskPaneControl = null;
                }

                if (equationModelTaskPaneControl != null)
                {
                    equationModelTaskPaneControl.Dispose();
                    equationModelTaskPaneControl = null;
                }
                
                if (partStatusTaskPaneView != null)
                {
                    partStatusTaskPaneView.DeleteView();
                    partStatusTaskPaneView = null;
                }

                if (equationModelTaskPaneView != null)
                {
                    equationModelTaskPaneView.DeleteView();
                    equationModelTaskPaneView = null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"清理任务窗格失败: {ex.Message}");
            }
        }
        
        /// <summary>
        /// 获取任务窗格控件实例
        /// </summary>
        public static PartStatusControl? GetTaskPaneControl()
        {
            return partStatusTaskPaneControl;
        }

        public static bool FocusPartStatusTaskPane()
        {
            if (partStatusTaskPaneControl == null)
            {
                return false;
            }

            try
            {
                partStatusTaskPaneControl.Select();
                partStatusTaskPaneControl.Focus();
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static EquationModelTaskPaneControl? GetEquationModelTaskPaneControl()
        {
            return equationModelTaskPaneControl;
        }

        public static SldWorks? GetSwApp()
        {
            return swApp;
        }

        public static System.Threading.SynchronizationContext? GetMainSynchronizationContext()
        {
            return mainSynchronizationContext;
        }
        
}}

