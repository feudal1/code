using System;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using View = SolidWorks.Interop.sldworks.View;

namespace tools
{
    public class get_all_edges
    {
        /// <summary>
        /// 获取所有边的信息
        /// </summary>
        static public void run(ModelDoc2 swModel)
        {
            if (swModel == null)
            {
                Console.WriteLine("错误：没有打开的活动文档。");
                return;
            }

            PartDoc partDoc = (PartDoc)swModel;

            // 获取所有实体
            object[] vBodies = (object[])partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false);
            
            // 存储邻接矩阵的字典：面索引 -> 相邻面集合
            var adjacencyMatrix = new System.Collections.Generic.Dictionary<int, System.Collections.Generic.HashSet<int>>();
            var faceIndexMap = new System.Collections.Generic.Dictionary<Face2, int>(); // 面对象到索引的映射
            var facesList = new System.Collections.Generic.List<Face2>(); // 面对象列表

            int faceCount = 0;

            foreach (Body2 body in vBodies)
            {
                object[] vedges = (object[])body.GetEdges();

                foreach (Edge edge in vedges)
                {
                    var twoAdjacentFaces = (object[])edge.GetTwoAdjacentFaces();

                    if (twoAdjacentFaces != null && twoAdjacentFaces.Length >= 2)
                    {
                        Face2 face1 = (Face2)twoAdjacentFaces[0];
                        Face2 face2 = (Face2)twoAdjacentFaces[1];
                        var face1_surface=face1.IGetSurface();
                        var face1_type = face1_surface.IsCylinder()?"圆柱面":"平面";
                        var face2_surface=face2.IGetSurface();
                        var face2_type = face2_surface.IsCylinder()?"圆柱面":"平面";
                        Console.WriteLine($"face1type：{face1_type}，face2type：{face2_type}");
                        // 为每个面分配唯一索引
                        if (!faceIndexMap.ContainsKey(face1))
                        {
                            faceIndexMap[face1] = faceCount++;
                            facesList.Add(face1);
                        }

                        if (!faceIndexMap.ContainsKey(face2))
                        {
                            faceIndexMap[face2] = faceCount++;
                            facesList.Add(face2);
                        }

                        int index1 = faceIndexMap[face1];
                        int index2 = faceIndexMap[face2];

                        // 初始化邻接矩阵条目
                        if (!adjacencyMatrix.ContainsKey(index1))
                            adjacencyMatrix[index1] = new System.Collections.Generic.HashSet<int>();

                        if (!adjacencyMatrix.ContainsKey(index2))
                            adjacencyMatrix[index2] = new System.Collections.Generic.HashSet<int>();

                        // 添加邻接关系
                        adjacencyMatrix[index1].Add(index2);
                        adjacencyMatrix[index2].Add(index1);
                    }
                }
            }

            // 输出邻接矩阵信息
            Console.WriteLine($"总共找到 {faceCount} 个面");
            Console.WriteLine("邻接关系：");

            for (int i = 0; i < faceCount; i++)
            {
                Console.Write($"面 {i}: ");
                if (adjacencyMatrix.ContainsKey(i))
                {
                    foreach (int adjacentFace in adjacencyMatrix[i])
                    {
                        Console.Write($"{adjacentFace} ");
                    }
                }
                Console.WriteLine();
            }

            // 如果需要完整的邻接矩阵形式，可以创建二维数组
            bool[,] fullAdjacencyMatrix = CreateFullAdjacencyMatrix(adjacencyMatrix, faceCount);
            
            // 打印完整的邻接矩阵
            PrintAdjacencyMatrix(fullAdjacencyMatrix, faceCount);
        }

        /// <summary>
        /// 创建完整的邻接矩阵
        /// </summary>
        private static bool[,] CreateFullAdjacencyMatrix(
            System.Collections.Generic.Dictionary<int, System.Collections.Generic.HashSet<int>> adjacencyDict, 
            int size)
        {
            bool[,] matrix = new bool[size, size];

            foreach (var kvp in adjacencyDict)
            {
                int faceIndex = kvp.Key;
                foreach (int adjacentFace in kvp.Value)
                {
                    matrix[faceIndex, adjacentFace] = true;
                    matrix[adjacentFace, faceIndex] = true; // 确保对称性
                }
            }

            return matrix;
        }

        /// <summary>
        /// 打印邻接矩阵
        /// </summary>
        private static void PrintAdjacencyMatrix(bool[,] matrix, int size)
        {
            Console.WriteLine("\n完整邻接矩阵:");
            Console.Write("   ");
            for (int j = 0; j < size; j++)
            {
                Console.Write($"{j % 10} ");
            }
            Console.WriteLine();

            for (int i = 0; i < size; i++)
            {
                Console.Write($"{i % 10}: ");
                for (int j = 0; j < size; j++)
                {
                    Console.Write($"{(matrix[i, j] ? "1" : "0")} ");
                }
                Console.WriteLine();
            }
        }
    }
}