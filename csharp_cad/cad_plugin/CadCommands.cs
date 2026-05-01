namespace cad_plugin;

using Autodesk.AutoCAD.Runtime;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.DatabaseServices;
using cad_tools;
using System.IO;
using System.Runtime.InteropServices;
using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.Interop;
using Autodesk.AutoCAD.Interop.Common;

public partial class CadPluginCommands
{
    private static readonly CADWLRecommendationSettings DefaultRecommendationSettings = new CADWLRecommendationSettings();

    [CommandMethod("HELLO")]
    public void HelloCommand()
    {
        Editor ed = Application.DocumentManager.MdiActiveDocument.Editor;
        ed.WriteMessage("\nHello from CAD Plugin!\n");
    }

    [CommandMethod("mergedwg")]
    public void DrawDividerCommand()
    {
        draw_divider.process_subfolders_with_divider();
    }
    
    [CommandMethod("COPYFILE")]
    public void CopyCurrentFileToClipboard()
    {
        try
        {
            // 获取当前活动的文档
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null)
            {
                Editor ed = Application.DocumentManager.MdiActiveDocument?.Editor;
                ed?.WriteMessage("\n错误：没有活动的文档！\n");
                return;
            }

            Editor editor = doc.Editor;
            
            // 先保存当前文档
            editor.WriteMessage("\n正在保存当前文档...\n");
            doc.Database.SaveAs(doc.Name, DwgVersion.Current);
            editor.WriteMessage("文档保存成功！\n");

            // 获取当前文件的路径
            string filePath = doc.Name;
            
            // 检查文件是否存在
            if (!File.Exists(filePath))
            {
                editor.WriteMessage($"\n错误：文件不存在: {filePath}\n");
                return;
            }

            // 使用Windows API将文件路径复制到剪贴板
            bool success = CopyFileToClipboard(filePath);
            
            // 显示结果消息
            if (success)
            {
                editor.WriteMessage($"\n已将文件复制到剪贴板: {Path.GetFileName(filePath)}\n");
                editor.WriteMessage($"完整路径: {filePath}\n");
            }
            else
            {
                editor.WriteMessage("\n复制到剪贴板失败！\n");
            }
        }
        catch (System.Exception ex)
        {
            Editor ed = Application.DocumentManager.MdiActiveDocument?.Editor;
            ed?.WriteMessage($"\n复制文件到剪贴板时出错: {ex.Message}\n");
        }
    }
    
    /// <summary>
    /// 将文件路径复制到Windows剪贴板
    /// </summary>
    /// <param name="filePath">要复制的文件路径</param>
    /// <returns>是否成功复制</returns>
    private bool CopyFileToClipboard(string filePath)
    {
        try
        {
            // 使用COM接口来设置剪贴板数据
            // 这种方法适用于需要复制文件到剪贴板的场景
            System.Collections.Specialized.StringCollection fileList = new System.Collections.Specialized.StringCollection();
            fileList.Add(filePath);
            
            // 使用Windows Forms的Clipboard类
            System.Windows.Forms.Clipboard.SetFileDropList(fileList);
            
            return true;
        }
        catch (System.Exception ex)
        {
            // 如果Windows Forms方法失败，尝试其他方法
            System.Diagnostics.Debug.WriteLine($"设置剪贴板失败: {ex.Message}");
            return false;
        }
    }

    [CommandMethod("WL_LEARN_CURRENT")]
    public void LearnCurrentDrawing()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var ed = doc?.Editor;
        try
        {
            var acadDoc = GetActiveAcadDocument();
            if (acadDoc == null)
            {
                ed?.WriteMessage("\n错误：无法获取当前AutoCAD文档。\n");
                return;
            }

            var selectedEntities = doc == null ? new List<AcadEntity>() : GetSelectedAcadEntities(doc, acadDoc);
            var graphName = Path.GetFileNameWithoutExtension(acadDoc.FullName);
            var graph = selectedEntities.Count > 0
                ? CADGraphEdgeBuilder.BuildGraphFromEntities(acadDoc, selectedEntities, $"{graphName}_selected")
                : CADGraphEdgeBuilder.BuildGraphFromDocument(acadDoc, graphName);
            var wlFrequencies = CADWLGraphKernel.PerformWLIterations(graph, iterations: 3);

            var db = new CADDimensionDatabase();
            int graphId = db.UpsertCADGraphWithWL(graph, wlFrequencies);

            var rules = selectedEntities.Count > 0
                ? CADDimensionExtractor.ExtractRulesFromEntities(selectedEntities)
                : CADDimensionExtractor.ExtractRulesFromDocument(acadDoc);
            int savedRules = CADDimensionExtractor.SaveExtractedRules(db, graphId, rules);

            string scope = selectedEntities.Count > 0 ? $"选择集({selectedEntities.Count})" : "整图";
            ed?.WriteMessage($"\nWL学习完成[{scope}]：图节点 {graph.Nodes.Count}，提取标注规则 {savedRules} 条。\n");
        }
        catch (System.Exception ex)
        {
            ed?.WriteMessage($"\nWL学习失败：{ex.Message}\n");
        }
    }

    [CommandMethod("WL_RECOMMEND_CURRENT")]
    public void RecommendCurrentDrawing()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var ed = doc?.Editor;
        try
        {
            var acadDoc = GetActiveAcadDocument();
            if (acadDoc == null)
            {
                ed?.WriteMessage("\n错误：无法获取当前AutoCAD文档。\n");
                return;
            }

            var selectedEntities = doc == null ? new List<AcadEntity>() : GetSelectedAcadEntities(doc, acadDoc);
            var graph = selectedEntities.Count > 0
                ? CADGraphEdgeBuilder.BuildGraphFromEntities(acadDoc, selectedEntities, "CurrentDrawing_selected")
                : CADGraphEdgeBuilder.BuildGraphFromDocument(acadDoc, "CurrentDrawing");
            var wlFrequencies = CADWLGraphKernel.PerformWLIterations(graph, iterations: 3);

            var db = new CADDimensionDatabase();
            int sampleCount = db.GetAnnotatedGraphCount();
            if (sampleCount < DefaultRecommendationSettings.MinDistinctGraphs)
            {
                ed?.WriteMessage($"\n样本不足：当前仅 {sampleCount} 个已标注图，至少需要 {DefaultRecommendationSettings.MinDistinctGraphs} 个。\n");
                return;
            }

            var recommendations = CADWLRecommendationPolicy.Recommend(db, wlFrequencies, DefaultRecommendationSettings);
            if (recommendations.Count == 0)
            {
                ed?.WriteMessage("\n未找到满足阈值的推荐规则。\n");
                return;
            }

            ed?.WriteMessage($"\n推荐结果（Top {Math.Min(5, recommendations.Count)}）：\n");
            for (int i = 0; i < Math.Min(5, recommendations.Count); i++)
            {
                var r = recommendations[i];
                ed?.WriteMessage(
                    $"#{i + 1} 来源:{r.SourceGraph} 规则:{r.RuleName} 类型:{r.RuleType} 值:{r.DimensionValue} 相似度:{r.Similarity:F3} 置信度:{r.Confidence:F2}\n");
            }

            var eval = CADWLEvaluator.EvaluateHistoricalHitRate(recommendations);
            string scope = selectedEntities.Count > 0 ? $"选择集({selectedEntities.Count})" : "整图";
            ed?.WriteMessage($"评估代理指标[{scope}] Top1={eval.Top1:F3}, Top3={eval.Top3:F3}\n");
        }
        catch (System.Exception ex)
        {
            ed?.WriteMessage($"\n推荐失败：{ex.Message}\n");
        }
    }

    [CommandMethod("WL_APPLY_SUGGESTIONS")]
    public void ApplyCurrentSuggestions()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var ed = doc?.Editor;
        if (doc == null)
        {
            return;
        }

        try
        {
            var acadDoc = GetActiveAcadDocument();
            if (acadDoc == null)
            {
                ed?.WriteMessage("\n错误：无法获取当前AutoCAD文档。\n");
                return;
            }

            var selectedEntities = GetSelectedAcadEntities(doc, acadDoc);
            var graph = selectedEntities.Count > 0
                ? CADGraphEdgeBuilder.BuildGraphFromEntities(acadDoc, selectedEntities, "CurrentDrawing_selected")
                : CADGraphEdgeBuilder.BuildGraphFromDocument(acadDoc, "CurrentDrawing");
            var wlFrequencies = CADWLGraphKernel.PerformWLIterations(graph, iterations: 3);
            var db = new CADDimensionDatabase();
            var recommendations = CADWLRecommendationPolicy.Recommend(db, wlFrequencies, DefaultRecommendationSettings);

            if (!CADWLRecommendationPolicy.CanAutoApply(recommendations, DefaultRecommendationSettings))
            {
                ed?.WriteMessage($"\n最高相似度不足 {DefaultRecommendationSettings.AutoApplySimilarity:F2}，仅展示建议不自动应用。\n");
                return;
            }

            var prompt = new PromptKeywordOptions("\n检测到高置信建议，是否写入注释摘要？")
            {
                AllowNone = false
            };
            prompt.Keywords.Add("Yes");
            prompt.Keywords.Add("No");
            prompt.Keywords.Default = "No";
            var result = ed?.GetKeywords(prompt);
            if (result == null || result.StringResult != "Yes")
            {
                ed?.WriteMessage("\n已取消应用。\n");
                return;
            }

            string annotationSummary = BuildSuggestionSummary(recommendations);
            InsertSummaryMText(doc, annotationSummary);
            ed?.WriteMessage("\n已将推荐摘要写入图纸（MText），可基于摘要执行人工确认标注。\n");
        }
        catch (System.Exception ex)
        {
            ed?.WriteMessage($"\n应用建议失败：{ex.Message}\n");
        }
    }

    [CommandMethod("WL_EVAL_CURRENT")]
    public void EvaluateCurrentRecommendations()
    {
        var doc = Application.DocumentManager.MdiActiveDocument;
        var ed = doc?.Editor;
        try
        {
            var acadDoc = GetActiveAcadDocument();
            if (acadDoc == null)
            {
                ed?.WriteMessage("\n错误：无法获取当前AutoCAD文档。\n");
                return;
            }

            var selectedEntities = doc == null ? new List<AcadEntity>() : GetSelectedAcadEntities(doc, acadDoc);
            var graph = selectedEntities.Count > 0
                ? CADGraphEdgeBuilder.BuildGraphFromEntities(acadDoc, selectedEntities, "CurrentDrawing_selected")
                : CADGraphEdgeBuilder.BuildGraphFromDocument(acadDoc, "CurrentDrawing");
            var wlFrequencies = CADWLGraphKernel.PerformWLIterations(graph, iterations: 3);
            var db = new CADDimensionDatabase();
            var recommendations = CADWLRecommendationPolicy.Recommend(db, wlFrequencies, DefaultRecommendationSettings);
            var metrics = CADWLEvaluator.EvaluateHistoricalHitRate(recommendations);
            string scope = selectedEntities.Count > 0 ? $"选择集({selectedEntities.Count})" : "整图";
            ed?.WriteMessage($"\n当前图推荐评估[{scope}]：Top1={metrics.Top1:F3}, Top3={metrics.Top3:F3}, 候选数={recommendations.Count}\n");
        }
        catch (System.Exception ex)
        {
            ed?.WriteMessage($"\n评估失败：{ex.Message}\n");
        }
    }

    private static AcadDocument? GetActiveAcadDocument()
    {
        var acadApp = CadConnect.GetOrCreateInstance();
        return acadApp?.ActiveDocument;
    }

    private static List<AcadEntity> GetSelectedAcadEntities(Document doc, AcadDocument acadDoc)
    {
        var result = new List<AcadEntity>();
        var selection = doc.Editor.SelectImplied();
        if (selection.Status != PromptStatus.OK || selection.Value == null)
        {
            return result;
        }

        var selectedHandles = new HashSet<string>(
            selection.Value.GetObjectIds().Select(id => id.Handle.ToString().ToUpperInvariant()));
        if (selectedHandles.Count == 0)
        {
            return result;
        }

        foreach (AcadEntity entity in acadDoc.ModelSpace)
        {
            string handle = TryGetEntityHandle(entity);
            if (!string.IsNullOrEmpty(handle) && selectedHandles.Contains(handle))
            {
                result.Add(entity);
            }
        }

        return result;
    }

    private static string TryGetEntityHandle(AcadEntity entity)
    {
        try
        {
            return (entity.Handle ?? string.Empty).ToUpperInvariant();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string BuildSuggestionSummary(
        List<(string RuleName, string DimensionValue, string DimensionType, double Similarity, double Confidence, string AnnotationStyle, string RuleType, string SourceGraph)> recommendations)
    {
        var lines = new List<string> { "WL Suggestions" };
        for (int i = 0; i < Math.Min(5, recommendations.Count); i++)
        {
            var r = recommendations[i];
            lines.Add($"{i + 1}. {r.RuleType}:{r.DimensionValue} sim={r.Similarity:F3} src={r.SourceGraph}");
        }
        return string.Join("\\P", lines);
    }

    private static void InsertSummaryMText(Document doc, string text)
    {
        var db = doc.Database;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            var modelSpace = (BlockTableRecord)tr.GetObject(blockTable[BlockTableRecord.ModelSpace], OpenMode.ForWrite);

            var mtext = new MText
            {
                Contents = text,
                Location = new Autodesk.AutoCAD.Geometry.Point3d(0, 0, 0),
                TextHeight = 2.5
            };
            modelSpace.AppendEntity(mtext);
            tr.AddNewlyCreatedDBObject(mtext, true);
            tr.Commit();
        }
    }
}