using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System.Text;

namespace tools
{
    public class select_part_byname
    {

        static public double run(ModelDoc2 swModel,string partName)
        {
            try
            {
                // 检查是否为装配体文档
                if (swModel.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
                {
                    Console.WriteLine("错误：当前文档不是装配体文件。");
                    return -1;
                }

                var asmdoc = (AssemblyDoc)swModel;
                var component = asmdoc.GetComponentByName(partName);
                
                if (component == null)
                {
                    Console.WriteLine($"错误：未找到名为 '{partName}' 的零件。");
                    return -1;
                }
                
                component.Select(true);
                Console.WriteLine($"成功选中零件：{partName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发生错误：{ex.Message}");
                Console.WriteLine("提示：请确保 SolidWorks 正在运行。");
                return -1;
            }

            return 0;
        }

    }
}