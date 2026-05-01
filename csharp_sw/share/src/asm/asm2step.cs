namespace tools;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
public class one2step
{
    static public int run(ModelDoc2 swModel, string? rootFolderName = null)
    {
        string fullPath = swModel.GetPathName();
        string partname = Path.GetFileNameWithoutExtension(fullPath);
        
        if (string.IsNullOrEmpty(fullPath))
        {
            Console.WriteLine("错误：文档尚未保存，请先保存文件。");
            return 0;
        }
                
        // 获取当前文件所在目录
        string? currentDirectory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrWhiteSpace(currentDirectory))
        {
            Console.WriteLine("错误：无法获取文件所在目录。");
            return 0;
        }

        // 构建完整的输出路径
        string outputPath;
        bool isSquareTube = partname.Contains("方管");
        string effectiveRootName = rootFolderName;
        if (string.IsNullOrWhiteSpace(effectiveRootName))
        {
            effectiveRootName = Path.GetFileName(currentDirectory);
        }
        string safeRootName = SanitizeFolderName(effectiveRootName ?? string.Empty);
        string targetFolderName = isSquareTube ? $"{safeRootName}方管" : $"{safeRootName}钣金";
        outputPath = Path.Combine(currentDirectory, targetFolderName, $"{partname}.STEP");
        
        string outputDirectory = Path.GetDirectoryName(outputPath);
        if (!Directory.Exists(outputDirectory))
        {
            Directory.CreateDirectory(outputDirectory);
        }       
        var result=swModel.SaveAs3(outputPath, 0, 2);
                
        Console.WriteLine($"{result}，已导出 STEP 文件到：{outputPath}");
        return 1 ;
    }

    static private string SanitizeFolderName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "导出";
        }

        string result = name.Trim();
        foreach (char c in Path.GetInvalidFileNameChars())
        {
            result = result.Replace(c, '_');
        }

        return string.IsNullOrWhiteSpace(result) ? "导出" : result;
    }
  
  
}