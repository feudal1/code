using System;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using View=SolidWorks.Interop.sldworks.View;

namespace tools
{
    public class get_dim_info
    {
        /// <summary>
        /// 获取工程图中的尺寸信息
        /// </summary>
        static public void run(ModelDoc2 swModel)
        {
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
            foreach (object objView in objViews)
            {
                if (objView == null) continue;
                
                var swView = (View)objView;
                get_info(swView);
                // 获取视图中的所有注解

            }
        }

        static public void get_info(View swView) {
            object[] objAnnotations = (object[])swView.GetAnnotations();

            if (objAnnotations == null || objAnnotations.Length == 0)
            {
                return;
            }

            // 遍历每个注解
            foreach (object objAnnotation in objAnnotations)
            {
                if (objAnnotation == null) continue;

                var annotation = (Annotation)objAnnotation;

                var position = (double[])annotation.GetPosition();
                if (position != null ) {
                    var annoname=annotation.GetName();
                    Console.WriteLine($"{annoname},注解位置：{position[0] * 1000:F2},{position[1] * 1000:F2}");
                
                }
               
                // 检查是否为显示尺寸类型
                if (annotation.GetType() == (int)swAnnotationType_e.swDisplayDimension)
                {
                    try
                    {
                        DisplayDimension swDisplayDimension = (DisplayDimension)annotation.GetSpecificAnnotation();
                      
                        Dimension swDimension = (Dimension)swDisplayDimension.GetDimension();
                        var mathPoints = (object[])swDimension.ReferencePoints;
                      

                        // 访问具体的点坐标
                        if (mathPoints != null && mathPoints.Length >= 3)
                        {
                            // 线性尺寸 - 前两个点
                            var startPoint = (double[])((MathPoint)mathPoints[0]).ArrayData;
                            var endPoint = (double[])((MathPoint)mathPoints[1]).ArrayData;
                           

                            // 角度尺寸 - 第三个点作为顶点
                            var angleVertex = (double[])((MathPoint)mathPoints[2]).ArrayData;

                            Console.WriteLine($"Start Point: {startPoint[0] * 1000:F2}, {startPoint[1] * 1000:F2}, {startPoint[2] * 1000:F2}");
                            Console.WriteLine($"End Point: {endPoint[0] * 1000:F2}, {endPoint[1] * 1000:F2}, {endPoint[2] * 1000:F2}");
                            Console.WriteLine($"Angle Vertex: {angleVertex[0] * 1000:F2}, {angleVertex[1] * 1000:F2}, {angleVertex[2] * 1000:F2}");
                        }
                        Console.WriteLine($"Dimension Name: {swDimension.FullName}");
                        Console.WriteLine($"Dimension Value: {swDimension.Value:F2}");
                        Console.WriteLine("---");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"处理尺寸时出错: {ex.Message}");
                    }
                }
            }

        }
    }
}