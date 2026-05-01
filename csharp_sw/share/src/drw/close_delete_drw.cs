using System;
using System.IO;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace tools
{
    public class close_delete_drw
    {
        public static void run(SldWorks swApp, ModelDoc2 swModel)
        {
            try
            {
                if (swModel == null)
                {
                    Console.WriteLine("错误：没有活动文档。");
                    return;
                }

                if (swModel.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
                {
                    Console.WriteLine("错误：当前文档不是工程图。");
                    return;
                }

                string drwPath = swModel.GetPathName();
                if (string.IsNullOrWhiteSpace(drwPath))
                {
                    Console.WriteLine("错误：当前工程图未保存，无法删除文件。");
                    return;
                }

                string docTitle = swModel.GetTitle();
                Console.WriteLine($"准备关闭工程图：{docTitle}");
                swApp.CloseDoc(docTitle);

                if (!File.Exists(drwPath))
                {
                    Console.WriteLine($"文件不存在（可能已被删除）：{drwPath}");
                    return;
                }

                File.Delete(drwPath);
                Console.WriteLine($"已删除工程图文件：{drwPath}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"关闭并删除工程图失败：{ex.Message}");
            }
        }
    }
}
