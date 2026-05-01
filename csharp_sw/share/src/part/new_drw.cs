using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using View = SolidWorks.Interop.sldworks.View;

namespace tools
{
    public class New_drw
    {
        private const string BendNoteFontName = "宋体";

        static public void run(SldWorks swApp, ModelDoc2 swModel)
        {
            try
            {
                var partdoc = swModel;
                string fullpath= swModel .GetPathName();
                string drwpath = Path.ChangeExtension(fullpath, ".slddrw");
                 
                 // 检查工程图文件是否已存在
                 if (File.Exists(drwpath))
                 {
                     Console.WriteLine($"工程图已存在，直接打开：{drwpath}");
                  swApp.OpenDoc(drwpath, (int)swDocumentTypes_e.swDocDRAWING);
                    
                  return;
                 }
                 
                // SolidWorks 宿主下 AppContext.BaseDirectory 往往是 SW 安装目录，应用 share.dll 所在目录
                string? dllDir = Path.GetDirectoryName(typeof(New_drw).Assembly.Location);
                if (string.IsNullOrEmpty(dllDir))
                    dllDir = AppContext.BaseDirectory;
                string templatePath = Path.Combine(dllDir, "my_a4.drwdot");
                if (!File.Exists(templatePath))
                    templatePath = Path.Combine(dllDir, "my_a4.DRWDOT");
                Console.WriteLine($"工程图模板 DLL 目录: {dllDir}");
                Console.WriteLine($"工程图模板路径: {templatePath}");
                Console.WriteLine($"工程图模板文件存在: {File.Exists(templatePath)}");
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
                    ApplyDrawingDocumentFont(swModel, BendNoteFontName);
                 
                    
                        DrawingDoc drawingDoc = (DrawingDoc)swModel;
                      

                drawingDoc.GenerateViewPaletteViews(fullpath);
              
               var view1 = CreateLargestAreaModelView(swModel, drawingDoc, fullpath, 0.08, 0.10);
                if (view1 == null)
                {
                    Console.WriteLine("自动选主视图失败，回退到 *上视/*Top");
                    view1 = drawingDoc.CreateDrawViewFromModelView3(fullpath, "*上视", 0.08, 0.10, 0);
                    if (view1 == null) view1 = drawingDoc.CreateDrawViewFromModelView3(fullpath, "*Top", 0.08, 0.10, 0);
                }
                if (view1 == null)
                {
                    Console.WriteLine("主视图创建失败，终止当前工程图流程");
                    return;
                }
                 swModel.Extension.SelectByID2(view1.Name, "DRAWINGVIEW", 0, 0, 0, false, 0, null, 0);
                var view2 = drawingDoc.CreateUnfoldedViewAt3(0.20, 0.10, 0, false);
                            swModel.Extension.SelectByID2(view1.Name, "DRAWINGVIEW", 0, 0, 0, false, 0, null, 0);
                var view3 = drawingDoc.CreateUnfoldedViewAt3(0.08, 0.15, 0, false);

               swModel.Extension.SelectByID2(view1.Name, "DRAWINGVIEW", 0, 0, 0, false, 0, null, 0);
                    swModel.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDisplaySketches, false);

            
              // 创建平板视图（view4），后续仅对这个视图内的注解改字体高度
              var view4 = drawingDoc.CreateFlatPatternViewFromModelView3(partdoc.GetPathName(), "", 0.20, 0.15, 0, false, false);
              Console.WriteLine(view4 == null ? "view4 创建失败" : $"view4 创建成功，名称：{view4.Name}");

              // 注意：这里是米单位，0.0035 = 3.5mm
              SetViewTextCharHeight(swModel, view4, 0.35);
        
                     var boolstatus =
                swModel.Extension.SetUserPreferenceDouble((int)swUserPreferenceDoubleValue_e.swDetailingArrowWidth, 0,
                    0.002);
            boolstatus =
                swModel.Extension.SetUserPreferenceDouble((int)swUserPreferenceDoubleValue_e.swDetailingArrowHeight,
                    0, 0.0005);
            boolstatus =
                swModel.Extension.SetUserPreferenceDouble((int)swUserPreferenceDoubleValue_e.swDetailingArrowLength,
                    0, 0.0031);
            boolstatus =
                swModel.Extension.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDetailingAutoInsertCenterMarksForHoles,
                    0, false);
            boolstatus =
                swModel.Extension.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDetailingAutoInsertDowelSymbols,
                    0, false);
           swApp.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swDxfVersion, (int)swDxfFormat_e.swDxfFormat_R2000);
              Console.WriteLine("swDxfVersion:R2000");
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

        /// <summary>
        /// 仅修改指定视图中注解的文字高度，不影响全局文档偏好。
        /// </summary>
        private static void SetViewTextCharHeight(ModelDoc2 swModel, View? swView, double charHeight)
        {
            if (swView == null)
            {
                Console.WriteLine("SetViewTextCharHeight 终止：视图为空");
                return;
            }

            object[]? annotations = swView.GetAnnotations() as object[];
            if (annotations == null || annotations.Length == 0)
            {
                Console.WriteLine($"视图[{swView.Name}]没有可处理的注解");
                return;
            }

            Console.WriteLine($"开始处理视图[{swView.Name}]注解，数量：{annotations.Length}，目标字高：{charHeight}");
            int successCount = 0;
            int skipCount = 0;
            int failCount = 0;

            foreach (object item in annotations)
            {
                if (item == null)
                {
                    skipCount++;
                    continue;
                }
                Annotation? annotation = item as Annotation;
                if (annotation == null)
                {
                    skipCount++;
                    continue;
                }

                try
                {
                    int annotationType = annotation.GetType();
                    string annotationTypeName = Enum.GetName(typeof(swAnnotationType_e), annotationType) ?? $"Unknown({annotationType})";
                    string annotationName = annotation.GetName();
                    string annotationText = GetAnnotationText(annotation);
                    Console.WriteLine($"注解信息 => 名称:[{annotationName}] 类型:[{annotationTypeName}] 文本:[{annotationText}]");

                    TextFormat? textFormat = annotation.GetTextFormat(0) as TextFormat;
                    if (textFormat == null)
                    {
                        skipCount++;
                        Console.WriteLine($"跳过注解[{annotation.GetName()}]：无法获取 TextFormat");
                        continue;
                    }

                    double oldHeight = textFormat.CharHeight;
                    int oldHeightPts = textFormat.CharHeightInPts;
                    string oldFontName = textFormat.TypeFaceName;
                    int targetPts = (int)Math.Round(charHeight * 1000.0 / 25.4 * 72.0); // 米 -> pt

                    textFormat.CharHeight = charHeight;
                    textFormat.CharHeightInPts = targetPts;
                    textFormat.TypeFaceName = BendNoteFontName;

                    bool useDocBefore = annotation.GetUseDocTextFormat(0);

                    // 严格按 API 说明：先 useDoc=true 且 TextFormat=null，再切回 useDoc=false
                    bool setDocOn = annotation.ISetTextFormat(0, true, null);
                    if (!setDocOn)
                    {
                        setDocOn = annotation.SetTextFormat(-1, true, null);
                    }

                    bool rewriteNoteOk = false;
                    if (annotationType == (int)swAnnotationType_e.swNote)
                    {
                        Note? note = annotation.GetSpecificAnnotation() as Note;
                        if (note != null)
                        {
                            string noteText = note.GetText() ?? string.Empty;
                            rewriteNoteOk = note.SetText(noteText);
                        }
                    }

                    bool setAllOk = annotation.ISetTextFormat(0, false, textFormat);
                    if (!setAllOk)
                    {
                        setAllOk = annotation.SetTextFormat(-1, false, textFormat);
                    }

                    bool setFirstOk = annotation.SetTextFormat(0, false, textFormat);
                    bool setOk = setAllOk || setFirstOk;
                    bool useDocAfter = annotation.GetUseDocTextFormat(0);
                    Console.WriteLine(
                        $"SetTextFormat结果[{annotationTypeName}]：useDoc[{useDocBefore}->{useDocAfter}] doc=true[{setDocOn}] -> doc=false[-1:{setAllOk},0:{setFirstOk}] rewriteNote[{rewriteNoteOk}]，字体：{oldFontName}->{BendNoteFontName}");

                    if (annotationType == (int)swAnnotationType_e.swDisplayDimension)
                    {
                        DisplayDimension? displayDimension = annotation.GetSpecificAnnotation() as DisplayDimension;
                        if (displayDimension != null)
                        {
                            // 关闭尺寸引出线“使用文档设置”，避免仍然继承文档线型
                            bool setExtLineStyleOk = displayDimension.SetLineFontExtensionStyle(false, 0);
                            Console.WriteLine($"DisplayDimension 引出线样式设置：useDoc=false, style=0, 结果={setExtLineStyleOk}");
                        }
                    }

                    Console.WriteLine(
                        $"应用字高[{annotationName}] SetTextFormat={setOk}, CharHeight:{oldHeight:F6}->{charHeight:F6}, CharHeightInPts:{oldHeightPts}->{targetPts}");
                    successCount++;
                }
                catch (Exception ex)
                {
                    failCount++;
                    Console.WriteLine($"设置视图注解文字高度失败：{ex.Message}");
                }
            }

            Console.WriteLine($"视图[{swView.Name}]文字高度处理完成：成功 {successCount}，跳过 {skipCount}，失败 {failCount}");
        }

        /// <summary>
        /// 尽可能提取注解文本，便于在输出窗排查目标注解。
        /// </summary>
        private static string GetAnnotationText(Annotation annotation)
        {
            if (annotation == null) return "<null>";

            try
            {
                int annotationType = annotation.GetType();
                if (annotationType == (int)swAnnotationType_e.swDisplayDimension)
                {
                    DisplayDimension? displayDimension = annotation.GetSpecificAnnotation() as DisplayDimension;
                    if (displayDimension == null) return "<displayDimension:null>";

                    string prefix = displayDimension.GetText((int)swDimensionTextParts_e.swDimensionTextPrefix) ?? "";
                    string calloutAbove = displayDimension.GetText((int)swDimensionTextParts_e.swDimensionTextCalloutAbove) ?? "";
                    string calloutBelow = displayDimension.GetText((int)swDimensionTextParts_e.swDimensionTextCalloutBelow) ?? "";
                    string suffix = displayDimension.GetText((int)swDimensionTextParts_e.swDimensionTextSuffix) ?? "";

                    string dimText = $"{prefix} {calloutAbove} {calloutBelow} {suffix}".Trim();
                    return string.IsNullOrWhiteSpace(dimText) ? "<displayDimension:empty>" : dimText;
                }

                Note? note = annotation.GetSpecificAnnotation() as Note;
                if (note != null)
                {
                    string noteText = note.GetText() ?? "";
                    return string.IsNullOrWhiteSpace(noteText) ? "<note:empty>" : noteText;
                }

                return "<该类型暂不支持直接读取文本>";
            }
            catch (Exception ex)
            {
                return $"<读取文本失败:{ex.Message}>";
            }
        }

        private static void ApplyDrawingDocumentFont(ModelDoc2 swModel, string fontName)
        {
            try
            {
                TextFormat? noteFormat = swModel.GetUserPreferenceTextFormat((int)swUserPreferenceTextFormat_e.swDetailingNoteTextFormat) as TextFormat;
                if (noteFormat != null)
                {
                    noteFormat.TypeFaceName = fontName;
                    bool noteOk = swModel.SetUserPreferenceTextFormat((int)swUserPreferenceTextFormat_e.swDetailingNoteTextFormat, noteFormat);
                    Console.WriteLine($"文档Note字体设置：{fontName}，结果={noteOk}");
                }

                TextFormat? dimFormat = swModel.GetUserPreferenceTextFormat((int)swUserPreferenceTextFormat_e.swDetailingDimensionTextFormat) as TextFormat;
                if (dimFormat != null)
                {
                    dimFormat.TypeFaceName = fontName;
                    bool dimOk = swModel.SetUserPreferenceTextFormat((int)swUserPreferenceTextFormat_e.swDetailingDimensionTextFormat, dimFormat);
                    Console.WriteLine($"文档Dimension字体设置：{fontName}，结果={dimOk}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"设置文档字体失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 在若干候选方向中创建并比较视图外框面积，保留面积最大的视图作为主视图。
        /// </summary>
        private static View? CreateLargestAreaModelView(ModelDoc2 swModel, DrawingDoc drawingDoc, string modelPath, double x, double y)
        {
            // 常规只在三个主方向中选主视图，兼顾速度与稳定性
            string[] candidateNames = new[]
            {
                "*前视", "*Front",
                "*上视", "*Top",
                "*右视", "*Right"
            }.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

            View? bestView = null;
            double bestArea = -1.0;

            foreach (string viewName in candidateNames)
            {
                View? currentView = drawingDoc.CreateDrawViewFromModelView3(modelPath, viewName, x, y, 0);
                if (currentView == null) continue;

                double currentArea = GetViewOutlineArea(currentView);
                Console.WriteLine($"候选视图[{viewName}] 面积={currentArea:F6}");

                if (currentArea > bestArea)
                {
                    if (bestView != null)
                    {
                        DeleteDrawingView(swModel, bestView);
                    }
                    bestView = currentView;
                    bestArea = currentArea;
                }
                else
                {
                    DeleteDrawingView(swModel, currentView);
                }
            }

            if (bestView != null)
            {
                Console.WriteLine($"主视图选择结果：{bestView.Name}，面积={bestArea:F6}");
            }

            return bestView;
        }

        private static double GetViewOutlineArea(View view)
        {
            try
            {
                double[]? outline = view.GetOutline() as double[];
                if (outline == null || outline.Length < 4) return 0.0;

                double width = Math.Abs(outline[2] - outline[0]);
                double height = Math.Abs(outline[3] - outline[1]);
                return width * height;
            }
            catch
            {
                return 0.0;
            }
        }

        private static void DeleteDrawingView(ModelDoc2 swModel, View view)
        {
            try
            {
                bool selected = swModel.Extension.SelectByID2(view.Name, "DRAWINGVIEW", 0, 0, 0, false, 0, null, 0);
                if (selected)
                {
                    swModel.EditDelete();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"删除视图[{view.Name}]失败：{ex.Message}");
            }
        }

    }
}