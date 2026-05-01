using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace tools
{
    public class New_drw_cnc
    {
        static public void run(SldWorks swApp, ModelDoc2 swModel)
        {
            try
            {
                var partdoc = swModel;
                string fullpath= swModel .GetPathName();
                int docType = swModel.GetType();
                
                // 根据文档类型生成不同的工程图路径
                string drwpath = "";
                if (docType == (int)swDocumentTypes_e.swDocPART)
                {
                    drwpath = swModel.GetPathName().Replace("prt", "PRT").Replace("PRT", "drw");
                }
                else if (docType == (int)swDocumentTypes_e.swDocASSEMBLY)
                {
                    drwpath = swModel.GetPathName().Replace("sldasm", "SLDASM").Replace("SLDASM", "drw");
                }
                else
                {
                    Console.WriteLine($"不支持的文档类型：{docType}");
                    return;
                }
                 
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
                string templatePath = Path.Combine(dllDir, "space.drwdot");
                if (!File.Exists(templatePath))
                    templatePath = Path.Combine(dllDir, "space.DRWDOT");
              swApp.NewDocument(templatePath, 0, 0, 0);
               
               swModel = (ModelDoc2)swApp.ActiveDoc;
               swModel.Extension.SetUserPreferenceInteger((int)swUserPreferenceIntegerValue_e.swDetailingLinearDimPrecision,
                   (int)swUserPreferenceOption_e.swDetailingDimension, 1);
                  if (swModel.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
                {
                    Console.WriteLine("错误：无法创建工程图");
                    return;
                }
                else
                {
                 
                    
                        DrawingDoc drawingDoc = (DrawingDoc)swModel;
                      
                  
                drawingDoc.GenerateViewPaletteViews(fullpath);

               var view1 = drawingDoc.CreateDrawViewFromModelView3(fullpath, "*上视", 0.08, 0.10, 0);
                if (view1 == null) Console.WriteLine("view=null");
                    if (view1==null) view1 = drawingDoc.CreateDrawViewFromModelView3(fullpath, "*Top", 0.13, 0.22, 0);
                
                // 创建展开视图（零件和装配体都使用相同方式）
                var view2 = drawingDoc.CreateUnfoldedViewAt3(0.20, 0.10, 0, false);
                swModel.Extension.SelectByID2(view1.Name, "DRAWINGVIEW", 0, 0, 0, false, 0, null, 0);
                var view3 = drawingDoc.CreateUnfoldedViewAt3(0.08, 0.15, 0, false);
                
               swModel.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDisplaySketches, false);
              // swModel.Extension.SelectByID2(view1.Name, "DRAWINGVIEW", 0, 0, 0, false, 0, null, 0);
            
                     var boolstatus =
                swModel.Extension.SetUserPreferenceDouble((int)swUserPreferenceDoubleValue_e.swDetailingArrowWidth, 0,
                    0.002);
            boolstatus =
                swModel.Extension.SetUserPreferenceDouble((int)swUserPreferenceDoubleValue_e.swDetailingArrowHeight,
                    0, 0.0005);
            boolstatus =
                swModel.Extension.SetUserPreferenceDouble((int)swUserPreferenceDoubleValue_e.swDetailingArrowLength,
                    0, 0.0031);
           
                          swApp.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swDxfOutputFonts, 1);
                swApp.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swDxfVersion, (int)swDxfFormat_e.swDxfFormat_R12);
                Console.WriteLine("swDxfVersion:R12");
                   swModel.Extension.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDetailingAutoInsertCenterMarksForHoles,
                    0, false);
            boolstatus =
                swModel.Extension.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDetailingAutoInsertDowelSymbols,
                    0, false);
               swModel.EditRebuild3();

              swModel.SaveAs3(drwpath, 0, 0);
  Console.WriteLine($"成功，已创建工程图{drwpath}");
 

                }
            
            
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发生错误：{ex.Message}");
                Console.WriteLine("提示：请确保 SolidWorks 正在运行。");
            }
        }
    }
}
