using System;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace tools
{
    public class close_current_doc
    {
        static public void run(SldWorks swApp, ModelDoc2 swModel)
        {
            try
            {
             swApp.CloseDoc(swModel.GetPathName());
                Console.WriteLine("成功关闭");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发生错误：{ex.Message}");
                Console.WriteLine("提示：请确保 SolidWorks 正在运行。");
            }
        }
    }
}