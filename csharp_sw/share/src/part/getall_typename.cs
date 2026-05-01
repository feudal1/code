using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System.Text;

namespace tools
{
    public class get_all_typename
    {

        static public double run(ModelDoc2 swModel)
        {
            try
            {
 
              
                Feature swFeature = (Feature)swModel.FirstFeature();

                while (swFeature != null)
                {
                    Console.WriteLine(swFeature.Name + " [" + swFeature.GetTypeName2() + "]");
                      var swSubFeat = (Feature)swFeature.GetFirstSubFeature();
 
                    while ((swSubFeat != null))
                    {
                        
                      
                        
                            Console.WriteLine(swSubFeat.Name + " [" + swSubFeat.GetTypeName2() + "]");
                           var swSubSubFeat = (Feature)swSubFeat.GetFirstSubFeature();

                        

                        swSubFeat = (Feature)swSubFeat.GetNextSubFeature();
                    }



                    swFeature = (Feature)swFeature.GetNextFeature();
                }

          
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发生错误：{ex.Message}");
                Console.WriteLine("提示：请确保 SolidWorks 正在运行。");
            }

            return 0;
        }

    }
}