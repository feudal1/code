
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;



using Dimension = SolidWorks.Interop.sldworks.Dimension;
using Edge = SolidWorks.Interop.sldworks.Edge;
using View = SolidWorks.Interop.sldworks.View;

namespace solidgai
{
    public static class sw方法
    {
        public static void 图纸处理(ISldWorks sldWorks)
        {


         

          // Console.WriteLine("成功？:"+ sldWorks.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swTangentEdgeDisplayDefault, (int)swDisplayTangentEdges_e.swTangentEdgesHidden));
            ModelDoc2 swModel; DrawingDoc drawingDoc;
            swModel = (ModelDoc2)sldWorks.ActiveDoc;
            var 过图 = swModel.GetCustomInfoValue("", "过图");

            if (过图 == "已过图") return;
            else
            {
                var myTextFormat = (TextFormat)swModel.GetUserPreferenceTextFormat((int)swUserPreferenceTextFormat_e.swDetailingDimensionTextFormat);
                myTextFormat.CharHeightInPts = 14;

                var boolstatus = swModel.SetUserPreferenceTextFormat((int)swUserPreferenceTextFormat_e.swDetailingDimensionTextFormat, myTextFormat);
                //Console.WriteLine("成功?" + boolstatus);
                swModel.AddCustomInfo2("过图", (int)swCustomInfoType_e.swCustomInfoText, "已过图");
                drawingDoc = (DrawingDoc)swModel;
                Sheet swSheet = (Sheet)drawingDoc.GetCurrentSheet();

                    double[] sheetProperties = (double[])swSheet.GetProperties2();
              //  Console.WriteLine("sheetProperties:"+sheetProperties[4]);
                //   swSheet.SetProperties2((int)sheetProperties[0], (int)sheetProperties[1], (double)sheetProperties[2], (double)sheetProperties[3], true, (double)sheetProperties[5], (double)sheetProperties[6], true);

                object[] views = (object[])swSheet.GetViews();
              
                foreach (var view1 in views)
                {
                  
                    View view = (View)view1;

                  // Console.WriteLine(view.GetDisplayTangentEdges()+"--" + view.GetDisplayTangentEdges2());
                    view.SetDisplayTangentEdges2(0);
                    if (view.GetDisplayMode() != 3) { 
                      view.SetDisplayMode3(false, (int)swDisplayMode_e.swFACETED_HIDDEN_GREYED, false, true);
                    }

                    var baseview = (View)view.GetBaseView();

                    
                    if (baseview == null) continue;
                  
                    if (sheetProperties[4] != 1) { 
                    var vposition = ((double[])view.Position).Select(i => Math.Round(i, 2)).ToArray();
                    Debug.WriteLine("viewlocation=" + vposition[0] + "," + vposition[1]);
                    Debug.WriteLine("baseview.Name=" + baseview.Name.ToString());
                    var bvposition = ((double[])baseview.Position).Select(i => Math.Round(i, 2)).ToArray();
                    Debug.WriteLine("viewlocation=" + bvposition[0] + "," + bvposition[1]);
                    var newposition = new double[2] { vposition[0], vposition[1] };
                    if (bvposition[0] == vposition[0])
                    {
                        newposition = new double[2] { vposition[0], 2 * bvposition[1] - vposition[1] };
                    }
                    else if (bvposition[1] == vposition[1])
                    {
                        newposition = new double[2] { 2 * bvposition[0] - vposition[0], vposition[1] };
                    }
                   

                    view.Position = newposition;
                    }

                }
               
                drawingDoc.EditTemplate();

                drawingDoc.EditSketch();

                object[] dsVarBlkDefinitions = (object[])swModel.SketchManager.GetSketchBlockDefinitions();

                if (dsVarBlkDefinitions != null && dsVarBlkDefinitions.Length > 0)
                {
                    foreach (var dsBlkDefinition in dsVarBlkDefinitions)
                    {
                        SketchBlockDefinition swBlockDef = (SketchBlockDefinition)dsBlkDefinition;

                        var swBlockInstances = (object[])swBlockDef.GetInstances();
                        if (swBlockInstances != null && swBlockInstances.Length > 0)
                        {
                            foreach (var swBlockInstance in swBlockInstances)
                            {

                                SketchBlockInstance Instance = (SketchBlockInstance)swBlockInstance;

                                swModel.SketchManager.ExplodeSketchBlockInstance(Instance);



                            }
                        }
                    }
                }
                drawingDoc.EditSheet();
                drawingDoc.EditSketch();
                drawingDoc.EditTemplate();

                drawingDoc.EditSketch();

                dsVarBlkDefinitions = (object[])swModel.SketchManager.GetSketchBlockDefinitions();

                if (dsVarBlkDefinitions != null && dsVarBlkDefinitions.Length > 0)
                {
                    foreach (var dsBlkDefinition in dsVarBlkDefinitions)
                    {
                        SketchBlockDefinition swBlockDef = (SketchBlockDefinition)dsBlkDefinition;

                        var swBlockInstances = (object[])swBlockDef.GetInstances();
                        if (swBlockInstances != null && swBlockInstances.Length > 0)
                        {
                            foreach (var swBlockInstance in swBlockInstances)
                            {

                                SketchBlockInstance Instance = (SketchBlockInstance)swBlockInstance;

                                swModel.SketchManager.ExplodeSketchBlockInstance(Instance);



                            }
                        }
                    }
                }

                bool boolStatus = swModel.Extension.SketchBoxSelect(0.174494, 0.016451, 0.000000, 0.231207, 0.006062, 0.000000);

                swModel.EditDelete();
                swModel.Extension.SketchBoxSelect(0.229515, 0.006888, 0.000000, 0.206200, 0.015797, 0.000000);

                swModel.EditDelete();

                drawingDoc.EditSheet();
                drawingDoc.EditSketch();




                swModel.ClearSelection2(true);
                // drawingDoc.GenerateViewPaletteViews(swModel.GetPathName());
            }

        }
        public static void 标折弯尺寸(ISldWorks SwApp)
        {

            ModelDoc2 swModel = (ModelDoc2)SwApp.ActiveDoc;
            DrawingDoc swDrawing = (DrawingDoc)swModel;
            Sheet sheet = (Sheet)swDrawing.GetCurrentSheet();
            double 板厚 = 0;
            var swMathUtils = SwApp.IGetMathUtility();

            string partname = Path.GetFileNameWithoutExtension(swModel.GetPathName());
            var Views = (object[])sheet.GetViews();

            foreach (var view2 in Views)
            {
                View view = (View)view2;

                double Offset = 0.05;
                if ((view.GetName2().Contains("图") || view.GetName2().Contains("Drawing View"))
                   )
                {

                    string viewname = view.GetName2();
                    Debug.WriteLine("视图名称为：" + viewname + "，  视图方向" + view.GetOrientationName());
                    var swViewXform = (MathTransform)view.ModelToViewTransform;
                    var ViewTransformDATA = (double[])swViewXform.ArrayData;

                    Debug.WriteLine($"：{Math.Round(ViewTransformDATA[0], 2)}，{Math.Round(ViewTransformDATA[1], 2)}，{Math.Round(ViewTransformDATA[2], 2)}，{Math.Round(ViewTransformDATA[13], 2)}");
                    Debug.WriteLine($"：{Math.Round(ViewTransformDATA[3], 2)}，{Math.Round(ViewTransformDATA[4], 2)}，{Math.Round(ViewTransformDATA[5], 2)}，{Math.Round(ViewTransformDATA[14], 2)}");
                    Debug.WriteLine($"：{Math.Round(ViewTransformDATA[6], 2)}，{Math.Round(ViewTransformDATA[7], 2)}，{Math.Round(ViewTransformDATA[8], 2)}，{Math.Round(ViewTransformDATA[15], 2)}");
                    Debug.WriteLine($"：{Math.Round(ViewTransformDATA[9], 2)}，{Math.Round(ViewTransformDATA[10], 2)}，{Math.Round(ViewTransformDATA[11], 2)}，{Math.Round(ViewTransformDATA[12], 2)}");


                    var vBounds = (double[])view.GetOutline();
                    view.SetDisplayMode3(false, (int)swDisplayMode_e.swFACETED_HIDDEN_GREYED, false, true);
                    Console.WriteLine("view.GetDisplayMode()=" + view.GetDisplayMode());
                    swModel.EditRebuild3();
                    PartDoc partDoc = (PartDoc)view.ReferencedDocument;
                    object[] vBodies = (object[])partDoc.GetBodies2((int)swBodyType_e.swSolidBody, false);
                    Body2 body = (Body2)vBodies[0];
                    object[] vAffectedFaces = (object[])body.GetFaces();



                    Dictionary<(double, double, string), (double, Face)> 外面集 = new Dictionary<(double, double, string), (double, Face)>();
                    foreach (Face face in vAffectedFaces)
                    {
                        double 面面积 = Math.Round(face.GetArea() * 1000000, 1);
                        var surface = face.IGetSurface();
                        if (surface.IsCylinder())
                        {
                            double[] CylinderParams = (double[])surface.CylinderParams;
                            double 折弯半径 = Math.Round(CylinderParams[6] * 1000, 1);
                            Debug.WriteLine($"面面积：{面面积}，折弯半径 {折弯半径}");
                            Debug.WriteLine($"向量：{CylinderParams[3]}，{CylinderParams[4]}，{CylinderParams[5]}");
                            var edges = (object[])face.GetEdges();
                            var 圆心坐标 = 得到圆心坐标();
                            Debug.WriteLine($"圆心坐标：{圆心坐标.Item1.Item1}，{圆心坐标.Item1.Item2}");
                            Debug.WriteLine($"方向：{圆心坐标.Item1.Item3}，半径：{圆心坐标.Item2}");
                            ((double, double, string), double) 得到圆心坐标()//x,y,方向,半径
                            {
                                double x, y;
                                string 圆弧轴线方向;
                                foreach (var edgeobj in edges)
                                {
                                    Edge edge = (Edge)edgeobj;
                                    Curve ccurve = (Curve)edge.GetCurve();

                                    if (ccurve.IsCircle())
                                    {
                                        double[] CircleParams = (double[])ccurve.CircleParams;

                                        if (Math.Round(CircleParams[3], 1) != 0)
                                        {
                                            圆弧轴线方向 = "x";
                                            x = Math.Round(CircleParams[1] * 1000, 1);
                                            y = Math.Round(CircleParams[2] * 1000, 1);
                                            return ((x, y, 圆弧轴线方向), 折弯半径);
                                        }
                                        else if (Math.Round(CircleParams[4], 1) != 0)
                                        {
                                            圆弧轴线方向 = "y";
                                            x = Math.Round(CircleParams[0] * 1000, 1);
                                            y = Math.Round(CircleParams[2] * 1000, 1);
                                            return ((x, y, 圆弧轴线方向), 折弯半径);
                                        }
                                        else if (Math.Round(CircleParams[5], 1) != 0)
                                        {
                                            圆弧轴线方向 = "z";
                                            x = Math.Round(CircleParams[0] * 1000, 1);
                                            y = Math.Round(CircleParams[1] * 1000, 1);
                                            return ((x, y, 圆弧轴线方向), 折弯半径);
                                        }
                                    }
                                }
                                return ((0, 0, ""), 0);
                            }
                            (double, Face) 原直径和面;
                            if (外面集.TryGetValue(圆心坐标.Item1, out 原直径和面))
                            {
                                外面集[圆心坐标.Item1] = 原直径和面.Item1 > 折弯半径 ? 原直径和面 : (折弯半径, face);
                            }
                            else { 外面集[圆心坐标.Item1] = (折弯半径, face); }
                        }
                    }
                    var 外面面集 = 外面集.Select(item => ((item.Key.Item1, item.Key.Item2), item.Key.Item3, item.Value.Item2)).ToList();
                    Debug.WriteLine($"抓到折弯数={外面面集.Count}");
                    //特征face，子face
            
                    double[] tround2(double[] x)
                    {
                        double[] outp = new double[] {
                            Math.Round(x.ElementAt(0)*1000,2),
                              Math.Round(x.ElementAt(1)*1000,2),
                                Math.Round(x.ElementAt(2)*1000,2)
                            };
                        foreach (double o in outp)
                        {
                            Debug.WriteLine(o);
                        }
                        return outp;
                    }
                    bool doublesame(double[] x, double[] y)
                    {
                        bool outp = Math.Abs(x.First() - y.First()) < 0.1
                                && Math.Abs(x.Skip(1).First() - y.Skip(1).First()) < 0.1
                                && Math.Abs(x.Skip(2).First() - y.Skip(2).First()) < 0.1;
                        return outp;
                    }

                    Dictionary<Face, Face> 断面字典 = new Dictionary<Face, Face>();
                    var 外面接面集 = new List<(Face, (double x, double y), string 圆弧轴线方向, double[])>();//面，接线
                    foreach (((double, double) 折弯中心, string 圆弧轴线方向, Face 外面) in 外面面集)
                    {
                        //折弯中心，折弯外平面
                        var 外面edges = (object[])外面.GetEdges();

                        foreach (var edgeobj in 外面edges)
                        {
                            Edge edge = (Edge)edgeobj;
                            Curve Lcurve = (Curve)edge.GetCurve();

                            if (Lcurve.IsLine())
                            {
                                var start = (Vertex)edge.GetStartVertex();
                                var startpoint = tround2((double[])start.GetPoint());

                                var end = (Vertex)edge.GetEndVertex();
                                var endpoint = tround2((double[])end.GetPoint());

                                var lpara = (double[])Lcurve.LineParams;

                                var 两直线界面 = (object[])edge.GetTwoAdjacentFaces();
                                foreach (var faceobj in 两直线界面)
                                {
                                    var face = (Face)faceobj;
                                    if (((Surface)face.GetSurface()).IsPlane())
                                    {
                                        外面接面集.Add((face, 折弯中心, 圆弧轴线方向, lpara));
                                        var 接面edges = (object[])face.GetEdges();
                                        Debug.WriteLine("接面edges.count" + 接面edges.Length);
                                        foreach (var 接面edgeobj in 接面edges)
                                        {
                                            Edge 接面edge = (Edge)接面edgeobj;
                                            Curve Lcurve2 = (Curve)接面edge.GetCurve();
                                            if (Lcurve2.IsLine())
                                            {
                                                var lpara2 = (double[])Lcurve2.LineParams;
                                                var 接面start = (Vertex)接面edge.GetStartVertex();
                                                var 接面startpoint = tround2((double[])接面start.GetPoint());

                                                var 接面end = (Vertex)接面edge.GetEndVertex();
                                                var 接面endpoint = tround2((double[])接面end.GetPoint());

                                                if (!doublesame(接面startpoint, startpoint)
                                                    && !doublesame(接面startpoint, endpoint)
                                                  && !doublesame(接面endpoint, startpoint)
                                                   && !doublesame(接面endpoint, endpoint)
                                                    )
                                                {

                                                    var 接面两直线界面 = (object[])接面edge.GetTwoAdjacentFaces();
                                                    foreach (var faceobj接面 in 接面两直线界面)
                                                    {
                                                        var face接面 = (Face)faceobj接面;
                                                        if (face接面 != face && ((Surface)face接面.GetSurface()).IsPlane())
                                                        {
                                                            Debug.WriteLine("检查2");
                                                            断面字典.Add(face, face接面);
                                                            外面接面集.Add((face接面, 折弯中心, 圆弧轴线方向, lpara2));
                                                        }
                                                        else
                                                        {
                                                            Debug.WriteLine(" 接面面积=" + face接面.GetArea() * 1000000);
                                                            Debug.WriteLine("=face?" + (face接面 == face));
                                                            Debug.WriteLine("isplane?" + ((Surface)face接面.GetSurface()).IsPlane());
                                                        }
                                                    }
                                                }
                                            }

                                        }
                                    }
                                }
                            }
                        }


                    }
                    Debug.WriteLine($"外面接面集={外面接面集.Count}");
                    Debug.WriteLine($"断面字典={断面字典.Count}");
                    List<(Face, string, bool, double)> 子faces = new List<(Face, string, bool, double)>();
                    foreach ((Face 外面接面, (double, double) 折弯中心, string 圆弧轴线方向, double[] lpara) in 外面接面集)
                    {
                        var 外面接面surface = (Surface)外面接面.GetSurface();
                        var planepara = (double[])外面接面surface.PlaneParams;
                        (double, double, double) 平面法向 = (Math.Round(planepara[0], 1), Math.Round(planepara[1], 1), Math.Round(planepara[2], 1));
                        Debug.WriteLine($"平面朝向:{平面法向.Item1},{平面法向.Item2},{平面法向.Item3}");

                        bool 正方向 = false;
                        string 方向 = "";
                        double 面位置值 = 0;
                        if (平面法向.Item1 != 0)
                        {
                            正方向 = lpara[0] * 1000 < 折弯中心.Item1;//x，y或者 x，z
                            if (板厚 == 0) 板厚 = Math.Round(Math.Abs(lpara[0] * 1000 - 折弯中心.Item1), 1);
                            方向 = "x";
                            面位置值 = Math.Round(lpara[0] * 1000, 1);

                        }
                        else if (平面法向.Item2 != 0)//x，y或者y，z
                        {
                            double 圆心位置值 = 圆弧轴线方向 == "z" ? 折弯中心.Item2 : 折弯中心.Item1;
                            正方向 = lpara[1] * 1000 < 圆心位置值;
                            if (板厚 == 0) 板厚 = Math.Round(Math.Abs(lpara[1] * 1000 - 圆心位置值), 1);
                            方向 = "y";
                            面位置值 = Math.Round(lpara[1] * 1000, 1);
                        }
                        else if (平面法向.Item3 != 0)//x，z或者y，z
                        {
                            正方向 = lpara[2] * 1000 < 折弯中心.Item2;
                            if (板厚 == 0) 板厚 = Math.Round(Math.Abs(lpara[2] * 1000 - 折弯中心.Item2), 1);

                            方向 = "z";
                            面位置值 = Math.Round(lpara[2] * 1000, 1);
                        }
                        子faces.Add((外面接面, 方向, 正方向, 面位置值));
                        Debug.WriteLine("面积=" + Math.Round(外面接面.GetArea() * 1000000, 1) + $"    ,正方向?  {正方向}");



                    }
                    var 过滤子faces = new HashSet<(Face, string, bool, double)>(子faces);
                    Debug.WriteLine($"过滤子faces前后：{子faces.Count}=>{过滤子faces.Count}");

                    #region //全部面集
                    //List<(Face, string, double, Edge)> 面集 = vAffectedFaces
                    //    .Where(item =>
                    //    {
                    //        Surface plansurface = (Surface)((Face)item).GetSurface();
                    //        object[] edges = (object[])((Face)item).GetEdges();
                    //        return plansurface.IsPlane() && ((Face)item).GetArea() * 1000000 > 10 && edges.Length > 3;
                    //    })
                    //    .Select(item =>
                    //    {
                    //        Surface plansurface = (Surface)((Face)item).GetSurface();

                    //        double[] Params = (double[])plansurface.PlaneParams;
                    //        (double, double, double) 平面朝向 = (Math.Round(Params[0]), Math.Round(Params[1]), Math.Round(Params[2]));
                    //        string 方向 = "";
                    //        double 位置值 = 0;
                    //        object[] edges = (object[])((Face)item).GetEdges();
                    //        Debug.WriteLine("edges.count=" + edges.Length + ",面积为=" + ((Face)item).GetArea() * 1000000 + $"平面朝向={平面朝向.Item1},{平面朝向.Item2},{平面朝向.Item3}");

                    //        Edge eedge = (Edge)edges[0];

                    //        for (int i = 0; i < edges.Length; i++)
                    //        {
                    //            eedge = (Edge)edges[i];
                    //            Curve curve = (Curve)eedge.GetCurve();
                    //            if (curve.IsLine())
                    //            {
                    //                Vertex StartVertex = (Vertex)eedge.GetStartVertex();

                    //                double[] pParams = (double[])StartVertex.GetPoint();


                    //                if (平面朝向.Item1 != 0)
                    //                {
                    //                    位置值 = Math.Round((double)pParams[0] * 1000, 1);
                    //                    方向 = "x";
                    //                }
                    //                else if (平面朝向.Item2 != 0)
                    //                {
                    //                    位置值 = Math.Round((double)pParams[1] * 1000, 1);
                    //                    方向 = "y";
                    //                }
                    //                else if (平面朝向.Item3 != 0)
                    //                {
                    //                    位置值 = Math.Round((double)pParams[2] * 1000, 1);
                    //                    方向 = "z";
                    //                }
                    //                break;
                    //            }
                    //        }

                    //        Debug.WriteLine("面位置为：" + 位置值 + ",方向：" + 方向);
                    //        return ((Face)item, 方向, 位置值, eedge);
                    //    }).ToList();
                    //Debug.WriteLine("面集.count=" + 面集.Count);

                    //放置尺寸
                    #endregion

                    Dictionary<Face, Face> 标过的面 = new Dictionary<Face, Face>();
                    foreach (var face实例 in 过滤子faces)
                    {
                        //           List<Face> 同向量集 = 面集.Where(item => item.Item2 == face实例.Item2).OrderByDescending(item => item.Item3)
                        //.Select(item =>
                        //{

                        //    return item.Item1;
                        //}).ToList();
                        string 放置方式 = "无";
                        if (face实例.Item2 == "x")
                        {
                            if (Math.Round(ViewTransformDATA[0]) != 0) { 放置方式 = "水平"; }
                            else if (Math.Round(ViewTransformDATA[1]) != 0) { 放置方式 = "竖直"; }
                        }
                        else if (face实例.Item2 == "y")
                        {
                            if (Math.Round(ViewTransformDATA[3]) != 0) { 放置方式 = "水平"; }
                            else if (Math.Round(ViewTransformDATA[4]) != 0) { 放置方式 = "竖直"; }
                        }
                        else if (face实例.Item2 == "z")
                        {
                            if (Math.Round(ViewTransformDATA[6]) != 0) { 放置方式 = "水平"; }
                            else if (Math.Round(ViewTransformDATA[7]) != 0) { 放置方式 = "竖直"; }
                        }
                        Debug.WriteLine("面朝向：" + face实例.Item2 + "，放置方式为：" + 放置方式 + ",data1值" + Math.Round(ViewTransformDATA[0]));
                        if (放置方式 == "无") continue;

                        (Face, double) 折弯匹配面 = default;
                        foreach (var 子face in 过滤子faces)
                        {
                            if (子face.Item2 == face实例.Item2
                                && 子face.Item3 != face实例.Item3)
                            {

                                if (face实例.Item3 && 子face.Item4 > face实例.Item4
                                && 子face.Item4 - face实例.Item4 - 0.5 > 板厚)
                                {
                                    if (折弯匹配面 == default) 折弯匹配面 = (子face.Item1, 子face.Item4);
                                    else 折弯匹配面 = 折弯匹配面.Item2 < 子face.Item4 ? 折弯匹配面 : (子face.Item1, 子face.Item4);
                                }
                                else if (!face实例.Item3 && 子face.Item4 < face实例.Item4
                                      && face实例.Item4 - 子face.Item4 - 0.5 > 板厚)
                                {

                                    if (折弯匹配面 == default) 折弯匹配面 = (子face.Item1, 子face.Item4);
                                    else 折弯匹配面 = 折弯匹配面.Item2 > 子face.Item4 ? 折弯匹配面 : (子face.Item1, 子face.Item4);
                                }

                            }

                        }
                        Debug.WriteLine("折弯匹配面.count=default?" + 折弯匹配面 == default);
                        Face 匹配面;

                        if (折弯匹配面 != default)
                        {
                            匹配面 = 折弯匹配面.Item1!;

                            Face 标过此面;
                            
                            if (标过的面.TryGetValue(匹配面, out 标过此面!) && 标过此面 == face实例.Item1)
                            {
                                Debug.WriteLine("标过此面");
                            }
                            else
                            {
                                标过的面.Add(face实例.Item1, 匹配面);
                                Debug.WriteLine("没标过此面");
                                标尺寸(face实例.Item1, 匹配面);
                            }
                        }


                        void 标尺寸(Face 面1, Face 面2)
                        {
                            object[] edges1 = (object[])面1.GetEdges();
                            object[] edges2 = (object[])面2.GetEdges();


                            激活线段(edges1);
                            激活线段(edges2);
                            void 激活线段(object[] edges)
                            {
                                double 相差 = 0; int i = 0;
                                while (true)
                                {
                                    Curve linecurve = (Curve)((Edge)edges[i]).GetCurve();
                                    if (!linecurve.IsLine()) { i++; continue; }
                                    Vertex vertex = (Vertex)((Edge)edges[i]).GetStartVertex();
                                    Vertex endvertex = (Vertex)((Edge)edges[i]).GetEndVertex();
                                    SelectionMgr swSelMgr = (SelectionMgr)swModel.SelectionManager;
                                    SelectData swSelData = swSelMgr.CreateSelectData();

                                    //((Entity)vertex).Select4(false, swSelData);
                                    //((Entity)face实例.Item5).Select4(true, swSelData);

                                    var vPt = (double[])vertex.GetPoint();
                                    var swMathPt = (MathPoint)swMathUtils.CreatePoint(vPt);
                                    swMathPt = (MathPoint)swMathPt.MultiplyTransform(swViewXform);
                                    var MathPtData = (double[])swMathPt.ArrayData;

                                    var endvPt = (double[])endvertex.GetPoint();
                                    var endswMathPt = (MathPoint)swMathUtils.CreatePoint(endvPt);
                                    endswMathPt = (MathPoint)endswMathPt.MultiplyTransform(swViewXform);
                                    var endMathPtData = (double[])endswMathPt.ArrayData;
                                    Console.WriteLine($"swMathPt1:{Math.Round((MathPtData[0] + endMathPtData[0]) / 2, 3)},{Math.Round((MathPtData[1] + endMathPtData[1]) / 2, 3)},{Math.Round((MathPtData[2] + endMathPtData[2]) / 2, 3)}");
                                    if (放置方式 == "水平")
                                    {
                                        相差 = Math.Round(MathPtData[1] * 1000 - endMathPtData[1] * 1000, 1);
                                    }
                                    else if (放置方式 == "竖直")
                                    {
                                        相差 = Math.Round(MathPtData[0] * 1000 - endMathPtData[0] * 1000, 1);
                                    }

                                    if (相差 != 0) { swModel.Extension.SelectByRay((MathPtData[0] + endMathPtData[0]) / 2, (MathPtData[1] + endMathPtData[1]) / 2, 0, 0, 0, -1, 0.0001, 1, false, 0, 0); break; }
                                    i++;

                                }

                            }



                            double Xpos, Ypos;

                            if (放置方式 == "水平")
                            {
                                Xpos = (vBounds[0] + vBounds[2]) / 2;
                                Ypos = (vBounds[3] - Offset);
                                var myDisplayDim = swModel.AddDimension2(Xpos, Ypos, 0);
                            }
                            else if (放置方式 == "竖直")
                            {
                                Ypos = (vBounds[1] + vBounds[3]) / 2;
                                Xpos = (vBounds[0] + Offset);
                                var myDisplayDim = swModel.AddDimension2(Xpos, Ypos, 0);
                            }
                            Offset = Offset + 0.005;

                            swModel.ClearSelection2(true);
                        }
                    }
                    Debug.WriteLine("标过的面个数=" + 标过的面.Count);


                }
            }

        }
        public static void 删除尺寸(ISldWorks SwApp)
        {
            ModelDoc2 swModelDoc = (ModelDoc2)SwApp.ActiveDoc;
            var drawingDoc = (DrawingDoc)swModelDoc;
            var swSheet = (Sheet)drawingDoc.GetCurrentSheet();
            var swViews = (object[])swSheet.GetViews();
            List<Annotation> 尺寸删除集 = new List<Annotation>();
            for (int j = 0; j < swViews.Length; j++)
            {
                View swView = (View)swViews[j];
                if (swView.GetOrientationName() != "平板型式")
                {
                    object[] annotations = (object[])swView.GetAnnotations();
                    if (annotations != null && annotations.Length > 0)
                    {
                        foreach (object annotation1 in annotations)
                        {
                            Annotation annotation = (Annotation)annotation1;
                            if (annotation.GetType() == (int)swAnnotationType_e.swDisplayDimension)
                            {
                                DisplayDimension swDisplayDimension = (DisplayDimension)annotation.GetSpecificAnnotation();
                                Dimension swDimension = (Dimension)swDisplayDimension.GetDimension();
                                // 初始化文本部分
                                var textPrefix = swDisplayDimension.GetText((int)swDimensionTextParts_e.swDimensionTextPrefix);
                                var textSuffix = swDisplayDimension.GetText((int)swDimensionTextParts_e.swDimensionTextSuffix);
                                var calloutAbove = swDisplayDimension.GetText((int)swDimensionTextParts_e.swDimensionTextCalloutAboveDefinition);
                                var calloutBelow = swDisplayDimension.GetText((int)swDimensionTextParts_e.swDimensionTextCalloutBelowDefinition);
                                if (textPrefix == "" && textSuffix == ""
                                    && calloutAbove == "" && calloutBelow == "")
                                {
                                    尺寸删除集.Add(annotation);
                                }

                            }
                        }
                    }
                }

            }
            foreach (var 删除尺寸 in 尺寸删除集)
            {
                删除尺寸.Select(true);
            }
            swModelDoc.EditDelete();

        }
        public static void 标沉孔(ISldWorks SwApp)
        {
            ModelDoc2 swModelDoc = (ModelDoc2)SwApp.ActiveDoc;
            var drawingDoc = (DrawingDoc)swModelDoc;
            var swSheet = (Sheet)drawingDoc.GetCurrentSheet();
            var swMathUtils = SwApp.IGetMathUtility();
            var swSelMgr = (SelectionMgr)swModelDoc.SelectionManager;
            var swSelData = (SelectData)swSelMgr.CreateSelectData();
            var Offset = 0.005;
            var selectType = swSelMgr.GetSelectedObjectType3(1, -1);


            if (selectType != (int)swSelectType_e.swSelDRAWINGVIEWS)
            {

                Console.WriteLine("没选到\nYou Select :" + Enum.GetName(typeof(swSelectType_e), selectType));
                return;
            }
            var selboj = (View)swSelMgr.GetSelectedObject(1);

            var swViewXform = (MathTransform)selboj.ModelToViewTransform;
            var ViewTransformDATA = (double[])swViewXform.ArrayData;
            var vBounds = (double[])selboj.GetOutline();
            Debug.WriteLine($"：{Math.Round(ViewTransformDATA[0], 2)}，{Math.Round(ViewTransformDATA[1], 2)}，{Math.Round(ViewTransformDATA[2], 2)}，{Math.Round(ViewTransformDATA[13], 2)}");
            Debug.WriteLine($"：{Math.Round(ViewTransformDATA[3], 2)}，{Math.Round(ViewTransformDATA[4], 2)}，{Math.Round(ViewTransformDATA[5], 2)}，{Math.Round(ViewTransformDATA[14], 2)}");
            Debug.WriteLine($"：{Math.Round(ViewTransformDATA[6], 2)}，{Math.Round(ViewTransformDATA[7], 2)}，{Math.Round(ViewTransformDATA[8], 2)}，{Math.Round(ViewTransformDATA[15], 2)}");
            Debug.WriteLine($"：{Math.Round(ViewTransformDATA[9], 2)}，{Math.Round(ViewTransformDATA[10], 2)}，{Math.Round(ViewTransformDATA[11], 2)}，{Math.Round(ViewTransformDATA[12], 2)}");
            double[] round1(double[] x)
            {

                return x.Select(item => Math.Round(item * 1000, 1)).ToArray();
            }
            if (selboj != null)
            {

                Debug.WriteLine("viewname:" + selboj.Name + "\ncompcount:" + selboj.GetVisibleComponentCount());
                var comp = ((object[])selboj.GetVisibleComponents())[0];
                var ve = (object[])selboj.GetVisibleEntities((Component2)comp, (int)swViewEntityType_e.swViewEntityType_Face);
                if (ve != null)
                {
                    Debug.WriteLine("vecount:" + ve.Count());
                    foreach (var v in ve)
                    {
                        var face = v as Face2;
                        var surface = face!.IGetSurface();
                        if (surface.IsCone())
                        {
                            string 放置方式 = "";
                            var conepara = round1((double[])surface.ConeParams);
                            Debug.WriteLine($"轴向量：{conepara[3]},{conepara[4]},{conepara[5]}");
                            if (Math.Abs(conepara[3]) < 0.01)
                            {
                                if (Math.Round(ViewTransformDATA[0]) != 0) { 放置方式 = "水平"; }
                                else if (Math.Round(ViewTransformDATA[1]) != 0) { 放置方式 = "竖直"; }
                            }
                            else if (Math.Abs(conepara[4]) < 0.01)
                            {
                                if (Math.Round(ViewTransformDATA[3]) != 0) { 放置方式 = "水平"; }
                                else if (Math.Round(ViewTransformDATA[4]) != 0) { 放置方式 = "竖直"; }
                            }
                            else if (Math.Abs(conepara[5]) < 0.01)
                            {
                                if (Math.Round(ViewTransformDATA[6]) != 0) { 放置方式 = "水平"; }
                                else if (Math.Round(ViewTransformDATA[7]) != 0) { 放置方式 = "竖直"; }
                            }

                            Debug.WriteLine("抓到沉孔,边数：" + face.GetEdgeCount());
                            var edges = (object[])face.GetEdges();
                            foreach (var edgeobj in edges)
                            {
                                var edge = edgeobj as Edge;
                                
                                var ecurve = edge!.IGetCurve();

                                var cpara = (double[])ecurve.CircleParams;
                                var cppara = round1((double[])ecurve.CircleParams);
                                Debug.WriteLine($"{cppara[0]},{cppara[1]},{cppara[2]} ||{cppara[3]},{cppara[4]},{cppara[5]}||半径：{cppara[6]}");
                                var fpt = cpara.Take(3).ToArray();
                                var spt = cpara.Take(3).ToArray();
                                if (Math.Abs(cpara[3]) > 0.1)
                                {
                                    fpt[1] = cpara[1] + cpara[6];
                                    fpt[2] = cpara[2] + cpara[6];
                                    spt[1] = cpara[1] - cpara[6];
                                    spt[2] = cpara[2] - cpara[6];
                                }
                                else if (Math.Abs(cpara[4]) > 0.1)
                                {
                                    fpt[0] = cpara[0] + cpara[6];
                                    fpt[2] = cpara[2] + cpara[6];
                                    spt[0] = cpara[0] - cpara[6];
                                    spt[2] = cpara[2] - cpara[6];
                                }

                                else if (Math.Abs(cpara[5]) > 0.1)
                                {
                                    fpt[0] = cpara[0] + cpara[6];
                                    fpt[1] = cpara[1] + cpara[6];
                                    spt[0] = cpara[0] - cpara[6];
                                    spt[1] = cpara[1] - cpara[6];
                                }


                                var endswMathPt = (MathPoint)swMathUtils.CreatePoint(fpt);
                                endswMathPt = (MathPoint)endswMathPt.MultiplyTransform(swViewXform);
                                var ad = (double[])endswMathPt.ArrayData;
                                fpt = round1(fpt);
                                Debug.WriteLine($"{fpt[0]},{fpt[1]},{fpt[2]}");
                                Debug.WriteLine($"{ad[0]},{ad[1]},{ad[2]}");
                                var skpt = swModelDoc.SketchManager.CreatePoint(ad[0], ad[1], ad[2]);
                                skpt.Select(true);
                                var endswMathPt2 = (MathPoint)swMathUtils.CreatePoint(spt);
                                endswMathPt2 = (MathPoint)endswMathPt2.MultiplyTransform(swViewXform);
                                var ad2 = (double[])endswMathPt2.ArrayData;
                                spt = round1(spt);
                                Debug.WriteLine($"{spt[0]},{spt[1]},{spt[2]}");
                                Debug.WriteLine($"{ad2[0]},{ad2[1]},{ad2[2]}");
                                var skpt2 = swModelDoc.SketchManager.CreatePoint(ad2[0], ad2[1], ad2[2]);
                                skpt2.Select(true);
                                double Xpos, Ypos;
                            
                                if (放置方式 == "水平")
                                {
                                    Xpos = (vBounds[0] + vBounds[2]) / 2;
                                    Ypos = (vBounds[3] - Offset);
                                    //var myDisplayDim = swModelDoc.AddDimension2(Xpos, Ypos, 0);
                                }
                                else if (放置方式 == "竖直")
                                {
                                    Ypos = (vBounds[1] + vBounds[3]) / 2;
                                    Xpos = (vBounds[0] + Offset);
                                    //  var myDisplayDim = swModelDoc.AddDimension2(Xpos, Ypos, 0);
                                }
                            }
                        }


                    }
                }
                else { Debug.WriteLine("ve:null"); }
            }
            else { Console.WriteLine("viewname:null"); }
        }

  
  
    }
}
