using System;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using View = SolidWorks.Interop.sldworks.View;

namespace tools
{
    public class selected_view_note_text_size
    {
        private const string BendNoteFontName = "宋体";

        public static void run(SldWorks swApp, ModelDoc2 swModel, double charHeightMeters = 0.85)
        {
            try
            {
                if (swModel == null)
                {
                    Console.WriteLine("错误：没有活动文档。");
                    return;
                }

                if (swModel.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
                {
                    Console.WriteLine("错误：当前文档不是工程图。");
                    return;
                }

                // 某些系统托管折弯说明会按文档样式渲染，先同步文档字体，避免界面仍显示默认字体效果
                ApplyDrawingDocumentFont(swModel, BendNoteFontName);

                var selMgr = (SelectionMgr)swModel.SelectionManager;
                if (selMgr == null || selMgr.GetSelectedObjectType3(1, -1) != (int)swSelectType_e.swSelDRAWINGVIEWS)
                {
                    Console.WriteLine("请先选中一个视图。");
                    return;
                }

                var view = selMgr.GetSelectedObject6(1, -1) as View;
                if (view == null)
                {
                    Console.WriteLine("错误：无法获取选中视图。");
                    return;
                }

                object[]? annotations = view.GetAnnotations() as object[];
                if (annotations == null || annotations.Length == 0)
                {
                    Console.WriteLine($"视图[{view.Name}]没有注解。");
                    return;
                }

                int charHeightPts = (int)Math.Round(charHeightMeters * 1000.0 / 25.4 * 72.0); // m -> pt
                Console.WriteLine($"开始处理选中视图[{view.Name}]，注解数量：{annotations.Length}，目标字高：{charHeightMeters:F6}m ({charHeightPts}pt)");
                int successCount = 0;
                int failCount = 0;

                foreach (object item in annotations)
                {
                    Annotation? annotation = item as Annotation;
                    if (annotation == null) continue;

                    try
                    {
                        TextFormat? textFormat = annotation.GetTextFormat(0) as TextFormat;
                        if (textFormat == null) continue;

                        string annotationName = annotation.GetName();
                        int annotationType = annotation.GetType();
                        string annotationTypeName = Enum.GetName(typeof(swAnnotationType_e), annotationType) ?? annotationType.ToString();
                        string annotationText = GetAnnotationText(annotation);

                        int oldPts = textFormat.CharHeightInPts;
                        double oldMeters = textFormat.CharHeight;
                        string oldFontName = textFormat.TypeFaceName;
                        textFormat.CharHeightInPts = charHeightPts;
                        textFormat.CharHeight = charHeightMeters;
                        textFormat.TypeFaceName = BendNoteFontName;
                        bool setOk;
                        if (annotationType == (int)swAnnotationType_e.swNote)
                        {
                            bool useDocBefore = annotation.GetUseDocTextFormat(0);

                        

                            // 对可能包含富文本的 Note，重写纯文本后再应用本地字体
                            Note? note = annotation.GetSpecificAnnotation() as Note;
                            bool rewriteNoteOk = false;
                            if (note != null)
                            {
                                string noteText = note.GetText() ?? string.Empty;
                                rewriteNoteOk = note.SetText(noteText);
                            }

                            bool setAllOk = annotation.ISetTextFormat(0, false, textFormat);
                            if (!setAllOk)
                            {
                                setAllOk = annotation.SetTextFormat(-1, false, textFormat);
                            }

                            bool setFirstOk = annotation.SetTextFormat(0, false, textFormat);
                            setOk = setAllOk || setFirstOk;
                            bool useDocAfter = annotation.GetUseDocTextFormat(0);
                            Console.WriteLine($"Note字体模式切换：useDoc[{useDocBefore}->{useDocAfter}] ] -> doc=false[-1:{setAllOk},0:{setFirstOk}] rewriteNote[{rewriteNoteOk}]，字体：{oldFontName}->{BendNoteFontName}");

                        }
                        else
                        {
                            setOk = annotation.SetTextFormat(0, false, textFormat);
                        }

                        Console.WriteLine($"注解[{annotationName}] 类型[{annotationTypeName}] 文本[{annotationText}] 设置结果[{setOk}] CharHeightInPts:{oldPts}->{charHeightPts} CharHeight:{oldMeters:F6}->{charHeightMeters:F6}");
                        if (setOk) successCount++;
                    }
                    catch (Exception ex)
                    {
                        failCount++;
                        Console.WriteLine($"设置注解字高失败：{ex.Message}");
                    }
                }

                swModel.GraphicsRedraw2();
                swModel.EditRebuild3();
                Console.WriteLine($"选中视图处理完成：成功 {successCount}，失败 {failCount}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"处理选中视图注解字号失败：{ex.Message}");
            }
        }

        private static string GetAnnotationText(Annotation annotation)
        {
            try
            {
                if (annotation.GetType() == (int)swAnnotationType_e.swDisplayDimension)
                {
                    DisplayDimension? dd = annotation.GetSpecificAnnotation() as DisplayDimension;
                    if (dd == null) return "<displayDimension:null>";
                    string prefix = dd.GetText((int)swDimensionTextParts_e.swDimensionTextPrefix) ?? "";
                    string suffix = dd.GetText((int)swDimensionTextParts_e.swDimensionTextSuffix) ?? "";
                    return $"{prefix} {suffix}".Trim();
                }

                Note? note = annotation.GetSpecificAnnotation() as Note;
                if (note != null)
                {
                    return note.GetText() ?? "<note:empty>";
                }

                return "<暂不支持读取文本>";
            }
            catch (Exception ex)
            {
                return $"<读取失败:{ex.Message}>";
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

    }
}
