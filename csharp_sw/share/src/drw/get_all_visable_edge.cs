using System;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System.Text;
using View = SolidWorks.Interop.sldworks.View;
using System.Diagnostics;

namespace tools
{
    public class get_all_visable_edge
    {
        static public void run(ModelDoc2 swModel)
        {
            #region 获取所有视图
            if (swModel == null)
            {
                Console.WriteLine("错误：没有打开的活动文档。");
                return;
            }

            var drawingDoc = (DrawingDoc)swModel;

            // 获取当前图纸
            var swSheet = (Sheet)drawingDoc.GetCurrentSheet();
            if (swSheet == null)
            {
                Console.WriteLine("错误：无法获取当前图纸。");
                return;
            }

            // 获取图纸上的所有视图
            object[] objViews = (object[])swSheet.GetViews();

            if (objViews == null)
            {
                Console.WriteLine("警告：当前图纸上没有视图。");
                return;
            }

            // 遍历每个视图
            foreach (var objView in objViews)
            {
                View view = (View)objView;

        
                if (view.GetName2().Contains("图") || view.GetName2().Contains("Drawing View"))

                {
                    get_all_visible_faces(view);
                }
            }
            #endregion

        }

        /// <summary>
        /// 获取视图中的所有可见面
        /// </summary>
        static private void get_all_visible_faces(View view)
        {
            try
            {
                Debug.WriteLine("viewname:" + view.Name + "\ncompcount:" + view.GetVisibleComponentCount());
                
                // 获取视图中的所有可见组件
                object[] visibleComponents = (object[])view.GetVisibleComponents();
                
                if (visibleComponents == null || visibleComponents.Length == 0)
                {
                    Debug.WriteLine("  该视图没有可见组件");
                    return;
                }

                // 遍历每个可见组件
                foreach (Component2 comp in visibleComponents)
                {
                    if (comp == null) continue;
                    
                    Debug.WriteLine($"  组件：{comp.Name2}");
                    
                    // 获取该组件的所有可见面
                    object[] visibleFaces = (object[])view.GetVisibleEntities(
                        comp, 
                        (int)swViewEntityType_e.swViewEntityType_Face
                    );
            

                    if (visibleFaces != null && visibleFaces.Length > 0)
                    {
                        Debug.WriteLine($"    可见面数量：{visibleFaces.Length}");
                        
                        // 遍历每个可见面
                        foreach (Face2 face in visibleFaces)
                        {
                            if (face == null) continue;
                            
                            var area = Math.Round(face.GetArea() * 1000000, 2);
                            var surface = face.IGetSurface();
                            
                            string faceType = "未知";
                            if (surface.IsPlane()) faceType = "平面";
                            else if (surface.IsCylinder()) faceType = "圆柱面";
                            else if (surface.IsCone()) faceType = "圆锥面";
                            else if (surface.IsSphere()) faceType = "球面";
                            
                            Debug.WriteLine($"      面 - 面积：{area} mm², 类型：{faceType}");
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"    该组件没有可见面");
                    }
                    object[] visibleedges = (object[])view.GetVisibleEntities(
                        comp, 
                        (int)swViewEntityType_e.swViewEntityType_Edge
                    );

                    if (visibleedges != null && visibleedges.Length > 0)
                    {
                        Debug.WriteLine($"    可见边数量：{visibleedges.Length}");
                        
                        // 遍历每个可见边
                        foreach (Edge edge in visibleedges)
                        {
                            if (edge == null) continue;
                            var curve = (Curve)edge.GetCurve();   
                            
                            string curveType = "未知";
                            string dimension = "";
                            
                            if (curve.IsLine())
                            {
                                curveType = "直线";
                                var lpara = (double[])curve.LineParams;
                                var lstart = (double[])edge.GetStartVertex();
                                var lend = (double[])edge.GetEndVertex();
                      
                         
                            }
                            else if (curve.IsCircle())
                            {
                                curveType = "圆";
                      
                            }
                            else if (curve.IsEllipse())
                            {
                                curveType = "椭圆";
                                dimension = "";
                            }
                   
                            
                            if (string.IsNullOrEmpty(dimension))
                            {
                                Debug.WriteLine($"      边 - 类型：{curveType}");
                            }
                            else
                            {
                                Debug.WriteLine($"      边 - 类型：{curveType}, {dimension}");
                            }
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"    该组件没有可见边");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"获取可见面时出错：{ex.Message}");
            }
        }
    }
}