using System;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using View = SolidWorks.Interop.sldworks.View;
using System.Diagnostics;

namespace tools
{
    public class select_face_recognize
    {
        /// <summary>
        /// 获取当前选中的面并分析面积
        /// </summary>
        static public void run(ModelDoc2 swModel)
        {
            if (swModel == null)
            {
                Console.WriteLine("错误：没有打开的活动文档。");
                return;
            }

            Debug.WriteLine("\n=== 开始分析选中的面 ===");
            
            // 获取选择管理器
            SelectionMgr selectionMgr = (SelectionMgr)swModel.SelectionManager;
            
            if (selectionMgr == null)
            {
                Debug.WriteLine("错误：无法获取选择管理器。");
                return;
            }

            // 获取选中对象数量
            int selectCount = selectionMgr.GetSelectedObjectCount();
            
            if (selectCount == 0)
            {
                Debug.WriteLine("警告：没有选中的对象，请先选择面。");
                return;
            }

            Debug.WriteLine($"选中对象总数：{selectCount}");

            // 遍历所有选中的对象
            for (int i = 1; i <= selectCount; i++)
            {
                try
                {
                    // 获取选中对象的类型
                    int objectType = selectionMgr.GetSelectedObjectType3(i, -1);
                    
                    Debug.WriteLine($"\n--- 选中对象 {i} ---");
                    Debug.WriteLine($"对象类型：{(swSelectType_e)objectType}");

                    // 判断是否为面
                    if (objectType == (int)swSelectType_e.swSelFACES)
                    {
                        Face2 selectedFace = (Face2)selectionMgr.GetSelectedObject6(i, -1);
                        
                        if (selectedFace != null)
                        {
                            AnalyzeFace(selectedFace, i);
                        }
                        else
                        {
                            Debug.WriteLine($"  错误：无法获取面对象 {i}");
                        }
                    }
                    else if (objectType == (int)swSelectType_e.swSelEDGES)
                    {
                        // 如果选中的是边，获取相邻面
                        Edge selectedEdge = (Edge)selectionMgr.GetSelectedObject6(i, -1);
                        
                        if (selectedEdge != null)
                        {
                            Debug.WriteLine("  选中的是边，获取相邻面:");
                            var adjacentFaces = (object[])selectedEdge.GetTwoAdjacentFaces();
                            
                            if (adjacentFaces != null && adjacentFaces.Length > 0)
                            {
                                for (int j = 0; j < adjacentFaces.Length; j++)
                                {
                                    if (adjacentFaces[j] is Face2 adjFace)
                                    {
                                        Debug.WriteLine($"  相邻面 {j + 1}:");
                                        AnalyzeFace(adjFace, i);
                                    }
                                }
                            }
                            else
                            {
                                Debug.WriteLine("  该边没有相邻面。");
                            }
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"  跳过非面对象 (类型：{(swSelectType_e)objectType})");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"  处理选中对象 {i} 时出错：{ex.Message}");
                }
            }

            Debug.WriteLine("\n=== 分析完成 ===");
        }

        /// <summary>
        /// 分析面的详细信息
        /// </summary>
        static private void AnalyzeFace(Face2 face, int index)
        {
            if (face == null)
            {
                Debug.WriteLine($"    面对象为空");
                return;
            }

            try
            {
                // 计算面积 (mm²)
                double area = Math.Round(face.GetArea() * 1000000, 2);
                
                // 获取曲面类型
                var surface = face.IGetSurface();
                string faceType = "未知";
                
                if (surface.IsPlane()) 
                    faceType = "平面";
                else if (surface.IsCylinder()) 
                    faceType = "圆柱面";
                else if (surface.IsCone()) 
                    faceType = "圆锥面";
                else if (surface.IsSphere()) 
                    faceType = "球面";
                else if (surface.IsTorus()) 
                    faceType = "圆环面";
                else 
                    faceType = "其他曲面";

                // 获取面的法向
                double[] normal = (double[])face.Normal;
                string normalStr = "";
                if (normal != null && normal.Length >= 3)
                {
                    normalStr = $", 法向：({normal[0]:F3}, {normal[1]:F3}, {normal[2]:F3})";
                }

                // 获取面的边界信息
                object[] edges = (object[])face.GetEdges();
                int edgeCount = edges?.Length ?? 0;

                // 输出面的信息
                Debug.WriteLine($"  面 {index}:");
                Debug.WriteLine($"    面积：{area} mm²");
                Debug.WriteLine($"    类型：{faceType}{normalStr}");
                Debug.WriteLine($"    边数：{edgeCount}");

                // 如果是圆柱面，显示半径
                if (surface.IsCylinder())
                {
                    double[] cylinderParams = (double[])surface.CylinderParams;
                    if (cylinderParams != null && cylinderParams.Length >= 7)
                    {
                        double radius = Math.Round(cylinderParams[6] * 1000, 2);
                        Debug.WriteLine($"    圆柱半径：{radius} mm");
                    }
                }
                // 如果是圆锥面，显示角度
                else if (surface.IsCone())
                {
                    double[] coneParams = (double[])surface.ConeParams;
                    if (coneParams != null && coneParams.Length >= 7)
                    {
                        double angle = Math.Round(coneParams[6] * 180 / Math.PI, 2);
                        Debug.WriteLine($"    圆锥半角：{angle}°");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"    获取面信息时出错：{ex.Message}");
            }
        }
    }
}