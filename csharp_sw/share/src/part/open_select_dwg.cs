
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

using System.Diagnostics;
namespace tools
{
    public class open_select_dwg
    {
        static public void run(ModelDoc2 swModel,Body2 body)
        {
           
          
            var features=(object[])body.GetFeatures();
            var swFeature=(IFeature)(features[0]);
            
            string outputfile="";
            var fullPath = swModel.GetPathName();
            var  partname = Path.GetFileNameWithoutExtension(fullPath);

            if (string.IsNullOrEmpty(fullPath))
            {
                Console.WriteLine("错误：文档尚未保存，请先保存文件。");
                    
            }
            var directory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrEmpty(directory))
            {
                Console.WriteLine("错误：无法获取文件所在目录。");
                  
            }
            Console.WriteLine("get_select_type");
            if (swFeature.GetTypeName2() == "SheetMetal")
            {
                var pathSettings = DwgOutputPaths.Get();
                SheetMetalFeatureData swSheetMetalData = (SheetMetalFeatureData)swFeature.GetDefinition();
                var thickness =  Math.Round(swSheetMetalData.Thickness*1000,2).ToString();
                string outputRoot = DwgOutputPaths.ResolveOutputRoot(directory);
                outputfile = Path.Combine(outputRoot, pathSettings.SheetMetalFolder, thickness);
                if (!Directory.Exists(outputfile))
                {
                    Directory.CreateDirectory(outputfile);
                }
            }
 

           var dwgFileName = Path.Combine(outputfile, partname + "_" + body.Name + ".dwg");
            if(File.Exists(dwgFileName))
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = dwgFileName,
                    UseShellExecute = true
                });
                Console.WriteLine($"已打开：{dwgFileName}");
            }
            else
            {
                Console.WriteLine($"没有 dxf,{dwgFileName}");
            }
        }
    }
}