using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace tools
{
    public class drw2dwg
    {
        static public string run(ModelDoc2 swModel, SldWorks swApp)
        {
            if (swApp == null)
            {
                Console.WriteLine("错误：SolidWorks 应用对象为空。");
                return string.Empty;
            }
            
           
            string fullpath = swModel.GetPathName();
           
            string? directory = Path.GetDirectoryName(fullpath);
            if (string.IsNullOrEmpty(directory))
            {
                Console.WriteLine("错误：无法获取文件所在目录。");
                return string.Empty;
            }
            string outputRoot = DwgOutputPaths.ResolveOutputRoot(directory);
            var pathSettings = DwgOutputPaths.Get();
            
          
         
            Debug.WriteLine($"正在转换Drw为DWG。{fullpath}");
            // 设置自定义映射文件
            if (swApp != null)
            {
                string pluginDir = Path.GetDirectoryName(typeof(drw2dwg).Assembly.Location);
              
                string mapFilePath = Path.Combine(@"C:\Users\Administrator\", "dwgmaping");
              
                if (File.Exists(mapFilePath))
                {
                    Debug.WriteLine($"已设置自定义映射文件。{mapFilePath}");
                    swApp.SetUserPreferenceStringListValue(
                        (int)swUserPreferenceStringListValue_e.swDxfMappingFiles, 
                        mapFilePath);
                    
                  
                }
                else
                {
                    Debug.WriteLine($"错误：无法找到自定义映射文件。{mapFilePath}");

                }
            }

    

          
           var drawingDoc = (DrawingDoc)swModel;
        // if(File.Exists(slddrt))
        // {
        //     bool status = drawingDoc.SetupSheet5("图纸1", 12, 12, 1, 2, true, slddrt, 0.297, 0.21, "默认", true);
        //    Console.WriteLine($"SetupSheet5 status: {status}");
        // }
        
            var swSheet = drawingDoc.IGetCurrentSheet() as Sheet;
            if (swSheet == null)
            {
                Console.WriteLine("错误：当前工程图没有活动图纸。");
                return string.Empty;
            }

            var swViews = swSheet.GetViews() as object[];
            if (swViews == null || swViews.Length < 2)
            {
                Console.WriteLine("错误：当前图纸视图不足，无法识别引用模型。");
                return string.Empty;
            }

            var baseView = swViews[1] as SolidWorks.Interop.sldworks.View;
            var partDoc = baseView?.ReferencedDocument;
            if (partDoc == null)
            {
                Console.WriteLine("错误：当前工程图未找到引用模型。");
                return string.Empty;
            }
            
            swApp.SetUserPreferenceIntegerValue((int)swUserPreferenceIntegerValue_e.swDxfOutputNoScale, 1); 
            
            string outputfile;
            
            // 先判断是否为装配体，是装配体就不是 CNC
            if (partDoc.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                Debug.WriteLine($"{partDoc.GetPathName()},type:assembly");

                outputfile = Path.Combine(outputRoot, pathSettings.WeldmentFolder);
            }
            else
            {
                var thickness = get_thickness.run(partDoc);
                Debug.WriteLine($"{partDoc.GetPathName()},thickness:{thickness}");
                
                if (thickness == 0)
                {
                    
                    outputfile = Path.Combine(outputRoot, pathSettings.CncFolder);
                }
                else
                {
                  
  
                    var meterialDB = "";
                    string meterial=((PartDoc)partDoc).GetMaterialPropertyName( out meterialDB);
                    
                    var meterialthick = thickness.ToString();
                    Debug.WriteLine("meterial:"+meterial);
                    if (meterial.ToLower().Contains("sus"))meterialthick= pathSettings.SusThicknessPrefix + thickness.ToString() ;
                    outputfile = Path.Combine(outputRoot, pathSettings.EngineeringFolder, meterialthick);
                    
                }
            }
            
 
            if (!Directory.Exists(outputfile))
            {
                Directory.CreateDirectory(outputfile);
            }
            string dwgFileName = Path.Combine(outputfile, Path.GetFileNameWithoutExtension(fullpath) + ".dwg");
            int errors=0, warnings=0;
            var result = swModel.SaveAs4(
                dwgFileName, 
                (int)swSaveAsVersion_e.swSaveAsCurrentVersion, 
                (int)swSaveAsOptions_e.swSaveAsOptions_Silent, 
               ref errors, 
                ref warnings);
                
            Console.WriteLine($"{result}，已创建工程图{dwgFileName}");
            return dwgFileName;
        }
    }
}