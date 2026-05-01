using Autodesk.AutoCAD.Interop;
using Autodesk.AutoCAD.Interop.Common;
using System;
namespace cad_tools
{
    public class get_all_dim_style
    {
static public void run()
{
    AcadApplication? acadApp = CadConnect.GetOrCreateInstance();
    if (acadApp == null)
    {
        Console.WriteLine("错误：无法连接到 AutoCAD 应用程序。");
        return;
    }


    try
    {
        // 打开源文件 (DXF)
        AcadDocument acadDoc = acadApp.ActiveDocument;
        
        // 获取所有标注样式
        AcadDimStyles dimStyles = acadDoc.DimStyles;
        
        Console.WriteLine($"找到 {dimStyles.Count} 个标注样式");
        
        // 遍历所有标注样式
        foreach (AcadDimStyle dimStyle in dimStyles)
        {
            string oldName = dimStyle.Name;
          
            string newName = $"{oldName}_{ acadDoc.Name}";
            
            try
            {
                dimStyle.Name = newName;
                Console.WriteLine($"重命名：{oldName} -> {newName}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"重命名失败 {oldName}: {ex.Message}");
            }
        }
        acadDoc.Save();
        
        Console.WriteLine("标注样式重命名完成");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"发生错误：{ex.Message}");
    }
}

}}