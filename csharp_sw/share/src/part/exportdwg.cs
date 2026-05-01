using System;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System.Text;
namespace tools
{
    public class Exportdwg
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
                string folderName = Path.GetFileName(directory);
                string outputRootName = string.IsNullOrWhiteSpace(folderName) ? "钣金" : $"{folderName}钣金";
                string outputfile = Path.Combine(directory, outputRootName, thickness);
                if (!Directory.Exists(outputfile))
                {
                    Directory.CreateDirectory(outputfile);
                }
                string dwgFileName = Path.Combine(outputfile, Path.GetFileNameWithoutExtension(fullPath) + ".dwg");
                double[] dataAlignment = new double[12];
                dataAlignment[0] = 0.0;
                dataAlignment[1] = 0.0;
                dataAlignment[2] = 0.0;
                dataAlignment[3] = 1.0;
                dataAlignment[4] = 0.0;
                dataAlignment[5] = 0.0;
                dataAlignment[6] = 0.0;
                dataAlignment[7] = 1.0;
                dataAlignment[8] = 0.0;
                dataAlignment[9] = 1.0;
                dataAlignment[10] = 0.0;
                dataAlignment[11] = 0.0;
                int options; options = 97;
                swPart.ExportToDWG(dwgFileName, fullPath, (int)swExportToDWG_e.swExportToDWG_ExportSheetMetal, true, dataAlignment, false, false, options, null);



                Console.WriteLine($"成功！生成文档保存在：{dwgFileName}");
                return dwgFileName;

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