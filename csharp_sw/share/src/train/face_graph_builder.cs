using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace tools
{
    /// <summary>
    /// WL 图核算法中的面节点
    /// </summary>
    public class FaceNode
    {
        public int Id { get; set; }                      // 节点索引
        public Face2 FaceObject { get; set; } = null!;   // 面对象引用
        public string FaceType { get; set; } = "";       // 面类型标签
        public double Area { get; set; }                 // 面积
        public List<int> NeighborIds { get; set; }       // 邻居节点索引列表
        public string CurrentLabel { get; set; } = "";   // 当前迭代标签
            
        public FaceNode()
        {
            NeighborIds = new List<int>();
        }
    }

    /// <summary>
    /// Body 的拓扑图表征
    /// </summary>
    public class BodyGraph
    {
        public string PartName { get; set; } = "";       // 零件名称
        public string BodyName { get; set; } = "";       // Body 名称（原始名称）
        public string FullBodyName { get; set; } = "";   // 完整 Body 名称（PartName+BodyName）
        public List<FaceNode> Nodes { get; set; }        // 节点列表
        public Dictionary<string, int> LabelFrequency { get; set; }  // 标签频率统计
        
        public BodyGraph()
        {
            Nodes = new List<FaceNode>();
            LabelFrequency = new Dictionary<string, int>();
        }
    }

    /// <summary>
    /// Face 的拓扑图表征（用于 face 级别标注）
    /// </summary>
    public class FaceGraph
    {
        public string PartName { get; set; } = "";       // 零件名称
        public string BodyName { get; set; } = "";       // Body 名称
        public int FaceId { get; set; }                  // Face 在 Body 中的索引
        public string FullFaceName { get; set; } = "";   // 完整 Face 名称（PartName+BodyName+FaceId）
        public List<FaceNode> Nodes { get; set; }        // 节点列表（包含该面的所有相邻面）
        public Dictionary<string, int> LabelFrequency { get; set; }  // 标签频率统计
        
        public FaceGraph()
        {
            Nodes = new List<FaceNode>();
            LabelFrequency = new Dictionary<string, int>();
        }
    }

    /// <summary>
    /// 从 SolidWorks 零件构建面邻接图
    /// </summary>
    public static class FaceGraphBuilder
    {
        /// <summary>
        /// 从零件文档构建所有 Body 的图列表
        /// </summary>
        public static List<BodyGraph> BuildGraphs(ModelDoc2 swModel)
        {
            if (swModel == null)
            {
                Console.WriteLine("错误：没有打开的活动文档。");
                return new List<BodyGraph>();
            }

            PartDoc partDoc = (PartDoc)swModel;
            var graphs = new List<BodyGraph>();

            // 获取所有实体
            object[] vBodies = (object[])partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false);
            
            if (vBodies == null || vBodies.Length == 0)
            {
                Console.WriteLine("警告：未找到实体 body");
                return graphs;
            }

            Console.WriteLine($"找到 {vBodies.Length} 个 body");

            // 为每个 body 构建独立的图
            foreach (Body2 body in vBodies)
            {
                try
                {
                    var graph = BuildSingleBodyGraph(swModel, body);
                    if (graph != null && graph.Nodes.Count > 0)
                    {
                        graphs.Add(graph);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"构建 body 图失败：{ex.Message}");
                }
            }

            Console.WriteLine($"成功构建 {graphs.Count} 个 body 的拓扑图");
            return graphs;
        }

        /// <summary>
        /// 从单个 face 构建图（以该面为中心，包含其所有相邻面）
        /// </summary>
        public static FaceGraph BuildSingleFaceGraph(ModelDoc2 swModel, Body2 body, Face2 targetFace)
        {
            FaceGraph graph = new FaceGraph();
            string partName = swModel.GetTitle();
            string partNameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(partName);
            graph.PartName = partNameWithoutExt;
            graph.BodyName = body.Name;
            // 使用面的 ID 作为标识（SolidWorks API 中每个面对象有唯一 ID）
            int faceId = targetFace.GetFaceId();
            graph.FaceId = faceId;
            graph.FullFaceName = $"{graph.PartName}+{graph.BodyName}+Face{faceId}";

            var adjacencyDict = new Dictionary<int, HashSet<int>>();
            var faceIndexMap = new Dictionary<Face2, int>();
            var facesList = new List<Face2>();
            int faceCount = 0;

            object[] vEdges = (object[])body.GetEdges();
            if (vEdges == null)
            {
                Console.WriteLine($"  Face [{faceId}] 所在 body 没有边");
                return graph;
            }

            foreach (Edge edge in vEdges)
            {
                var twoAdjacentFaces = (object[])edge.GetTwoAdjacentFaces();
                if (twoAdjacentFaces == null || twoAdjacentFaces.Length < 2) continue;

                Face2 face1 = (Face2)twoAdjacentFaces[0];
                Face2 face2 = (Face2)twoAdjacentFaces[1];

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

                if (!adjacencyDict.ContainsKey(index1))
                    adjacencyDict[index1] = new HashSet<int>();

                if (!adjacencyDict.ContainsKey(index2))
                    adjacencyDict[index2] = new HashSet<int>();

                adjacencyDict[index1].Add(index2);
                adjacencyDict[index2].Add(index1);
            }

            for (int i = 0; i < faceCount; i++)
            {
                Face2 face = facesList[i];
                Surface surface = face.IGetSurface();
                
                string faceType = GetFaceType(surface);
                double area = Math.Round(face.GetArea() * 1000000, 2);

                FaceNode node = new FaceNode
                {
                    Id = i,
                    FaceObject = face,
                    FaceType = faceType,
                    Area = area,
                    CurrentLabel = faceType,
                    NeighborIds = adjacencyDict.ContainsKey(i) 
                        ? adjacencyDict[i].ToList() 
                        : new List<int>()
                };

                graph.Nodes.Add(node);
            }

            Console.WriteLine($"  ✓ Face [{graph.FullFaceName}] 构建完成：{graph.Nodes.Count} 个面");
            return graph;
        }

        /// <summary>
        /// 从零件文档构建所有 Face 的图列表（用于 face 级别标注）
        /// </summary>
        public static List<FaceGraph> BuildFaceGraphs(ModelDoc2 swModel)
        {
            if (swModel == null)
            {
                Console.WriteLine("错误：没有打开的活动文档。");
                return new List<FaceGraph>();
            }

            PartDoc partDoc = (PartDoc)swModel;
            var graphs = new List<FaceGraph>();

            object[] vBodies = (object[])partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false);
            
            if (vBodies == null || vBodies.Length == 0)
            {
                Console.WriteLine("警告：未找到实体 body");
                return graphs;
            }

            Console.WriteLine($"找到 {vBodies.Length} 个 body");

            foreach (Body2 body in vBodies)
            {
                try
                {
                    object[] vFaces = (object[])body.GetFaces();
                    if (vFaces == null) continue;

                    foreach (Face2 face in vFaces)
                    {
                        try
                        {
                            var graph = BuildSingleFaceGraph(swModel, body, face);
                            if (graph != null && graph.Nodes.Count > 0)
                            {
                                graphs.Add(graph);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"构建 face 图失败：{ex.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"处理 body 失败：{ex.Message}");
                }
            }

            Console.WriteLine($"成功构建 {graphs.Count} 个 face 的拓扑图");
            return graphs;
        }

        /// <summary>
        /// 从单个 body 构建图
        /// </summary>
        public static BodyGraph BuildSingleBodyGraph(ModelDoc2 swModel, Body2 body)
        {
            BodyGraph graph = new BodyGraph();
            // 获取零件名并过滤扩展名
            string partName = swModel.GetTitle();
            string partNameWithoutExt = System.IO.Path.GetFileNameWithoutExtension(partName);
            graph.PartName = partNameWithoutExt;
            graph.BodyName = body.Name;
            graph.FullBodyName = $"{graph.PartName}+{graph.BodyName}";  // 组合名称：零件名（无扩展名）+Body 名

            // 存储邻接关系的字典：面索引 -> 邻居面索引集合
            var adjacencyDict = new Dictionary<int, HashSet<int>>();
            var faceIndexMap = new Dictionary<Face2, int>();
            var facesList = new List<Face2>();
            int faceCount = 0;

            object[] vEdges = (object[])body.GetEdges();
            if (vEdges == null)
            {
                Console.WriteLine($"  Body [{graph.BodyName}] 没有边");
                return graph;
            }

            // 遍历边，提取面和邻接关系
            foreach (Edge edge in vEdges)
            {
                var twoAdjacentFaces = (object[])edge.GetTwoAdjacentFaces();
                if (twoAdjacentFaces == null || twoAdjacentFaces.Length < 2) continue;

                Face2 face1 = (Face2)twoAdjacentFaces[0];
                Face2 face2 = (Face2)twoAdjacentFaces[1];

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
                if (!adjacencyDict.ContainsKey(index1))
                    adjacencyDict[index1] = new HashSet<int>();

                if (!adjacencyDict.ContainsKey(index2))
                    adjacencyDict[index2] = new HashSet<int>();

                // 添加邻接关系
                adjacencyDict[index1].Add(index2);
                adjacencyDict[index2].Add(index1);
            }

            // 创建节点并分配初始标签
            for (int i = 0; i < faceCount; i++)
            {
                Face2 face = facesList[i];
                Surface surface = face.IGetSurface();
                
                // 确定面类型
                string faceType = GetFaceType(surface);
                
                // 计算面积 (mm²)
                double area = Math.Round(face.GetArea() * 1000000, 2);

                FaceNode node = new FaceNode
                {
                    Id = i,
                    FaceObject = face,
                    FaceType = faceType,
                    Area = area,
                    CurrentLabel = faceType,  // 初始标签为面类型
                    NeighborIds = adjacencyDict.ContainsKey(i) 
                        ? adjacencyDict[i].ToList() 
                        : new List<int>()
                };

                graph.Nodes.Add(node);
            }

            Console.WriteLine($"  ✓ Body [{graph.BodyName}] 构建完成：{graph.Nodes.Count} 个面");
            return graph;
        }

        /// <summary>
        /// 根据曲面几何特性确定面类型
        /// </summary>
        private static string GetFaceType(Surface surface)
        {
            if (surface.IsPlane()) return "平面";
            if (surface.IsCylinder()) return "圆柱面";
            if (surface.IsCone()) return "圆锥面";
            if (surface.IsSphere()) return "球面";
            if (surface.IsTorus()) return "圆环面";
     
            return "其他曲面";
        }
    }
}
