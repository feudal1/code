using System;
using System.Collections.Generic;
using Autodesk.AutoCAD.Interop;

namespace cad_tools
{
    /// <summary>
    /// CAD WL学习示例
    /// </summary>
    public static class CADWLExamples
    {
        /// <summary>
        /// 示例1：学习已有标注的图纸
        /// </summary>
        public static void LearnFromAnnotatedDrawing(string dwgPath)
        {
            Console.WriteLine("\n========== CAD特征学习示例 ==========\n");
            
            // 1. 连接到CAD
            var acadApp = CadConnect.GetOrCreateInstance();
            if (acadApp == null)
            {
                Console.WriteLine("错误：无法连接到AutoCAD");
                return;
            }

            // 2. 打开图纸
            Console.WriteLine($"打开图纸：{dwgPath}");
            var doc = acadApp.Documents.Open(dwgPath, false, false);

            // 3. 构建图形
            Console.WriteLine("\n构建图形特征...");
            var graph = CADGraphEdgeBuilder.BuildGraphFromDocument(doc, "示例图形");

            // 4. 执行WL迭代
            Console.WriteLine("\n执行WL迭代...");
            var wlFreq = CADWLGraphKernel.PerformWLIterations(graph, iterations: 3);
            
            // 5. 保存到数据库
            var database = new CADDimensionDatabase();
            int graphId = database.UpsertCADGraphWithWL(graph, wlFreq);

            // 6. 添加标注规则（这里需要手动输入或从图纸提取）
            Console.WriteLine("\n添加标注规则...");
            database.AddDimensionRule(graphId, 
                ruleName: "高度标注",
                ruleType: "linear",
                dimensionValue: "27.30",
                dimensionType: "垂直尺寸",
                annotationStyle: "默认",
                confidence: 1.0,
                notes: "学习自示例图纸");

            database.AddDimensionRule(graphId,
                ruleName: "宽度标注",
                ruleType: "linear",
                dimensionValue: "12.80",
                dimensionType: "水平尺寸",
                annotationStyle: "默认",
                confidence: 1.0,
                notes: "学习自示例图纸");

            doc.Close(false);
            Console.WriteLine("\n✓ 学习完成！");
        }

        /// <summary>
        /// 示例2：根据已有图纸推荐标注
        /// </summary>
        public static void RecommendDimensionsForNewDrawing(string newDwgPath)
        {
            Console.WriteLine("\n========== CAD标注推荐示例 ==========\n");
            
            var acadApp = CadConnect.GetOrCreateInstance();
            if (acadApp == null) return;

            Console.WriteLine($"打开新图纸：{newDwgPath}");
            var doc = acadApp.Documents.Open(newDwgPath, false, true);

            // 构建图形
            var graph = CADGraphEdgeBuilder.BuildGraphFromDocument(doc, "新图形");
            var wlFreq = CADWLGraphKernel.PerformWLIterations(graph, iterations: 3);

            // 查询推荐规则
            var database = new CADDimensionDatabase();
            var recommendations = database.FindRecommendedRules(wlFreq, topK: 10, minSimilarity: 0.5);

            Console.WriteLine($"\n找到 {recommendations.Count} 条推荐标注规则：\n");
            
            for (int i = 0; i < Math.Min(5, recommendations.Count); i++)
            {
                var (ruleName, dimValue, dimType, similarity, confidence, style, ruleType, sourceGraph) = recommendations[i];
                Console.WriteLine($"推荐 {i + 1}:");
                Console.WriteLine($"  规则：{ruleName}");
                Console.WriteLine($"  标注值：{dimValue}");
                Console.WriteLine($"  类型：{dimType}");
                Console.WriteLine($"  规则类型：{ruleType}");
                Console.WriteLine($"  来源图：{sourceGraph}");
                Console.WriteLine($"  相似度：{similarity:F3} ({similarity * 100:F1}%)");
                Console.WriteLine($"  置信度：{confidence:F2}");
                Console.WriteLine();
            }

            doc.Close(false);
        }

        /// <summary>
        /// 示例3：批量学习文件夹中的图纸
        /// </summary>
        public static void BatchLearnFromFolder(string folderPath)
        {
            Console.WriteLine($"\n========== 批量学习：{folderPath} ==========\n");
            
            var graphs = CADGraphEdgeBuilder.BuildGraphsFromFolder(folderPath);
            var database = new CADDimensionDatabase();

            foreach (var graph in graphs)
            {
                Console.WriteLine($"\n处理：{graph.GraphName}");
                var wlFreq = CADWLGraphKernel.PerformWLIterations(graph, iterations: 3);
                database.UpsertCADGraphWithWL(graph, wlFreq);
            }

            Console.WriteLine($"\n✓ 批量学习完成！共处理 {graphs.Count} 个图形");
        }

        /// <summary>
        /// 示例4：计算图形相似度矩阵
        /// </summary>
        public static void ComputeSimilarityMatrix(string folderPath)
        {
            Console.WriteLine("\n========== 计算相似度矩阵 ==========\n");
            
            var graphs = CADGraphEdgeBuilder.BuildGraphsFromFolder(folderPath);
            var matrix = CADWLGraphKernel.ComputeSimilarityMatrix(graphs, wlIterations: 3, decayFactor: 0.5);

            Console.WriteLine("\n相似度矩阵：\n");
            Console.Write("".PadRight(20));
            for (int j = 0; j < graphs.Count; j++)
            {
                Console.Write($"{graphs[j].GraphName.Substring(0, Math.Min(10, graphs[j].GraphName.Length))}".PadRight(15));
            }
            Console.WriteLine();

            for (int i = 0; i < graphs.Count; i++)
            {
                Console.Write($"{graphs[i].GraphName.Substring(0, Math.Min(10, graphs[i].GraphName.Length))}".PadRight(20));
                for (int j = 0; j < graphs.Count; j++)
                {
                    Console.Write($"{matrix[i, j]:F3}".PadRight(15));
                }
                Console.WriteLine();
            }
        }
    }
}
