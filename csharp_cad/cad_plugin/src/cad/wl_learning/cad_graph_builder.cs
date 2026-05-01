using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.Interop;
using Autodesk.AutoCAD.Interop.Common;

namespace cad_tools
{
    /// <summary>
    /// CAD图形构建器 - 从CAD文档提取2D图形特征
    /// </summary>
    public static class CADGraphEdgeBuilder
    {
        private const double DefaultConnectionTolerance = 1.0;

        private sealed class Point3
        {
            public double X { get; }
            public double Y { get; }
            public double Z { get; }

            public Point3(double x, double y, double z = 0)
            {
                X = x;
                Y = y;
                Z = z;
            }
        }

        /// <summary>
        /// 从当前CAD文档构建图形
        /// </summary>
        /// <param name="acadDoc">CAD文档</param>
        /// <param name="graphName">图形名称（可选，默认为文件名）</param>
        /// <returns>CAD 2D图</returns>
        public static CADGraphEdgeGraph BuildGraphFromDocument(AcadDocument acadDoc, string graphName = "")
        {
            var graph = new CADGraphEdgeGraph
            {
                SourceFile = acadDoc.FullName,
                GraphName = string.IsNullOrEmpty(graphName) 
                    ? System.IO.Path.GetFileNameWithoutExtension(acadDoc.FullName) 
                    : graphName
            };

            try
            {
                BuildGraphFromEntities(graph, acadDoc.ModelSpace.Cast<AcadEntity>());

                Console.WriteLine($"✓ 成功构建CAD图形：{graph.GraphName}，包含 {graph.Nodes.Count} 个节点");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"× 构建CAD图形失败：{ex.Message}");
            }

            return graph;
        }

        /// <summary>
        /// 仅从指定实体集合构建图形（用于选择集提取）。
        /// </summary>
        public static CADGraphEdgeGraph BuildGraphFromEntities(
            AcadDocument acadDoc,
            IEnumerable<AcadEntity> entities,
            string graphName = "")
        {
            var graph = new CADGraphEdgeGraph
            {
                SourceFile = acadDoc.FullName,
                GraphName = string.IsNullOrEmpty(graphName)
                    ? System.IO.Path.GetFileNameWithoutExtension(acadDoc.FullName)
                    : graphName
            };

            try
            {
                BuildGraphFromEntities(graph, entities);
                Console.WriteLine($"✓ 成功构建CAD图形（选择集）：{graph.GraphName}，包含 {graph.Nodes.Count} 个节点");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"× 构建CAD图形（选择集）失败：{ex.Message}");
            }

            return graph;
        }

        private static void BuildGraphFromEntities(CADGraphEdgeGraph graph, IEnumerable<AcadEntity> entities)
        {
            int nodeId = 0;
            var nodeMap = new Dictionary<object, CADGraphEdgeNode>();

            foreach (var entity in entities)
            {
                var node = CreateNodeFromEntity(entity, nodeId++);
                if (node != null)
                {
                    graph.Nodes.Add(node);
                    nodeMap[entity] = node;
                }
            }

            BuildConnections(graph, nodeMap, DefaultConnectionTolerance);
        }

        /// <summary>
        /// 从CAD实体创建节点
        /// </summary>
        private static CADGraphEdgeNode? CreateNodeFromEntity(AcadEntity entity, int nodeId)
        {
            var node = new CADGraphEdgeNode
            {
                Id = nodeId
            };

            try
            {
                switch (entity)
                {
                    case AcadLine line:
                        node.EdgeType = "Line";
                        node.GeometryValue = CalculateLineLength(line);
                        var startPt = (double[])line.StartPoint;
                        var endPt = (double[])line.EndPoint;
                        node.IsHorizontal = Math.Abs(endPt[1] - startPt[1]) < 0.001;
                        node.IsVertical = Math.Abs(endPt[0] - startPt[0]) < 0.001;
                        node.Angle = CalculateLineAngle(line);
                        break;

                    case AcadArc arc:
                        node.EdgeType = "Arc";
                        node.GeometryValue = arc.Radius;
                        node.Angle = (arc.EndAngle - arc.StartAngle) * 180.0 / Math.PI;
                        break;

                    case AcadCircle circle:
                        node.EdgeType = "Circle";
                        node.GeometryValue = circle.Radius;
                        node.IsHorizontal = true;
                        node.IsVertical = true;
                        break;

                    case AcadLWPolyline polyline:
                        node.EdgeType = "Polyline";
                        node.GeometryValue = CalculatePolylineLength(polyline);
                        break;

                    case AcadEllipse ellipse:
                        node.EdgeType = "Ellipse";
                        node.GeometryValue = ellipse.MajorRadius;
                        break;

                    default:
                        return null; // 跳过不支持的实体类型
                }

                node.CurrentLabel = node.EdgeType;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"警告：无法解析实体：{ex.Message}");
                return null;
            }

            return node;
        }

        /// <summary>
        /// 建立连接关系（基于端点重合）
        /// </summary>
        private static void BuildConnections(
            CADGraphEdgeGraph graph,
            Dictionary<object, CADGraphEdgeNode> nodeMap,
            double tolerance)
        {
            // 每个节点提前提取关键连接点，避免在双重循环里重复计算
            var nodeConnectionPoints = new Dictionary<int, List<Point3>>();
            foreach (var pair in nodeMap)
            {
                var points = ExtractConnectionPoints(pair.Key);
                if (points.Count > 0)
                {
                    nodeConnectionPoints[pair.Value.Id] = points;
                }
            }

            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                var nodeA = graph.Nodes[i];
                if (!nodeConnectionPoints.ContainsKey(nodeA.Id)) continue;

                for (int j = i + 1; j < graph.Nodes.Count; j++)
                {
                    var nodeB = graph.Nodes[j];
                    if (!nodeConnectionPoints.ContainsKey(nodeB.Id)) continue;

                    if (!AreConnected(
                            nodeConnectionPoints[nodeA.Id],
                            nodeConnectionPoints[nodeB.Id],
                            tolerance))
                    {
                        continue;
                    }

                    if (!nodeA.ConnectedNodes.Contains(nodeB.Id))
                    {
                        nodeA.ConnectedNodes.Add(nodeB.Id);
                    }
                    if (!nodeB.ConnectedNodes.Contains(nodeA.Id))
                    {
                        nodeB.ConnectedNodes.Add(nodeA.Id);
                    }
                }
            }
        }

        /// <summary>
        /// 判断两个节点是否连接（需要具体实现端点检测）
        /// </summary>
        private static bool AreConnected(List<Point3> pointsA, List<Point3> pointsB, double tolerance)
        {
            foreach (var pointA in pointsA)
            {
                foreach (var pointB in pointsB)
                {
                    if (Distance(pointA, pointB) <= tolerance)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static double Distance(Point3 p1, Point3 p2)
        {
            double dx = p1.X - p2.X;
            double dy = p1.Y - p2.Y;
            double dz = p1.Z - p2.Z;
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static List<Point3> ExtractConnectionPoints(object entity)
        {
            var points = new List<Point3>();

            try
            {
                switch (entity)
                {
                    case AcadLine line:
                        points.Add(ToPoint((double[])line.StartPoint));
                        points.Add(ToPoint((double[])line.EndPoint));
                        break;
                    case AcadArc arc:
                        points.Add(ToPoint((double[])arc.StartPoint));
                        points.Add(ToPoint((double[])arc.EndPoint));
                        points.Add(ToPoint((double[])arc.Center));
                        break;
                    case AcadCircle circle:
                        var center = (double[])circle.Center;
                        double radius = circle.Radius;
                        points.Add(new Point3(center[0] + radius, center[1], center[2]));
                        points.Add(new Point3(center[0] - radius, center[1], center[2]));
                        points.Add(new Point3(center[0], center[1] + radius, center[2]));
                        points.Add(new Point3(center[0], center[1] - radius, center[2]));
                        break;
                    case AcadLWPolyline polyline:
                        var coords = (double[])polyline.Coordinates;
                        for (int i = 0; i + 1 < coords.Length; i += 2)
                        {
                            points.Add(new Point3(coords[i], coords[i + 1], 0));
                        }
                        break;
                    case AcadEllipse ellipse:
                        points.Add(ToPoint((double[])ellipse.Center));
                        break;
                }
            }
            catch
            {
                // 单个实体提取失败时忽略，避免影响整体构图。
            }

            return points
                .GroupBy(p => $"{Math.Round(p.X, 3)}|{Math.Round(p.Y, 3)}|{Math.Round(p.Z, 3)}")
                .Select(g => g.First())
                .ToList();
        }

        private static Point3 ToPoint(double[] values)
        {
            double x = values.Length > 0 ? values[0] : 0;
            double y = values.Length > 1 ? values[1] : 0;
            double z = values.Length > 2 ? values[2] : 0;
            return new Point3(x, y, z);
        }

        /// <summary>
        /// 计算直线长度
        /// </summary>
        private static double CalculateLineLength(AcadLine line)
        {
            var startPt = (double[])line.StartPoint;
            var endPt = (double[])line.EndPoint;
            double dx = endPt[0] - startPt[0];
            double dy = endPt[1] - startPt[1];
            double dz = endPt[2] - startPt[2];
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        /// <summary>
        /// 计算直线角度（相对于X轴）
        /// </summary>
        private static double CalculateLineAngle(AcadLine line)
        {
            var startPt = (double[])line.StartPoint;
            var endPt = (double[])line.EndPoint;
            double dx = endPt[0] - startPt[0];
            double dy = endPt[1] - startPt[1];
            double angle = Math.Atan2(dy, dx) * 180.0 / Math.PI;
            return angle < 0 ? angle + 360 : angle;
        }

        /// <summary>
        /// 计算多段线长度
        /// </summary>
        private static double CalculatePolylineLength(AcadLWPolyline polyline)
        {
            double length = 0;
            var coords = (double[])polyline.Coordinates;
            
            for (int i = 0; i < coords.Length - 2; i += 2)
            {
                double dx = coords[i + 2] - coords[i];
                double dy = coords[i + 3] - coords[i + 1];
                length += Math.Sqrt(dx * dx + dy * dy);
            }
            
            return length;
        }

        /// <summary>
        /// 批量构建文件夹中所有CAD文档的图形
        /// </summary>
        public static List<CADGraphEdgeGraph> BuildGraphsFromFolder(string folderPath)
        {
            var graphs = new List<CADGraphEdgeGraph>();
            
            if (!System.IO.Directory.Exists(folderPath))
            {
                Console.WriteLine($"错误：文件夹不存在：{folderPath}");
                return graphs;
            }

            var dwgFiles = System.IO.Directory.GetFiles(folderPath, "*.dwg");
            Console.WriteLine($"\n找到 {dwgFiles.Length} 个DWG文件");

            var acadApp = CadConnect.GetOrCreateInstance();
            if (acadApp == null)
            {
                Console.WriteLine("错误：无法连接到AutoCAD");
                return graphs;
            }

            foreach (var dwgFile in dwgFiles)
            {
                try
                {
                    Console.WriteLine($"\n处理：{System.IO.Path.GetFileName(dwgFile)}");
                    var doc = acadApp.Documents.Open(dwgFile, false, true);
                    var graph = BuildGraphFromDocument(doc, System.IO.Path.GetFileNameWithoutExtension(dwgFile));
                    graphs.Add(graph);
                    doc.Close(false);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"× 处理失败：{ex.Message}");
                }
            }

            return graphs;
        }
    }
}
