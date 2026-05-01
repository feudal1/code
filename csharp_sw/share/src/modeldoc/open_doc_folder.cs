using System;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace tools
{
    public class open_doc_folder
    {
        static public void run(ModelDoc2 swModel)
        {
            try
            {
                string pathname=swModel.GetPathName();
                string directory = Path.GetDirectoryName(pathname)!;
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
{
    FileName = directory,
    UseShellExecute = true
});
Console.WriteLine($"已打开文档所在文件夹。{directory}");

                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发生错误: {ex.Message}");
                Console.WriteLine("提示：请确保 SolidWorks 正在运行。");
            }
        }
    }
}