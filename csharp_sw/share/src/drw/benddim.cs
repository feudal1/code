using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using View = SolidWorks.Interop.sldworks.View;

namespace tools
{
    public class benddim
    {
        /// <summary>两面为平面且夹角在此范围内（相对 90°）则不创建该尺寸。</summary>
        const double RightAngleSkipToleranceDeg = 1.0;

        /// <summary>从 OneBend 读出的折弯角在此范围内（相对 90°）时，该折弯不参与任何「视图草图画点」类标注（内弧草图点—边、节点间双内弧点距等）。</summary>
        const double BendAngleNear90SkipSketchPointDimsTolDeg = 10.0;

        /// <summary>除「节点内一级面间」外，其余标注要求两面法向平行（锐角 ≤ 本值视为平行）。</summary>
        const double ParallelPlaneToleranceDeg = 1.0;

        /// <summary>退出草图后按「创建时草图坐标」匹配新建 SketchPoint 的容差（米）。仅用模型逆变换时易与视图缩放不一致导致误拒。</summary>
        const double SketchPointPickSketchSpaceTolM = 0.005;

        /// <summary>工程图视图草图常为平面：Z 与目标偏差在此内仍视为同一点（米）。</summary>
        const double SketchPointPickZSlackM = 0.025;

        /// <summary>FindVisibleEdge 专用：模型空间端点容差（米），约 0.8mm。</summary>
        const double VisibleEdgeEndpointTolM = 0.0008;

        /// <summary>共线子段回退：可见边到模型边所在直线的最大距离（米），略放宽以适配展开图。</summary>
        const double VisibleEdgeColinearSubsetLineDistM = 0.0015;

        /// <summary>共线子段回退：沿模型边方向的端点外扩（米）。</summary>
        const double VisibleEdgeColinearSubsetAlongTolM = 0.004;

        /// <summary>共线子段回退：与模型边重叠长度下限 = max(本值米, 模型边长×比例)。</summary>
        const double VisibleEdgeColinearSubsetOverlapFloorM = 0.002;
        const double VisibleEdgeColinearSubsetOverlapFracLen = 0.004;

        /// <summary>为 true 时：FindVisibleEdge 严格匹配与共线子段均失败则打印当前视图可见边清单（便于对照模型边）。</summary>
        const bool BendDimLogVisibleEdgesOnFindVisibleEdgeFail = true;

        /// <summary>单次失败时最多列出多少条可见边（按长度降序）；超出部分只报总数。</summary>
        const int BendDimLogVisibleEdgesMaxLines = 150;

        /// <summary>一次标注流程内最多打印几份完整「可见边清单」（同一视图内容相同，默认只打 1 份）。</summary>
        const int BendDimLogVisibleEdgesMaxDumpsPerRun = 1;

        static int _bendDimVisibleEdgeFailDumpCount;

        /// <summary>短直棱（≤本值米）：展开图与 BREP 可有微小错层，用「等长+同向+中点近」匹配可见边。</summary>
        const double VisibleEdgeShortStubMaxLenM = 0.006;

        const double VisibleEdgeShortStubMidpointMaxM = 0.0028;
        const double VisibleEdgeShortStubLengthExtraM = 0.00035;

        /// <summary>折弯区短直棱（6～本值 mm）：BREP 与展开图棱长可差数毫米，用「同向+线距+放宽长度」匹配（如模型 ~21 mm 与可见 22.94、23.94）。</summary>
        const double VisibleEdgeBendChunkMaxLenM = 0.055;

        const double VisibleEdgeBendChunkLenTolFloorM = 0.0036;
        const double VisibleEdgeBendChunkLenTolPerLen = 0.20;
        const double VisibleEdgeBendChunkLineDistFloorM = 0.0045;
        const double VisibleEdgeBendChunkLineDistPerLen = 0.14;
        const double VisibleEdgeBendChunkMinAlignAbsCos = 0.96;

        /// <summary>可见边与模型边长度差：相对模型边长的最大比例。</summary>
        const double VisibleEdgeLengthRelTol = 0.012;

        /// <summary>长度差绝对下限（米），避免极短边上相对容差过大。</summary>
        const double VisibleEdgeLengthAbsFloorM = 0.00025;

        /// <summary>节点内「一级面间」用代表点距离筛候选：超过 max(地板, 内圆柱半径×系数) 不加入，避免大内面与远处小内面误配。</summary>
        const double InnerInnerPairDistFloorM = 0.018;
        const double InnerInnerPairDistPerInnerRadius = 70.0;

        /// <summary>两内圆弧一级面边—边：棱方向与折弯轴线方向余弦绝对值超过本值则视为过近轴线（近似平行），不参与候选。</summary>
        const double InnerInnerEdgeMaxAbsCosAxis = 0.99;

        /// <summary>内弧中点草图点 + 某二级面：与面无序对无关，同一节点上同一二级面只标一次，避免与一级—二级内弧路径重复。</summary>
        const string InnerArcPointSecondaryDedupeLevel = "内弧中点二级";

        /// <summary>尺寸文字相对两被标边中点连线的法向偏移起点（米），略离开几何。</summary>
        const double DimensionTextPerpBaseOffsetM = 0.003;

        /// <summary>多道尺寸沿法向递增错开（米），减轻文字重叠。</summary>
        const double DimensionTextPerpStaggerStepM = 0.0025;

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
            /// <summary>OneBend 折弯角（度），与 <see cref="OneBendFeatureData.BendAngle"/> 一致；未读到时为 NaN。</summary>
            public double BendAngleDeg = double.NaN;
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
            bool restoreInputDimValOnCreate = false;
            bool prevInputDimValOnCreate = false;
            try
            {
                var swModel = (ModelDoc2)swApp.ActiveDoc;
                if (swModel == null) { Console.WriteLine("没有活动文档"); return; }

                var swSelMgr = (SelectionMgr)swModel.SelectionManager;
                if (swSelMgr.GetSelectedObjectType3(1, -1) != (int)swSelectType_e.swSelDRAWINGVIEWS)
                { Console.WriteLine("请先选择一个视图"); return; }

                var view = (View)swSelMgr.GetSelectedObject(1);
                var partDoc = (PartDoc)view.ReferencedDocument;
                if (partDoc == null) { Console.WriteLine("无法获取零件文档"); return; }

                ResetBendDimVisibleEdgeDebugDumpCount();
                ApplyGlobalAnnotationTextHeight((ModelDoc2)partDoc);

                prevInputDimValOnCreate = swApp.GetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate);
                swApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, false);
                restoreInputDimValOnCreate = true;

                swApp.CommandInProgress = true;

                var xformData = (double[])view.ModelToViewTransform.ArrayData;

                ExitDrawingSketchIfActive(swModel);

                int count = 0;
                int totalDimensions = 0;
                double textStaggerM = 0;
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
                            if (IsLinearSheetMetalBendSubFeature(subFeat))
                            {
                                Console.WriteLine("找到折弯特征" + subFeat.Name);
                                try
                                {
                                    var node = BuildBendNode(subFeat);
                                    if (node != null)
                                    {
                                        bendNodes.Add(node);
                                        if (node.SecondaryFaces.Count == 0)
                                            AddReason(reasonStats, "节点构建_无可用二级面", subFeat.Name);
                                    }
                                    else
                                    {
                                        Console.WriteLine($"折弯特征 {subFeat.Name} 未构建出有效节点，已跳过");
                                        AddReason(reasonStats, "节点构建失败", subFeat.Name);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Console.WriteLine($"折弯特征 {subFeat.Name} 处理失败，已跳过: {ex.Message}");
                                    AddReason(reasonStats, "节点构建异常", subFeat.Name);
                                }
                            }

                            subFeat = (Feature)subFeat.GetNextSubFeature();
                        }
                    }
                }

                var graphEdges = BuildBendEdges(bendNodes);
                Console.WriteLine($"折弯图构建完成：节点={bendNodes.Count}，边={graphEdges.Count}");
                SaveBendGraphAsJson(bendNodes, graphEdges);

                for (int nodeIndex = 0; nodeIndex < bendNodes.Count; nodeIndex++)
                {
                    var node = bendNodes[nodeIndex];
                    try
                    {
                        int dimCount = ProcessBendNode(swApp, swModel, view, xformData, node, nodeIndex, ref textStaggerM, dimensionedPairs, reasonStats);
                        totalDimensions += dimCount;
                        if (dimCount > 0)
                            count++;
                    }
                    catch (Exception ex)
                    {
                        string nodeName = node.BendFeature?.Name ?? "未知折弯";
                        Console.WriteLine($"折弯节点 {nodeName} 标注失败，已跳过: {ex.Message}");
                        AddReason(reasonStats, "节点内处理异常", FormatBendNodeContext(node, nodeIndex));
                    }
                }

                totalDimensions += ProcessGraphEdges(swApp, swModel, view, xformData, bendNodes, graphEdges, ref textStaggerM, dimensionedPairs, reasonStats);

                var graphArcMidKeys = new HashSet<string>();
                totalDimensions += ProcessGraphEdgesInnerArcMidpointDimensions(
                    swApp, swModel, view, xformData, bendNodes, graphEdges, ref textStaggerM, graphArcMidKeys, reasonStats);

                Console.WriteLine($"标注完成，共 {totalDimensions} 个尺寸，涉及 {count}/{bendNodes.Count} 个折弯节点");
                PrintReasonStats(reasonStats);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"错误: {ex.Message}");
            }
            finally
            {
                if (restoreInputDimValOnCreate)
                {
                    try
                    {
                        swApp.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swInputDimValOnCreate, prevInputDimValOnCreate);
                    }
                    catch
                    {
                        // ignored
                    }
                }

                try
                {
                    swApp.CommandInProgress = false;
                }
                catch
                {
                    // ignored
                }
            }
        }

        static List<BendEdge> BuildBendEdges(List<BendNode> nodes)
        {
            var edges = new List<BendEdge>();
            for (int i = 0; i < nodes.Count; i++)
            {
                var axisI = nodes[i].Axis;
                if (axisI == null) continue;

                for (int j = i + 1; j < nodes.Count; j++)
                {
                    var axisJ = nodes[j].Axis;
                    if (axisJ == null) continue;

                    // 除共享一级面外，两折弯的轴线（与一级面上折弯对应平行边同向）须互相平行
                    if (!IsParallel(axisI, axisJ)) continue;

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

        /// <summary>与车间/kcheck 一致：类型名为 OneBend 或定义为 <see cref="OneBendFeatureData"/>（如 SketchBend）。</summary>
        static bool FeatureDefinesOneBendData(Feature feat)
        {
            try
            {
                return feat.GetDefinition() is OneBendFeatureData;
            }
            catch
            {
                return false;
            }
        }

        static bool IsLinearSheetMetalBendSubFeature(Feature subFeat) =>
            string.Equals(subFeat.GetTypeName(), "OneBend", StringComparison.Ordinal)
            || string.Equals(subFeat.GetTypeName2() ?? "", "OneBend", StringComparison.Ordinal)
            || FeatureDefinesOneBendData(subFeat);

        static bool TryReadOneBendAngleDegrees(Feature bendFeat, out double bendAngleDeg)
        {
            bendAngleDeg = double.NaN;
            try
            {
                if (bendFeat.GetDefinition() is OneBendFeatureData ob)
                {
                    bendAngleDeg = ob.BendAngle * 180.0 / Math.PI;
                    return true;
                }
            }
            catch
            {
                // ignored
            }

            return false;
        }

        /// <summary>折弯角约 90° 时跳过所有依赖视图草图 CreatePoint 的标注路径。</summary>
        static bool BendNodeSkipsSketchPointDimensions(BendNode node)
        {
            if (double.IsNaN(node.BendAngleDeg)) return false;
            return Math.Abs(node.BendAngleDeg - 90.0) <= BendAngleNear90SkipSketchPointDimsTolDeg;
        }

        static BendNode? BuildBendNode(Feature bendFeat)
        {
            var node = new BendNode { BendFeature = bendFeat };

            if (TryReadOneBendAngleDegrees(bendFeat, out var angDeg))
            {
                node.BendAngleDeg = angDeg;
                if (BendNodeSkipsSketchPointDimensions(node))
                    Console.WriteLine($"折弯「{bendFeat.Name}」角度≈90°（{angDeg:F1}°），跳过草图点画点类标注");
            }

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

        static string FormatBendNodeContext(BendNode node, int nodeIndex)
        {
            var name = node.BendFeature?.Name;
            return string.IsNullOrEmpty(name) ? $"#{nodeIndex}" : $"#{nodeIndex} {name}";
        }

        static string FormatGraphEdgeContext(List<BendNode> nodes, BendEdge edge)
        {
            string nameA = nodes[edge.NodeA].BendFeature?.Name ?? "?";
            string nameB = nodes[edge.NodeB].BendFeature?.Name ?? "?";
            return $"#{edge.NodeA}:{nameA} ↔ #{edge.NodeB}:{nameB}";
        }

        /// <summary>
        /// 两平面法向量夹角换算为「平面间锐角」0~90°（与工程图常见二面角一致）。
        /// </summary>
        static double AcuteAngleBetweenPlanesDegrees(double[] n1, double[] n2)
        {
            double dot = n1[0] * n2[0] + n1[1] * n2[1] + n1[2] * n2[2];
            dot = Math.Max(-1, Math.Min(1, dot));
            double ang = Math.Acos(dot) * 180 / Math.PI;
            return ang > 90 ? 180 - ang : ang;
        }

        static bool TryGetPlaneUnitNormal(Face face, out double nx, out double ny, out double nz)
        {
            nx = ny = nz = 0;
            var s = (Surface)face.GetSurface();
            if (!s.IsPlane()) return false;
            var p = (double[])s.PlaneParams;
            double len = Math.Sqrt(p[0] * p[0] + p[1] * p[1] + p[2] * p[2]);
            if (len < 1e-12) return false;
            nx = p[0] / len;
            ny = p[1] / len;
            nz = p[2] / len;
            return true;
        }

        /// <summary>两面均为平面且夹角在 90°±tol 内则返回 true（不创建该尺寸）。非平面不参与判断。</summary>
        static bool IsAbout90DegreesBetweenPlanes(Face planeA, Face planeB, double tolDeg)
        {
            if (!TryGetPlaneUnitNormal(planeA, out var ax, out var ay, out var az)) return false;
            if (!TryGetPlaneUnitNormal(planeB, out var bx, out var by, out var bz)) return false;
            double acute = AcuteAngleBetweenPlanesDegrees(new[] { ax, ay, az }, new[] { bx, by, bz });
            return Math.Abs(acute - 90) <= tolDeg;
        }

        /// <summary>两面均为平面且法向平行（同向或反向，锐角为 0°）在容差内则 true；任一面非平面则 false。</summary>
        static bool ArePlanesParallelWithinTolerance(Face planeA, Face planeB, double tolDeg)
        {
            if (!TryGetPlaneUnitNormal(planeA, out var ax, out var ay, out var az)) return false;
            if (!TryGetPlaneUnitNormal(planeB, out var bx, out var by, out var bz)) return false;
            double acute = AcuteAngleBetweenPlanesDegrees(new[] { ax, ay, az }, new[] { bx, by, bz });
            return acute <= tolDeg;
        }

        /// <summary>模型空间点变换到工程图图纸坐标 XY（与 IView.ModelToViewTransform 一致，AddDimension2 使用）。</summary>
        static bool TryModelPointToSheetXY(ISldWorks swApp, View view, double[] model3, out double sheetX, out double sheetY)
        {
            sheetX = sheetY = 0;
            try
            {
                var math = swApp.IGetMathUtility();
                if (math == null) return false;
                var mp = (MathPoint)math.CreatePoint(model3);
                if (mp == null) return false;
                var modelToSheet = (MathTransform)view.ModelToViewTransform;
                if (modelToSheet == null) return false;
                mp = (MathPoint)mp.MultiplyTransform(modelToSheet);
                var arr = (double[])mp.ArrayData;
                if (arr == null || arr.Length < 2) return false;
                sheetX = arr[0];
                sheetY = arr[1];
                return true;
            }
            catch
            {
                return false;
            }
        }

        static bool TryGetEdgeMidpointModel(Edge edge, out double[]? mid)
        {
            mid = null;
            try
            {
                var sv = (Vertex)edge.GetStartVertex();
                var ev = (Vertex)edge.GetEndVertex();
                if (sv == null || ev == null) return false;
                var ps = (double[])sv.GetPoint();
                var pe = (double[])ev.GetPoint();
                if (ps == null || pe == null || ps.Length < 3 || pe.Length < 3) return false;
                mid = new[]
                {
                    (ps[0] + pe[0]) * 0.5,
                    (ps[1] + pe[1]) * 0.5,
                    (ps[2] + pe[2]) * 0.5
                };
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>由两模型空间参考点得图纸 XY：弦中点 + 法向偏移 + 累积错开，使文字靠近被标边且多道尺寸不叠。</summary>
        static bool TryGetDimensionLeaderSheetXYFromTwoModelPoints(
            ISldWorks swApp,
            View view,
            double[] modelA,
            double[] modelB,
            ref double perpStaggerM,
            out double sheetX,
            out double sheetY)
        {
            sheetX = sheetY = 0;
            if (!TryModelPointToSheetXY(swApp, view, modelA, out var ax, out var ay)) return false;
            if (!TryModelPointToSheetXY(swApp, view, modelB, out var bx, out var by)) return false;
            double mx = (ax + bx) * 0.5;
            double my = (ay + by) * 0.5;
            double vx = bx - ax;
            double vy = by - ay;
            double len = Math.Sqrt(vx * vx + vy * vy);
            double nx, ny;
            if (len > 1e-12)
            {
                nx = -vy / len;
                ny = vx / len;
            }
            else
            {
                nx = 0;
                ny = 1;
            }

            double off = DimensionTextPerpBaseOffsetM + perpStaggerM;
            perpStaggerM += DimensionTextPerpStaggerStepM;
            sheetX = mx + nx * off;
            sheetY = my + ny * off;
            return true;
        }

        /// <summary>
        /// 加点之前的路径：两面各自一条候选直边 → 工程图可见边 → AddDimension2；不要求两面法向平行（两面不平行时仍可先试此路径）。
        /// </summary>
        static bool TryAddEdgeEdgeDrawingDimensionBetweenFaces(
            ISldWorks swApp,
            ModelDoc2 swModel,
            View view,
            Face planeA,
            Face planeB,
            List<Edge> edgeCandidates1,
            List<Edge> edgeCandidates2,
            ref double textStaggerM,
            out string failReason)
        {
            failReason = "";
            ExitDrawingSketchIfActive(swModel);

            if (IsAbout90DegreesBetweenPlanes(planeA, planeB, RightAngleSkipToleranceDeg))
            {
                failReason = "两面夹角约90°不标注";
                return false;
            }

            var vis1List = new List<Edge>();
            foreach (var e1 in edgeCandidates1)
            {
                var v = FindVisibleEdge(swApp, view, e1);
                if (v == null) continue;
                bool dup = false;
                foreach (var u in vis1List)
                {
                    if (ReferenceEquals(u, v)) { dup = true; break; }
                }
                if (!dup) vis1List.Add(v);
            }

            if (vis1List.Count == 0)
            {
                failReason = "一级面边不可见";
                return false;
            }

            var vis2List = new List<Edge>();
            foreach (var e2 in edgeCandidates2)
            {
                var v = FindVisibleEdge(swApp, view, e2);
                if (v == null) continue;
                bool dup = false;
                foreach (var u in vis2List)
                {
                    if (ReferenceEquals(u, v)) { dup = true; break; }
                }
                if (!dup) vis2List.Add(v);
            }

            if (vis2List.Count == 0)
            {
                failReason = "二级面边不可见";
                return false;
            }

            var selMgr = (SelectionMgr)swModel.SelectionManager;
            var selData = selMgr.CreateSelectData();
            selData.View = view;

            foreach (var visEdge1 in vis1List)
            {
                foreach (var visEdge2 in vis2List)
                {
                    if (ReferenceEquals(visEdge1, visEdge2)) continue;

                    if (!TryGetEdgeMidpointModel(visEdge1, out var mid1) || mid1 == null ||
                        !TryGetEdgeMidpointModel(visEdge2, out var mid2) || mid2 == null ||
                        !TryGetDimensionLeaderSheetXYFromTwoModelPoints(swApp, view, mid1, mid2, ref textStaggerM, out var x, out var y))
                        continue;

                    swModel.ClearSelection2(true);
                    ((Entity)visEdge1).Select4(true, selData);
                    ((Entity)visEdge2).Select4(true, selData);

                    var added = swModel.AddDimension2(x, y, 0) as DisplayDimension;
                    swModel.ClearSelection2(true);
                    if (added == null)
                    {
                        ((Entity)visEdge2).Select4(false, selData);
                        ((Entity)visEdge1).Select4(true, selData);
                        added = swModel.AddDimension2(x, y, 0) as DisplayDimension;
                        swModel.ClearSelection2(true);
                    }

                    if (added != null)
                        return true;
                }
            }

            failReason = "AddDimension2返回空";
            return false;
        }

        /// <summary>
        /// 节点内两内圆弧一级面「一级面间」：边—边失败后，用内圆柱弧中点与各二级面可见直边尝试草图点路径（与一级—二级不平行回退一致），不使用对侧一级面棱。
        /// 同一 <paramref name="node"/> 上同一二级面与内弧点的组合在 <paramref name="dimensionedPairs"/> 中只成功一次，避免与后续一级—二级内弧路径重复。
        /// </summary>
        static bool TryInnerArcMidpointToNodeSecondaryForInnerInnerPair(
            ISldWorks swApp,
            ModelDoc2 swModel,
            View view,
            double[] xformData,
            BendNode node,
            Face faceA,
            Face faceB,
            double[] axis,
            ref double textStaggerM,
            HashSet<string> dimensionedPairs,
            out string? lastFailDetail)
        {
            lastFailDetail = null;
            if (node.SecondaryFaces == null || node.SecondaryFaces.Count == 0)
            {
                lastFailDetail = "无二级面";
                return false;
            }

            if (BendNodeSkipsSketchPointDimensions(node))
            {
                lastFailDetail = "折弯角约90°跳过草图点画点标注";
                return false;
            }

            if (node.InnerCylinderFace != null)
            {
                bool allSecondaryArcDuped = true;
                foreach (var item in node.SecondaryFaces)
                {
                    if (item.face == null)
                    {
                        allSecondaryArcDuped = false;
                        break;
                    }

                    var k0 = GetFacePairKey(node.InnerCylinderFace, item.face, InnerArcPointSecondaryDedupeLevel);
                    if (!dimensionedPairs.Contains(k0))
                    {
                        allSecondaryArcDuped = false;
                        break;
                    }
                }

                if (allSecondaryArcDuped)
                {
                    lastFailDetail = "内弧中点二级已标注";
                    return false;
                }
            }

            string? lastPtTry = null;
            bool anySecondaryVisible = false;
            foreach (var item in node.SecondaryFaces)
            {
                var secFace = item.face;
                if (node.InnerCylinderFace != null && secFace != null)
                {
                    var dedupeKey = GetFacePairKey(node.InnerCylinderFace, secFace, InnerArcPointSecondaryDedupeLevel);
                    if (dimensionedPairs.Contains(dedupeKey))
                        continue;
                }

                var edgeCandidates2 = GetEdgesForDimension(secFace, axis, item.level, false);
                foreach (var e2 in edgeCandidates2)
                {
                    var visEdge2 = FindVisibleEdge(swApp, view, e2);
                    if (visEdge2 == null) continue;
                    anySecondaryVisible = true;
                    foreach (var placePrimary in new[] { faceA, faceB })
                    {
                        if (TryAddInnerArcCenterPointToSecondaryEdgeDimension(
                                swApp, swModel, view, node, placePrimary, visEdge2, xformData, ref textStaggerM,
                                out var ptFail))
                        {
                            if (node.InnerCylinderFace != null && secFace != null)
                                dimensionedPairs.Add(
                                    GetFacePairKey(node.InnerCylinderFace, secFace, InnerArcPointSecondaryDedupeLevel));
                            return true;
                        }

                        lastPtTry = ptFail;
                    }
                }
            }

            lastFailDetail = anySecondaryVisible
                ? (lastPtTry ?? "内弧中点-二级边路径未知失败")
                : "二级面候选边在视图中均不可见";
            return false;
        }

        /// <summary>
        /// 节点内「两内圆弧侧一级面」：边—边与「内弧中点—二级面边」分别记入 <paramref name="dimensionedPairs"/>（|边边| / |内弧点二级|），互不短路；两条路径各自能标则都标（每无序对最多 2 个尺寸）。
        /// </summary>
        static int TryDimensionNodeInnerInnerFirstLevelPair(
            ISldWorks swApp,
            ModelDoc2 swModel,
            View view,
            double[] xformData,
            BendNode node,
            Face faceA,
            Face faceB,
            double[] axis,
            ref double textStaggerM,
            string nodeCtx,
            Dictionary<string, int> reasonStats,
            HashSet<string> dimensionedPairs,
            out string? lastBlockReason)
        {
            lastBlockReason = null;
            ExitDrawingSketchIfActive(swModel);

            var keyEdge = GetFacePairKey(faceA, faceB, "节点内一级面间|边边");
            var keyArc = GetFacePairKey(faceA, faceB, "节点内一级面间|内弧点二级");

            bool haveSecondary = node.SecondaryFaces != null && node.SecondaryFaces.Count > 0;
            var edgeCandidatesA = GetEdgesForInnerInnerDimension(faceA, axis, "一级面间A");
            var edgeCandidatesB = GetEdgesForInnerInnerDimension(faceB, axis, "一级面间B");
            bool anyInnerInnerEdgeA = edgeCandidatesA.Count > 0;
            bool anyInnerInnerEdgeB = edgeCandidatesB.Count > 0;

            bool wantEdge = !dimensionedPairs.Contains(keyEdge) && anyInnerInnerEdgeA && anyInnerInnerEdgeB;
            bool wantArc = !dimensionedPairs.Contains(keyArc) && haveSecondary && !BendNodeSkipsSketchPointDimensions(node);

            if (!wantEdge && !wantArc)
                return 0;

            if ((!anyInnerInnerEdgeA || !anyInnerInnerEdgeB) && !haveSecondary)
            {
                lastBlockReason = "一级面间一侧无与轴足够偏离的直边";
                AddReason(reasonStats, "节点内一级面间_一侧无可用直边", nodeCtx);
                return 0;
            }

            bool parallelOk =
                ArePlanesParallelWithinTolerance(faceA, faceB, ParallelPlaneToleranceDeg);

            int count = 0;

            if (wantEdge)
            {
                if (TryAddEdgeEdgeDrawingDimensionBetweenFaces(
                        swApp, swModel, view, faceA, faceB,
                        edgeCandidatesA, edgeCandidatesB, ref textStaggerM, out var edgeFail))
                {
                    dimensionedPairs.Add(keyEdge);
                    count++;
                    if (!parallelOk)
                    {
                        Console.WriteLine("节点内一级面间（独立）边—边已创建尺寸（两面不平行）");
                        AddReason(reasonStats, "节点内一级面间_配对成功_不平行两边", nodeCtx);
                    }
                    else
                    {
                        Console.WriteLine("节点内一级面间（独立）边—边已创建尺寸（两面平行）");
                        AddReason(reasonStats, "节点内一级面间_配对成功", nodeCtx);
                    }
                }
                else
                {
                    lastBlockReason = edgeFail;
                    if (!parallelOk)
                    {
                        Console.WriteLine($"节点内一级面间（独立）两面不平行，边—边标注未成功：{edgeFail}");
                    }
                    else
                    {
                        Console.WriteLine($"节点内一级面间（独立）两面平行，边—边标注未成功：{edgeFail}");
                        if (edgeFail == "一级面边不可见")
                            AddReason(reasonStats, "节点内一级面间_一级面边不可见", nodeCtx);
                        else if (edgeFail == "二级面边不可见")
                            AddReason(reasonStats, "节点内一级面间_对侧边不可见", nodeCtx);
                        else if (edgeFail == "两面夹角约90°不标注")
                            AddReason(reasonStats, "节点内一级面间_跳过两面夹角约90度", nodeCtx);
                        else if (edgeFail == "AddDimension2返回空")
                            AddReason(reasonStats, "节点内一级面间_AddDimension2返回空", nodeCtx);
                        else if (edgeFail == "尺寸放置点(图纸坐标)计算失败")
                            AddReason(reasonStats, "节点内一级面间_尺寸放置点计算失败", nodeCtx);
                        else
                            AddReason(reasonStats, $"节点内一级面间_边边标注失败({edgeFail})", nodeCtx);
                    }
                }
            }

            if (wantArc)
            {
                if (TryInnerArcMidpointToNodeSecondaryForInnerInnerPair(
                        swApp, swModel, view, xformData, node, faceA, faceB, axis, ref textStaggerM, dimensionedPairs,
                        out var arcDetail))
                {
                    dimensionedPairs.Add(keyArc);
                    count++;
                    Console.WriteLine(
                        parallelOk
                            ? "节点内一级面间（独立）内弧中点—二级面边已创建尺寸（与边—边独立，两面平行）"
                            : "节点内一级面间（独立）内弧中点—二级面边已创建尺寸（与边—边独立，两面不平行）");
                    AddReason(reasonStats, "节点内一级面间_配对成功_内弧中点二级边", nodeCtx);
                }
                else
                {
                    lastBlockReason = arcDetail;
                    if (!parallelOk)
                    {
                        Console.WriteLine($"节点内一级面间（独立）内弧中点—二级边标注未成功：{arcDetail}");
                        AddReason(reasonStats, $"节点内一级面间_不平行_内弧中点二级边失败({arcDetail})", nodeCtx);
                    }
                    else
                    {
                        Console.WriteLine($"节点内一级面间（独立）内弧中点—二级边标注未成功：{arcDetail}");
                        AddReason(reasonStats, $"节点内一级面间_平行_内弧中点二级边失败({arcDetail})", nodeCtx);
                    }
                }
            }

            return count;
        }

        static int ProcessBendNode(ISldWorks swApp, ModelDoc2 swModel, View view, double[] xformData, BendNode node, int nodeIndex, ref double textStaggerM, HashSet<string> dimensionedPairs, Dictionary<string, int> reasonStats)
        {
            var axis = node.Axis;
            if (axis == null) return 0;
            string nodeCtx = FormatBendNodeContext(node, nodeIndex);
            if (!IsBendAxisNormalToDrawingViewPlane(axis, xformData))
            {
                AddReason(reasonStats, "跳过_圆柱轴未垂直于视图平面", nodeCtx);
                return 0;
            }

            var firstLevelFaces = node.FirstLevelFaces;
            var secondaryFaces = node.SecondaryFaces;

            // 6. 为每个一级面找到对应的二级面并标注
            // 使用全局已标注面集合防止重复标注（跨折弯共享）
            int dimensionCount = 0;

            // 独立阶段：两内圆弧侧一级面「一级面间」每一对无序面单独处理，与一级—二级逻辑互不抢占
            var faces = firstLevelFaces;
            for (int ii = 0; ii < faces.Count; ii++)
            {
                var fa = faces[ii];
                var sA = (Surface)fa.GetSurface();
                if (!sA.IsPlane()) continue;

                bool faInner = node.InnerCylinderFace != null && FacesIntersect(fa, node.InnerCylinderFace);
                for (int jj = ii + 1; jj < faces.Count; jj++)
                {
                    var fb = faces[jj];
                    var sB = (Surface)fb.GetSurface();
                    if (!sB.IsPlane()) continue;

                    bool fbInner = node.InnerCylinderFace != null && FacesIntersect(fb, node.InnerCylinderFace);
                    if (!faInner || !fbInner) continue;

                    var keyEdgeFf = GetFacePairKey(fa, fb, "节点内一级面间|边边");
                    var keyArcFf = GetFacePairKey(fa, fb, "节点内一级面间|内弧点二级");
                    if (dimensionedPairs.Contains(keyEdgeFf) && dimensionedPairs.Contains(keyArcFf)) continue;

                    double distFf = FaceToFaceDistance(fa, fb);
                    double maxRepDist = 0.05;
                    if (node.InnerCylinderFace != null &&
                        TryGetCylinderData(node.InnerCylinderFace, out _, out _, out var innerRad) &&
                        innerRad > 1e-9)
                        maxRepDist = Math.Max(InnerInnerPairDistFloorM, innerRad * InnerInnerPairDistPerInnerRadius);
                    if (distFf > maxRepDist) continue;

                    ExitDrawingSketchIfActive(swModel);
                    Console.WriteLine(
                        $"节点内一级面间配对（独立）：面积A = {fa.GetArea() * 1000000:F2} mm^2, 面积B = {fb.GetArea() * 1000000:F2} mm^2, 距离 = {distFf * 1000:F2} mm");

                    dimensionCount += TryDimensionNodeInnerInnerFirstLevelPair(
                        swApp, swModel, view, xformData, node, fa, fb, axis, ref textStaggerM, nodeCtx, reasonStats,
                        dimensionedPairs, out _);
                }
            }

            foreach (var firstFace in firstLevelFaces)
            {
                ExitDrawingSketchIfActive(swModel);
                // 内圆弧侧一级面：不参与「一级面—二级面」配对；「一级面—一级面间」仅允许两侧均为内圆弧相邻一级面（外圆柱侧一级面不做一级面间标注）
                bool firstFaceIsInnerCylinderSide =
                    node.InnerCylinderFace != null && FacesIntersect(firstFace, node.InnerCylinderFace);

                var s1 = (Surface)firstFace.GetSurface();
                if (!s1.IsPlane()) continue;

                // 为当前一级面生成候选（仅一级—二级）；两内圆弧一级面间已在节点开头独立阶段处理。排序：二级远者优先。
                var candidates = new List<(Face secFace, string level, string pairKey, double dist, Face sourceFirstFace)>();
                if (!firstFaceIsInnerCylinderSide)
                {
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
                            AddReason(reasonStats, "节点内_跳过已标注组合", nodeCtx);
                            continue;
                        }

                        double dist = FaceToFaceDistance(firstFace, secFace);
                        candidates.Add((secFace, item.level, pairKey, dist, item.sourceFirstFace));
                    }
                }
                else if (secondaryFaces.Count > 0)
                {
                    Console.WriteLine(
                        $"内圆弧侧一级面（面积 = {firstFace.GetArea() * 1000000:F2} mm^2）：不参与一级-二级配对（一级面间已在独立阶段处理）");
                    AddReason(reasonStats, "节点内_内圆弧一级面不参与一级二级配对", nodeCtx);
                }

                if (candidates.Count == 0)
                {
                    AddReason(reasonStats, "节点内_无候选配对", nodeCtx);
                    continue;
                }
                candidates.Sort(CompareNodeDimensionCandidates);

                bool placed = false;
                string? lastBlockReason = null;
                foreach (var cand in candidates)
                {
                    ExitDrawingSketchIfActive(swModel);
                    Console.WriteLine($"节点内配对尝试：一级面面积 = {firstFace.GetArea() * 1000000:F2} mm^2, {cand.level}面积 = {cand.secFace.GetArea() * 1000000:F2} mm^2, 距离 = {cand.dist * 1000:F2} mm");

                    // 标注 - 获取3D边并在视图中查找对应可见边
                    var edgeCandidates1 = GetEdgesForDimension(firstFace, axis, "一级面");
                    var edgeCandidates2 = GetEdgesForDimension(cand.secFace, axis, cand.level);
                    if (edgeCandidates2.Count == 0)
                    {
                        Console.WriteLine($"[{cand.level}] 未找到不平行于轴线的直边，按规则跳过该候选");
                        lastBlockReason = "二级面无可用直边";
                        AddReason(reasonStats, "节点内_二级面无可用直边", nodeCtx);
                        continue;
                    }

                    // 一级—二级：按真实两面平行度分支；不平行时仅试内弧中点 + 二级可见边（不做一级—二级边—边）。
                    bool parallelOk =
                        ArePlanesParallelWithinTolerance(firstFace, cand.secFace, ParallelPlaneToleranceDeg);

                    if (!parallelOk)
                    {
                        // 一级—二级两面不平行时：不做「两可见边直接 AddDimension2」。
                        // 否则易在外圆柱侧一级面与二级面之间误出角度，与「两内圆弧一级面、与圆柱轴不平行的边—边」
                        // （仅在节点开头独立阶段处理）混淆；此处只走内弧中点 + 二级可见边。
                        lastBlockReason = "一级二级不平行_跳过边边直接标注";
                        Console.WriteLine(
                            "两面不平行：一级—二级跳过两可见边直接标注（避免外一级面+二级误出角度；内一级面间边—边见节点内一级面间独立阶段）");

                        if (BendNodeSkipsSketchPointDimensions(node))
                        {
                            Console.WriteLine("两面不平行：折弯角约90°按规则跳过内弧草图点—二级边");
                            lastBlockReason = "折弯角约90°跳过内弧点标注";
                            AddReason(reasonStats, "节点内_折弯角约90跳过内弧点标注", nodeCtx);
                            continue;
                        }

                        Edge visEdge2Pt = null;
                        foreach (var e2 in edgeCandidates2)
                        {
                            visEdge2Pt = FindVisibleEdge(swApp, view, e2);
                            if (visEdge2Pt != null) break;
                        }

                        if (visEdge2Pt == null)
                        {
                            Console.WriteLine("两面不平行：未找到二级面可见直边，无法试内弧中点—边");
                            lastBlockReason = "不平行且二级边不可见";
                            AddReason(reasonStats, "节点内_不平行_二级边不可见", nodeCtx);
                            continue;
                        }

                        if (node.InnerCylinderFace != null)
                        {
                            var innerArcSecKey = GetFacePairKey(
                                node.InnerCylinderFace, cand.secFace, InnerArcPointSecondaryDedupeLevel);
                            if (dimensionedPairs.Contains(innerArcSecKey))
                            {
                                Console.WriteLine("跳过内弧中点—二级：该二级面已与内弧点组合标注（与一级面间去重）");
                                lastBlockReason = "内弧中点二级已标注";
                                AddReason(reasonStats, "节点内_跳过内弧中点二级已标注", nodeCtx);
                                continue;
                            }
                        }

                        if (ShouldSkipInnerArcBecauseMirrorOuterHasEdgeEdgeFirstSecondary(
                                node, firstFace, cand.sourceFirstFace, firstLevelFaces, secondaryFaces, dimensionedPairs))
                        {
                            Console.WriteLine(
                                "跳过内弧中点—二级：对侧外一级面已与另一侧二级面完成边—边一级—二级标注（对称不再内弧标对侧二级）");
                            lastBlockReason = "镜像侧已边边一级二级跳过内弧对侧二级";
                            AddReason(reasonStats, "节点内_跳过镜像内弧对侧已边边一级二级", nodeCtx);
                            continue;
                        }

                        if (TryAddInnerArcCenterPointToSecondaryEdgeDimension(
                                swApp, swModel, view, node, firstFace, visEdge2Pt, xformData, ref textStaggerM,
                                out var ptFailReason))
                        {
                            dimensionedPairs.Add(cand.pairKey);
                            if (node.InnerCylinderFace != null)
                                dimensionedPairs.Add(GetFacePairKey(
                                    node.InnerCylinderFace, cand.secFace, InnerArcPointSecondaryDedupeLevel));
                            dimensionCount++;
                            placed = true;
                            Console.WriteLine(
                                "节点内配对成功：已用内圆弧中点与二级面可见边创建尺寸（两面不平行·草图点路径）");
                            AddReason(reasonStats, "节点内_配对成功_内弧中点二级边", nodeCtx);
                            break;
                        }

                        Console.WriteLine($"两面不平行，内弧中点-二级边标注未成功：{ptFailReason}");
                        lastBlockReason = ptFailReason;
                        AddReason(reasonStats, $"节点内_不平行_内弧中点边失败({ptFailReason})", nodeCtx);
                        continue;
                    }

                    if (!TryAddEdgeEdgeDrawingDimensionBetweenFaces(
                            swApp, swModel, view, firstFace, cand.secFace,
                            edgeCandidates1, edgeCandidates2, ref textStaggerM, out var edgeFailPar))
                    {
                        Console.WriteLine($"节点内两可见边标注未成功：{edgeFailPar}");
                        lastBlockReason = edgeFailPar;
                        if (edgeFailPar == "一级面边不可见")
                            AddReason(reasonStats, "节点内_一级面边不可见", nodeCtx);
                        else if (edgeFailPar == "二级面边不可见")
                            AddReason(reasonStats, "节点内_二级面边不可见", nodeCtx);
                        else if (edgeFailPar == "两面夹角约90°不标注")
                            AddReason(reasonStats, "节点内_跳过两面夹角约90度", nodeCtx);
                        else if (edgeFailPar == "AddDimension2返回空")
                            AddReason(reasonStats, "节点内_AddDimension2返回空", nodeCtx);
                        else if (edgeFailPar == "尺寸放置点(图纸坐标)计算失败")
                            AddReason(reasonStats, "节点内_尺寸放置点计算失败", nodeCtx);
                        else
                            AddReason(reasonStats, $"节点内_边边标注失败({edgeFailPar})", nodeCtx);
                        continue;
                    }

                    dimensionedPairs.Add(cand.pairKey);
                    dimensionCount++;
                    placed = true;
                    Console.WriteLine("节点内配对成功：已创建尺寸");
                    AddReason(reasonStats, "节点内_配对成功", nodeCtx);
                    break;
                }

                if (!placed)
                {
                    Console.WriteLine($"节点内配对失败：一级面面积 = {firstFace.GetArea() * 1000000:F2} mm^2，所有候选均不可标注");
                    var tail = lastBlockReason ?? (candidates.Count == 0 ? "无候选" : "未知");
                    AddReason(reasonStats, $"节点内_配对失败(全部候选未通过·末次:{tail})", nodeCtx);
                }
            }

            return dimensionCount;
        }

        static int ProcessGraphEdges(ISldWorks swApp, ModelDoc2 swModel, View view, double[] xformData, List<BendNode> nodes, List<BendEdge> edges, ref double textStaggerM, HashSet<string> dimensionedPairs, Dictionary<string, int> reasonStats)
        {
            int dimensionCount = 0;
            foreach (var edge in edges)
            {
                ExitDrawingSketchIfActive(swModel);
                var connectedFaceA = edge.ConnectedFirstFaceA;
                var connectedFaceB = edge.ConnectedFirstFaceB;
                var axisA = nodes[edge.NodeA].Axis;
                string edgeCtx = FormatGraphEdgeContext(nodes, edge);

                if (connectedFaceA == null || connectedFaceB == null || axisA == null)
                {
                    AddReason(reasonStats, "节点间_连接信息缺失", edgeCtx);
                    continue;
                }

                var axisB = nodes[edge.NodeB].Axis;
                if (!IsBendAxisNormalToDrawingViewPlane(axisA, xformData) ||
                    axisB == null || !IsBendAxisNormalToDrawingViewPlane(axisB, xformData))
                {
                    AddReason(reasonStats, "节点间_跳过_一侧或两侧圆柱轴未垂直于视图平面", edgeCtx);
                    continue;
                }

                // 节点间规则：完全跳过连接面，只处理两节点外圆柱来源一级面中的非连接面配对
                var nodeAOuterFaces = nodes[edge.NodeA].OuterFirstLevelFaces;
                var nodeBOuterFaces = nodes[edge.NodeB].OuterFirstLevelFaces;
                if (nodeAOuterFaces.Count == 0 || nodeBOuterFaces.Count == 0)
                {
                    AddReason(reasonStats, "节点间_外圆柱一级面不足", edgeCtx);
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
                    AddReason(reasonStats, "节点间_无可标注面配对", edgeCtx);
                    continue;
                }

                pairCandidates.Sort((a, b) => a.dist.CompareTo(b.dist));
                bool placed = false;
                string? lastInterFail = null;
                foreach (var candidate in pairCandidates)
                {
                    var areaA = candidate.faceA.GetArea() * 1000000;
                    var areaB = candidate.faceB.GetArea() * 1000000;
                    Console.WriteLine(
                        $"节点间配对尝试：NodeA={edge.NodeA}, NodeB={edge.NodeB}, A面积={areaA:F2} mm^2, B面积={areaB:F2} mm^2, 距离={candidate.dist * 1000:F2} mm");

                    if (!TryAddDimension(swApp, swModel, view, xformData, candidate.faceA, candidate.faceB, axisA, "节点间外圆柱非连接一级面", ref textStaggerM, out var failReason))
                    {
                        lastInterFail = failReason;
                        AddReason(reasonStats, $"节点间_{failReason}", edgeCtx);
                        continue;
                    }

                    dimensionedPairs.Add(candidate.pairKey);
                    dimensionCount++;
                    placed = true;
                    AddReason(reasonStats, "节点间_配对成功", edgeCtx);
                    Console.WriteLine(
                        $"节点间配对成功：NodeA={edge.NodeA}, NodeB={edge.NodeB}, A面积={areaA:F2} mm^2, B面积={areaB:F2} mm^2");
                    break;
                }

                if (!placed)
                {
                    Console.WriteLine($"节点间配对失败：NodeA={edge.NodeA}, NodeB={edge.NodeB}，候选均不可标注");
                    var tail = lastInterFail ?? "未知";
                    AddReason(reasonStats, $"节点间_配对失败(全部候选未通过·末次:{tail})", edgeCtx);
                }
            }
            return dimensionCount;
        }

        /// <summary>
        /// 若两节点外圆柱来源一级面中，存在任一「非连接、不相交」配对且两面法向在容差内平行，则视为可用边边标注几何，不应再走内弧中点点距。
        /// </summary>
        static bool GraphEdgeHasParallelOuterNonConnectFirstFacePair(BendEdge edge, List<BendNode> nodes)
        {
            var connectedFaceA = edge.ConnectedFirstFaceA;
            var connectedFaceB = edge.ConnectedFirstFaceB;
            if (connectedFaceA == null || connectedFaceB == null) return false;

            var nodeAOuterFaces = nodes[edge.NodeA].OuterFirstLevelFaces;
            var nodeBOuterFaces = nodes[edge.NodeB].OuterFirstLevelFaces;
            if (nodeAOuterFaces.Count == 0 || nodeBOuterFaces.Count == 0) return false;

            foreach (var faceA in nodeAOuterFaces)
            {
                if (IsSameFace(faceA, connectedFaceA)) continue;
                foreach (var faceB in nodeBOuterFaces)
                {
                    if (IsSameFace(faceB, connectedFaceB)) continue;
                    if (IsSameFace(faceA, faceB)) continue;
                    if (FacesIntersect(faceA, faceB)) continue;

                    if (ArePlanesParallelWithinTolerance(faceA, faceB, ParallelPlaneToleranceDeg))
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 图边相连的两折弯：仅当两侧外圆柱一级面不存在「可标注的平行面配对」时（与节点间边边路径互斥），才在同一视图草图建两侧内圆弧中点并标两点距离（与节点内「点+边」一致：退出草图后按坐标重找 SketchPoint 再选）。
        /// </summary>
        static int ProcessGraphEdgesInnerArcMidpointDimensions(
            ISldWorks swApp,
            ModelDoc2 swModel,
            View view,
            double[] xformData,
            List<BendNode> nodes,
            List<BendEdge> edges,
            ref double textStaggerM,
            HashSet<string> doneKeys,
            Dictionary<string, int> reasonStats)
        {
            int dimensionCount = 0;
            foreach (var edge in edges)
            {
                ExitDrawingSketchIfActive(swModel);
                int iLo = Math.Min(edge.NodeA, edge.NodeB);
                int iHi = Math.Max(edge.NodeA, edge.NodeB);
                string pairKey = $"节点间内弧中点|{iLo}|{iHi}";
                if (doneKeys.Contains(pairKey)) continue;

                var na = nodes[edge.NodeA];
                var nb = nodes[edge.NodeB];
                string edgeCtx = FormatGraphEdgeContext(nodes, edge);

                var axisGa = na.Axis;
                var axisGb = nb.Axis;
                if (axisGa == null || axisGb == null ||
                    !IsBendAxisNormalToDrawingViewPlane(axisGa, xformData) ||
                    !IsBendAxisNormalToDrawingViewPlane(axisGb, xformData))
                {
                    AddReason(reasonStats, "节点间内弧中点_跳过_一侧或两侧圆柱轴未垂直于视图平面", edgeCtx);
                    continue;
                }

                if (GraphEdgeHasParallelOuterNonConnectFirstFacePair(edge, nodes))
                {
                    AddReason(reasonStats, "节点间内弧中点_外圆柱一级面存在平行对跳过画点", edgeCtx);
                    continue;
                }

                if (BendNodeSkipsSketchPointDimensions(na) || BendNodeSkipsSketchPointDimensions(nb))
                {
                    AddReason(reasonStats, "节点间内弧中点_折弯角约90跳过画点", edgeCtx);
                    continue;
                }

                if (!TryGetInnerBendArcMidpointModel(na.InnerCylinderFace, out var ma) || ma == null)
                {
                    AddReason(reasonStats, "节点间内弧中点_A无弧中点", edgeCtx);
                    continue;
                }

                if (!TryGetInnerBendArcMidpointModel(nb.InnerCylinderFace, out var mb) || mb == null)
                {
                    AddReason(reasonStats, "节点间内弧中点_B无弧中点", edgeCtx);
                    continue;
                }

                if (!TryCreateTwoSketchPointsThenPointToPointDimension(
                        swApp, swModel, view, ma, mb, ref textStaggerM, out var fail))
                {
                    AddReason(reasonStats, $"节点间内弧中点标注失败({fail})", edgeCtx);
                    continue;
                }

                doneKeys.Add(pairKey);
                dimensionCount++;
                AddReason(reasonStats, "节点间内弧中点标注成功", edgeCtx);
                Console.WriteLine($"节点间内弧中点标注成功：{edgeCtx}");
            }

            return dimensionCount;
        }

        /// <summary>内圆柱面上与折弯轮廓相关的圆弧边：排除近整圆（拓扑缝），避免用其参数中点误标到非折弯弧位置。</summary>
        const double InnerBendArcSkipNearFullCircleFactor = 0.92;

        /// <summary>
        /// 内圆柱面上与内半径一致的圆/圆弧边中，在「非近整圆」的边里取参数跨度最大的一条（通常为折弯轮廓弧），
        /// 用曲线参数中点 <see cref="Curve.Evaluate"/> 得到弧线上的中点（非圆心）。
        /// 不再使用圆柱轴向带中点作后备：该点不在折弯内圆弧上，会导致多标一个「非弧中点」。
        /// </summary>
        static bool TryGetInnerBendArcMidpointModel(Face? innerCylinderFace, out double[]? midOut)
        {
            midOut = null;
            if (innerCylinderFace == null) return false;
            if (!TryGetCylinderData(innerCylinderFace, out _, out _, out var innerRadius) || innerRadius < 1e-9)
                return false;

            double radiusTol = Math.Max(innerRadius * 0.06, 5e-5);
            double maxSpanForBendArc = 2.0 * Math.PI * InnerBendArcSkipNearFullCircleFactor;
            Edge? bestEdge = null;
            double bestParamSpan = -1;

            foreach (Edge e in (object[])innerCylinderFace.GetEdges())
            {
                var curve = (Curve)e.GetCurve();
                if (curve == null || !curve.IsCircle()) continue;

                var cp = (double[])curve.CircleParams;
                if (cp == null || cp.Length < 7) continue;
                double r = cp[6];
                if (Math.Abs(r - innerRadius) > radiusTol) continue;

                curve.GetEndParams(out var t0, out var t1, out _, out _);
                double span = Math.Abs(t1 - t0);
                if (span < 1e-12) continue;
                if (span >= maxSpanForBendArc)
                    continue;

                if (span > bestParamSpan)
                {
                    bestParamSpan = span;
                    bestEdge = e;
                }
            }

            if (bestEdge == null)
                return false;

            var c = (Curve)bestEdge.GetCurve();
            c.GetEndParams(out var p0, out var p1, out _, out _);
            double tMid = (p0 + p1) * 0.5;

            if (!TryCurveEvaluatePoint3D(c, tMid, out var pt))
                return false;

            midOut = pt;
            return true;
        }

        static bool TryCurveEvaluatePoint3D(Curve curve, double t, out double[]? pt)
        {
            pt = null;
            try
            {
                var ev = curve.Evaluate(t);
                if (ev is double[] da && da.Length >= 3)
                {
                    pt = new[] { da[0], da[1], da[2] };
                    return true;
                }

                if (ev is object[] oa && oa.Length >= 3 &&
                    TryCoerceToDouble(oa[0], out var x) &&
                    TryCoerceToDouble(oa[1], out var y) &&
                    TryCoerceToDouble(oa[2], out var z))
                {
                    pt = new[] { x, y, z };
                    return true;
                }
            }
            catch
            {
                // ignored
            }

            return false;
        }

        static bool TryCoerceToDouble(object? o, out double v)
        {
            v = 0;
            if (o == null) return false;
            switch (o)
            {
                case double d:
                    v = d;
                    return true;
                case float f:
                    v = f;
                    return true;
                case int i:
                    v = i;
                    return true;
                default:
                    return double.TryParse(o.ToString(), out v);
            }
        }

        /// <summary>
        /// 将零件模型空间点变到「当前工程图视图草图」局部坐标：先 IView.ModelToViewTransform（模型→图纸/视图在图纸上的位置），
        /// 再 ISketch.ModelToSketchTransform（图纸上的视图区域→该视图草图局部）。仅用第一步时 CreatePoint 会偏位。
        /// </summary>
        static bool TryModelPointToDrawingViewSketchLocal(
            ISldWorks swApp,
            View view,
            ModelDoc2 swModel,
            double[] model3,
            out double[] sketch3)
        {
            sketch3 = Array.Empty<double>();
            try
            {
                var math = swApp.IGetMathUtility();
                if (math == null) return false;
                var mp = (MathPoint)math.CreatePoint(model3);
                if (mp == null) return false;

                var modelToSheet = (MathTransform)view.ModelToViewTransform;
                if (modelToSheet == null) return false;
                mp = (MathPoint)mp.MultiplyTransform(modelToSheet);

                Sketch? sk = null;
                try
                {
                    if (swModel.SketchManager.ActiveSketch != null)
                        sk = (Sketch)swModel.SketchManager.ActiveSketch;
                }
                catch
                {
                    // ignored
                }

                if (sk == null)
                {
                    try
                    {
                        sk = (Sketch)view.GetSketch();
                    }
                    catch
                    {
                        sk = null;
                    }
                }

                if (sk == null) return false;

                var sheetToSketch = (MathTransform)sk.ModelToSketchTransform;
                if (sheetToSketch == null) return false;
                mp = (MathPoint)mp.MultiplyTransform(sheetToSketch);

                var arr = (double[])mp.ArrayData;
                if (arr == null || arr.Length < 3) return false;
                sketch3 = new[] { arr[0], arr[1], arr[2] };
                return true;
            }
            catch
            {
                return false;
            }
        }

        static void ExitDrawingSketchIfActive(ModelDoc2 swModel)
        {
            try
            {
                try
                {
                    swModel.SketchManager.AddToDB = false;
                }
                catch
                {
                    // ignored
                }

                // 非 90° 走「视图草图建点」路径时，若未完全退出编辑草图或 AddToDB 残留为 true，后续边到边 AddDimension2 会全部失败
                for (int i = 0; i < 6; i++)
                {
                    try
                    {
                        if (swModel.SketchManager.ActiveSketch == null)
                            break;
                        swModel.SketchManager.InsertSketch(false);
                    }
                    catch
                    {
                        break;
                    }
                }

                try
                {
                    swModel.ClearSelection2(true);
                }
                catch
                {
                    // ignored
                }
            }
            catch
            {
                // ignored
            }
        }

        static List<SketchPoint> CollectSketchPointsFromSketch(Sketch sketch)
        {
            var list = new List<SketchPoint>();
            if (sketch == null) return list;
            try
            {
                object? raw = null;
                try
                {
                    raw = sketch.GetSketchPoints();
                }
                catch
                {
                    // ignored
                }

                if (raw == null)
                {
                    try
                    {
                        raw = sketch.GetSketchPoints2();
                    }
                    catch
                    {
                        // ignored — 当前互操作无带参数的 GetSketchPoints2 重载
                    }
                }

                if (raw is object[] arr)
                {
                    foreach (var o in arr)
                    {
                        if (o == null) continue;
                        if (o is SketchPoint sp)
                        {
                            list.Add(sp);
                            continue;
                        }

                        try
                        {
                            list.Add((SketchPoint)o);
                        }
                        catch
                        {
                            // ignored — 部分 SW 版本返回非 SketchPoint 的 dispatch
                        }
                    }
                }
                else if (raw is SketchPoint single)
                {
                    list.Add(single);
                }
            }
            catch
            {
                // ignored
            }

            return list;
        }

        static SketchPoint? TryCoerceCreatePointResultToSketchPoint(object? createPointReturn)
        {
            if (createPointReturn == null) return null;
            if (createPointReturn is SketchPoint sp) return sp;
            try
            {
                return (SketchPoint)createPointReturn;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>仍在编辑视图草图时解析刚 CreatePoint 的点：优先 API 返回值，其次枚举（退出后 GetSketchPoints 常为空）。</summary>
        static SketchPoint? ResolveCreatedSketchPointWhileSketchActive(
            Sketch? activeSketch,
            HashSet<string> fingerprintsBefore,
            double[] sketchLocalUsedAtCreate,
            object? createPointReturn,
            SketchPoint? exclude,
            double sketchSpaceTolM)
        {
            var fromApi = TryCoerceCreatePointResultToSketchPoint(createPointReturn);
            if (fromApi != null)
                return fromApi;

            if (activeSketch == null)
                return null;

            var picked = PickSketchPointNearestCreatedLocal(
                activeSketch, fingerprintsBefore, sketchLocalUsedAtCreate, exclude, sketchSpaceTolM, newPointsOnly: true);
            if (picked != null)
                return picked;

            return PickSketchPointNearestCreatedLocal(
                activeSketch, fingerprintsBefore, sketchLocalUsedAtCreate, exclude, sketchSpaceTolM, newPointsOnly: false);
        }

        static string SketchPointFingerprint(SketchPoint sp)
        {
            return $"{Math.Round(sp.X, 9)}|{Math.Round(sp.Y, 9)}|{Math.Round(sp.Z, 9)}";
        }

        static HashSet<string> GetSketchPointFingerprints(Sketch? sketch)
        {
            var hs = new HashSet<string>();
            if (sketch == null) return hs;
            foreach (var sp in CollectSketchPointsFromSketch(sketch))
            {
                try
                {
                    hs.Add(SketchPointFingerprint(sp));
                }
                catch
                {
                    // ignored
                }
            }

            return hs;
        }

        /// <summary>视图草图局部坐标 → 零件模型坐标（与 TryModelPointToDrawingViewSketchLocal 互逆）。</summary>
        static bool TrySketchLocalToModelPoint(
            ISldWorks swApp,
            View view,
            Sketch sketch,
            double[] sketchLocal3,
            out double[]? model3)
        {
            model3 = null;
            try
            {
                var math = swApp.IGetMathUtility();
                var mp = (MathPoint)math.CreatePoint(sketchLocal3);
                var skInv = ((MathTransform)sketch.ModelToSketchTransform).Inverse();
                mp = (MathPoint)mp.MultiplyTransform(skInv);
                var viewInv = ((MathTransform)view.ModelToViewTransform).Inverse();
                mp = (MathPoint)mp.MultiplyTransform(viewInv);
                var arr = (double[])mp.ArrayData;
                if (arr == null || arr.Length < 3) return false;
                model3 = new[] { arr[0], arr[1], arr[2] };
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 在视图草图点中选取「本轮新建」或「模型位置与目标一致」的点；避免误选视图自带投影/一级面边端点（仅用草图距离最近会错）。
        /// </summary>
        static SketchPoint? PickSketchPointForModelTarget(
            ISldWorks swApp,
            View view,
            Sketch sketch,
            HashSet<string> fingerprintsBeforeCreate,
            double[] modelTarget,
            SketchPoint? exclude,
            double modelMatchTolM,
            bool newPointsOnly)
        {
            SketchPoint? best = null;
            double bestErr = double.MaxValue;
            foreach (var sp in CollectSketchPointsFromSketch(sketch))
            {
                if (exclude != null && ReferenceEquals(sp, exclude)) continue;
                try
                {
                    string fp = SketchPointFingerprint(sp);
                    if (newPointsOnly && fingerprintsBeforeCreate.Contains(fp))
                        continue;

                    if (!TrySketchLocalToModelPoint(swApp, view, sketch, new[] { sp.X, sp.Y, sp.Z }, out var pm) ||
                        pm == null)
                        continue;

                    double err = Distance3D(pm, modelTarget);
                    if (err < bestErr && err <= modelMatchTolM)
                    {
                        bestErr = err;
                        best = sp;
                    }
                }
                catch
                {
                    // ignored
                }
            }

            return best;
        }

        static SketchPoint? PickSketchPointForModelTargetWithFallback(
            ISldWorks swApp,
            View view,
            Sketch sketch,
            HashSet<string> fingerprintsBeforeCreate,
            double[] modelTarget,
            SketchPoint? exclude,
            double modelMatchTolM)
        {
            var p = PickSketchPointForModelTarget(
                swApp, view, sketch, fingerprintsBeforeCreate, modelTarget, exclude, modelMatchTolM, newPointsOnly: true);
            if (p != null) return p;
            return PickSketchPointForModelTarget(
                swApp, view, sketch, fingerprintsBeforeCreate, modelTarget, exclude, modelMatchTolM, newPointsOnly: false);
        }

        /// <summary>
        /// 用 CreatePoint 时传入的草图局部坐标匹配点（与 TrySketchLocalToModelPoint 逆变换解耦，避免「点画对但模型校验失败」）。
        /// </summary>
        static SketchPoint? PickSketchPointNearestCreatedLocal(
            Sketch sketch,
            HashSet<string> fingerprintsBeforeCreate,
            double[] sketchTarget3,
            SketchPoint? exclude,
            double absTolM,
            bool newPointsOnly)
        {
            SketchPoint? best = null;
            double bestD2 = double.MaxValue;
            double tol2 = absTolM * absTolM;
            foreach (var sp in CollectSketchPointsFromSketch(sketch))
            {
                if (exclude != null && ReferenceEquals(sp, exclude)) continue;
                try
                {
                    string fp = SketchPointFingerprint(sp);
                    if (newPointsOnly && fingerprintsBeforeCreate.Contains(fp))
                        continue;

                    double dx = sp.X - sketchTarget3[0];
                    double dy = sp.Y - sketchTarget3[1];
                    double dz = sp.Z - sketchTarget3[2];
                    double d2 = dx * dx + dy * dy + dz * dz;
                    double d2xy = dx * dx + dy * dy;
                    bool ok3 = d2 <= tol2;
                    bool okPlanar = d2xy <= tol2 && Math.Abs(dz) <= SketchPointPickZSlackM;
                    if (!ok3 && !okPlanar)
                        continue;

                    double score = ok3 ? d2 : d2xy;
                    if (score < bestD2)
                    {
                        bestD2 = score;
                        best = sp;
                    }
                }
                catch
                {
                    // ignored
                }
            }

            return best;
        }

        /// <summary>退出草图后解析新建点：优先草图坐标，其次模型坐标匹配，最后放宽指纹再试草图坐标。</summary>
        static SketchPoint? ResolveSketchPointAfterCreate(
            ISldWorks swApp,
            View view,
            Sketch sketch,
            HashSet<string> fingerprintsBefore,
            double[] sketchLocalUsedAtCreate,
            double[] modelTarget,
            SketchPoint? exclude,
            double sketchSpaceTolM,
            double modelMatchTolM)
        {
            var bySketch = PickSketchPointNearestCreatedLocal(
                sketch, fingerprintsBefore, sketchLocalUsedAtCreate, exclude, sketchSpaceTolM, newPointsOnly: true);
            if (bySketch != null)
                return bySketch;

            var byModel = PickSketchPointForModelTargetWithFallback(
                swApp, view, sketch, fingerprintsBefore, modelTarget, exclude, modelMatchTolM);
            if (byModel != null)
                return byModel;

            return PickSketchPointNearestCreatedLocal(
                sketch, fingerprintsBefore, sketchLocalUsedAtCreate, exclude, sketchSpaceTolM, newPointsOnly: false);
        }

        /// <summary>工程图视图草图上的点：用 <see cref="SketchPoint.Select4"/>，不能强转为 <see cref="Entity"/>（E_NOINTERFACE）。</summary>
        static void SelectSketchPointInView(SketchPoint sp, bool append, SelectData selData)
        {
            sp.Select4(append, selData);
        }

        /// <summary>先在同一视图草图建两点，退出草图后重取 SketchPoint，再标点点距离。</summary>
        static bool TryCreateTwoSketchPointsThenPointToPointDimension(
            ISldWorks swApp,
            ModelDoc2 swModel,
            View view,
            double[] modelA,
            double[] modelB,
            ref double textStaggerM,
            out string failReason)
        {
            failReason = "";

            ExitDrawingSketchIfActive(swModel);
            swModel.ClearSelection2(true);
            var selMgr = (SelectionMgr)swModel.SelectionManager;

            try
            {
                if (string.IsNullOrEmpty(view.Name) ||
                    !swModel.Extension.SelectByID2(view.Name, "DRAWINGVIEW", 0, 0, 0, false, 0, null, 0))
                {
                    failReason = "无法选中工程图视图以插入草图";
                    return false;
                }

                var fingerprintsBefore = GetSketchPointFingerprints((Sketch)view.GetSketch());

                swModel.SketchManager.InsertSketch(true);

                if (!TryModelPointToDrawingViewSketchLocal(swApp, view, swModel, modelA, out var skA))
                {
                    failReason = "点A模型到草图坐标失败";
                    return false;
                }

                swModel.SketchManager.AddToDB = true;
                object? retA = swModel.SketchManager.CreatePoint(skA[0], skA[1], skA[2]);
                if (retA == null)
                {
                    swModel.SketchManager.AddToDB = false;
                    failReason = "CreatePoint_A失败";
                    return false;
                }

                if (!TryModelPointToDrawingViewSketchLocal(swApp, view, swModel, modelB, out var skB))
                {
                    swModel.SketchManager.AddToDB = false;
                    failReason = "点B模型到草图坐标失败";
                    return false;
                }

                object? retB = swModel.SketchManager.CreatePoint(skB[0], skB[1], skB[2]);
                if (retB == null)
                {
                    swModel.SketchManager.AddToDB = false;
                    failReason = "CreatePoint_B失败";
                    return false;
                }

                swModel.SketchManager.AddToDB = false;

                Sketch? activeSk = null;
                try
                {
                    activeSk = (Sketch)swModel.SketchManager.ActiveSketch;
                }
                catch
                {
                    // ignored
                }

                SketchPoint? spA = ResolveCreatedSketchPointWhileSketchActive(
                    activeSk, fingerprintsBefore, skA, retA, null, SketchPointPickSketchSpaceTolM);
                SketchPoint? spB = spA != null
                    ? ResolveCreatedSketchPointWhileSketchActive(
                        activeSk, fingerprintsBefore, skB, retB, spA, SketchPointPickSketchSpaceTolM)
                    : ResolveCreatedSketchPointWhileSketchActive(
                        activeSk, fingerprintsBefore, skB, retB, null, SketchPointPickSketchSpaceTolM);

                swModel.SketchManager.InsertSketch(false);

                var vSketch = (Sketch)view.GetSketch();
                if (vSketch == null)
                {
                    failReason = "退出草图后无法取视图草图";
                    return false;
                }

                const double modelPickTolM = 0.006;
                if (spA == null)
                {
                    spA = ResolveSketchPointAfterCreate(
                        swApp, view, vSketch, fingerprintsBefore, skA, modelA, null, SketchPointPickSketchSpaceTolM, modelPickTolM);
                }

                if (spB == null)
                {
                    spB = ResolveSketchPointAfterCreate(
                        swApp, view, vSketch, fingerprintsBefore, skB, modelB, spA, SketchPointPickSketchSpaceTolM, modelPickTolM);
                }

                if (spA == null)
                {
                    failReason = "无法识别新建草图点A(草图坐标/模型校验)";
                    return false;
                }

                if (spB == null)
                {
                    failReason = "无法识别新建草图点B(草图坐标/模型校验)";
                    return false;
                }

                swModel.ClearSelection2(true);
                var selData = selMgr.CreateSelectData();
                selData.View = view;
                SelectSketchPointInView(spA, false, selData);
                SelectSketchPointInView(spB, true, selData);

                if (!TryGetDimensionLeaderSheetXYFromTwoModelPoints(swApp, view, modelA, modelB, ref textStaggerM, out var x, out var y))
                {
                    swModel.ClearSelection2(true);
                    failReason = "尺寸放置点(图纸坐标)计算失败";
                    return false;
                }

                DisplayDimension? added = swModel.AddDimension2(x, y, 0) as DisplayDimension;
                if (added == null)
                {
                    swModel.ClearSelection2(true);
                    SelectSketchPointInView(spB, false, selData);
                    SelectSketchPointInView(spA, true, selData);
                    added = swModel.AddDimension2(x, y, 0) as DisplayDimension;
                }

                swModel.ClearSelection2(true);
                if (added == null)
                {
                    failReason = "AddDimension2返回空";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                failReason = ex.Message;
                return false;
            }
            finally
            {
                ExitDrawingSketchIfActive(swModel);
            }
        }

        /// <summary>
        /// 两面不平行时：在视图草图中建「内圆弧中点」（圆边上的参数中点），退出草图后再选点与二级可见边并 AddDimension2（避免留在草图模式导致其它标注失效）。
        /// </summary>
        static bool TryAddInnerArcCenterPointToSecondaryEdgeDimension(
            ISldWorks swApp,
            ModelDoc2 swModel,
            View view,
            BendNode node,
            Face firstFace,
            Edge visEdge2,
            double[] xformData,
            ref double textStaggerM,
            out string failReason)
        {
            failReason = "";
            if (node.InnerCylinderFace == null)
            {
                failReason = "无内圆柱面";
                return false;
            }

            if (BendNodeSkipsSketchPointDimensions(node))
            {
                failReason = "折弯角约90°跳过草图点画点标注";
                return false;
            }

            if (!TryGetInnerBendArcMidpointModel(node.InnerCylinderFace, out var modelCenter) || modelCenter == null)
            {
                failReason = "内弧中点计算失败";
                return false;
            }

            string placement = GetPlacement(firstFace, xformData);
            if (placement == "none")
            {
                failReason = "放置方向无效";
                return false;
            }

            ExitDrawingSketchIfActive(swModel);
            swModel.ClearSelection2(true);

            var selMgr = (SelectionMgr)swModel.SelectionManager;

            try
            {
                // 当前互操作里 IView 无 Select(append, SelData)；与 new_drw 一致用 DRAWINGVIEW 按名选中
                if (string.IsNullOrEmpty(view.Name) ||
                    !swModel.Extension.SelectByID2(view.Name, "DRAWINGVIEW", 0, 0, 0, false, 0, null, 0))
                {
                    failReason = "无法选中工程图视图以插入草图";
                    return false;
                }

                var fingerprintsBefore = GetSketchPointFingerprints((Sketch)view.GetSketch());

                swModel.SketchManager.InsertSketch(true);

                if (!TryModelPointToDrawingViewSketchLocal(swApp, view, swModel, modelCenter, out var skCoords))
                {
                    failReason = "模型点到视图草图局部坐标变换失败";
                    return false;
                }

                swModel.SketchManager.AddToDB = true;
                object? retPt = swModel.SketchManager.CreatePoint(skCoords[0], skCoords[1], skCoords[2]);
                if (retPt == null)
                {
                    swModel.SketchManager.AddToDB = false;
                    failReason = "CreatePoint失败";
                    return false;
                }

                swModel.SketchManager.AddToDB = false;

                Sketch? activeSk = null;
                try
                {
                    activeSk = (Sketch)swModel.SketchManager.ActiveSketch;
                }
                catch
                {
                    // ignored
                }

                SketchPoint? skPointResolved = ResolveCreatedSketchPointWhileSketchActive(
                    activeSk, fingerprintsBefore, skCoords, retPt, null, SketchPointPickSketchSpaceTolM);

                // 必须先退出草图编辑，再在图纸环境下选边与 AddDimension2；否则易残留草图模式，后续所有标注失败
                swModel.SketchManager.InsertSketch(false);

                var vSketch = (Sketch)view.GetSketch();
                if (vSketch == null)
                {
                    failReason = "退出草图后无法取视图草图";
                    return false;
                }

                const double modelPickTolM = 0.006;
                if (skPointResolved == null)
                {
                    skPointResolved = ResolveSketchPointAfterCreate(
                        swApp, view, vSketch, fingerprintsBefore, skCoords, modelCenter, null, SketchPointPickSketchSpaceTolM, modelPickTolM);
                }

                if (skPointResolved == null)
                {
                    failReason = "无法识别新建草图点(与边标注·草图坐标/模型校验)";
                    return false;
                }

                swModel.ClearSelection2(true);
                var selData = selMgr.CreateSelectData();
                selData.View = view;

                SelectSketchPointInView(skPointResolved, false, selData);
                ((Entity)visEdge2).Select4(true, selData);

                if (!TryGetEdgeMidpointModel(visEdge2, out var edgeMid) || edgeMid == null ||
                    !TryGetDimensionLeaderSheetXYFromTwoModelPoints(swApp, view, modelCenter, edgeMid, ref textStaggerM, out var x, out var y))
                {
                    swModel.ClearSelection2(true);
                    failReason = "尺寸放置点(图纸坐标)计算失败";
                    return false;
                }

                DisplayDimension? addedDimension = swModel.AddDimension2(x, y, 0) as DisplayDimension;
                if (addedDimension == null)
                {
                    swModel.ClearSelection2(true);
                    ((Entity)visEdge2).Select4(false, selData);
                    SelectSketchPointInView(skPointResolved, true, selData);
                    addedDimension = swModel.AddDimension2(x, y, 0) as DisplayDimension;
                }

                swModel.ClearSelection2(true);
                if (addedDimension == null)
                {
                    failReason = "AddDimension2返回空";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                failReason = ex.Message;
                return false;
            }
            finally
            {
                ExitDrawingSketchIfActive(swModel);
            }
        }

        static bool TryAddDimension(ISldWorks swApp, ModelDoc2 swModel, View view, double[] xformData, Face face1, Face face2, double[] axis, string level, ref double textStaggerM, out string failReason)
        {
            failReason = "";
            ExitDrawingSketchIfActive(swModel);

            string placement = GetPlacement(face1, xformData);
            if (placement == "none")
            {
                failReason = "放置方向无效";
                return false;
            }

            if (!ArePlanesParallelWithinTolerance(face1, face2, ParallelPlaneToleranceDeg))
            {
                failReason = "两面非法向平行";
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
                visEdge1 = FindVisibleEdge(swApp, view, e1);
                if (visEdge1 != null) break;
            }
            if (visEdge1 == null)
            {
                failReason = "一级面边不可见";
                return false;
            }

            foreach (var e2 in edgeCandidates2)
            {
                visEdge2 = FindVisibleEdge(swApp, view, e2);
                if (visEdge2 != null) break;
            }
            if (visEdge2 == null)
            {
                failReason = "二级面边不可见";
                return false;
            }

            if (IsAbout90DegreesBetweenPlanes(face1, face2, RightAngleSkipToleranceDeg))
            {
                failReason = "两面夹角约90°不标注";
                return false;
            }

            var selMgr = (SelectionMgr)swModel.SelectionManager;
            var selData = selMgr.CreateSelectData();
            selData.View = view;
            ((Entity)visEdge1).Select4(true, selData);
            ((Entity)visEdge2).Select4(true, selData);

            if (!TryGetEdgeMidpointModel(visEdge1, out var mid1) || mid1 == null ||
                !TryGetEdgeMidpointModel(visEdge2, out var mid2) || mid2 == null ||
                !TryGetDimensionLeaderSheetXYFromTwoModelPoints(swApp, view, mid1, mid2, ref textStaggerM, out var x, out var y))
            {
                swModel.ClearSelection2(true);
                failReason = "尺寸放置点(图纸坐标)计算失败";
                return false;
            }

            var addedDimension = swModel.AddDimension2(x, y, 0);
            if (addedDimension == null)
            {
                swModel.ClearSelection2(true);
                failReason = "AddDimension2返回空";
                return false;
            }

            swModel.ClearSelection2(true);
            return true;
        }

        static void AddReason(Dictionary<string, int> reasonStats, string reason, string? context = null)
        {
            if (string.IsNullOrWhiteSpace(reason)) return;
            var key = string.IsNullOrEmpty(context) ? reason : $"{reason} · {context}";
            if (!reasonStats.TryGetValue(key, out var count))
                reasonStats[key] = 1;
            else
                reasonStats[key] = count + 1;
        }

        /// <summary>
        /// 去掉「 · 」后的折弯/边上下文，用于汇总行与「原因类型」一致时的合计。
        /// </summary>
        static string ReasonBaseKey(string fullKey)
        {
            const string sep = " · ";
            int i = fullKey.IndexOf(sep, StringComparison.Ordinal);
            return i < 0 ? fullKey : fullKey.Substring(0, i);
        }

        /// <summary>
        /// 与 bend_graph.json 相同：当前加载的插件 DLL 所在目录（SolidWorks 可能未从 bin\Debug 加载）。
        /// </summary>
        static string GetBendDimPluginDirectory()
        {
            try
            {
                var loc = typeof(benddim).Assembly.Location;
                if (!string.IsNullOrEmpty(loc))
                {
                    var dir = Path.GetDirectoryName(loc);
                    if (!string.IsNullOrEmpty(dir)) return dir;
                }
            }
            catch
            {
                // ignored
            }

            return string.IsNullOrEmpty(AppContext.BaseDirectory) ? "." : AppContext.BaseDirectory;
        }

        static void PrintReasonStats(Dictionary<string, int> reasonStats)
        {
            var sb = new StringBuilder();
            void Emit(string line = "")
            {
                sb.AppendLine(line);
                Console.WriteLine(line);
            }

            Emit($"[benddim 原因统计 v2] {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            Emit("若下面明细行没有「·」及折弯名/边信息，说明 SW 仍在使用旧版 DLL；请重新编译 sw_plugin 后完全退出并重启 SolidWorks。");
            Emit($"当前程序集: {typeof(benddim).Assembly.Location}");
            Emit();

            Emit("标注原因统计（明细，「·」后为折弯或节点边）：");
            if (reasonStats.Count == 0)
            {
                Emit("  无统计数据");
            }
            else
            {
                foreach (var item in reasonStats.OrderByDescending(x => x.Value).ThenBy(x => x.Key))
                    Emit($"  {item.Key}: {item.Value}");

                Emit();
                Emit("按原因类型汇总（合并相同原因、忽略折弯/边上下文）：");
                foreach (var g in reasonStats
                             .GroupBy(kv => ReasonBaseKey(kv.Key))
                             .Select(x => (Key: x.Key, Sum: x.Sum(p => p.Value), Lines: x.Count()))
                             .OrderByDescending(x => x.Sum)
                             .ThenBy(x => x.Key))
                {
                    var spread = g.Lines > 1 ? $"，分布于 {g.Lines} 条明细" : "";
                    Emit($"  {g.Key}: {g.Sum} 次{spread}");
                }
            }

            var logPath = Path.Combine(GetBendDimPluginDirectory(), "benddim_reason_stats.log");
            Emit();
            Emit($"[benddim] 本日志路径: {logPath}");
            try
            {
                File.WriteAllText(logPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            catch (Exception ex)
            {
                Emit($"[benddim] 写入原因统计文件失败: {ex.Message}");
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

        /// <summary>节点内一级—二级候选排序：距离远者优先便于排版（一级面间已不在此列表，见 ProcessBendNode 独立阶段）。</summary>
        static int CompareNodeDimensionCandidates(
            (Face secFace, string level, string pairKey, double dist, Face sourceFirstFace) a,
            (Face secFace, string level, string pairKey, double dist, Face sourceFirstFace) b)
        {
            bool aFf = a.level == "一级面间";
            bool bFf = b.level == "一级面间";
            if (aFf != bFf)
                return aFf ? 1 : -1;
            if (!aFf)
                return b.dist.CompareTo(a.dist);
            return a.dist.CompareTo(b.dist);
        }

        /// <summary>
        /// 外圆柱一级面是否与内圆柱相邻（内圆弧侧一级面）。
        /// </summary>
        static bool IsInnerCylinderAdjacentFirstLevel(BendNode node, Face firstLevelFace) =>
            node.InnerCylinderFace != null && FacesIntersect(firstLevelFace, node.InnerCylinderFace);

        /// <summary>
        /// 若另一外圆柱一级面已与「来自不同内圆弧一级面」的二级面完成边—边一级—二级标注（且该二级面未再走内弧点路径），
        /// 则当前外一级面与对侧二级面不再走内弧中点—边，避免与对称侧已标尺寸重复。
        /// </summary>
        static bool ShouldSkipInnerArcBecauseMirrorOuterHasEdgeEdgeFirstSecondary(
            BendNode node,
            Face currentOuterFirst,
            Face currentCandSourceInner,
            List<Face> firstLevelFaces,
            List<(Face face, string level, Face sourceFirstFace)> secondaryFaces,
            HashSet<string> dimensionedPairs)
        {
            if (node.InnerCylinderFace == null) return false;
            if (IsInnerCylinderAdjacentFirstLevel(node, currentOuterFirst)) return false;

            foreach (var fo in firstLevelFaces)
            {
                if (ReferenceEquals(fo, currentOuterFirst)) continue;
                if (IsInnerCylinderAdjacentFirstLevel(node, fo)) continue;

                foreach (var item in secondaryFaces)
                {
                    if (ReferenceEquals(item.sourceFirstFace, fo)) continue;

                    var pk = GetFacePairKey(fo, item.face, item.level);
                    if (!dimensionedPairs.Contains(pk)) continue;

                    var arcK = GetFacePairKey(node.InnerCylinderFace, item.face, InnerArcPointSecondaryDedupeLevel);
                    if (dimensionedPairs.Contains(arcK)) continue;

                    if (!ReferenceEquals(item.sourceFirstFace, currentCandSourceInner))
                        return true;
                }
            }

            return false;
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

        /// <summary>
        /// 折弯主圆柱轴线是否垂直于当前工程图视图所在平面（与视线/投影方向平行）。
        /// 与 <see cref="GetPlacement"/> 一致：取 <see cref="View.ModelToViewTransform"/> 旋转子矩阵第三行作为视图平面法向在模型中的方向。
        /// </summary>
        static bool IsBendAxisNormalToDrawingViewPlane(double[] bendAxis, double[] modelToViewArrayData)
        {
            if (bendAxis == null || modelToViewArrayData == null || modelToViewArrayData.Length < 9)
                return false;
            var planeN = new[] { modelToViewArrayData[6], modelToViewArrayData[7], modelToViewArrayData[8] };
            if (NormalizeVector3(planeN) == null)
                return false;
            return IsParallel(bendAxis, planeN);
        }

        static double[]? NormalizeVector3(double[] v)
        {
            double m = Math.Sqrt(v[0] * v[0] + v[1] * v[1] + v[2] * v[2]);
            if (m < 1e-12) return null;
            return new[] { v[0] / m, v[1] / m, v[2] / m };
        }

        /// <summary>
        /// 两内圆弧一级面边—边专用：只保留与折弯轴线**明显不平行**的直棱，并按与轴线夹角从「更接近垂直」优先排序（再按长度），避免优先选沿折弯方向的长边。
        /// </summary>
        static List<Edge> GetEdgesForInnerInnerDimension(Face face, double[] axis, string faceLevel)
        {
            var candidates = new List<(Edge edge, double len, double absCos)>();
            var axisU = NormalizeVector3(axis);
            if (axisU == null)
                return GetEdgesForDimension(face, axis, faceLevel, false);

            foreach (Edge e in (object[])face.GetEdges())
            {
                var c = (Curve)e.GetCurve();
                if (!c.IsLine()) continue;
                var lineParams = (double[])c.LineParams;
                var edgeDir = new[] { lineParams[3], lineParams[4], lineParams[5] };
                var edgeU = NormalizeVector3(edgeDir);
                if (edgeU == null) continue;

                double absCos = Math.Abs(edgeU[0] * axisU[0] + edgeU[1] * axisU[1] + edgeU[2] * axisU[2]);
                if (absCos > InnerInnerEdgeMaxAbsCosAxis)
                    continue;

                c.GetEndParams(out var t0, out var t1, out _, out _);
                double len = Math.Abs(t1 - t0);
                candidates.Add((e, len, absCos));
            }

            candidates.Sort((a, b) =>
            {
                int c = a.absCos.CompareTo(b.absCos);
                return c != 0 ? c : b.len.CompareTo(a.len);
            });

            var label = string.IsNullOrEmpty(faceLevel) ? "" : $"[{faceLevel}] ";
            foreach (var item in candidates)
                Console.WriteLine($"  {label}候选标注边(内一级面间·与轴非平行优先)，长度: {item.len * 1000:F2} mm");

            if (candidates.Count == 0)
                Console.WriteLine($"  {label}未找到与折弯轴线足够偏离的直边");

            return candidates.Select(x => x.edge).ToList();
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
                    string nameA = e.NodeA >= 0 && e.NodeA < nodes.Count ? (nodes[e.NodeA].BendFeature?.Name ?? "") : "";
                    string nameB = e.NodeB >= 0 && e.NodeB < nodes.Count ? (nodes[e.NodeB].BendFeature?.Name ?? "") : "";
                    var edgeObj = new
                    {
                        nodeA = e.NodeA,
                        nodeB = e.NodeB,
                        featureNameA = nameA,
                        featureNameB = nameB,
                        connectedFaceAAreaMm2 = e.ConnectedFirstFaceA != null ? Math.Round(e.ConnectedFirstFaceA.GetArea() * 1000000, 3) : 0,
                        connectedFaceBAreaMm2 = e.ConnectedFirstFaceB != null ? Math.Round(e.ConnectedFirstFaceB.GetArea() * 1000000, 3) : 0
                    };
                    dump.Edges.Add(edgeObj);
                }

                // SolidWorks 宿主下勿用硬编码源码路径；与 new_drw 一致，写到当前程序集所在目录
                var outPath = Path.Combine(GetBendDimPluginDirectory(), "bend_graph.json");
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

                    if (!allowParallelEdges && IsParallel(edgeDir, axis))
                        continue;

                    double startParam = 0, endParam = 0;
                    bool isClosed = false, isPeriodic = false;
                    c.GetEndParams(out startParam, out endParam, out isClosed, out isPeriodic);
                    double len = Math.Abs(endParam - startParam);

                    candidates.Add((e, len));
                }
            }

            candidates.Sort((a, b) => b.len.CompareTo(a.len));

            var label = string.IsNullOrEmpty(faceLevel) ? "" : $"[{faceLevel}] ";
            foreach (var item in candidates)
                Console.WriteLine($"  {label}候选标注边，长度: {item.len * 1000:F2} mm");

            if (candidates.Count == 0)
                Console.WriteLine($"  {label}未找到不平行于轴线的直边");

            return candidates.Select(x => x.edge).ToList();
        }

        /// <summary>
        /// 在视图中查找3D边对应的最佳可见边：仅用严格模型空间判据（端点或共线段），不用单独视图投影匹配；长度差与端点容差见 VisibleEdge* 常量。
        /// </summary>
        static Edge FindVisibleEdge(ISldWorks swApp, View view, Edge modelEdge)
        {
            var startVert = (Vertex)modelEdge.GetStartVertex();
            var endVert = (Vertex)modelEdge.GetEndVertex();
            if (startVert == null || endVert == null) return null;

            var edgeStart = (double[])startVert.GetPoint();
            var edgeEnd = (double[])endVert.GetPoint();
            if (edgeStart == null || edgeEnd == null) return null;

            double lenModel = Distance3D(edgeStart, edgeEnd);
            double lenTol = lenModel > 1e-4
                ? Math.Max(VisibleEdgeLengthAbsFloorM, lenModel * VisibleEdgeLengthRelTol)
                : VisibleEdgeLengthAbsFloorM;

            var visibleComps = (object[])view.GetVisibleComponents();
            if (visibleComps == null) return null;

            Edge best = null;
            double bestScore = double.MaxValue;
            double tol = VisibleEdgeEndpointTolM;

            foreach (Component2 comp in visibleComps)
            {
                if (comp == null) continue;

                var visibleEdges = (object[])view.GetVisibleEntities(
                    comp, (int)swViewEntityType_e.swViewEntityType_Edge);
                if (visibleEdges == null) continue;

                foreach (object obj in visibleEdges)
                {
                    if (obj == null) continue;
                    if (!(obj is Edge visEdge)) continue;

                    try
                    {
                        if (Object.ReferenceEquals(visEdge, modelEdge))
                            return visEdge;

                        var meStartVert = (Vertex)visEdge.GetStartVertex();
                        var meEndVert = (Vertex)visEdge.GetEndVertex();
                        if (meStartVert == null || meEndVert == null) continue;

                        var meStart = (double[])meStartVert.GetPoint();
                        var meEnd = (double[])meEndVert.GetPoint();
                        if (meStart == null || meEnd == null) continue;

                        double lenVis = Distance3D(meStart, meEnd);
                        if (Math.Abs(lenModel - lenVis) > lenTol)
                            continue;

                        bool endpointsMatch =
                            (PointsWithinDistance(edgeStart, meStart, tol) && PointsWithinDistance(edgeEnd, meEnd, tol))
                            ||
                            (PointsWithinDistance(edgeStart, meEnd, tol) && PointsWithinDistance(edgeEnd, meStart, tol));

                        bool segmentMatch = SegmentsLikelyCoincidentVisible(edgeStart, edgeEnd, meStart, meEnd, tol);

                        if (!endpointsMatch && !segmentMatch)
                            continue;

                        double d0 = Distance3D(edgeStart, meStart) + Distance3D(edgeEnd, meEnd);
                        double d1 = Distance3D(edgeStart, meEnd) + Distance3D(edgeEnd, meStart);
                        double score = Math.Min(d0, d1);
                        if (segmentMatch && !endpointsMatch)
                            score *= 0.92;

                        if (score < bestScore)
                        {
                            bestScore = score;
                            best = visEdge;
                        }
                    }
                    catch
                    {
                        // continue
                    }
                }
            }

            if (best != null) return best;
            if (lenModel > 1e-5)
            {
                var subset = FindVisibleEdgeColinearSubset(swApp, view, edgeStart, edgeEnd, lenModel);
                if (subset != null) return subset;
            }

            if (lenModel > 1e-6 && lenModel <= VisibleEdgeShortStubMaxLenM)
            {
                var stub = FindVisibleEdgeShortStubLoose(swApp, view, edgeStart, edgeEnd, lenModel);
                if (stub != null) return stub;
            }

            if (lenModel > VisibleEdgeShortStubMaxLenM && lenModel <= VisibleEdgeBendChunkMaxLenM)
            {
                var bend = FindVisibleEdgeBendChunkLoose(swApp, view, edgeStart, edgeEnd, lenModel);
                if (bend != null) return bend;
            }

            if (BendDimLogVisibleEdgesOnFindVisibleEdgeFail &&
                _bendDimVisibleEdgeFailDumpCount < BendDimLogVisibleEdgesMaxDumpsPerRun)
            {
                _bendDimVisibleEdgeFailDumpCount++;
                LogVisibleEdgesForViewOnMatchFail(view, edgeStart, edgeEnd, lenModel);
            }
            else if (BendDimLogVisibleEdgesOnFindVisibleEdgeFail)
            {
                Console.WriteLine(
                    $"[benddim 可见边] 未匹配（略）：L={lenModel * 1000:F3} mm，端点(mm) " +
                    $"({edgeStart[0] * 1000:F1},{edgeStart[1] * 1000:F1},{edgeStart[2] * 1000:F1})→" +
                    $"({edgeEnd[0] * 1000:F1},{edgeEnd[1] * 1000:F1},{edgeEnd[2] * 1000:F1})（完整清单仅打印一次，不再重复）");
            }

            return null;
        }

        /// <summary>
        /// 极短直棱：展开图与 BREP 可有约 1～2mm 错层，严格端点失败时用「等长+同向+中点近」匹配可见边。
        /// </summary>
        static Edge FindVisibleEdgeShortStubLoose(
            ISldWorks swApp,
            View view,
            double[] m0,
            double[] m1,
            double lenModel)
        {
            var u = NormalizeVector3(new[] { m1[0] - m0[0], m1[1] - m0[1], m1[2] - m0[2] });
            if (u == null) return null;

            double midM0 = (m0[0] + m1[0]) * 0.5;
            double midM1 = (m0[1] + m1[1]) * 0.5;
            double midM2 = (m0[2] + m1[2]) * 0.5;
            double lenTol = Math.Max(
                VisibleEdgeLengthAbsFloorM,
                VisibleEdgeShortStubLengthExtraM + lenModel * 0.08);

            var visibleComps = (object[])view.GetVisibleComponents();
            if (visibleComps == null) return null;

            Edge best = null;
            double bestMidDist = double.MaxValue;

            foreach (Component2 comp in visibleComps)
            {
                if (comp == null) continue;
                var visibleEdges = (object[])view.GetVisibleEntities(
                    comp, (int)swViewEntityType_e.swViewEntityType_Edge);
                if (visibleEdges == null) continue;

                foreach (object obj in visibleEdges)
                {
                    if (obj == null || !(obj is Edge visEdge)) continue;
                    try
                    {
                        var sv = (Vertex)visEdge.GetStartVertex();
                        var ev = (Vertex)visEdge.GetEndVertex();
                        if (sv == null || ev == null) continue;
                        var v0 = (double[])sv.GetPoint();
                        var v1 = (double[])ev.GetPoint();
                        if (v0 == null || v1 == null || v0.Length < 3 || v1.Length < 3) continue;
                        var c = (Curve)visEdge.GetCurve();
                        if (c == null || !c.IsLine()) continue;

                        double lenVis = Distance3D(v0, v1);
                        if (Math.Abs(lenVis - lenModel) > lenTol) continue;

                        var w = NormalizeVector3(new[] { v1[0] - v0[0], v1[1] - v0[1], v1[2] - v0[2] });
                        if (w == null) continue;

                        double align = Math.Abs(u[0] * w[0] + u[1] * w[1] + u[2] * w[2]);
                        if (align < 0.97) continue;

                        double midV0 = (v0[0] + v1[0]) * 0.5;
                        double midV1 = (v0[1] + v1[1]) * 0.5;
                        double midV2 = (v0[2] + v1[2]) * 0.5;
                        double midDist = Distance3D(
                            new[] { midM0, midM1, midM2 },
                            new[] { midV0, midV1, midV2 });
                        if (midDist > VisibleEdgeShortStubMidpointMaxM) continue;

                        if (midDist < bestMidDist)
                        {
                            bestMidDist = midDist;
                            best = visEdge;
                        }
                    }
                    catch
                    {
                        // skip
                    }
                }
            }

            return best;
        }

        /// <summary>
        /// 折弯附近中等长度直棱：模型与展开图边长可差数毫米（用户口头的 21/23 mm 即约 22.94、23.94），
        /// 在严格等长失败后，用「同向 + 到对方直线距离 + 放宽长度差」匹配可见边。
        /// </summary>
        static Edge FindVisibleEdgeBendChunkLoose(
            ISldWorks swApp,
            View view,
            double[] m0,
            double[] m1,
            double lenModel)
        {
            var u = NormalizeVector3(new[] { m1[0] - m0[0], m1[1] - m0[1], m1[2] - m0[2] });
            if (u == null) return null;

            double lenTol = Math.Max(
                VisibleEdgeBendChunkLenTolFloorM,
                lenModel * VisibleEdgeBendChunkLenTolPerLen);
            double lineTol = Math.Max(
                VisibleEdgeBendChunkLineDistFloorM,
                lenModel * VisibleEdgeBendChunkLineDistPerLen);

            var visibleComps = (object[])view.GetVisibleComponents();
            if (visibleComps == null) return null;

            Edge best = null;
            double bestScore = double.MaxValue;

            foreach (Component2 comp in visibleComps)
            {
                if (comp == null) continue;
                var visibleEdges = (object[])view.GetVisibleEntities(
                    comp, (int)swViewEntityType_e.swViewEntityType_Edge);
                if (visibleEdges == null) continue;

                foreach (object obj in visibleEdges)
                {
                    if (obj == null || !(obj is Edge visEdge)) continue;
                    try
                    {
                        var sv = (Vertex)visEdge.GetStartVertex();
                        var ev = (Vertex)visEdge.GetEndVertex();
                        if (sv == null || ev == null) continue;
                        var v0 = (double[])sv.GetPoint();
                        var v1 = (double[])ev.GetPoint();
                        if (v0 == null || v1 == null || v0.Length < 3 || v1.Length < 3) continue;
                        var c = (Curve)visEdge.GetCurve();
                        if (c == null || !c.IsLine()) continue;

                        double lenVis = Distance3D(v0, v1);
                        if (Math.Abs(lenVis - lenModel) > lenTol) continue;

                        var w = NormalizeVector3(new[] { v1[0] - v0[0], v1[1] - v0[1], v1[2] - v0[2] });
                        if (w == null) continue;

                        double align = Math.Abs(u[0] * w[0] + u[1] * w[1] + u[2] * w[2]);
                        if (align < VisibleEdgeBendChunkMinAlignAbsCos) continue;

                        double dM0 = PointToLineDistance3D(m0, v0, v1);
                        double dM1 = PointToLineDistance3D(m1, v0, v1);
                        double dV0 = PointToLineDistance3D(v0, m0, m1);
                        double dV1 = PointToLineDistance3D(v1, m0, m1);
                        double lineSpread = Math.Max(Math.Max(dM0, dM1), Math.Max(dV0, dV1));
                        if (lineSpread > lineTol) continue;

                        double score = lineSpread + Math.Abs(lenVis - lenModel) * 0.35;
                        if (score < bestScore)
                        {
                            bestScore = score;
                            best = visEdge;
                        }
                    }
                    catch
                    {
                        // skip
                    }
                }
            }

            return best;
        }

        /// <summary>新一次折弯标注入口前可调用，使可见边调试计数归零。</summary>
        public static void ResetBendDimVisibleEdgeDebugDumpCount()
        {
            _bendDimVisibleEdgeFailDumpCount = 0;
        }

        /// <summary>
        /// FindVisibleEdge 失败时：打印当前视图全部可见直边（模型空间端点、长度），按长度降序取前若干条。
        /// </summary>
        static void LogVisibleEdgesForViewOnMatchFail(View view, double[] modelP0, double[] modelP1, double lenModelM)
        {
            try
            {
                var rows = new List<(double lenMm, string text)>();
                int total = 0;
                var visibleComps = (object[])view.GetVisibleComponents();
                if (visibleComps == null)
                {
                    Console.WriteLine("[benddim 可见边] GetVisibleComponents 为空，无法列举可见边。");
                    return;
                }

                foreach (Component2 comp in visibleComps)
                {
                    if (comp == null) continue;
                    var visibleEdges = (object[])view.GetVisibleEntities(
                        comp, (int)swViewEntityType_e.swViewEntityType_Edge);
                    if (visibleEdges == null) continue;

                    foreach (object obj in visibleEdges)
                    {
                        if (obj == null || !(obj is Edge visEdge)) continue;
                        try
                        {
                            var sv = (Vertex)visEdge.GetStartVertex();
                            var ev = (Vertex)visEdge.GetEndVertex();
                            if (sv == null || ev == null) continue;
                            var v0 = (double[])sv.GetPoint();
                            var v1 = (double[])ev.GetPoint();
                            if (v0 == null || v1 == null || v0.Length < 3 || v1.Length < 3) continue;
                            var c = (Curve)visEdge.GetCurve();
                            if (c == null || !c.IsLine()) continue;

                            double lenM = Distance3D(v0, v1);
                            total++;
                            string line =
                                $"L={lenM * 1000:F2} mm  " +
                                $"({v0[0] * 1000:F2},{v0[1] * 1000:F2},{v0[2] * 1000:F2})→" +
                                $"({v1[0] * 1000:F2},{v1[1] * 1000:F2},{v1[2] * 1000:F2})";
                            rows.Add((lenM * 1000, line));
                        }
                        catch
                        {
                            // skip
                        }
                    }
                }

                rows.Sort((a, b) => b.lenMm.CompareTo(a.lenMm));

                Console.WriteLine(
                    $"[benddim 可见边] 未匹配到模型边：长度={lenModelM * 1000:F3} mm，" +
                    $"端点(mm) ({modelP0[0] * 1000:F2},{modelP0[1] * 1000:F2},{modelP0[2] * 1000:F2})→" +
                    $"({modelP1[0] * 1000:F2},{modelP1[1] * 1000:F2},{modelP1[2] * 1000:F2})");
                Console.WriteLine(
                    $"[benddim 可见边] 当前视图直可见边共 {total} 条（仅直线），下列按长度降序最多 {BendDimLogVisibleEdgesMaxLines} 条（本命令仅完整列举一次）：");

                int n = Math.Min(rows.Count, BendDimLogVisibleEdgesMaxLines);
                for (int i = 0; i < n; i++)
                    Console.WriteLine($"  [{i + 1}] {rows[i].text}");

                if (rows.Count > BendDimLogVisibleEdgesMaxLines)
                    Console.WriteLine($"  … 另有 {rows.Count - BendDimLogVisibleEdgesMaxLines} 条未列出。");
                Console.WriteLine("[benddim 可见边] 后续其它模型边若仍匹配失败，仅打一行摘要，不再重复整表。");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[benddim 可见边] 列举失败: " + ex.Message);
            }
        }

        /// <summary>
        /// 模型长棱在展开图中常被拆成多段较短可见边；严格等长匹配失败后，
        /// 取与模型棱共线且其投影落在模型线段上并有足够重叠的可见边（优先更长重叠）。
        /// </summary>
        static Edge FindVisibleEdgeColinearSubset(
            ISldWorks swApp,
            View view,
            double[] m0,
            double[] m1,
            double lenModel)
        {
            var axisU = NormalizeVector3(new[] { m1[0] - m0[0], m1[1] - m0[1], m1[2] - m0[2] });
            if (axisU == null || lenModel < 1e-6) return null;

            double tolDist = Math.Max(VisibleEdgeColinearSubsetLineDistM, VisibleEdgeEndpointTolM * 3.0);
            double tolAlong = Math.Max(VisibleEdgeColinearSubsetAlongTolM, lenModel * 0.002);
            double minOverlap = Math.Max(
                VisibleEdgeColinearSubsetOverlapFloorM,
                lenModel * VisibleEdgeColinearSubsetOverlapFracLen);

            var visibleComps = (object[])view.GetVisibleComponents();
            if (visibleComps == null) return null;

            Edge best = null;
            double bestOverlap = -1;
            double bestLineDist = double.MaxValue;

            foreach (Component2 comp in visibleComps)
            {
                if (comp == null) continue;

                var visibleEdges = (object[])view.GetVisibleEntities(
                    comp, (int)swViewEntityType_e.swViewEntityType_Edge);
                if (visibleEdges == null) continue;

                foreach (object obj in visibleEdges)
                {
                    if (obj == null || !(obj is Edge visEdge)) continue;

                    try
                    {
                        var sv = (Vertex)visEdge.GetStartVertex();
                        var ev = (Vertex)visEdge.GetEndVertex();
                        if (sv == null || ev == null) continue;

                        var v0 = (double[])sv.GetPoint();
                        var v1 = (double[])ev.GetPoint();
                        if (v0 == null || v1 == null || v0.Length < 3 || v1.Length < 3) continue;

                        double lenVis = Distance3D(v0, v1);
                        if (lenVis < 1e-5) continue;

                        var dirVis = NormalizeVector3(new[] { v1[0] - v0[0], v1[1] - v0[1], v1[2] - v0[2] });
                        if (dirVis == null) continue;

                        double align = Math.Abs(
                            dirVis[0] * axisU[0] + dirVis[1] * axisU[1] + dirVis[2] * axisU[2]);
                        if (align < 0.985) continue;

                        double d0 = PointToLineDistance3D(v0, m0, m1);
                        double d1 = PointToLineDistance3D(v1, m0, m1);
                        double lineDist = Math.Max(d0, d1);
                        if (lineDist > tolDist) continue;

                        double t0 =
                            (v0[0] - m0[0]) * axisU[0] + (v0[1] - m0[1]) * axisU[1] + (v0[2] - m0[2]) * axisU[2];
                        double t1 =
                            (v1[0] - m0[0]) * axisU[0] + (v1[1] - m0[1]) * axisU[1] + (v1[2] - m0[2]) * axisU[2];
                        double ta = Math.Min(t0, t1);
                        double tb = Math.Max(t0, t1);

                        if (tb < -tolAlong || ta > lenModel + tolAlong) continue;

                        double oa = Math.Max(0, ta);
                        double ob = Math.Min(lenModel, tb);
                        double overlap = ob - oa;
                        if (overlap < minOverlap) continue;

                        if (overlap > bestOverlap + 1e-6 ||
                            (Math.Abs(overlap - bestOverlap) < 1e-6 && lineDist < bestLineDist))
                        {
                            bestOverlap = overlap;
                            bestLineDist = lineDist;
                            best = visEdge;
                        }
                    }
                    catch
                    {
                        // continue
                    }
                }
            }

            return best;
        }

        /// <summary>FindVisibleEdge 用：比 SegmentsLikelyCoincident 更严的共线、等长与中点对齐。</summary>
        static bool SegmentsLikelyCoincidentVisible(double[] a0, double[] a1, double[] b0, double[] b1, double tol)
        {
            double la = Distance3D(a0, a1);
            double lb = Distance3D(b0, b1);
            if (la < 1e-9 || lb < 1e-9) return false;

            double lenTol = Math.Max(tol * 1.2, Math.Max(la, lb) * 0.004);
            if (Math.Abs(la - lb) > lenTol) return false;

            double dA0 = PointToLineDistance3D(a0, b0, b1);
            double dA1 = PointToLineDistance3D(a1, b0, b1);
            double dB0 = PointToLineDistance3D(b0, a0, a1);
            double dB1 = PointToLineDistance3D(b1, a0, a1);
            double maxLineDist = Math.Max(Math.Max(dA0, dA1), Math.Max(dB0, dB1));
            if (maxLineDist > tol * 1.1) return false;

            double[] ma = { (a0[0] + a1[0]) * 0.5, (a0[1] + a1[1]) * 0.5, (a0[2] + a1[2]) * 0.5 };
            double[] mb = { (b0[0] + b1[0]) * 0.5, (b0[1] + b1[1]) * 0.5, (b0[2] + b1[2]) * 0.5 };
            return Distance3D(ma, mb) <= tol * 1.4;
        }

        static bool PointsWithinDistance(double[] p1, double[] p2, double maxDist)
        {
            if (p1 == null || p2 == null || p1.Length < 3 || p2.Length < 3) return false;
            return Distance3D(p1, p2) <= maxDist;
        }

        static double PointToLineDistance3D(double[] p, double[] lineA, double[] lineB)
        {
            double dx = lineB[0] - lineA[0];
            double dy = lineB[1] - lineA[1];
            double dz = lineB[2] - lineA[2];
            double L = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (L < 1e-12) return Distance3D(p, lineA);
            dx /= L;
            dy /= L;
            dz /= L;
            double vx = p[0] - lineA[0];
            double vy = p[1] - lineA[1];
            double vz = p[2] - lineA[2];
            double proj = vx * dx + vy * dy + vz * dz;
            var cx = lineA[0] + proj * dx;
            var cy = lineA[1] + proj * dy;
            var cz = lineA[2] + proj * dz;
            return Distance3D(p, new[] { cx, cy, cz });
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
