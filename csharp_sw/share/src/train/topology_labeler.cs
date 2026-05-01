using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace tools
{
    /// <summary>
    /// 零件拓扑标注器 - 提供交互式标注功能
    /// </summary>
    public static class TopologyLabeler
    {
        private static TopologyDatabase? _database;
        private static readonly string DefaultDbPath = @"E:\my_data\topology_labels.db";

        /// <summary>
        /// 初始化数据库
        /// </summary>
        public static void Initialize(string? dbPath = null)
        {
            // 确保数据库目录存在
            string dbDirectory = System.IO.Path.GetDirectoryName(dbPath ?? DefaultDbPath);
            if (!string.IsNullOrEmpty(dbDirectory) && !System.IO.Directory.Exists(dbDirectory))
            {
                System.IO.Directory.CreateDirectory(dbDirectory);
                Console.WriteLine($"✓ 已创建数据库目录：{dbDirectory}");
            }
            
            _database = new TopologyDatabase(dbPath ?? DefaultDbPath);
            Console.WriteLine("✓ 拓扑标注系统已初始化");
        }

        /// <summary>
        /// 获取数据库实例（用于外部访问）
        /// </summary>
        public static TopologyDatabase? GetDatabase()
        {
            if (_database == null)
            {
                Initialize();
            }
            return _database;
        }

        /// <summary>
        /// 标注当前打开的零件的所有 body
        /// </summary>
        public static void LabelCurrentPart(ModelDoc2 swModel, int wlIterations = 1)
        {
            if (swModel == null)
            {
                Console.WriteLine("× 错误：没有打开的活动文档");
                return;
            }

            if (_database == null)
            {
                Initialize();
            }

            try
            {
                // 构建所有 body 的拓扑图
                Console.WriteLine("\n=== 构建零件拓扑图 ===");
                var graphs = FaceGraphBuilder.BuildGraphs(swModel);
                
                if (graphs.Count == 0)
                {
                    Console.WriteLine("× 无法构建拓扑图");
                    return;
                }

                // 执行 WL 迭代计算标签频率
                Console.WriteLine($"\n=== 执行 WL 迭代 ({wlIterations} 次) ===");
                foreach (var graph in graphs)
                {
                    var wlFreq = WLGraphKernel.PerformWLIterations(graph, wlIterations);
                    // graph.LabelFrequency 已被更新为最后一轮迭代的结果
                }

                // 获取零件信息
                string partName = swModel.GetTitle();
                string fullPath = swModel.GetPathName();

                // 存储到数据库（此时 graphs.LabelFrequency 已包含最后一轮 WL 迭代结果）
                Console.WriteLine("\n=== 存储到数据库 ===");
                var bodyIds = _database!.UpsertPartWithBodies(partName, fullPath, graphs);

                // 显示每个 body 的现有标注
                foreach (var (bodyId, graph) in bodyIds.Zip(graphs, (id, g) => (id, g)))
                {
                    var existingLabels = _database.GetBodyLabels(bodyId);
                    if (existingLabels.Count > 0)
                    {
                        Console.WriteLine($"\n=== Body [{graph.BodyName}] 现有标注 ===");
                        foreach (var labelCategory in existingLabels)
                        {
                            foreach (var (value, confidence, notes) in labelCategory.Value)
                            {
                                Console.WriteLine($"  {labelCategory.Key}: {value} (置信度：{confidence}) - {notes}");
                            }
                        }
                    }
                }

                // 显示所有已使用的标注类别
                Console.WriteLine("\n=== 可用标注类别参考 ===");
                var allCategories = _database.GetAllCategories();
                if (allCategories.Count > 0)
                {
                    Console.WriteLine($"共 {allCategories.Count} 个类别:");
                    foreach (var cat in allCategories)
                    {
                        Console.WriteLine($"  - {cat}");
                    }
                }
                else
                {
                    Console.WriteLine("  (暂无历史标注类别)");
                }

                // 提示用户输入新标注
                Console.WriteLine("\n=== 添加标注 ===");
                Console.WriteLine("请输入要标注的 body 索引（0-{0}）或直接按回车跳过", graphs.Count - 1);
                
                // 只标注一个类别
                Console.Write("标注类别 > ");
                string? category = Console.ReadLine()?.Trim();
                                    
                if (!string.IsNullOrEmpty(category))
                {
                    Console.Write("Body 索引 > ");
                    if (int.TryParse(Console.ReadLine(), out int bodyIndex) && bodyIndex >= 0 && bodyIndex < graphs.Count)
                    {
                        int targetBodyId = bodyIds[bodyIndex];
                        // 添加标注（值设为空）
                        _database!.AddLabel(targetBodyId, category!, value: "", confidence: 1.0, notes: "");
                        Console.WriteLine($"✓ 标注已添加：{category} (Body: {graphs[bodyIndex].BodyName})");
                    }
                    else
                    {
                        Console.WriteLine("× 无效的 body 索引");
                    }
                }
                else
                {
                    Console.WriteLine("✓ 标注完成");
                }

                // 显示统计信息
                ShowStatistics();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"× 标注过程出错：{ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        /// <summary>
        /// 查看所有已标注的 body
        /// </summary>
        public static void ViewAllParts()
        {
            if (_database == null)
            {
                Initialize();
            }

            var bodies = _database?.GetAllBodiesWithLabels();
            
            if (bodies == null || bodies.Count == 0)
            {
                Console.WriteLine("数据库中暂无零件");
                return;
            }

            Console.WriteLine($"\n=== 已标注 Body ({bodies.Count} 个) ===\n");
            
            foreach (var (partId, partName, bodyId, bodyName, labels) in bodies)
            {
                Console.WriteLine($"[{partId}] {bodyName}");
                
                if (labels.Count > 0)
                {
                    Console.WriteLine("    标注:");
                    foreach (var label in labels)
                    {
                        Console.WriteLine($"      {label.Key}: {label.Value}");
                    }
                }
                Console.WriteLine();
            }
        }

        /// <summary>
        /// 按标注类别搜索 body
        /// </summary>
        public static void SearchByCategory(string category, string? value = null)
        {
            if (_database == null)
            {
                Initialize();
            }

            var results = _database?.SearchByLabel(category, value);
            
            if (results == null || results.Count == 0)
            {
                Console.WriteLine($"未找到符合条件的 body");
                return;
            }

            Console.WriteLine($"\n=== 搜索结果：{category}" + (value != null ? $" = {value}" : "") + " ===\n");
            
            foreach (var (partId, partName, bodyId, bodyName, labelValue) in results)
            {
                Console.WriteLine($"[{partId}] {bodyName} => {labelValue}");
            }
        }

        /// <summary>
        /// 显示数据库统计信息
        /// </summary>
        public static void ShowStatistics()
        {
            if (_database == null)
            {
                Initialize();
            }

            var stats = _database?.GetStatistics();
            
            if (stats == null) return;
            
            var (partCount, bodyCount, labelCount, categoryStats) = stats.Value;
            
            Console.WriteLine($"\n=== 数据库统计 ===");
            Console.WriteLine($"零件总数：{partCount}");
            Console.WriteLine($"Body 总数：{bodyCount}");
            Console.WriteLine($"标注总数：{labelCount}");
            Console.WriteLine("标注类别分布:");
            
            foreach (var stat in categoryStats)
            {
                Console.WriteLine($"  {stat.Key}: {stat.Value} 个标注");
            }
            Console.WriteLine();
        }

        /// <summary>
        /// 删除指定标注
        /// </summary>
        public static void DeleteLabel(int labelId)
        {
            if (_database == null)
            {
                Initialize();
            }

            _database?.DeleteLabel(labelId);
        }

        /// <summary>
        /// 删除零件 ID 为 x 的所有数据（包括 WL 结果和标注）
        /// </summary>
        public static void DeletePartData(int partId)
        {
            if (_database == null)
            {
                Initialize();
            }

            _database?.DeletePartData(partId);
        }

        /// <summary>
        /// 导出零件的 WL 结果
        /// </summary>
        public static void ExportPartWL(int partId)
        {
            if (_database == null)
            {
                Initialize();
            }

            try
            {
                string json = _database!.ExportWLResult(partId);
                string fileName = $"part_{partId}_wl_export.json";
                System.IO.File.WriteAllText(fileName, json);
                Console.WriteLine($"✓ 已导出 WL 结果到：{fileName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"× 导出失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 批量标注文件夹中的零件
        /// </summary>
        public static void BatchLabelFolder(string folderPath, SldWorks swApp)
        {
            if (_database == null)
            {
                Initialize();
            }

            Console.WriteLine($"\n=== 批量标注文件夹：{folderPath} ===\n");
            
            var sldprtFiles = System.IO.Directory.GetFiles(folderPath, "*.sldprt");
            var sldasmFiles = System.IO.Directory.GetFiles(folderPath, "*.sldasm");
            var allFiles = sldprtFiles.Concat(sldasmFiles).ToArray();

            if (allFiles.Length == 0)
            {
                Console.WriteLine("× 未找到 SolidWorks 零件文件");
                return;
            }

            Console.WriteLine($"找到 {allFiles.Length} 个文件\n");

            foreach (var filePath in allFiles)
            {
                Console.WriteLine($"\n处理：{System.IO.Path.GetFileName(filePath)}");
                
                // 打开文档
                int errors = 0;
                int warnings = 0;
                ModelDoc2 model = swApp.OpenDoc6(filePath, 
                    (int)swDocumentTypes_e.swDocPART, 
                    (int)swOpenDocOptions_e.swOpenDocOptions_Silent, 
                    "", ref errors, ref warnings);

                if (model == null)
                {
                    Console.WriteLine($"× 无法打开文件：{filePath}");
                    continue;
                }

                try
                {
                    // 自动计算并存储 WL 特征
                    var graphs = FaceGraphBuilder.BuildGraphs(model);
                    if (graphs.Count > 0)
                    {
                        string partName = model.GetTitle();
                        
                        var bodyIds = _database!.UpsertPartWithBodies(partName, filePath, graphs);
                        Console.WriteLine($"✓ 已存储：{partName} ({graphs.Count} 个 body, 总面数：{graphs.Sum(g => g.Nodes.Count)})");
                    }
                    else
                    {
                        Console.WriteLine($"○ 跳过（无实体）：{model.GetTitle()}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"× 处理失败：{ex.Message}");
                }
                finally
                {
                    // 关闭文档
                    swApp.CloseDoc(model.GetTitle());
                }
            }

            Console.WriteLine("\n=== 批量处理完成 ===");
            ShowStatistics();
            
            Console.WriteLine("\n提示：使用 'view_parts' 命令查看已标注的 body，然后手动添加标注");
        }

        /// <summary>
        /// 根据当前零件的 WL 特征查找推荐类别
        /// </summary>
        public static void FindCategoriesByWL(ModelDoc2 swModel, int wlIterations = 1, int topK = 5)
        {
            if (swModel == null)
            {
                Console.WriteLine("× 错误：没有打开的活动文档");
                return;
            }

            if (_database == null)
            {
                Initialize();
            }

            try
            {
                // 构建所有 body 的拓扑图
                Console.WriteLine("\n=== 构建零件拓扑图 ===");
                var graphs = FaceGraphBuilder.BuildGraphs(swModel);
                
                if (graphs.Count == 0)
                {
                    Console.WriteLine("× 无法构建拓扑图");
                    return;
                }

                // 合并所有 body 的 WL 频率（简单叠加）
                Console.WriteLine($"\n=== 执行 WL 迭代 ({wlIterations} 次) ===");
                var combinedFrequencies = new List<Dictionary<string, int>>();
                
                foreach (var graph in graphs)
                {
                    var wlFreq = WLGraphKernel.PerformWLIterations(graph, wlIterations);
                    if (combinedFrequencies.Count == 0)
                    {
                        combinedFrequencies = wlFreq;
                    }
                    else
                    {
                        // 合并频率
                        for (int i = 0; i < wlFreq.Count; i++)
                        {
                            foreach (var kvp in wlFreq[i])
                            {
                                if (!combinedFrequencies[i].ContainsKey(kvp.Key))
                                    combinedFrequencies[i][kvp.Key] = 0;
                                combinedFrequencies[i][kvp.Key] += kvp.Value;
                            }
                        }
                    }
                }

                // 查找相似类别
                Console.WriteLine("\n=== 查找推荐类别 ===");
                int lastIter = combinedFrequencies.Count - 1;
                Console.WriteLine($"使用合并后的 WL 频率（迭代 {lastIter}）：{JsonConvert.SerializeObject(combinedFrequencies[lastIter])}");
                
                var recommendations = _database!.FindTopCategoriesByWLTags(combinedFrequencies, topK: topK, minSimilarity: 0.3);
                
                if (recommendations.Count == 0)
                {
                    Console.WriteLine("未找到相似的已标注 body");
                    // 调试：显示数据库中的内容
                    Console.WriteLine("\n[调试] 数据库中已有的标注数据:");
                    var allBodies = _database.GetAllBodiesWithLabels();
                    if (allBodies.Count > 0)
                    {
                        foreach (var (partId, partName, bodyId, bodyName, labels) in allBodies)
                        {
                            Console.WriteLine($"  - {bodyName}: {labels.Count} 个标注");
                        }
                    }
                    else
                    {
                        Console.WriteLine("  数据库中暂无任何标注数据");
                    }
                    return;
                }

                Console.WriteLine($"\n{'=',60}");
                Console.WriteLine("TOP-{0} 推荐类别", topK);
                Console.WriteLine($"{'=',60}");
                
                for (int i = 0; i < recommendations.Count; i++)
                {
                    var (category, count, avgSim, avgConf) = recommendations[i];
                    Console.WriteLine($"{i + 1}. 类别：{category}");
                    Console.WriteLine($"   出现次数：{count} 次");
                    Console.WriteLine($"   平均相似度：{avgSim:F3} ({avgSim * 100:F1}%)");
                    Console.WriteLine($"   平均置信度：{avgConf:F2}");
                    Console.WriteLine();
                }

                Console.WriteLine($"{'=',60}\n");
                
                // 自动显示详细匹配结果
                ViewDetailedMatches(combinedFrequencies, topK: topK);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"× 查找过程出错：{ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        /// <summary>
        /// 查看详细匹配结果
        /// </summary>
        private static void ViewDetailedMatches(List<Dictionary<string, int>> wlFrequencies, int topK = 10)
        {
            // 使用最后一轮迭代进行查询
            int queryIter = wlFrequencies.Count - 1;
            var detailedResults = _database!.FindCategoriesByWLTags(wlFrequencies, topK: topK, minSimilarity: 0.3);

            if (detailedResults.Count == 0)
            {
                Console.WriteLine("无详细匹配结果");
                return;
            }

            Console.WriteLine($"\n{'=',80}");
            Console.WriteLine("详细匹配结果");
            Console.WriteLine($"{'=',80}\n");

            for (int i = 0; i < detailedResults.Count; i++)
            {
                var (category, partName, bodyName, similarity, confidence, notes) = detailedResults[i];
                Console.WriteLine($"[{i + 1}] {bodyName}");
                Console.WriteLine($"    类别：{category}");
                Console.WriteLine($"    相似度：{similarity:F3} ({similarity * 100:F1}%)");
                Console.WriteLine($"    置信度：{confidence:F2}");
                if (!string.IsNullOrEmpty(notes))
                {
                    Console.WriteLine($"    备注：{notes}");
                }
                Console.WriteLine();
            }

            Console.WriteLine($"{'=',80}\n");
        }

        /// <summary>
        /// 使用 WL 标签查找相似的用户标注（直接显示 body 的标注信息）
        /// </summary>
        public static void FindLabelsByWL(ModelDoc2 swModel, int wlIterations = 1, int topK = 10, double minSimilarity = 0.3)
        {
            if (swModel == null)
            {
                Console.WriteLine("× 错误：没有打开的活动文档");
                return;
            }

            if (_database == null)
            {
                Initialize();
            }

            try
            {
                // 构建所有 body 的拓扑图
                Console.WriteLine("\n=== 构建零件拓扑图 ===");
                var graphs = FaceGraphBuilder.BuildGraphs(swModel);
                
                if (graphs.Count == 0)
                {
                    Console.WriteLine("× 无法构建拓扑图");
                    return;
                }

                // 合并所有 body 的 WL 频率（简单叠加）
                Console.WriteLine($"\n=== 执行 WL 迭代 ({wlIterations} 次) ===");
                var combinedFrequencies = new List<Dictionary<string, int>>();
                
                foreach (var graph in graphs)
                {
                    var wlFreq = WLGraphKernel.PerformWLIterations(graph, wlIterations);
                    if (combinedFrequencies.Count == 0)
                    {
                        combinedFrequencies = wlFreq;
                    }
                    else
                    {
                        // 合并频率
                        for (int i = 0; i < wlFreq.Count; i++)
                        {
                            foreach (var kvp in wlFreq[i])
                            {
                                if (!combinedFrequencies[i].ContainsKey(kvp.Key))
                                    combinedFrequencies[i][kvp.Key] = 0;
                                combinedFrequencies[i][kvp.Key] += kvp.Value;
                            }
                        }
                    }
                }

                // 查找相似的标注
                Console.WriteLine("\n=== 查找相似的用户标注 ===");
                int lastIter = combinedFrequencies.Count - 1;
                Console.WriteLine($"使用合并后的 WL 频率（迭代 {lastIter}）：{JsonConvert.SerializeObject(combinedFrequencies[lastIter])}");
                
                // 使用数据库方法查找相似的标注
                var matches = _database!.FindBodiesByWLTags(combinedFrequencies, topK: topK, minSimilarity: minSimilarity);
                
                if (matches.Count == 0)
                {
                    Console.WriteLine("未找到相似的已标注 body");
                    return;
                }

                // 显示结果
                Console.WriteLine($"\n{'=',80}");
                Console.WriteLine("TOP-5 相似标注");
                Console.WriteLine($"{'=',80}\n");

                var top5 = matches.Take(5).ToList();
                for (int i = 0; i < top5.Count; i++)
                {
                    var (partId, partName, bodyId, bodyName, labelCategory, labelValue, similarity, confidence, notes) = matches[i];
                    Console.WriteLine($"{i + 1}. Body：{partName}+{bodyName}");
                    Console.WriteLine($"   标注：{labelCategory} = {labelValue}");
                    Console.WriteLine($"   相似度：{similarity:F3} ({similarity * 100:F1}%)");
                    Console.WriteLine($"   置信度：{confidence:F2}");
                    if (!string.IsNullOrEmpty(notes))
                    {
                        Console.WriteLine($"   备注：{notes}");
                    }
                    Console.WriteLine();
                }

                Console.WriteLine($"{'=',80}\n");
                
                // 显示详细匹配结果
                Console.WriteLine($"{'=',80}");
                Console.WriteLine("详细匹配结果");
                Console.WriteLine($"{'=',80}\n");

                for (int i = 0; i < matches.Count; i++)
                {
                    var (partId, partName, bodyId, bodyName, labelCategory, labelValue, similarity, confidence, notes) = matches[i];
                    Console.WriteLine($"[{i + 1}] {partName}+{bodyName}");
                    Console.WriteLine($"    标注：{labelCategory} = {labelValue}");
                    Console.WriteLine($"    相似度：{similarity:F3} ({similarity * 100:F1}%)");
                    Console.WriteLine($"    置信度：{confidence:F2}");
                    if (!string.IsNullOrEmpty(notes))
                    {
                        Console.WriteLine($"    备注：{notes}");
                    }
                    Console.WriteLine();
                }

                Console.WriteLine($"{'=',80}\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"× 查找过程出错：{ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        /// <summary>
        /// 清空拓扑数据库
        /// </summary>
        public static void ClearDatabase(string mode = "all")
        {
            if (_database == null)
            {
                Initialize();
            }

            Console.WriteLine($"\n=== 清空拓扑数据库 ===");
            
            try
            {
                switch (mode.ToLower())
                {
                    case "wl":
                        _database!.ClearAllWLResults();
                        Console.WriteLine("✓ WL 结果已清空");
                        break;
                    
                    case "labels":
                        _database!.ClearAllLabels();
                        Console.WriteLine("✓ 标注数据已清空");
                        break;
                    
                    case "all":
                    default:
                        _database!.ClearAll();
                        Console.WriteLine("✓ 所有数据已清空");
                        break;
                }
                
                ShowStatistics();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"× 清空数据库失败：{ex.Message}");
            }
        }
    }
}
