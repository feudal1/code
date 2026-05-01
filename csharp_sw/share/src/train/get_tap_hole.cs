using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System.Text;
using View = SolidWorks.Interop.sldworks.View;

namespace tools
{
    public class get_tap_hole
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
                    get_dim_info.get_info(view);
                 
                   




                }
            }
            #endregion

            #region 零件部分
            static  void do_part(PartDoc partDoc)
            {
                try
                {

                
                    Feature swFeature = (Feature)partDoc.FirstFeature();
                    while (swFeature != null)
                    {
                        Console.WriteLine($"swFeaturename:{swFeature.Name}");
                        if (swFeature.GetTypeName2() == "SheetMetal")
                        {
                            SheetMetalFeatureData swSheetMetalData = (SheetMetalFeatureData)swFeature.GetDefinition();
                            double thickness = Math.Round(swSheetMetalData.Thickness * 1000, 2);

                            Console.WriteLine("厚度:" + thickness);

                        }
                        swFeature = (Feature)swFeature.GetNextFeature();
                    }

                }
                catch (Exception ex)
                {
                    Console.WriteLine($"发生错误: {ex.Message}");
                    Console.WriteLine("提示：请确保 SolidWorks 正在运行。");
                } 
            
            }
            #endregion

        }
    }
}