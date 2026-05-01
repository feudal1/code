using System;
using System.Collections.Generic;
using System.IO;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
namespace tools
{
    /// <summary>
    /// 批量计算零件拓扑相似度
    /// </summary>
    public static class SimilarityCalculator
    {
        /// <summary>
        /// 从文件夹中加载所有零件并构建图
        /// </summary>
        public static List<BodyGraph> LoadPartsFromFolder(string folderPath, ISldWorks swApp)
        {
            var graphs = new List<BodyGraph>();
            
            if (!Directory.Exists(folderPath))
            {
                Console.WriteLine($"错误：文件夹不存在 - {folderPath}");
                return graphs;
            }

            // 获取所有 SolidWorks 零件文件
            string[] partFiles = Directory.GetFiles(folderPath, "*.sldprt", SearchOption.AllDirectories);
            
            Console.WriteLine($"在文件夹中找到 {partFiles.Length} 个零件文件\n");

            foreach (string file in partFiles)
            {
                // 跳过 SolidWorks 临时文件
                string fileName = Path.GetFileName(file);
                if (fileName.StartsWith("~$"))
                {
                    Console.WriteLine($"跳过临时文件：{fileName}");
                    continue;
                }
                
                try
                {
                    Console.WriteLine($"正在加载：{fileName}");
                    int errors = 0;
                    int warnings = 0;
                    // 以只读方式打开零件文档，避免关闭时提示保存
                    ModelDoc2 model = swApp.OpenDoc6(
                        file, 
                        (int)swDocumentTypes_e.swDocPART, 
                         (int)swOpenDocOptions_e.swOpenDocOptions_Silent, 
                        "", 
                        ref  errors, 
                        ref  warnings);

                    if (model != null)
                    {
                        // 构建图结构
                        var bodyGraphs = FaceGraphBuilder.BuildGraphs(model);
                                            
                        // 将每个 body 的图添加到结果中
                        if (bodyGraphs != null && bodyGraphs.Count > 0)
                        {
                            graphs.AddRange(bodyGraphs);
                        }
                    
                      model.Save2(true);
                        // 关闭文档 (只读打开的文档不会提示保存)
                       swApp.CloseDoc(model.GetTitle());
                    }
                    else
                    {
                        Console.WriteLine($"  打开失败，错误代码：{errors}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  处理文件时出错：{ex.Message}");
                }
            }

            Console.WriteLine($"\n成功加载 {graphs.Count} 个零件的图结构");
            return graphs;
        }

        /// <summary>
        /// 从当前已打开的零件中构建图列表
        /// </summary>
        public static List<BodyGraph> BuildGraphsFromOpenParts(ModelDoc2[] models)
        {
            var graphs = new List<BodyGraph>();
            
            for (int i = 0; i < models.Length; i++)
            {
                if (models[i] != null)
                {
                    // 为每个零件构建所有 body 的图
                    var bodyGraphs = FaceGraphBuilder.BuildGraphs(models[i]);
                    
                    if (bodyGraphs != null && bodyGraphs.Count > 0)
                    {
                        graphs.AddRange(bodyGraphs);
                    }
                }
            }
            
            return graphs;
        }

        /// <summary>
        /// 计算单个零件对的相似度
        /// </summary>
        public static double CalculatePairSimilarity(BodyGraph graph1, BodyGraph graph2, int iterations = 1)
        {
            Console.WriteLine($"\n计算零件对相似度:");
            Console.WriteLine($"  Body 1: {graph1.PartName}/{graph1.BodyName} ({graph1.Nodes.Count} 个面)");
            Console.WriteLine($"  Body 2: {graph2.PartName}/{graph2.BodyName} ({graph2.Nodes.Count} 个面)");

            // 执行 WL 迭代
            var freqList1 = WLGraphKernel.PerformWLIterations(graph1, iterations);
            var freqList2 = WLGraphKernel.PerformWLIterations(graph2, iterations);

            // 计算综合相似度
            double similarity = WLGraphKernel.CalculateComprehensiveSimilarity(freqList1, freqList2);
            
            Console.WriteLine($"\n最终相似度：{similarity:F4} ({similarity * 100:F2}%)");
            
            return similarity;
        }

        /// <summary>
        /// 运行完整分析流程
        /// </summary>
        public static void RunAnalysis(List<BodyGraph> graphs, int wlIterations = 1, double decayFactor = 0.5)
        {
            if (graphs == null || graphs.Count < 2)
            {
                Console.WriteLine("警告：至少需要 2 个零件才能计算相似度");
                return;
            }

            Console.WriteLine($"\n{'='*60}");
            Console.WriteLine("开始 Weisfeiler-Lehman 图核拓扑相似度分析");
            Console.WriteLine($"{'='*60}");
            Console.WriteLine($"零件数量：{graphs.Count}");
            Console.WriteLine($"WL 迭代次数：{wlIterations}");
            Console.WriteLine($"权重衰减因子：{decayFactor}");
            Console.WriteLine($"{'='*60}\n");

            // 打印每个零件的基本信息
            Console.WriteLine("Body 列表:");
            for (int i = 0; i < graphs.Count; i++)
            {
                var graph = graphs[i];
                Console.WriteLine($"  {i + 1}. {graph.PartName}/{graph.BodyName,-40} [{graph.Nodes.Count} 个面]");
            }
            Console.WriteLine();

            // 计算相似度矩阵
            double[,] matrix = WLGraphKernel.ComputeSimilarityMatrix(graphs, wlIterations, decayFactor);

            // 输出详细报告
            OutputDetailedReport(matrix, graphs);
        }

        /// <summary>
        /// 输出详细分析报告
        /// </summary>
        private static void OutputDetailedReport(double[,] matrix, List<BodyGraph> graphs)
        {
            int n = matrix.GetLength(0);
            
            Console.WriteLine("\n========== 相似度分析报告 ==========\n");

            // 找出最相似的零件对
            var pairs = new List<(int i, int j, double similarity)>();
            
            for (int i = 0; i < n; i++)
            {
                for (int j = i + 1; j < n; j++)
                {
                    pairs.Add((i, j, matrix[i, j]));
                }
            }

            // 按相似度排序
            pairs.Sort((a, b) => b.similarity.CompareTo(a.similarity));

            Console.WriteLine("TOP-5 最相似零件对:");
            for (int k = 0; k < Math.Min(5, pairs.Count); k++)
            {
                var pair = pairs[k];
                Console.WriteLine($"  {k + 1}. {graphs[pair.i].PartName} <-> {graphs[pair.j].PartName}: " +
                    $"{pair.similarity:F4} ({pair.similarity * 100:F2}%)");
            }

            Console.WriteLine("\n最不相似的零件对:");
            pairs.Reverse();
            for (int k = 0; k < Math.Min(3, pairs.Count); k++)
            {
                var pair = pairs[k];
                Console.WriteLine($"  {k + 1}. {graphs[pair.i].PartName} <-> {graphs[pair.j].PartName}: " +
                    $"{pair.similarity:F4} ({pair.similarity * 100:F2}%)");
            }

            // 统计信息
            double avgSimilarity = 0.0;
            double maxSimilarity = 0.0;
            double minSimilarity = 1.0;
            
            foreach (var pair in pairs)
            {
                avgSimilarity += pair.similarity;
                if (pair.similarity > maxSimilarity) maxSimilarity = pair.similarity;
                if (pair.similarity < minSimilarity) minSimilarity = pair.similarity;
            }
            
            avgSimilarity /= pairs.Count;

            Console.WriteLine("\n统计信息:");
            Console.WriteLine($"  平均相似度：{avgSimilarity:F4} ({avgSimilarity * 100:F2}%)");
            Console.WriteLine($"  最大相似度：{maxSimilarity:F4} ({maxSimilarity * 100:F2}%)");
            Console.WriteLine($"  最小相似度：{minSimilarity:F4} ({minSimilarity * 100:F2}%)");
            
            Console.WriteLine("\n========================================\n");
        }
    }
}
