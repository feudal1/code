using System;
using SolidWorks.Interop.sldworks;

namespace tools
{
    /// <summary>
    /// 拓扑标注工具使用示例
    /// </summary>
    public static class TopologyLabelingExample
    {
        /// <summary>
        /// 完整标注流程示例
        /// </summary>
        public static void RunFullExample(SldWorks swApp, ModelDoc2 swModel)
        {
            Console.WriteLine("========== 零件拓扑标注系统 - 使用示例 ==========\n");

            // 1. 初始化数据库（首次使用时调用）
            Console.WriteLine("步骤 1: 初始化数据库");
            TopologyLabeler.Initialize("topology_labels.db");
            
            // 2. 标注当前零件
            Console.WriteLine("\n步骤 2: 标注当前零件");
            TopologyLabeler.LabelCurrentPart(swModel, wlIterations: 1);
            
            // 3. 查看所有已标注零件
            Console.WriteLine("\n步骤 3: 查看所有零件");
            TopologyLabeler.ViewAllParts();
            
            // 4. 按类别搜索零件
            Console.WriteLine("\n步骤 4: 搜索特定类别的零件");
            TopologyLabeler.SearchByCategory("结构类型", "框架");
            
            // 5. 查看统计信息
            Console.WriteLine("\n步骤 5: 查看统计");
            TopologyLabeler.ShowStatistics();
            
            Console.WriteLine("\n========== 示例完成 ==========");
        }

        /// <summary>
        /// 快速标注模式（仅计算和存储，不立即标注）
        /// </summary>
        public static void QuickLabel(SldWorks swApp, ModelDoc2 swModel)
        {
            if (swModel == null)
            {
                Console.WriteLine("× 错误：没有打开的零件");
                return;
            }

            Console.WriteLine("=== 快速标注模式 ===\n");
            
            // 自动计算 WL 特征并存储
            var graphs = FaceGraphBuilder.BuildGraphs(swModel);
            if (graphs == null || graphs.Count == 0)
            {
                Console.WriteLine("× 无法构建拓扑图");
                return;
            }

            // 使用第一个 body 的图
            var graph = graphs[0];
            var wlFrequencies = WLGraphKernel.PerformWLIterations(graph, 1);
            
            string partName = swModel.GetTitle();
            string fullPath = swModel.GetPathName();
            
            var db = new TopologyDatabase();
            
            // 使用第一个 body 的图
            var bodyGraphs = new List<BodyGraph> { graph };
            var bodyIds = db.UpsertPartWithBodies(partName, fullPath, bodyGraphs);
            
            Console.WriteLine($"\n✓ 零件已存储 (Body IDs: {string.Join(", ", bodyIds)})");
            Console.WriteLine($"提示：使用 'label_view_body {bodyIds[0]}' 添加标注");
        }

        /// <summary>
        /// 批量处理文件夹中的所有零件
        /// </summary>
        public static void BatchProcessExample(SldWorks swApp, string folderPath)
        {
            Console.WriteLine("=== 批量处理模式 ===\n");
            
            // 自动扫描、计算并存储所有零件的 WL 特征
            TopologyLabeler.BatchLabelFolder(folderPath, swApp);
            
            Console.WriteLine("\n批量处理完成！");
            Console.WriteLine("提示：使用 'view_parts' 查看所有零件，然后逐个添加标注");
        }
    }
}
