using System;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System.Text;
namespace tools
{
    public class Exportdxf_flatview
    {

        static public string run(ModelDoc2 swModel, string thickness)
        {
            try
            {
       
                // 后续逻辑不变...
        

                if (swModel == null || swModel.GetType() != (int)swDocumentTypes_e.swDocPART)
                {
                    Console.WriteLine("错误：请打开一个 SolidWorks 零件文档 (.sldprt)。");
                    return "";
                }

                string fullPath = swModel.GetPathName();

                if (string.IsNullOrEmpty(fullPath))
                {
                    Console.WriteLine("错误：文档尚未保存，请先保存文件。");
                    return "";
                }
                string? directory = Path.GetDirectoryName(fullPath);
                if (string.IsNullOrEmpty(directory))
                {
                    Console.WriteLine("错误：无法获取文件所在目录。");
                    return "";
                }
                PartDoc swPart = (PartDoc)swModel;
                string outputfile = directory + "\\" + "下料" + "\\" + thickness;
                if (!Directory.Exists(outputfile))
                {
                    Directory.CreateDirectory(outputfile);
                }
                string dxfFileName = directory + "\\" + "下料" + "\\" + thickness + "\\" + Path.GetFileNameWithoutExtension(fullPath) + ".dwg";
  
               var result=swPart.ExportFlatPatternView(dxfFileName,0);



                Console.WriteLine($"{result}！生成文档保存在：{dxfFileName}");
                return dxfFileName;

            }
            catch (Exception ex)
            {
                Console.WriteLine($"发生错误: {ex.Message}");
                Console.WriteLine("提示：请确保 SolidWorks 正在运行。");

            }
            return "";


        }


    }
}