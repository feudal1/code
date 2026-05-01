using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System.Text;

namespace tools
{
    public class get_thickness_from_solidfolder
    {

        static public double run(ModelDoc2 swModel)
        {
            try
            {
 
              
                Feature swFeature = (Feature)swModel.FirstFeature();

                while (swFeature != null)
                {
                    if (swFeature.GetTypeName2() == "SolidBodyFolder")
                    {
                        IBodyFolder swBodyFolder = (IBodyFolder)swFeature.GetSpecificFeature2();

                        // 获取实体数量
                        int bodyCount = swBodyFolder.GetBodyCount();
                        Debug.WriteLine("实体数量：" + bodyCount);

                        // 遍历子特征获取自定义属性
                        Feature subfeat = (Feature)swFeature.GetFirstSubFeature();

                        while (subfeat != null)
                        {
                            var manger = subfeat.CustomPropertyManager;

                            object? vPropNames = null;
                            object? vPropTypes = null;
                            object? vPropValues = null;
                            object? resolved = null;
                            object? linkProp = null;

                            manger.GetAll3(ref vPropNames, ref vPropTypes, ref vPropValues, ref resolved, ref linkProp);

                            string[] propValues = (string[])vPropValues;
                            string[] propNames = (string[])vPropNames;

                            // 查找厚度相关属性
                            for (int j = 0; j < propNames.Length; j++)
                            {
                                if (propNames[j] == "厚度" || propNames[j] == "Thickness" || propNames[j] == "钣金厚度")
                                {
                                    if (double.TryParse(propValues[j], out double thickness))
                                    {
                                        thickness = Math.Round(thickness, 2);
                                        Console.WriteLine("厚度：" + thickness);
                                        return thickness;
                                    }
                                }
                            }

                            subfeat = (Feature)subfeat.GetNextSubFeature();
                        }
                        return 0;

                    }

                    swFeature = (Feature)swFeature.GetNextFeature();
                }

                Console.WriteLine("未找到板厚信息");
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