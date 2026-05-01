using Autodesk.AutoCAD.Interop;
using Autodesk.AutoCAD.Interop.Common;
namespace cad_tools
{
    public class close_cad_doc
    {
static public void run()
{
  
          
            AcadApplication? acadApp =CadConnect.GetOrCreateInstance();
            if (acadApp == null)
            {
                Console.WriteLine("错误：无法连接到 AutoCAD 应用程序。");
                return;
            }
       
                  
                        // 打开文档 (ReadOnly=false, Password="")
                        var doc = acadApp.ActiveDocument;
                       doc.Close();
                        
                      
           
        

   
}

}}