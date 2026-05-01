using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace tools
{
    public class Asm_new_drw
    {
        static public void run(SldWorks swApp, ModelDoc2 swModel)
        {
            try
            {
                var asmDoc = swModel;
                string fullpath = swModel.GetPathName();
                string drwpath = swModel.GetPathName().Replace("asm", "ASM").Replace("ASM", "drw");
                
                // 检查工程图文件是否已存在
                if (File.Exists(drwpath))
                {
                    Console.WriteLine($"工程图已存在，直接打开：{drwpath}");
                    swApp.OpenDoc(drwpath, (int)swDocumentTypes_e.swDocDRAWING);
                    return;
                }
                
                                 string? dllDir = Path.GetDirectoryName(typeof(New_drw).Assembly.Location);
                if (string.IsNullOrEmpty(dllDir))
                    dllDir = AppContext.BaseDirectory;
                string templatePath = Path.Combine(dllDir, "my_a4.drwdot");
                if (!File.Exists(templatePath))
                    templatePath = Path.Combine(dllDir, "my_a4.DRWDOT");
                
                
                swApp.NewDocument(templatePath, 0, 0, 0);
                swModel = (ModelDoc2)swApp.ActiveDoc;
                if (swModel.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
                {
                    Console.WriteLine("错误：无法创建工程图");
                    return;
                }
                
                DrawingDoc drawingDoc = (DrawingDoc)swModel;
                
                // 生成视图调色板视图
                drawingDoc.GenerateViewPaletteViews(fullpath);
                
                // 从面板拖拽主视图（类似 DropDrawingViewFromPalette2）
                var view1 = drawingDoc.CreateDrawViewFromModelView3(fullpath, "*前视", 0.117, 0.118, 0);
                if (view1 == null) 
                {
                    Console.WriteLine("view=null");
                    view1 = drawingDoc.CreateDrawViewFromModelView3(fullpath, "*Front", 0.117, 0.118, 0);
                }
                
                // 创建第一个展开视图
                var view2 = drawingDoc.CreateUnfoldedViewAt3(0.117, 0.178, 0, false);
                
                // 选择主视图
                swModel.Extension.SelectByID2(view1.Name, "DRAWINGVIEW", 0, 0, 0, false, 0, null, 0);
                
                // 激活主视图
                drawingDoc.ActivateView(view1.Name);
                
                // 创建第二个展开视图
                var view3 = drawingDoc.CreateUnfoldedViewAt3(0.213, 0.118, 0, false);
                
                // 清除选择
                swModel.ClearSelection2(true);
                
                // 获取活动视图并插入 BOM 表
                IView activeView = (IView)drawingDoc.ActiveDrawingView;
                if (activeView == null)
                {
                    Console.WriteLine("警告：无法获取活动视图");
                    return;
                }
                
                // 获取配置名称
                string configuration = swApp.GetActiveConfigurationName(fullpath);
                
                var scaleRatio = (double)activeView.ScaleDecimal;
                string bomTemplatePath = @"C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\lang\chinese-simplified\bom-standard.sldbomtbt";
                if (!File.Exists(bomTemplatePath))
                {
                    // 如果指定路径不存在，使用备用路径
                    bomTemplatePath = @"E:\solidworks\SOLIDWORKS\lang\chinese-simplified\bom-standard.sldbomtbt";
                    Console.WriteLine($"警告：未找到 BOM 模板 {bomTemplatePath}，使用备用路径");
                }
                BomTableAnnotation bomTable = activeView.InsertBomTable4(
                        false,                           // 使用默认锚点位置
                        0.0505 * scaleRatio,                   // X 位置（米）/ 图纸比例
                        0.045 * scaleRatio,                   // Y 位置（米）/ 图纸比例
                        (int)swBOMConfigurationAnchorType_e.swBOMConfigurationAnchor_BottomLeft,
                        (int)swBomType_e.swBomType_Indented,
                        configuration,                   // 配置名称
                        bomTemplatePath,
                        false,                           // 不覆盖现有文件
                        1,                               // 行数
                        false                            // 不显示对话框
                    );
                    
                    if (bomTable == null)
                    {
                        Console.WriteLine("警告：BOM 表插入失败");
                    }
                   swModel.Extension.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDetailingAutoInsertCenterMarksForHoles,
                    0, false);
           
                swModel.Extension.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDetailingAutoInsertDowelSymbols,
                    0, false);
                // 重建工程图
                swModel.EditRebuild3();
                
                // 激活动图纸
                drawingDoc.ActivateSheet("图纸 1");
               
                     swApp.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swDxfVersion, (int)swDxfFormat_e.swDxfFormat_R2000);
                     Console.WriteLine("swDxfVersion:R2000");
                // 保存工程图
                swModel.SaveAs3(drwpath, 0, 0);
                
                Console.WriteLine($"成功，已创建装配体工程图：{drwpath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发生错误：{ex.Message}");
                Console.WriteLine("提示：请确保 SolidWorks 正在运行。");
            }
        }
    }
}