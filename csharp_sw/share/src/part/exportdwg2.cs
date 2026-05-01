//https://help.solidworks.com/2023/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IFlatPatternFeatureData_members.html
//https://help.solidworks.com/2023/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IFeature~GetTypeName.html
//https://help.solidworks.com/2023/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.ISheetMetalFeatureData_members.html
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
namespace tools
{
    public class exportdwg2
    {
        static public bool run(ModelDoc2 swModel, string thickness)
        {
            bool total_success = false;
            try
            {
                                string fullPath = swModel.GetPathName();

                if (string.IsNullOrEmpty(fullPath))
                {
                    Console.WriteLine("错误：文档尚未保存，请先保存文件。");
                    
                }
                string? directory = Path.GetDirectoryName(fullPath);
                if (string.IsNullOrEmpty(directory))
                {
                    Console.WriteLine("错误：无法获取文件所在目录。");
                  
                }
                var pathSettings = DwgOutputPaths.Get();
                PartDoc swPart = (PartDoc)swModel;
                string outputfile = Path.Combine(directory ?? string.Empty, pathSettings.LegacyTestRootFolder, pathSettings.SheetMetalFolder, thickness);
                if (!Directory.Exists(outputfile))
                {
                    Directory.CreateDirectory(outputfile);
                }
               
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

                Feature swFeature = (Feature)swModel.FirstFeature();

               

                while (swFeature != null)
                {
                    if (swFeature.GetTypeName2() == "FlatPattern")
                    {
                         string dwgFileName = Path.Combine(outputfile, swFeature.Name + ".dwg");
                        var swFlatPatt = (FlatPatternFeatureData)swFeature.GetDefinition();
                         swFeature .SetSuppression((int)swFeatureSuppressionAction_e.swUnSuppressFeature);
                         
                        
                         
                         var fixface = (    Face2)swFlatPatt.FixedFace2;
                        var fixface_area=Math.Round( fixface.GetArea()*1000000,2);
                        
                        var subfeat = (Feature)swFeature.GetFirstSubFeature();
                        bool hasbend=false;
                        while (subfeat != null)
                        {

                            Console.WriteLine(subfeat.GetTypeName());
                            if (subfeat.GetTypeName() == "UiBend")
                            {
                                hasbend=true;

                            }
                            subfeat = (Feature)subfeat.GetNextSubFeature();
                        }
                        Face2 bigger_face = fixface;
                        if (!hasbend) {
                        
                            var edgeobjs = (object[])fixface.GetEdges();
                            foreach (var edgeobj in edgeobjs)
                            {
                                var edge = (Edge)edgeobj;
                                var twoface=(object[])edge.GetTwoAdjacentFaces();
                                var other_face=((Face2)twoface[0]).IsSame(fixface) ? (Face2)twoface[1] : (Face2)twoface[0];
                                var other_face_area=Math.Round( other_face.GetArea()*1000000,2);
                                bigger_face=other_face_area > fixface_area ? other_face : fixface;
                                



                            }

                        }
                   
                          
                        var bigger_face_ent = (Entity)bigger_face;
                      
                        var select_result=bigger_face_ent.Select4(true, null);

                        if (select_result)
                        {Console.WriteLine($"选择面成功");
                        }

                        var success=swPart.ExportToDWG(dwgFileName, fullPath, (int)swExportToDWG_e.swExportToDWG_ExportSelectedFacesOrLoops, true, dataAlignment, false, false, options, null);
swFeature .SetSuppression((int)swFeatureSuppressionAction_e.swSuppressFeature);
                        if (success) { total_success = true; Console.WriteLine($"导出成功{dwgFileName}");}

                        
                  
               
                    }
                          swFeature = (Feature)swFeature.GetNextFeature();
                }
              
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发生错误: {ex.Message}");
                Console.WriteLine("提示：请确保 SolidWorks 正在运行。");
            }
            return total_success;
            
        }
    }
}