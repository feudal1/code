using System.Diagnostics;
namespace tools
{
    public class open_cad_doc_by_shell
    {
static public void run(string filePath)
{
  
          
        
           if (File.Exists(filePath))
                    {
                      
                  
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = filePath,
                            UseShellExecute = true
                        });
                        
                        Console.WriteLine($" 已打开：{filePath}");
                    }
                    else
                    {
                        Console.WriteLine($"文件不存在：{filePath}");
                    }
        

   
}

}}