using System;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace tools
{
    public class Getcurrentdocname
    {
        static public void run(ModelDoc2 swModel)
        {
            try
            {
                string pathname=swModel.GetPathName();
                Console.WriteLine("当前文档名称："+pathname);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发生错误: {ex.Message}");
                Console.WriteLine("提示：请确保 SolidWorks 正在运行。");
            }
        }
    }
}