using Autodesk.AutoCAD.Interop;
using Autodesk.AutoCAD.Interop.Common;
namespace cad_tools
{
    public class open_cad_doc_by_name
    {
static public void run(string filePath)
{
  
          
            AcadApplication? acadApp =CadConnect.GetOrCreateInstance();
            if (acadApp == null)
            {
                Console.WriteLine("错误：无法连接到 AutoCAD 应用程序。");
                return;
            }
           if (File.Exists(filePath))
                    {
                      
                  
                        // 打开文档 (ReadOnly=false, Password="")
                        var doc = acadApp.Documents.Open(filePath, false, "");
                        doc.Activate(); // 激活窗口
                        
                        Console.WriteLine($" 已打开：{filePath}");
                    }
                    else
                    {
                        Console.WriteLine($"文件不存在：{filePath}");
                    }
        

   
}

}}