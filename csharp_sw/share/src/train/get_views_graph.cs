using System;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using View=SolidWorks.Interop.sldworks.View;

namespace tools
{
    public class get_views_graph
    {
        /// <summary>
        /// 获取工程图中的视图信息
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
    foreach (var objView in objViews)
      {
          View view = (View)objView;
 
       
          if (view.GetName2().Contains("图") || view.GetName2().Contains("Drawing View"))
             
          {

                    get_info(view);








          }
      }
    }


        static public void get_info(View view) {

            string viewname = view.GetName2();
            Console.WriteLine("视图名称为：" + viewname + "，  视图方向" + view.GetOrientationName());
            var swViewXform = (MathTransform)view.ModelToViewTransform;
            var ViewTransformDATA = (double[])swViewXform.ArrayData;

            Console.WriteLine($"：{Math.Round(ViewTransformDATA[0], 2)}，{Math.Round(ViewTransformDATA[1], 2)}，{Math.Round(ViewTransformDATA[2], 2)}，{Math.Round(ViewTransformDATA[13], 2)}");
            Console.WriteLine($"：{Math.Round(ViewTransformDATA[3], 2)}，{Math.Round(ViewTransformDATA[4], 2)}，{Math.Round(ViewTransformDATA[5], 2)}，{Math.Round(ViewTransformDATA[14], 2)}");
            Console.WriteLine($"：{Math.Round(ViewTransformDATA[6], 2)}，{Math.Round(ViewTransformDATA[7], 2)}，{Math.Round(ViewTransformDATA[8], 2)}，{Math.Round(ViewTransformDATA[15], 2)}");
            Console.WriteLine($"：{Math.Round(ViewTransformDATA[9], 2)}，{Math.Round(ViewTransformDATA[10], 2)}，{Math.Round(ViewTransformDATA[11], 2)}，{Math.Round(ViewTransformDATA[12], 2)}");

        }





    }





}