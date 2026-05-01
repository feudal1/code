using System;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace tools
{
    public class Unsupress
    {
        static public void run(ModelDoc2 swModel)
        {
            try
            {
                if (swModel == null)
                {
                    Console.WriteLine("错误null");
                    return;
                }
                if (swModel == null || swModel.GetType() != (int)swDocumentTypes_e.swDocPART)
                {
                    
                    Console.WriteLine("错误：请打开一个 SolidWorks 零件文档 (.sldprt)。");
                    return;
                }

                string fullPath = swModel.GetPathName();
                
                if (string.IsNullOrEmpty(fullPath))
                {
                    Console.WriteLine("错误：文档尚未保存，请先保存文件。");
                    return;
                }

                string? directory = Path.GetDirectoryName(fullPath);
                if (string.IsNullOrEmpty(directory))
                {
                    Console.WriteLine("错误：无法获取文件所在目录。");
                    return;
                }
                PartDoc swPart = (PartDoc)swModel;
                
                string dxfFileName = directory + "\\" + Path.GetFileNameWithoutExtension(fullPath) + ".dxf";
   
                    
               Feature swFeature = (Feature)swModel.FirstFeature();
               while (swFeature != null)
                {
                    if (swFeature.GetTypeName2() == "FlatPattern")
                    {

                    
                        FlatPatternFeatureData swFlatPatt = (FlatPatternFeatureData)swFeature.GetDefinition();

                        string featurename = swFeature.Name;
                        Console.WriteLine("特征名:"+featurename);
                        swModel.Extension.SelectByID2(featurename, "BODYFEATURE", 0, 0, 0, false, 0, null, 0);
                        swModel.EditUnsuppress2();
                        
                
                    
                    }
           
                 swFeature = (Feature)swFeature.GetNextFeature();
                
                }

            Console.WriteLine($"成功！");
            }
            
            catch (Exception ex)
            {
                Console.WriteLine($"发生错误: {ex.Message}");
                Console.WriteLine("提示：请确保 SolidWorks 正在运行。");
            }



        }
    }
}