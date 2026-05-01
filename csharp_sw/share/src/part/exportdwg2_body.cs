//https://help.solidworks.com/2023/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IFlatPatternFeatureData_members.html
//https://help.solidworks.com/2023/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IFeature~GetTypeName.html
//https://help.solidworks.com/2023/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.ISheetMetalFeatureData_members.html
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
namespace tools
{
    public class exportdwg2_body
    {
        private static string? directory = "";
        private static string outputRootFolderName = "钣金";
        private static PartDoc swPart = null;
        private  static string fullPath = "";
        private static string partname = "";
        private  static double[] dataAlignment=new double[12];
        private  static int options;
        private static int successcount = 0;
        
        
                static public void exportfeature(Body2 body,ModelDoc2 swModel)
                {
                    string outputfile = "";
                    string thickness = "无";
                    object[] features = (object[])body.GetFeatures();
                
                        
                 
                    Feature swFeature2 = (Feature)features[features.Length-1];
                    
                    if (swFeature2.GetTypeName2() == "FlatPattern")
                    {
                        Feature swFeature = (Feature)features[0];
                   
                        if (swFeature.GetTypeName2() == "SheetMetal")
                        {
                            SheetMetalFeatureData swSheetMetalData = (SheetMetalFeatureData)swFeature.GetDefinition();
                            thickness =  Math.Round(swSheetMetalData.Thickness*1000,2).ToString();
                            var pathSettings = DwgOutputPaths.Get();
                            string materialThickness = DwgOutputPaths.BuildMaterialThicknessFolderName(swModel, thickness);
                            outputfile = Path.Combine(directory ?? string.Empty, outputRootFolderName, pathSettings.SheetMetalFolder, materialThickness);
                    
                        }
                        if (!Directory.Exists(outputfile))
                        {
                            Directory.CreateDirectory(outputfile);
                        }
                         string dwgFileName = Path.Combine(outputfile, partname+"_"+body.Name + ".dwg");
                        var swFlatPatt = (FlatPatternFeatureData)swFeature2.GetDefinition();
                         swFeature2 .SetSuppression((int)swFeatureSuppressionAction_e.swUnSuppressFeature);
                         // 展开后检查模型错误
                         ModelDocExtension swModelDocExt = (ModelDocExtension)swModel.Extension;
                         int nbrWhatsWrong = swModelDocExt.GetWhatsWrongCount();
                         
                         if (nbrWhatsWrong > 0)
                         { 
                             Console.WriteLine($"展开后模型存在 {nbrWhatsWrong} 个错误 ，{swModel.GetTitle()}");
                             
                             // 输出具体错误信息
                             for (int i = 0; i < nbrWhatsWrong; i++)
                             {
                                 object feature = null;
                                 object errorCode = null;
                                 object description = null;
                                 swModelDocExt.GetWhatsWrong(out feature, out errorCode, out description);
                                 Console.WriteLine($"  错误 {i + 1}: {description}");
                             }
                         }
                         var fixface = (    Face2)swFlatPatt.FixedFace2;
                        var fixface_area=Math.Round( fixface.GetArea()*1000000,2);
                        
                        var subfeat = (Feature)swFeature2.GetFirstSubFeature();
                        bool hasbend=false;
                        while (subfeat != null)
                        {

                            Debug.WriteLine(subfeat.GetTypeName());
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
                        {Debug.WriteLine($"选择面成功");
                        }

                        var success=swPart.ExportToDWG(dwgFileName, fullPath, (int)swExportToDWG_e.swExportToDWG_ExportSelectedFacesOrLoops, true, dataAlignment, false, false, options, null);
swFeature2 .SetSuppression((int)swFeatureSuppressionAction_e.swSuppressFeature);
                        Console.WriteLine($"{success},导出{dwgFileName}");
                        if (success) { successcount++; }

                        
                  
               
                    }
                      
                   
                  
                 
                    
                       
                
              
        }
        static public int run(ModelDoc2 swModel, string? rootFolderName = null)
        {
           
            try
            {
                successcount = 0;
                fullPath = swModel.GetPathName();
                partname = Path.GetFileNameWithoutExtension(fullPath);

                if (string.IsNullOrEmpty(fullPath))
                {
                    Console.WriteLine("错误：文档尚未保存，请先保存文件。");
                    
                }
                directory = Path.GetDirectoryName(fullPath);
                if (string.IsNullOrEmpty(directory))
                {
                    Console.WriteLine("错误：无法获取文件所在目录。");
                  
                }
                outputRootFolderName = BuildOutputRootFolderName(rootFolderName, directory);
                swPart = (PartDoc)swModel;
    
               
         
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
                 options = 97;

               

                var partdoc = (PartDoc)swModel;
                var bodys = (object[])partdoc.GetBodies2((int)swBodyType_e.swSolidBody, false);

                foreach (var objbody in bodys)
                {
                    var body = (Body2)objbody;
                   
                   
                        exportfeature(body, swModel);
                    
                  
                    
                }
                
                
                

            }
            catch (Exception ex)
            {
                Console.WriteLine($"发生错误: {ex.Message}");
                Console.WriteLine("提示：请确保 SolidWorks 正在运行。");
            }
        
            return successcount;
        }

        private static string BuildOutputRootFolderName(string? rootFolderName, string? currentDirectory)
        {
            if (!string.IsNullOrWhiteSpace(rootFolderName))
            {
                string result = rootFolderName.Trim();
                foreach (char c in Path.GetInvalidFileNameChars())
                {
                    result = result.Replace(c, '_');
                }

                return string.IsNullOrWhiteSpace(result) ? "钣金" : result;
            }
            string rootPath = DwgOutputPaths.ResolveOutputRoot(currentDirectory ?? string.Empty);
            string resolved = Path.GetFileName(rootPath);
            return string.IsNullOrWhiteSpace(resolved) ? DwgOutputPaths.Get().FallbackRootName : resolved;
        }
    }
}