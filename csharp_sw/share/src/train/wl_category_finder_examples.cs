using System;
using SolidWorks.Interop.sldworks;
using tools;

namespace examples
{
    /// <summary>
    /// WL 标签查找类别功能使用示例
    /// </summary>
    public class WLTagCategoryFinderExample
    {
        /// <summary>
        /// 示例 1：使用交互式方法查找当前零件的推荐类别
        /// </summary>
        public static void Example1_FindCategoriesInteractive(SldWorks swApp)
        {
            Console.WriteLine("=== 示例 1：交互式查找类别 ===\n");
            
            ModelDoc2 model = swApp.IActiveDoc2;
            
            if (model == null)
            {
                Console.WriteLine("请先打开一个零件文档！");
                return;
            }

            // 方法 1：使用封装好的交互式方法（最简单）
            TopologyLabeler.FindCategoriesByWL(model, wlIterations: 1, topK: 5);
        }

        /// <summary>
        /// 示例 2：直接调用数据库 API 获取推荐类别
        /// </summary>
        public static void Example2_DatabaseAPI(SldWorks swApp)
        {
            Console.WriteLine("=== 示例 2：使用数据库 API ===\n");
            
            ModelDoc2 model = swApp.IActiveDoc2;
            if (model == null)
            {
                Console.WriteLine("请先打开一个零件文档！");
                return;
            }

            try
            {
                // 步骤 1：初始化数据库
                var database = new TopologyDatabase("topology_labels.db");
                
                // 步骤 2：构建零件拓扑图
                Console.WriteLine("正在构建拓扑图...");
                var graphs = FaceGraphBuilder.BuildGraphs(model);
                
                if (graphs == null || graphs.Count == 0)
                {
                    Console.WriteLine("无法构建拓扑图！");
                    return;
                }
                
                // 使用第一个 body 的图
                var graph = graphs[0];
                Console.WriteLine($"✓ 拓扑图已构建（{graph.Nodes.Count} 个面）");
                
                // 步骤 3：执行 WL 迭代，获取标签频率
                Console.WriteLine("\n正在执行 WL 迭代...");
                var wlFrequencies = WLGraphKernel.PerformWLIterations(graph, iterations: 1);
                
                // 步骤 4：查找推荐类别（简化版 - 只返回统计信息）
                Console.WriteLine("\n正在查找推荐类别...");
                var recommendations = database.FindTopCategoriesByWLTags(
                    wlFrequencies, 
                    topK: 5, 
                    minSimilarity: 0.3
                );
                
                // 步骤 5：显示结果
                if (recommendations.Count == 0)
                {
                    Console.WriteLine("未找到相似的已标注零件。");
                    Console.WriteLine("提示：请先标注一些零件以建立数据库。");
                    return;
                }
                
                Console.WriteLine($"\n{'='*60}");
                Console.WriteLine("推荐类别（按出现次数排序）");
                Console.WriteLine($"{'='*60}\n");
                
                for (int i = 0; i < recommendations.Count; i++)
                {
                    var (category, count, avgSim, avgConf) = recommendations[i];
                    Console.WriteLine($"[{i + 1}] {category}");
                    Console.WriteLine($"    出现次数：{count}");
                    Console.WriteLine($"    平均相似度：{avgSim:F3} ({avgSim * 100:F1}%)");
                    Console.WriteLine($"    平均置信度：{avgConf:F2}");
                    Console.WriteLine();
                }
                
                Console.WriteLine($"{'='*60}\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"错误：{ex.Message}");
            }
        }

        /// <summary>
        /// 示例 3：获取详细的匹配结果（包含具体零件信息）
        /// </summary>
        public static void Example3_DetailedMatches(SldWorks swApp)
        {
            Console.WriteLine("=== 示例 3：查看详细匹配结果 ===\n");
            
            ModelDoc2 model = swApp.IActiveDoc2 ;
            if (model == null)
            {
                Console.WriteLine("请先打开一个零件文档！");
                return;
            }

            try
            {
                // 初始化数据库
                var database = new TopologyDatabase("topology_labels.db");
                
                // 构建拓扑图并执行 WL 迭代
                var graphs = FaceGraphBuilder.BuildGraphs(model);
                if (graphs == null || graphs.Count == 0)
                {
                    Console.WriteLine("无法构建拓扑图！");
                    return;
                }
                
                // 使用第一个 body 的图
                var graph = graphs[0];
                var wlFrequencies = WLGraphKernel.PerformWLIterations(graph, iterations: 1);
                
                // 获取详细匹配结果（返回具体零件和类别）
                var detailedResults = database.FindCategoriesByWLTags(
                    wlFrequencies, 
                    topK: 20,      // 返回最多 20 个匹配
                    minSimilarity: 0.4  // 相似度阈值 0.4
                );
                
                if (detailedResults.Count == 0)
                {
                    Console.WriteLine("未找到匹配的零件。");
                    return;
                }
                
                // 显示详细信息
                Console.WriteLine($"\n{'='*80}");
                Console.WriteLine($"详细匹配结果（共 {detailedResults.Count} 条）");
                Console.WriteLine($"{'='*80}\n");
                
                for (int i = 0; i < detailedResults.Count; i++)
                {
                    var (category, partName, bodyName, similarity, confidence, notes) = detailedResults[i];
                    
                    Console.WriteLine($"匹配 {i + 1}:");
                    Console.WriteLine($"  零件名称：{partName}/{bodyName}");
                    Console.WriteLine($"  类    别：{category}");
                    Console.WriteLine($"  相 似 度：{similarity:F4} ({similarity * 100:F2}%)");
                    Console.WriteLine($"  置 信 度：{confidence:F2}");
                    
                    if (!string.IsNullOrEmpty(notes))
                    {
                        Console.WriteLine($"  备    注：{notes}");
                    }
                    
                    Console.WriteLine();
                }
                
                Console.WriteLine($"{'='*80}\n");
                
                // 可以进一步分析结果
                Console.WriteLine("统计分析:");
                var categoryGroups = detailedResults
                    .GroupBy(r => r.Category)
                    .Select(g => new
                    {
                        Category = g.Key,
                        Count = g.Count(),
                        AvgSimilarity = g.Average(r => r.Similarity),
                        MaxSimilarity = g.Max(r => r.Similarity)
                    })
                    .OrderByDescending(x => x.Count)
                    .ToList();
                
                foreach (var group in categoryGroups)
                {
                    Console.WriteLine($"  {group.Category}: {group.Count}次 " +
                        $"[平均相似度：{group.AvgSimilarity:F3}, " +
                        $"最高相似度：{group.MaxSimilarity:F3}]");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"错误：{ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

 
        /// <summary>
        /// 示例 5：自定义相似度阈值和排序策略
        /// </summary>
        public static void Example5_CustomThreshold(SldWorks swApp)
        {
            Console.WriteLine("=== 示例 5：自定义阈值和排序 ===\n");
            
            ModelDoc2 model = swApp.IActiveDoc2;
            if (model == null)
            {
                Console.WriteLine("请先打开一个零件文档！");
                return;
            }

            var database = new TopologyDatabase("topology_labels.db");
            
            // 构建拓扑图和 WL 特征
            var graphs = FaceGraphBuilder.BuildGraphs(model);
            if (graphs == null || graphs.Count == 0)
            {
                Console.WriteLine("无法构建拓扑图！");
                return;
            }
            
            // 使用第一个 body 的图
            var graph = graphs[0];
            var wlFrequencies = WLGraphKernel.PerformWLIterations(graph, 1);
            
            // 使用不同的相似度阈值进行比较
            double[] thresholds = { 0.3, 0.5, 0.7 };
            
            foreach (double threshold in thresholds)
            {
                Console.WriteLine($"\n阈值：{threshold}");
                Console.WriteLine("-".PadRight(40, '-'));
                
                var results = database.FindTopCategoriesByWLTags(
                    wlFrequencies, 
                    topK: 5, 
                    minSimilarity: threshold
                );
                
                if (results.Count == 0)
                {
                    Console.WriteLine("  无匹配结果");
                }
                else
                {
                    foreach (var (category, count, avgSim, avgConf) in results)
                    {
                        Console.WriteLine($"  {category,-15} [次数：{count}, " +
                            $"相似度：{avgSim:F3}]");
                    }
                }
            }
            
            Console.WriteLine();
        }
    }
}
