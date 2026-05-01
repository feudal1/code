using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace tools
{
    /// <summary>
    /// Weisfeiler-Lehman 图核实现 - 计算零件拓扑相似度
    /// </summary>
    public static class WLGraphKernel
    {
        /// <summary>
        /// 执行 WL 迭代，更新节点标签（返回所有轮次迭代结果）
        /// </summary>
        /// <param name="graph">零件图或 Body 图</param>
        /// <param name="iterations">迭代次数（默认 3 层）</param>
        /// <returns>每次迭代后的标签频率列表（包含初始和所有迭代轮次）</returns>
        public static List<Dictionary<string, int>> PerformWLIterations(BodyGraph graph, int iterations = 3)
        {
            var labelFrequenciesPerIter = new List<Dictionary<string, int>>();
                    
            if (graph == null || graph.Nodes.Count == 0)
            {
                Console.WriteLine("警告：空图，无法执行 WL 迭代");
                return labelFrequenciesPerIter;
            }
        
            // 初始化所有节点的标签
            foreach (var node in graph.Nodes)
            {
                node.CurrentLabel = node.FaceType;
            }
        
            // 统计初始迭代的标签频率 (迭代 0)
            var initialFreq = CountLabelFrequencies(graph);
            labelFrequenciesPerIter.Add(initialFreq);
        
            Console.WriteLine($"  [{graph.FullBodyName}] 迭代 0: {initialFreq.Count} 种标签");
            for (int iter = 1; iter <= iterations; iter++)
            {
                // 为每个节点生成新标签
                var newLabels = new Dictionary<int, string>();
                        
                foreach (var node in graph.Nodes)
                {
                    // 收集邻居标签并排序
                    var neighborLabels = node.NeighborIds
                        .Select(neighborId => graph.Nodes[neighborId].CurrentLabel)
                        .OrderBy(label => label)
                        .ToList();
        
                    // 构造新标签：当前标签 + 排序后的邻居标签集合
                    string combinedLabel = CombineLabels(node.CurrentLabel, neighborLabels);
                    newLabels[node.Id] = combinedLabel;
                }
        
                // 更新所有节点的标签
                foreach (var node in graph.Nodes)
                {
                    node.CurrentLabel = newLabels[node.Id];
                }
        
                // 统计本次迭代的标签频率
                var freq = CountLabelFrequencies(graph);
                labelFrequenciesPerIter.Add(freq);
                        
                Console.WriteLine($"  [{graph.FullBodyName}] 迭代 {iter}: {freq.Count} 种标签");
            }
        
            // 所有迭代完成后，生成最终的 WL 图指纹
            PrintFirst20Chars(graph, $"完成");
                    
            // 返回所有迭代轮次的频率数据，由调用者决定如何使用
            return labelFrequenciesPerIter;
        }

        /// <summary>
        /// 执行 WL 迭代（Face 图版本）
        /// </summary>
        /// <param name="graph">Face 图</param>
        /// <param name="iterations">迭代次数（默认 3 层）</param>
        /// <returns>每次迭代后的标签频率列表</returns>
        public static List<Dictionary<string, int>> PerformWLIterations(FaceGraph graph, int iterations = 3)
        {
            var labelFrequenciesPerIter = new List<Dictionary<string, int>>();
                    
            if (graph == null || graph.Nodes.Count == 0)
            {
                Console.WriteLine("警告：空图，无法执行 WL 迭代");
                return labelFrequenciesPerIter;
            }
        
            // 初始化所有节点的标签
            foreach (var node in graph.Nodes)
            {
                node.CurrentLabel = node.FaceType;
            }
        
            // 统计初始迭代的标签频率 (迭代 0)
            var initialFreq = CountLabelFrequencies(graph);
            labelFrequenciesPerIter.Add(initialFreq);
        
            Console.WriteLine($"  [{graph.FullFaceName}] 迭代 0: {initialFreq.Count} 种标签");
            for (int iter = 1; iter <= iterations; iter++)
            {
                // 为每个节点生成新标签
                var newLabels = new Dictionary<int, string>();
                        
                foreach (var node in graph.Nodes)
                {
                    // 收集邻居标签并排序
                    var neighborLabels = node.NeighborIds
                        .Select(neighborId => graph.Nodes[neighborId].CurrentLabel)
                        .OrderBy(label => label)
                        .ToList();
        
                    // 构造新标签：当前标签 + 排序后的邻居标签集合
                    string combinedLabel = CombineLabels(node.CurrentLabel, neighborLabels);
                    newLabels[node.Id] = combinedLabel;
                }
        
                // 更新所有节点的标签
                foreach (var node in graph.Nodes)
                {
                    node.CurrentLabel = newLabels[node.Id];
                }
        
                // 统计本次迭代的标签频率
                var freq = CountLabelFrequencies(graph);
                labelFrequenciesPerIter.Add(freq);
                        
                Console.WriteLine($"  [{graph.FullFaceName}] 迭代 {iter}: {freq.Count} 种标签");
            }
        
            // 所有迭代完成后，生成最终的 WL 图指纹
            PrintFirst20CharsForFace(graph, $"完成");
                    
            // 返回所有迭代轮次的频率数据，由调用者决定如何使用
            return labelFrequenciesPerIter;
        }

        /// <summary>
        /// 打印图中第一个节点的前 20 个字符标签
        /// </summary>
        private static void PrintFirst20Chars(BodyGraph graph, string iterInfo)
        {
            if (graph.Nodes.Count == 0) return;
            
            var firstNode = graph.Nodes[0];
            string label = firstNode.CurrentLabel ?? "";
            string displayText = label.Length <= 20 ? label : label.Substring(0, 20);
            
            Console.WriteLine($"    [{iterInfo}] 首个面标签前 20 字符：{displayText}");
        }

        /// <summary>
        /// 打印 Face 图中第一个节点的前 20 个字符标签
        /// </summary>
        private static void PrintFirst20CharsForFace(FaceGraph graph, string iterInfo)
        {
            if (graph.Nodes.Count == 0) return;
            
            var firstNode = graph.Nodes[0];
            string label = firstNode.CurrentLabel ?? "";
            string displayText = label.Length <= 20 ? label : label.Substring(0, 20);
            
            Console.WriteLine($"    [{iterInfo}] 首个面标签前 20 字符：{displayText}");
        }

        /// <summary>
        /// 组合当前标签和邻居标签生成新标签
        /// </summary>
        private static string CombineLabels(string currentLabel, List<string> neighborLabels)
        {
            // 格式：当前标签_ (邻居标签 1，邻居标签 2，...)
            string neighborsStr = string.Join(",", neighborLabels);
            string combined = $"{currentLabel}_({neighborsStr})";
            
            // 使用 MD5 哈希压缩标签，避免标签过长
            return HashLabel(combined);
        }

        /// <summary>
        /// 对标签字符串进行 MD5 哈希，生成短标识符
        /// </summary>
        private static string HashLabel(string label)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] inputBytes = Encoding.UTF8.GetBytes(label);
                byte[] hashBytes = md5.ComputeHash(inputBytes);
                
                // 转换为十六进制字符串 (取前 8 个字符)
                StringBuilder sb = new StringBuilder();
                for (int i = 0; i < 4; i++)
                {
                    sb.Append(hashBytes[i].ToString("X2"));
                }
                return "L" + sb.ToString();  // L 开头表示这是生成的标签
            }
        }

        /// <summary>
        /// 统计 Body 图中各标签的出现频率
        /// </summary>
        private static Dictionary<string, int> CountLabelFrequencies(BodyGraph graph)
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
        /// 统计 Face 图中各标签的出现频率
        /// </summary>
        private static Dictionary<string, int> CountLabelFrequencies(FaceGraph graph)
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
        /// 计算两个零件的相似度（基于 WL 标签频率的余弦相似度）
        /// </summary>
        public static double CalculateSimilarity(
            Dictionary<string, int> freq1, 
            Dictionary<string, int> freq2,
            bool useCosine = true)
        {
            if (freq1.Count == 0 || freq2.Count == 0)
                return 0.0;

            // 直接使用传入的频率字典
            var allLabels = new HashSet<string>(freq1.Keys);
            allLabels.UnionWith(freq2.Keys);

            double dotProduct = 0.0;
            double magnitude1 = 0.0;
            double magnitude2 = 0.0;

            // 计算点积和模长
            foreach (var label in allLabels)
            {
                int count1 = freq1.ContainsKey(label) ? freq1[label] : 0;
                int count2 = freq2.ContainsKey(label) ? freq2[label] : 0;

                dotProduct += count1 * count2;
                magnitude1 += count1 * count1;
                magnitude2 += count2 * count2;
            }

            magnitude1 = Math.Sqrt(magnitude1);
            magnitude2 = Math.Sqrt(magnitude2);

            if (useCosine)
            {
                // 余弦相似度
                if (magnitude1 == 0 || magnitude2 == 0)
                    return 0.0;
                    
                return dotProduct / (magnitude1 * magnitude2);
            }
            else
            {
                // 归一化点积
                double maxProduct = Math.Max(freq1.Values.Sum(), freq2.Values.Sum());
                if (maxProduct == 0) return 0.0;
                
                return dotProduct / maxProduct;
            }
        }
        
        /// <summary>
        /// 从规范化字符串解析频率字典
        /// 格式："标签 1:次数 1|标签 2:次数 2|..."
        /// </summary>
        private static Dictionary<string, int> ParseCanonicalForm(string canonicalForm)
        {
            var result = new Dictionary<string, int>();
            
            if (string.IsNullOrEmpty(canonicalForm))
                return result;
            
            var parts = canonicalForm.Split('|');
            foreach (var part in parts)
            {
                var kv = part.Split(':');
                if (kv.Length == 2 && int.TryParse(kv[1], out int count))
                {
                    result[kv[0]] = count;
                }
            }
            
            return result;
        }

        /// <summary>
        /// 计算两个零件的综合相似度 (加权组合多次迭代的结果)
        /// </summary>
        public static double CalculateComprehensiveSimilarity(
            List<Dictionary<string, int>> freqList1,
            List<Dictionary<string, int>> freqList2,
            double decayFactor = 0.5)
        {
            if (freqList1.Count == 0 || freqList2.Count == 0)
                return 0.0;

            int maxIterations = Math.Min(freqList1.Count, freqList2.Count);
            double totalSimilarity = 0.0;
            double totalWeight = 0.0;

            // 越后面的迭代权重越低 (指数衰减)
            for (int i = 0; i < maxIterations; i++)
            {
                double weight = Math.Pow(decayFactor, i);
                double sim = CalculateSimilarity(freqList1[i], freqList2[i], useCosine: true);
                
                totalSimilarity += weight * sim;
                totalWeight += weight;
            }

            return totalSimilarity / totalWeight;
        }

        /// <summary>
        /// 批量计算多个 body 之间的相似度矩阵
        /// </summary>
        public static double[,] ComputeSimilarityMatrix(
            List<BodyGraph> graphs,
            int wlIterations = 1,
            double decayFactor = 0.5)
        {
            int n = graphs.Count;
            double[,] similarityMatrix = new double[n, n];

            Console.WriteLine($"\n开始计算 {n} 个 body 的相似度矩阵...");

            // 预先计算每个 body 的 WL 迭代结果
            var allFrequencies = new List<List<Dictionary<string, int>>>();
            
            for (int i = 0; i < n; i++)
            {
                Console.WriteLine($"\n处理 Body [{i + 1}/{n}]: {graphs[i].PartName}/{graphs[i].BodyName}");
                var freqList = PerformWLIterations(graphs[i], wlIterations);
                allFrequencies.Add(freqList);
            }

            // 计算相似度矩阵
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    if (i == j)
                    {
                        similarityMatrix[i, j] = 1.0;  // 自身相似度为 1
                    }
                    else if (j > i)
                    {
                        double sim = CalculateComprehensiveSimilarity(
                            allFrequencies[i], 
                            allFrequencies[j], 
                            decayFactor);
                        
                        similarityMatrix[i, j] = sim;
                        similarityMatrix[j, i] = sim;  // 对称矩阵
                    }
                }
            }

            // 打印相似度矩阵
            PrintSimilarityMatrix(similarityMatrix, graphs.Select(g => $"{g.PartName}/{g.BodyName}").ToList());

            return similarityMatrix;
        }

        /// <summary>
        /// 打印相似度矩阵
        /// </summary>
        private static void PrintSimilarityMatrix(double[,] matrix, List<string> partNames)
        {
            int n = matrix.GetLength(0);
            
            Console.WriteLine("\n========== 零件拓扑相似度矩阵 ==========");
            
            // 打印表头
            Console.Write("        ");
            for (int j = 0; j < n; j++)
            {
                Console.Write($"{j + 1,-8}");
            }
            Console.WriteLine();
            
            // 打印每一行
            for (int i = 0; i < n; i++)
            {
                Console.Write($"{i + 1,-4}:   ");
                for (int j = 0; j < n; j++)
                {
                    Console.Write($"{matrix[i, j],-8:F3}");
                }
                Console.WriteLine();
            }
            
            Console.WriteLine("\n零件名称对应:");
            for (int i = 0; i < n; i++)
            {
                Console.WriteLine($"  {i + 1}. {partNames[i]}");
            }
            Console.WriteLine("==========================================\n");
        }
    }
}
