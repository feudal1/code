using Autodesk.AutoCAD.Interop;
using Autodesk.AutoCAD.Interop.Common;
namespace cad_tools
{
    public class dxf2dwg
    {
static public void run(string filePath)
{
    AcadApplication? acadApp = CadConnect.GetOrCreateInstance();
    if (acadApp == null)
    {
        Console.WriteLine("错误：无法连接到 AutoCAD 应用程序。");
        return;
    }

  
    string targetPath =filePath.Replace(".DXF", ".dwg").Replace(".dxf", ".dwg");
if (File.Exists(targetPath))
{
    Console.WriteLine($"{targetPath}已存在");
    return;
}
    try
    {
        // 2. 打开源文件 (DXF)
        AcadDocument acadDoc = acadApp.Documents.Open(filePath);

        // 3. 另存为 DWG 格式
        // 第二个参数为文件格式常量，null 表示当前格式，也可指定具体版本如 AcadSaveAsType.ac2018_dwg
        acadDoc.SaveAs(targetPath, AcSaveAsType.ac2000_dwg);
         acadDoc.Close(true);
        Console.WriteLine($"转换完成{targetPath}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"发生错误：{ex.Message}");
    }
}

}}