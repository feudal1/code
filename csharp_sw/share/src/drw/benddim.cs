using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using View = SolidWorks.Interop.sldworks.View;

namespace tools
{
    public class benddim
    {
        class BendNode
        {
            public Feature? BendFeature;
            public Face? MainCylinderFace;
            public Face? InnerCylinderFace;
            public Face? OuterCylinderFace;
            public double[]? Axis;
            public double[]? Center;
            public List<Face> FirstLevelFaces = new List<Face>();
            public List<Face> OuterFirstLevelFaces = new List<Face>();
            public List<(Face face, string level, Face sourceFirstFace)> SecondaryFaces = new List<(Face face, string level, Face sourceFirstFace)>();
        }

        class BendEdge
        {
            public int NodeA;
            public int NodeB;
            public Face? ConnectedFirstFaceA;
            public Face? ConnectedFirstFaceB;
        }

        class BendGraphDump
        {
            public DateTime CreatedAt;
            public int NodeCount;
            public int EdgeCount;
            public List<object> Nodes = new List<object>();
            public List<object> Edges = new List<object>();
        }

        public static void AddBendDimensions(ISldWorks swApp)
        {
            try
            {
                var swModel = (ModelDoc2)swApp.ActiveDoc;
                swApp.CommandInProgress = true;
                if (swModel == null) { Console.WriteLine("没有活动文档"); return; }

                var swSelMgr = (SelectionMgr)swModel.SelectionManager;
                if (swSelMgr.GetSelectedObjectType3(1, -1) != (int)swSelectType_e.swSelDRAWINGVIEWS)
                { Console.WriteLine("请先选择一个视图"); return; }

                var view = (View)swSelMgr.GetSelectedObject(1);
                var partDoc = (PartDoc)view.ReferencedDocument;
                if (partDoc == null) { Console.WriteLine("无法获取零件文档"); return; }
                ApplyGlobalAnnotationTextHeight((ModelDoc2)partDoc);

                var xformData = (double[])view.ModelToViewTransform.ArrayData;
                var bounds = (double[])view.GetOutline();

                int count = 0;
                int totalDimensions = 0;
                double offset = 0.005;
                var reasonStats = new Dictionary<string, int>();
                
                // 全局已标注面组合集合，用于跨折弯去重
                var dimensionedPairs = new HashSet<string>();
                var bendNodes = new List<BendNode>();

                // 遍历 Body 的特征找折弯
                foreach (Body2 body in (object[])partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false))
                {
                    foreach (Feature feat in (object[])body.GetFeatures())
                    {
                        var subFeat = (Feature)feat.GetFirstSubFeature();
                        while (subFeat != null)
                        {
                            if (subFeat.GetTypeName() == "OneBend" )
                            {
                                Console.WriteLine("找到折弯特征" + subFeat.Name);
                                try
                                {
                                    var node = BuildBendNode(subFeat);
                                    if (node != null)
                                    {
                                        bendNodes.Add(node);
                                        if (node.SecondaryFaces.Count == 0)
                                            AddReason(reasonStats, "节点构建_无可用二级面");
                                    }
                                    else
                                    {
                                        Console.WriteLine($"折弯特征 {subFeat.Name} 未构建出有效节点，已跳过");
                                        AddReason(reasonStats, "节点构建失败");
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"折弯特征 {subFeat.Name} 处理失败，已跳过: {ex.Message}");
                                    AddReason(reasonStats, "节点构建异常");
                                }
                            }

                            subFeat = (Feature)subFeat.GetNextSubFeature();
                        }
                    }
                }

                var graphEdges = BuildBendEdges(bendNodes);
                Console.WriteLine($"折弯图构建完成：节点={bendNodes.Count}，边={graphEdges.Count}");
                SaveBendGraphAsJson(bendNodes, graphEdges);

                foreach (var node in bendNodes)
                {
                    try
                    {
                        int dimCount = ProcessBendNode(swModel, view, xformData, bounds, node, ref offset, dimensionedPairs, reasonStats);
                        totalDimensions += dimCount;
                        if (dimCount > 0)
                            count++;
                    }
                    catch (Exception ex)
                    {
                        string nodeName = node.BendFeature?.Name ?? "未知折弯";
                        Console.WriteLine($"折弯节点 {nodeName} 标注失败，已跳过: {ex.Message}");
                        AddReason(reasonStats, "节点内处理异常");
                    }
                }

                totalDimensions += ProcessGraphEdges(swModel, view, xformData, bounds, bendNodes, graphEdges, ref offset, dimensionedPairs, reasonStats);

                Console.WriteLine($"标注完成，共 {totalDimensions} 个尺寸，涉及 {count}/{bendNodes.Count} 个折弯节点");
                PrintReasonStats(reasonStats);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"错误: {ex.Message}");
            }
        }

        static List<BendEdge> BuildBendEdges(List<BendNode> nodes)
        {
            var edges = new List<BendEdge>();
            for (int i = 0; i < nodes.Count; i++)
            {
                for (int j = i + 1; j < nodes.Count; j++)
                {
                    Face? connectedA = null;
                    Face? connectedB = null;
                    foreach (var fa in nodes[i].FirstLevelFaces)
                    {
                        foreach (var fb in nodes[j].FirstLevelFaces)
                        {
                            if (!IsSameFace(fa, fb)) continue;
                            connectedA = fa;
                            connectedB = fb;
                            break;
                        }
                        if (connectedA != null) break;
                    }
                    if (connectedA == null || connectedB == null) continue;

                    edges.Add(new BendEdge
                    {
                        NodeA = i,
                        NodeB = j,
                        ConnectedFirstFaceA = connectedA,
                        ConnectedFirstFaceB = connectedB
                    });
                }
            }
            return edges;
        }

        static BendNode? BuildBendNode(Feature bendFeat)
        {
            var node = new BendNode { BendFeature = bendFeat };

            // 1. 找最大圆柱面（节点主圆柱）
            Face cylFace = null;
            double[] axis = null;
            double[] center = null;
            double maxArea = 0;

            foreach (Face f in (object[])bendFeat.GetFaces())
            {
                var s = (Surface)f.GetSurface();
                if (s.IsCylinder())
                {
                    double area = f.GetArea();
                    if (area > maxArea)
                    {
                        maxArea = area;
                        var p = (double[])s.CylinderParams;
                        axis = new[] { p[3], p[4], p[5] };
                        center = new[] { p[0], p[1], p[2] };
                        cylFace = f;
                    }
                }
            }
            if (cylFace == null) return null;
            Console.WriteLine("最大圆柱面面积: " + maxArea * 1000000 + " mm^2");
            node.MainCylinderFace = cylFace;
            node.Axis = axis;
            node.Center = center;

            // 1.1 当前折弯通常有一对内/外圆柱面：半径小的是内圆柱，半径大的是外圆柱
            Face bendInnerCylinderFace = null;
            Face bendOuterCylinderFace = null;
            double minRadius = double.MaxValue;
            double maxRadius = double.MinValue;
            foreach (Face f in (object[])bendFeat.GetFaces())
            {
                if (!TryGetCylinderData(f, out _, out var cylAxis, out var radius)) continue;
                if (!IsParallel(cylAxis, axis)) continue;

                if (radius < minRadius)
                {
                    minRadius = radius;
                    bendInnerCylinderFace = f;
                }

                if (radius > maxRadius)
                {
                    maxRadius = radius;
                    bendOuterCylinderFace = f;
                }
            }
            if (bendInnerCylinderFace != null && bendOuterCylinderFace != null && bendInnerCylinderFace != bendOuterCylinderFace)
                Console.WriteLine($"识别到折弯内外圆柱面：R内={minRadius * 1000:F2} mm, R外={maxRadius * 1000:F2} mm");
            node.InnerCylinderFace = bendInnerCylinderFace;
            node.OuterCylinderFace = bendOuterCylinderFace;

            // 2/3. 从外圆柱和内圆柱都收集一级面（每个节点应包含两侧共4个一级面）
            var firstLevelFaces = new List<Face>();
            int parallelEdgeCount = 0;
            var outerFirstLevelFaces = new List<Face>();
            if (bendOuterCylinderFace != null)
                parallelEdgeCount += CollectFirstLevelFacesFromCylinder(bendOuterCylinderFace, axis, firstLevelFaces, outerFirstLevelFaces);
            if (bendInnerCylinderFace != null && bendInnerCylinderFace != bendOuterCylinderFace)
                parallelEdgeCount += CollectFirstLevelFacesFromCylinder(bendInnerCylinderFace, axis, firstLevelFaces);

            Console.WriteLine("找到 " + parallelEdgeCount + " 条与轴线平行的边");
            if (firstLevelFaces.Count == 0) return null;
            Console.WriteLine("找到 " + firstLevelFaces.Count + " 个下一级面");
            Console.WriteLine("其中外圆柱来源一级面: " + outerFirstLevelFaces.Count + " 个");
            node.FirstLevelFaces = firstLevelFaces;
            node.OuterFirstLevelFaces = outerFirstLevelFaces;

            // 4. 在下一级面里找与圆柱轴平行的最远的线，然后取相交面
            var secondLevelPairs = new List<(Face sourceFirstFace, Face secondFace)>();
            foreach (var face in firstLevelFaces)
            {
                Edge farthestEdge = null;
                double maxDist = -1;

                foreach (Edge e in (object[])face.GetEdges())
                {
                    var c = (Curve)e.GetCurve();
                    if (c.IsLine())
                    {
                        var lineParams = (double[])c.LineParams;
                        var edgeDir = new[] { lineParams[3], lineParams[4], lineParams[5] };
                        if (IsParallel(edgeDir, axis))
                        {
                            // 计算边到圆柱中心的距离
                            var pt = new[] { lineParams[0], lineParams[1], lineParams[2] };
                            double dist = PointToAxisDistance(pt, center, axis);
                            if (dist > maxDist)
                            {
                                maxDist = dist;
                                farthestEdge = e;
                            }
                        }
                    }
                }

                if (farthestEdge != null)
                {
                    foreach (Face f in (object[])farthestEdge.GetTwoAdjacentFaces())
                    {
                        if (f == face) continue;
                        var sf = (Surface)f.GetSurface();
                        if (sf.IsCylinder())
                        {
                            Console.WriteLine("二级面候选为圆柱面，已按规则跳过");
                            continue;
                        }

                        if (!secondLevelPairs.Any(x => x.sourceFirstFace == face && x.secondFace == f))
                        {
                            secondLevelPairs.Add((face, f));
                        }
                    }
                }
            }
            if (secondLevelPairs.Count == 0)
            {
                Console.WriteLine("未找到可用二级面：保留折弯节点，仅参与图结构/跨节点处理");
            }
            else
            {
                Console.WriteLine("找到 " + secondLevelPairs.Count + " 个二级面");
            }

            // 5. 仅使用二级面，不再继续获取第三级面
            var secondaryFaces = new List<(Face face, string level, Face sourceFirstFace)>(); // 面及其级别+来源一级面
            foreach (var pair in secondLevelPairs)
            {
                var sourceFirstFace = pair.sourceFirstFace;
                var face = pair.secondFace;
                if (!secondaryFaces.Any(x => x.face == face && x.sourceFirstFace == sourceFirstFace))
                {
                    secondaryFaces.Add((face, "二级面", sourceFirstFace));
                }
            }
            node.SecondaryFaces = secondaryFaces;

            return node;
        }

        static int ProcessBendNode(ModelDoc2 swModel, View view, double[] xformData, double[] bounds, BendNode node, ref double offset, HashSet<string> dimensionedPairs, Dictionary<string, int> reasonStats)
        {
            var axis = node.Axis;
            if (axis == null) return 0;
            var firstLevelFaces = node.FirstLevelFaces;
            var secondaryFaces = node.SecondaryFaces;

            // 6. 为每个一级面找到对应的二级面并标注
            // 使用全局已标注面集合防止重复标注（跨折弯共享）
            int dimensionCount = 0;
            
            foreach (var firstFace in firstLevelFaces)
            {
                // 规则：内圆弧面的一级面不参与和二级面的节点内配对
                if (node.InnerCylinderFace != null && FacesIntersect(firstFace, node.InnerCylinderFace))
                {
                    Console.WriteLine($"跳过内圆弧侧一级面（面积 = {firstFace.GetArea() * 1000000:F2} mm^2）");
                    AddReason(reasonStats, "节点内_跳过内圆弧侧一级面");
                    continue;
                }

                var s1 = (Surface)firstFace.GetSurface();
                if (!s1.IsPlane()) continue;

                // 确定放置方式
                string placement = GetPlacement(firstFace, xformData);
                if (placement == "none") continue;

                // 为当前一级面生成所有可用二级面候选，并按距离从远到近尝试
                var candidates = new List<(Face secFace, string level, string pairKey, double dist)>();
                foreach (var item in secondaryFaces)
                {
                    if (item.sourceFirstFace == firstFace) continue;
                    var secFace = item.face;
                    if (secFace == firstFace) continue;
                    if (FacesIntersect(firstFace, secFace)) continue;

                    var pairKey = GetFacePairKey(firstFace, secFace, item.level);
                    if (dimensionedPairs.Contains(pairKey))
                    {
                        Console.WriteLine($"跳过已标注组合：一级面面积 = {firstFace.GetArea() * 1000000:F2} mm^2, {item.level}面积 = {secFace.GetArea() * 1000000:F2} mm^2");
                        AddReason(reasonStats, "节点内_跳过已标注组合");
                        continue;
                    }

                    double dist = FaceToFaceDistance(firstFace, secFace);
                    candidates.Add((secFace, item.level, pairKey, dist));
                }

                if (candidates.Count == 0)
                {
                    AddReason(reasonStats, "节点内_无候选二级面");
                    continue;
                }
                candidates.Sort((a, b) => b.dist.CompareTo(a.dist));

                bool placed = false;
                foreach (var cand in candidates)
                {
                    Console.WriteLine($"节点内配对尝试：一级面面积 = {firstFace.GetArea() * 1000000:F2} mm^2, {cand.level}面积 = {cand.secFace.GetArea() * 1000000:F2} mm^2, 距离 = {cand.dist * 1000:F2} mm");

                    // 标注 - 获取3D边并在视图中查找对应可见边
                    var edgeCandidates1 = GetEdgesForDimension(firstFace, axis, "一级面");
                    var edgeCandidates2 = GetEdgesForDimension(cand.secFace, axis, cand.level);
                    if (edgeCandidates2.Count == 0)
                    {
                        Console.WriteLine($"[{cand.level}] 未找到不平行于轴线的直边，按规则跳过该候选");
                        AddReason(reasonStats, "节点内_二级面无可用直边");
                    }

                    Edge visEdge1 = null, visEdge2 = null;

                    foreach (var e1 in edgeCandidates1)
                    {
                        visEdge1 = FindVisibleEdge(view, e1);
                        if (visEdge1 != null) break;
                    }

                    if (visEdge1 == null)
                    {
                        Console.WriteLine("一级面的标注边在视图中均不可见，继续尝试下一个候选配对");
                        AddReason(reasonStats, "节点内_一级面边不可见");
                        continue;
                    }

                    foreach (var e2 in edgeCandidates2)
                    {
                        visEdge2 = FindVisibleEdge(view, e2);
                        if (visEdge2 != null) break;
                    }

                    if (visEdge2 == null)
                    {
                        Console.WriteLine("二级面的标注边在视图中均不可见，继续尝试下一个候选配对");
                        AddReason(reasonStats, "节点内_二级面边不可见");
                        continue;
                    }

                    // 创建 SelectData 并设置视图上下文
                    var selMgr = (SelectionMgr)swModel.SelectionManager;
                    var selData = selMgr.CreateSelectData();
                    selData.View = view;

                    // 将可见边转换为 Entity 并选择
                    ((Entity)visEdge1).Select4(true, selData);
                    ((Entity)visEdge2).Select4(true, selData);

                    double x, y;
                    if (placement == "h")
                    {
                        x = (bounds[0] + bounds[2]) / 2;
                        y = bounds[3] - offset;
                    }
                    else
                    {
                        y = (bounds[1] + bounds[3]) / 2;
                        x = bounds[0] + offset;
                    }

                    var addedDimension = swModel.AddDimension2(x, y, 0) as DisplayDimension;
                    swModel.ClearSelection2(true);
                    if (addedDimension == null)
                    {
                        Console.WriteLine("AddDimension2 返回空，继续尝试下一个候选配对");
                        AddReason(reasonStats, "节点内_AddDimension2返回空");
                        continue;
                    }

                    // 仅在成功添加尺寸后才去重，避免失败后误占配对
                    dimensionedPairs.Add(cand.pairKey);
                    offset += 0.005;
                    dimensionCount++;
                    placed = true;
                    Console.WriteLine("节点内配对成功：已创建尺寸");
                    AddReason(reasonStats, "节点内_配对成功");
                    break;
                }

                if (!placed)
                {
                    Console.WriteLine($"节点内配对失败：一级面面积 = {firstFace.GetArea() * 1000000:F2} mm^2，所有候选均不可标注");
                    AddReason(reasonStats, "节点内_配对失败");
                }
            }

            return dimensionCount;
        }

        static int ProcessGraphEdges(ModelDoc2 swModel, View view, double[] xformData, double[] bounds, List<BendNode> nodes, List<BendEdge> edges, ref double offset, HashSet<string> dimensionedPairs, Dictionary<string, int> reasonStats)
        {
            int dimensionCount = 0;
            foreach (var edge in edges)
            {
                var connectedFaceA = edge.ConnectedFirstFaceA;
                var connectedFaceB = edge.ConnectedFirstFaceB;
                var axisA = nodes[edge.NodeA].Axis;
                if (connectedFaceA == null || connectedFaceB == null || axisA == null)
                {
                    AddReason(reasonStats, "节点间_连接信息缺失");
                    continue;
                }

                // 节点间规则：完全跳过连接面，只处理两节点外圆柱来源一级面中的非连接面配对
                var nodeAOuterFaces = nodes[edge.NodeA].OuterFirstLevelFaces;
                var nodeBOuterFaces = nodes[edge.NodeB].OuterFirstLevelFaces;
                if (nodeAOuterFaces.Count == 0 || nodeBOuterFaces.Count == 0)
                {
                    AddReason(reasonStats, "节点间_外圆柱一级面不足");
                    continue;
                }

                var pairCandidates = new List<(Face faceA, Face faceB, string pairKey, double dist)>();
                foreach (var faceA in nodeAOuterFaces)
                {
                    if (IsSameFace(faceA, connectedFaceA)) continue;
                    foreach (var faceB in nodeBOuterFaces)
                    {
                        if (IsSameFace(faceB, connectedFaceB)) continue;
                        if (IsSameFace(faceA, faceB)) continue;
                        if (FacesIntersect(faceA, faceB)) continue;

                        var pairKey = GetFacePairKey(faceA, faceB, "节点间外圆柱非连接一级面");
                        if (dimensionedPairs.Contains(pairKey)) continue;

                        double dist = FaceToFaceDistance(faceA, faceB);
                        pairCandidates.Add((faceA, faceB, pairKey, dist));
                    }
                }

                if (pairCandidates.Count == 0)
                {
                    Console.WriteLine($"节点间未找到可标注的外圆柱非连接面配对：NodeA={edge.NodeA}, NodeB={edge.NodeB}");
                    AddReason(reasonStats, "节点间_无可标注面配对");
                    continue;
                }

                pairCandidates.Sort((a, b) => b.dist.CompareTo(a.dist));
                bool placed = false;
                foreach (var candidate in pairCandidates)
                {
                    var areaA = candidate.faceA.GetArea() * 1000000;
                    var areaB = candidate.faceB.GetArea() * 1000000;
                    Console.WriteLine(
                        $"节点间配对尝试：NodeA={edge.NodeA}, NodeB={edge.NodeB}, A面积={areaA:F2} mm^2, B面积={areaB:F2} mm^2, 距离={candidate.dist * 1000:F2} mm");

                    if (!TryAddDimension(swModel, view, xformData, bounds, candidate.faceA, candidate.faceB, axisA, "节点间外圆柱非连接一级面", ref offset, out var failReason))
                    {
                        AddReason(reasonStats, $"节点间_{failReason}");
                        continue;
                    }

                    dimensionedPairs.Add(candidate.pairKey);
                    dimensionCount++;
                    placed = true;
                    AddReason(reasonStats, "节点间_配对成功");
                    Console.WriteLine(
                        $"节点间配对成功：NodeA={edge.NodeA}, NodeB={edge.NodeB}, A面积={areaA:F2} mm^2, B面积={areaB:F2} mm^2");
                    break;
                }

                if (!placed)
                {
                    Console.WriteLine($"节点间配对失败：NodeA={edge.NodeA}, NodeB={edge.NodeB}，候选均不可标注");
                    AddReason(reasonStats, "节点间_配对失败");
                }
            }
            return dimensionCount;
        }

        static bool TryAddDimension(ModelDoc2 swModel, View view, double[] xformData, double[] bounds, Face face1, Face face2, double[] axis, string level, ref double offset, out string failReason)
        {
            failReason = "";
            string placement = GetPlacement(face1, xformData);
            if (placement == "none")
            {
                failReason = "放置方向无效";
                return false;
            }

            var edgeCandidates1 = GetEdgesForDimension(face1, axis, "一级面");
            var edgeCandidates2 = GetEdgesForDimension(face2, axis, level);
            if (edgeCandidates2.Count == 0)
            {
                failReason = "二级面无可用直边";
                return false;
            }
            Edge visEdge1 = null, visEdge2 = null;

            foreach (var e1 in edgeCandidates1)
            {
                visEdge1 = FindVisibleEdge(view, e1);
                if (visEdge1 != null) break;
            }
            if (visEdge1 == null)
            {
                failReason = "一级面边不可见";
                return false;
            }

            foreach (var e2 in edgeCandidates2)
            {
                visEdge2 = FindVisibleEdge(view, e2);
                if (visEdge2 != null) break;
            }
            if (visEdge2 == null)
            {
                failReason = "二级面边不可见";
                return false;
            }

            var selMgr = (SelectionMgr)swModel.SelectionManager;
            var selData = selMgr.CreateSelectData();
            selData.View = view;
            ((Entity)visEdge1).Select4(true, selData);
            ((Entity)visEdge2).Select4(true, selData);

            double x, y;
            if (placement == "h")
            {
                x = (bounds[0] + bounds[2]) / 2;
                y = bounds[3] - offset;
            }
            else
            {
                y = (bounds[1] + bounds[3]) / 2;
                x = bounds[0] + offset;
            }
            var addedDimension = swModel.AddDimension2(x, y, 0);
            if (addedDimension == null)
            {
                swModel.ClearSelection2(true);
                failReason = "AddDimension2返回空";
                return false;
            }
            offset += 0.005;
            swModel.ClearSelection2(true);
            return true;
        }

        static void AddReason(Dictionary<string, int> reasonStats, string reason)
        {
            if (string.IsNullOrWhiteSpace(reason)) return;
            if (!reasonStats.TryGetValue(reason, out var count))
                reasonStats[reason] = 1;
            else
                reasonStats[reason] = count + 1;
        }

        static void PrintReasonStats(Dictionary<string, int> reasonStats)
        {
            Console.WriteLine("标注原因统计：");
            if (reasonStats.Count == 0)
            {
                Console.WriteLine("  无统计数据");
                return;
            }

            foreach (var item in reasonStats.OrderByDescending(x => x.Value).ThenBy(x => x.Key))
            {
                Console.WriteLine($"  {item.Key}: {item.Value}");
            }
        }

        static bool FacesIntersect(Face faceA, Face faceB)
        {
            foreach (Edge e in (object[])faceA.GetEdges())
            {
                foreach (Face adj in (object[])e.GetTwoAdjacentFaces())
                {
                    if (adj == faceB) return true;
                }
            }
            return false;
        }

        static bool IsSameFace(Face faceA, Face faceB)
        {
            if (faceA == null || faceB == null) return false;
            if (Object.ReferenceEquals(faceA, faceB)) return true;
            return faceA.GetHashCode() == faceB.GetHashCode();
        }

        static double FaceToFaceDistance(Face faceA, Face faceB)
        {
            var pa = GetFaceRepresentativePoint(faceA);
            var pb = GetFaceRepresentativePoint(faceB);
            if (pa == null || pb == null) return 0;
            return Distance3D(pa, pb);
        }

        static double[]? GetFaceRepresentativePoint(Face face)
        {
            foreach (Edge e in (object[])face.GetEdges())
            {
                var v = (Vertex)e.GetStartVertex();
                if (v == null) continue;
                var pt = (double[])v.GetPoint();
                if (pt != null && pt.Length >= 3) return new[] { pt[0], pt[1], pt[2] };
            }
            return null;
        }

        static double Distance3D(double[] a, double[] b)
        {
            var dx = a[0] - b[0];
            var dy = a[1] - b[1];
            var dz = a[2] - b[2];
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        static int CollectFirstLevelFacesFromCylinder(Face cylFace, double[] axis, List<Face> firstLevelFaces, List<Face>? sourceFacesCollector = null)
        {
            int parallelCount = 0;
            foreach (Edge e in (object[])cylFace.GetEdges())
            {
                var c = (Curve)e.GetCurve();
                if (!c.IsLine()) continue;

                var lineParams = (double[])c.LineParams;
                var edgeDir = new[] { lineParams[3], lineParams[4], lineParams[5] };
                if (!IsParallel(edgeDir, axis)) continue;

                parallelCount++;
                foreach (Face f in (object[])e.GetTwoAdjacentFaces())
                {
                    if (f == cylFace) continue;
                    if (!firstLevelFaces.Contains(f))
                        firstLevelFaces.Add(f);

                    if (sourceFacesCollector != null && !sourceFacesCollector.Contains(f))
                        sourceFacesCollector.Add(f);
                }
            }
            return parallelCount;
        }

        static bool IsParallel(double[] a, double[] b)
        {
            double dot = Math.Abs(a[0] * b[0] + a[1] * b[1] + a[2] * b[2]);
            double ma = Math.Sqrt(a[0] * a[0] + a[1] * a[1] + a[2] * a[2]);
            double mb = Math.Sqrt(b[0] * b[0] + b[1] * b[1] + b[2] * b[2]);
            return Math.Abs(dot - ma * mb) < 0.001;
        }

        static double PointToAxisDistance(double[] point, double[] axisCenter, double[] axisDir)
        {
            // 计算点到轴线的距离
            // 向量从轴线中心指向点
            double[] v = { point[0] - axisCenter[0], point[1] - axisCenter[1], point[2] - axisCenter[2] };
            
            // 投影到轴线方向
            double dot = v[0] * axisDir[0] + v[1] * axisDir[1] + v[2] * axisDir[2];
            double axisLen = Math.Sqrt(axisDir[0] * axisDir[0] + axisDir[1] * axisDir[1] + axisDir[2] * axisDir[2]);
            double proj = dot / axisLen;
            
            // 投影点
            double[] projPoint = {
                axisCenter[0] + proj * axisDir[0] / axisLen,
                axisCenter[1] + proj * axisDir[1] / axisLen,
                axisCenter[2] + proj * axisDir[2] / axisLen
            };
            
            // 距离
            double dx = point[0] - projPoint[0];
            double dy = point[1] - projPoint[1];
            double dz = point[2] - projPoint[2];
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        static bool TryGetCylinderData(Face face, out double[] center, out double[] axis, out double radius)
        {
            center = Array.Empty<double>();
            axis = Array.Empty<double>();
            radius = 0;

            var s = (Surface)face.GetSurface();
            if (!s.IsCylinder()) return false;

            var p = (double[])s.CylinderParams;
            if (p == null || p.Length < 7) return false;

            center = new[] { p[0], p[1], p[2] };
            axis = new[] { p[3], p[4], p[5] };
            radius = p[6];
            return true;
        }

        static void SaveBendGraphAsJson(List<BendNode> nodes, List<BendEdge> edges)
        {
            try
            {
                var dump = new BendGraphDump
                {
                    CreatedAt = DateTime.Now,
                    NodeCount = nodes.Count,
                    EdgeCount = edges.Count
                };

                for (int i = 0; i < nodes.Count; i++)
                {
                    var n = nodes[i];
                    var nodeObj = new
                    {
                        index = i,
                        featureName = n.BendFeature?.Name ?? "",
                        mainCylinderAreaMm2 = n.MainCylinderFace != null ? n.MainCylinderFace.GetArea() * 1000000 : 0,
                        innerRadiusMm = TryGetCylinderRadiusMm(n.InnerCylinderFace),
                        outerRadiusMm = TryGetCylinderRadiusMm(n.OuterCylinderFace),
                        firstLevelFaceAreasMm2 = n.FirstLevelFaces.Select(f => Math.Round(f.GetArea() * 1000000, 3)).ToList()
                    };
                    dump.Nodes.Add(nodeObj);
                }

                foreach (var e in edges)
                {
                    var edgeObj = new
                    {
                        nodeA = e.NodeA,
                        nodeB = e.NodeB,
                        connectedFaceAAreaMm2 = e.ConnectedFirstFaceA != null ? Math.Round(e.ConnectedFirstFaceA.GetArea() * 1000000, 3) : 0,
                        connectedFaceBAreaMm2 = e.ConnectedFirstFaceB != null ? Math.Round(e.ConnectedFirstFaceB.GetArea() * 1000000, 3) : 0
                    };
                    dump.Edges.Add(edgeObj);
                }

                var outPath = Path.Combine("E:\\cqh\\code\\my_c#\\c#_sw\\share\\src\\drw", "bend_graph.json");
                var json = JsonConvert.SerializeObject(dump, Formatting.Indented);
                File.WriteAllText(outPath, json);
                Console.WriteLine($"折弯图已保存: {outPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"保存折弯图JSON失败: {ex.Message}");
            }
        }

        static double TryGetCylinderRadiusMm(Face? face)
        {
            if (face == null) return 0;
            if (!TryGetCylinderData(face, out _, out _, out var radius)) return 0;
            return Math.Round(radius * 1000, 3);
        }

        static string GetPlacement(Face face, double[] xf)
        {
            var s = (Surface)face.GetSurface();
            if (!s.IsPlane()) return "none";
            var p = (double[])s.PlaneParams;
            double nx = Math.Abs(p[0]), ny = Math.Abs(p[1]), nz = Math.Abs(p[2]);

            if (nx > ny && nx > nz)
                return Math.Round(xf[0]) != 0 ? "h" : (Math.Round(xf[1]) != 0 ? "v" : "none");
            if (ny > nx && ny > nz)
                return Math.Round(xf[3]) != 0 ? "h" : (Math.Round(xf[4]) != 0 ? "v" : "none");
            if (nz > nx && nz > ny)
                return Math.Round(xf[6]) != 0 ? "h" : (Math.Round(xf[7]) != 0 ? "v" : "none");
            return "none";
        }

        /// <summary>
        /// 获取面上所有不与轴线平行的直边，按长度降序排列（用于尺寸标注）
        /// 折弯标注需要选择不平行于折弯轴线的边，返回多条候选边供视图中逐个匹配
        /// </summary>
        static List<Edge> GetEdgesForDimension(Face face, double[] axis, string faceLevel = "", bool allowParallelEdges = false)
        {
            var candidates = new List<(Edge edge, double len)>();

            foreach (Edge e in (object[])face.GetEdges())
            {
                var c = (Curve)e.GetCurve();
                if (c.IsLine())
                {
                    var lineParams = (double[])c.LineParams;
                    var edgeDir = new[] { lineParams[3], lineParams[4], lineParams[5] };
                    
                    // 默认跳过与轴线平行的边；兜底模式允许保留平行边
                    if (!allowParallelEdges && IsParallel(edgeDir, axis))
                        continue;

                    double startParam = 0, endParam = 0;
                    bool isClosed = false, isPeriodic = false;
                    c.GetEndParams(out startParam, out endParam, out isClosed, out isPeriodic);
                    double len = Math.Abs(endParam - startParam);
                    
                    candidates.Add((e, len));
                }
            }

            // 按长度降序排列，优先使用长边
            candidates.Sort((a, b) => b.len.CompareTo(a.len));
            
            var label = string.IsNullOrEmpty(faceLevel) ? "" : $"[{faceLevel}] ";
            foreach (var item in candidates)
                Console.WriteLine($"  {label}候选标注边，长度: {item.len * 1000:F2} mm");
            
            if (candidates.Count == 0)
                Console.WriteLine($"  {label}未找到不平行于轴线的直边");

            return candidates.Select(x => x.edge).ToList();
        }

        /// <summary>
        /// 在视图中查找3D边对应的可见边
        /// 按组件获取可见边（返回可转换为Edge的对象），通过3D端点坐标近似匹配解决COM封送问题
        /// 注意：IDrawingEdge接口在SolidWorks C#互操作中不存在，不能类型转换；
        ///       GetVisibleEntities(null,...) 返回的对象也是__ComObject无法转换；
        ///       正确做法是用 GetVisibleEntities(comp,...) 按组件获取，返回的对象可转换为Edge
        /// </summary>
        static Edge FindVisibleEdge(View view, Edge modelEdge)
        {
            // 获取目标3D边的端点坐标
            var startVert = (Vertex)modelEdge.GetStartVertex();
            var endVert = (Vertex)modelEdge.GetEndVertex();
            if (startVert == null || endVert == null) return null;

            var edgeStart = (double[])startVert.GetPoint();
            var edgeEnd = (double[])endVert.GetPoint();
            if (edgeStart == null || edgeEnd == null) return null;

            // 获取视图中的可见组件
            var visibleComps = (object[])view.GetVisibleComponents();
            if (visibleComps == null) return null;

            foreach (Component2 comp in visibleComps)

            {
                if (comp == null) continue;

                // 按组件获取可见边（与 GetVisibleEntities(null,...) 不同，按组件获取返回可转换为Edge的对象）
                var visibleEdges = (object[])view.GetVisibleEntities(
                    comp, (int)swViewEntityType_e.swViewEntityType_Edge);
                if (visibleEdges == null) continue;

                foreach (object obj in visibleEdges)
                {
                    if (obj == null) continue;

                    // GetVisibleEntities(comp,...) 返回的对象可转换为 Edge
                    if (!(obj is Edge visEdge)) continue;

                    try
                    {
                        // 先用引用判断（速度快）
                        if (Object.ReferenceEquals(visEdge, modelEdge)) return visEdge;

                        // 再用3D端点坐标近似判断（解决COM封送导致的引用不一致问题）
                        var meStartVert = (Vertex)visEdge.GetStartVertex();
                        var meEndVert = (Vertex)visEdge.GetEndVertex();
                        if (meStartVert == null || meEndVert == null) continue;

                        var meStart = (double[])meStartVert.GetPoint();
                        var meEnd = (double[])meEndVert.GetPoint();
                        if (meStart == null || meEnd == null) continue;

                        bool coordsMatch =
                            (IsPointClose(edgeStart, meStart) && IsPointClose(edgeEnd, meEnd))
                            ||
                            (IsPointClose(edgeStart, meEnd) && IsPointClose(edgeEnd, meStart));

                        if (coordsMatch) return visEdge;
                    }
                    catch
                    {
                        continue;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 判断两个3D点是否在容差范围内近似相等
        /// </summary>
        static bool IsPointClose(double[] p1, double[] p2, double tol = 0.001)
        {
            if (p1 == null || p2 == null || p1.Length < 3 || p2.Length < 3) return false;
            var dx = Math.Abs(p1[0] - p2[0]);
            var dy = Math.Abs(p1[1] - p2[1]);
            var dz = Math.Abs(p1[2] - p2[2]);
            var dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            var close = dx < tol && dy < tol && dz < tol;
           // Console.WriteLine($"[IsPointClose] p1=({p1[0]:F6},{p1[1]:F6},{p1[2]:F6}) p2=({p2[0]:F6},{p2[1]:F6},{p2[2]:F6}) dx={dx:F6} dy={dy:F6} dz={dz:F6} dist={dist:F6} tol={tol} => {close}");
            return close;
        }

        static double[] TransformPoint(MathUtility math, MathTransform xf, double[] pt)
        {
            var mp = (MathPoint)math.CreatePoint(pt);
            mp = (MathPoint)mp.MultiplyTransform(xf);
            return (double[])mp.ArrayData;
        }

        /// <summary>
        /// 使用零件文档全局明细注释文字格式，避免折弯注释被局部样式托管覆盖。
        /// </summary>
        static void ApplyGlobalAnnotationTextHeight(ModelDoc2 partModel)
        {
            try
            {
                var myTextFormat = partModel.Extension.GetUserPreferenceTextFormat(
                    (int)swUserPreferenceTextFormat_e.swDetailingAnnotationTextFormat, 0) as TextFormat;
                if (myTextFormat == null) return;

                myTextFormat.CharHeight = 0.0035;
                bool boolstatus = partModel.Extension.SetUserPreferenceTextFormat(
                    (int)swUserPreferenceTextFormat_e.swDetailingAnnotationTextFormat, 0, myTextFormat);
                Console.WriteLine($"全局注释文字高度设置为 0.0035，结果: {boolstatus}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"设置全局注释文字高度失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 生成一对面的唯一标识键，用于防止重复标注
        /// 基于Face对象的引用生成唯一键，确保只有完全相同的物理面才会被去重
        /// </summary>
        static string GetFacePairKey(Face face1, Face face2, string level)
        {
            // 使用Face对象的HashCode生成唯一标识
            // 按HashCode排序生成键，确保 (A,B) 和 (B,A) 生成相同的键
            int hash1 = face1.GetHashCode();
            int hash2 = face2.GetHashCode();
            
            var hashes = new[] { hash1, hash2 };
            Array.Sort(hashes);
            
            // 加入配对级别，确保一级对二级 与 一级对三级 不会互相去重
            return $"{hashes[0]}|{hashes[1]}|{level}";
        }
    }
}
