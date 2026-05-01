using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace cad_tools
{
    /// <summary>
    /// CAD 2D图形节点 - 表示CAD图纸中的图形元素
    /// </summary>
    public class CADGraphEdgeNode
    {
        /// <summary>
        /// 节点ID
        /// </summary>
        public int Id { get; set; }
        
        /// <summary>
        /// 节点类型：Line, Arc, Circle, Polyline, Ellipse
        /// </summary>
        public string EdgeType { get; set; } = "Unknown";
        
        /// <summary>
        /// 当前WL标签
        /// </summary>
        public string CurrentLabel { get; set; } = "Unknown";
        
        /// <summary>
        /// 几何特征（长度、半径等）
        /// </summary>
        public double GeometryValue { get; set; }
        
        /// <summary>
        /// 角度特征
        /// </summary>
        public double Angle { get; set; }
        
        /// <summary>
        /// 是否水平
        /// </summary>
        public bool IsHorizontal { get; set; }
        
        /// <summary>
        /// 是否垂直
        /// </summary>
        public bool IsVertical { get; set; }
        
        /// <summary>
        /// 连接的节点ID列表
        /// </summary>
        public List<int> ConnectedNodes { get; set; } = new List<int>();
    }

    /// <summary>
    /// CAD 2D图 - 表示一个视图或图形的拓扑结构
    /// </summary>
    public class CADGraphEdgeGraph
    {
        /// <summary>
        /// 图形名称（视图名称或文件名）
        /// </summary>
        public string GraphName { get; set; } = "";
        
        /// <summary>
        /// 源文件名
        /// </summary>
        public string SourceFile { get; set; } = "";
        
        /// <summary>
        /// 节点列表
        /// </summary>
        public List<CADGraphEdgeNode> Nodes { get; set; } = new List<CADGraphEdgeNode>();
    }

    /// <summary>
    /// CAD WL图核实现 - 计算2D图形拓扑相似度
    /// </summary>
    public static class CADWLGraphKernel
    {
        /// <summary>
        /// 执行WL迭代，更新节点标签
        /// </summary>
        /// <param name="graph">CAD 2D图</param>
        /// <param name="iterations">迭代次数（默认3层）</param>
        /// <returns>每次迭代后的标签频率列表</returns>
        public static List<Dictionary<string, int>> PerformWLIterations(CADGraphEdgeGraph graph, int iterations = 3)
        {
            var labelFrequenciesPerIter = new List<Dictionary<string, int>>();
            
            if (graph == null || graph.Nodes.Count == 0)
            {
                Console.WriteLine("警告：空CAD图，无法执行WL迭代");
                return labelFrequenciesPerIter;
            }
        
            // 初始化所有节点的标签（基于边类型和几何特征）
            foreach (var node in graph.Nodes)
            {
                node.CurrentLabel = BuildInitialLabel(node);
            }
        
            // 统计初始迭代的标签频率（迭代0）
            labelFrequenciesPerIter.Add(CountLabelFrequencies(graph));
        
            // 执行WL迭代
            for (int iter = 1; iter <= iterations; iter++)
            {
                var newLabels = new Dictionary<int, string>();
        
                foreach (var node in graph.Nodes)
                {
                    // 获取邻居标签并排序
                    var neighborLabels = node.ConnectedNodes
                        .Where(nid => graph.Nodes.Any(n => n.Id == nid))
                        .Select(nid => graph.Nodes.First(n => n.Id == nid).CurrentLabel)
                        .OrderBy(l => l)
                        .ToList();
        
                    // 构建新标签：当前标签 + 邻居标签列表
                    var labelBuilder = new StringBuilder();
                    labelBuilder.Append($"[{node.CurrentLabel}]{string.Join(",", neighborLabels)}");
        
                    // 使用MD5生成新标签（压缩长度）
                    newLabels[node.Id] = HashLabel(labelBuilder.ToString());
                }
        
                // 更新所有节点标签
                foreach (var kvp in newLabels)
                {
                    var node = graph.Nodes.First(n => n.Id == kvp.Key);
                    node.CurrentLabel = kvp.Value;
                }
        
                // 统计当前迭代的标签频率
                labelFrequenciesPerIter.Add(CountLabelFrequencies(graph));
            }
        
            return labelFrequenciesPerIter;
        }

        /// <summary>
        /// 构建初始标签（基于边类型和几何特征）
        /// </summary>
        private static string BuildInitialLabel(CADGraphEdgeNode node)
        {
            double geometryBucket = QuantizeGeometry(node.GeometryValue);
            double angleBucket = QuantizeAngle(node.Angle);

            var parts = new List<string>
            {
                node.EdgeType,
                node.IsHorizontal ? "H" : node.IsVertical ? "V" : "D",
                $"G{geometryBucket:F1}"
            };
            
            if (angleBucket >= 0)
            {
                parts.Add($"A{angleBucket:F0}");
            }
            
            return string.Join("_", parts);
        }

        /// <summary>
        /// 对几何值分桶，减少小数噪声对标签的影响
        /// </summary>
        private static double QuantizeGeometry(double value)
        {
            if (value <= 0)
            {
                return 0;
            }

            const double step = 1.0;
            return Math.Round(value / step) * step;
        }

        /// <summary>
        /// 对角度按固定区间量化（默认15度）
        /// </summary>
        private static double QuantizeAngle(double angle)
        {
            if (angle <= 0)
            {
                return -1;
            }

            const double bucketSize = 15.0;
            var normalized = angle % 360.0;
            if (normalized < 0)
            {
                normalized += 360.0;
            }

            return Math.Round(normalized / bucketSize) * bucketSize;
        }

        /// <summary>
        /// 哈希标签（使用MD5前8位）
        /// </summary>
        private static string HashLabel(string input)
        {
            using (var md5 = MD5.Create())
            {
                var bytes = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
                return BitConverter.ToString(bytes, 0, 4).Replace("-", "").ToLower();
            }
        }

        /// <summary>
        /// 统计CAD图中各标签的出现频率
        /// </summary>
        private static Dictionary<string, int> CountLabelFrequencies(CADGraphEdgeGraph graph)
        {
            var frequency = new Dictionary<string, int>();
            
            foreach (var node in graph.Nodes)
            {
                if (!frequency.ContainsKey(node.CurrentLabel))
                {
                    frequency[node.CurrentLabel] = 0;
                }
                frequency[node.CurrentLabel]++;
            }
            
            return frequency;
        }

        /// <summary>
        /// 计算两个CAD图形的相似度（基于WL标签频率的余弦相似度）
        /// </summary>
        public static double CalculateSimilarity(
            Dictionary<string, int> freq1, 
            Dictionary<string, int> freq2,
            bool useCosine = true)
        {
            if (freq1.Count == 0 || freq2.Count == 0)
                return 0.0;

            if (useCosine)
            {
                return CalculateCosineSimilarity(freq1, freq2);
            }
            else
            {
                return CalculateJaccardSimilarity(freq1, freq2);
            }
        }

        /// <summary>
        /// 余弦相似度
        /// </summary>
        private static double CalculateCosineSimilarity(Dictionary<string, int> freq1, Dictionary<string, int> freq2)
        {
            // 获取所有标签的并集
            var allLabels = new HashSet<string>(freq1.Keys);
            allLabels.UnionWith(freq2.Keys);

            double dotProduct = 0;
            double norm1 = 0;
            double norm2 = 0;

            foreach (var label in allLabels)
            {
                int v1 = freq1.ContainsKey(label) ? freq1[label] : 0;
                int v2 = freq2.ContainsKey(label) ? freq2[label] : 0;

                dotProduct += v1 * v2;
                norm1 += v1 * v1;
                norm2 += v2 * v2;
            }

            if (norm1 == 0 || norm2 == 0)
                return 0.0;

            return dotProduct / (Math.Sqrt(norm1) * Math.Sqrt(norm2));
        }

        /// <summary>
        /// Jaccard相似度
        /// </summary>
        private static double CalculateJaccardSimilarity(Dictionary<string, int> freq1, Dictionary<string, int> freq2)
        {
            var intersection = new HashSet<string>(freq1.Keys);
            intersection.IntersectWith(freq2.Keys);

            var union = new HashSet<string>(freq1.Keys);
            union.UnionWith(freq2.Keys);

            return union.Count == 0 ? 0.0 : (double)intersection.Count / union.Count;
        }

        /// <summary>
        /// 批量计算多个CAD图形之间的相似度矩阵
        /// </summary>
        public static double[,] ComputeSimilarityMatrix(
            List<CADGraphEdgeGraph> graphs,
            int wlIterations = 3,
            double decayFactor = 0.5)
        {
            int n = graphs.Count;
            double[,] similarityMatrix = new double[n, n];

            Console.WriteLine($"\n开始计算 {n} 个CAD图形的相似度矩阵...");

            // 预先计算每个图形的WL迭代结果
            var allFrequencies = new List<List<Dictionary<string, int>>>();
            for (int i = 0; i < n; i++)
            {
                var frequencies = PerformWLIterations(graphs[i], iterations: wlIterations);
                allFrequencies.Add(frequencies);
                Console.WriteLine($"  ✓ 图形 {i + 1}/{n} 完成WL迭代");
            }

            // 计算相似度矩阵
            for (int i = 0; i < n; i++)
            {
                for (int j = i; j < n; j++)
                {
                    double similarity = CalculateMultiIterationSimilarity(
                        allFrequencies[i], 
                        allFrequencies[j], 
                        decayFactor);
                    
                    similarityMatrix[i, j] = similarity;
                    similarityMatrix[j, i] = similarity;
                }
            }

            return similarityMatrix;
        }

        /// <summary>
        /// 计算多轮迭代的综合相似度（带衰减因子）
        /// </summary>
        public static double CalculateMultiIterationSimilarity(
            List<Dictionary<string, int>> freqList1,
            List<Dictionary<string, int>> freqList2,
            double decayFactor = 0.5)
        {
            if (freqList1.Count == 0 || freqList2.Count == 0)
                return 0.0;

            int maxIterations = Math.Min(freqList1.Count, freqList2.Count);
            double totalSimilarity = 0.0;
            double totalWeight = 0.0;

            for (int i = 0; i < maxIterations; i++)
            {
                double weight = Math.Pow(decayFactor, i);
                double sim = CalculateSimilarity(freqList1[i], freqList2[i], useCosine: true);
                
                totalSimilarity += weight * sim;
                totalWeight += weight;
            }

            return totalSimilarity / totalWeight;
        }
    }
}
