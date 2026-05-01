using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System.Text;
namespace tools
{
    public class get_thickness
    {
        static public double run(ModelDoc2 swModel)
        {
       
            try
            {

                Feature swFeature = (Feature)swModel.FirstFeature();
                while (swFeature != null)
                {
                    if (swFeature.GetTypeName2() == "SheetMetal")
                    {
                        SheetMetalFeatureData swSheetMetalData = (SheetMetalFeatureData)swFeature.GetDefinition();
                        double thickness = Math.Round(swSheetMetalData.Thickness*1000,2);

                        Console.WriteLine("厚度:"+thickness);
                        Debug.WriteLine("厚度:"+thickness);
                        return thickness;
                    }
                          swFeature = (Feature)swFeature.GetNextFeature();
                }
              
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发生错误: {ex.Message}");
                Console.WriteLine("提示：请确保 SolidWorks 正在运行。");
            }
              return 0;
        }
    }
}