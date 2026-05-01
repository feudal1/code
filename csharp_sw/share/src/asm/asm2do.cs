using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml.Linq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace tools
{
    public class asm2do
    {
        static private Dictionary<string, int> docnameCount = new Dictionary<string, int>();
        static private Dictionary<string, int> dwgExportCount = new Dictionary<string, int>();
        static private readonly object _swApiLock = new object();
        static private string[] currentFilterKeywords = null;

        public delegate int SolidWorksAction( ModelDoc2 swModel,SldWorks swApp=null);

        static public string[]? run(SldWorks swApp, ModelDoc2 swModel, SolidWorksAction action, string[] filterKeywords = null)
        {
            try
            {
                // 清空上次的统计结果
                docnameCount.Clear();
                dwgExportCount.Clear();

                if (swModel == null)
                {
                    Console.WriteLine("错误：没有打开任何文档。");
                    return null;
                }

                // 确保是装配体
                if (swModel.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
                {
                    Console.WriteLine("错误：当前文档不是装配体。");
                    return null;
                }

                Console.WriteLine("=== 从BOM缓存清单获取零件信息 ===");

                if (!asm2bom.HasGeneratedPartList())
                {
                    Console.WriteLine("BOM清单未生成，先执行一次 asm2bom 生成清单...");
                    int bomResult = asm2bom.run(swApp, swModel, "零件", false).GetAwaiter().GetResult();
                    if (bomResult != 0)
                    {
                        Console.WriteLine("错误：无法通过 asm2bom 生成零件清单。");
                        return null;
                    }
                }

                List<BomPartExportInfo> bomParts = asm2bom.GetLastBomParts();
                Console.WriteLine($"BOM缓存共 {bomParts.Count} 个零件");

                // 存储需要处理的零件信息：零件路径 -> 零件名称
                Dictionary<string, string> partsToProcess = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (BomPartExportInfo bomPart in bomParts)
                {
                    if (bomPart == null || string.IsNullOrEmpty(bomPart.PartPath))
                    {
                        continue;
                    }

                    string partName = bomPart.PartName ?? Path.GetFileNameWithoutExtension(bomPart.PartPath);
                    if (string.IsNullOrEmpty(partName))
                    {
                        partName = Path.GetFileNameWithoutExtension(bomPart.PartPath);
                    }

                    // 如果有关键字筛选，检查是否匹配
                    if (filterKeywords != null && filterKeywords.Length > 0)
                    {
                        bool shouldProcess = false;
                        foreach (string keyword in filterKeywords)
                        {
                            if (!string.IsNullOrEmpty(keyword) && partName.Contains(keyword))
                            {
                                shouldProcess = true;
                                break;
                            }
                        }

                        if (!shouldProcess)
                        {
                            continue;
                        }
                    }

                    string resolvedPath = ResolvePartPath(bomPart.PartPath);
                    if (!partsToProcess.ContainsKey(resolvedPath))
                    {
                        partsToProcess[resolvedPath] = partName;
                        Debug.WriteLine($"[BOM缓存] 找到零件: {partName}, 路径: {resolvedPath}");
                    }
                }
                
                Console.WriteLine($"筛选后共需处理 {partsToProcess.Count} 个零件");
                
                if (partsToProcess.Count == 0)
                {
                    Console.WriteLine("没有找到需要处理的零件。");
                    return new string[0];
                }
                
                int processedCount = 0;
                int totalCount = partsToProcess.Count;
                
                // 处理每个零件
                foreach (var kvp in partsToProcess)
                {
                    string partPath = kvp.Key;
                    string partName = kvp.Value;
                    
                    processedCount++;
                    Console.WriteLine($"[{processedCount}/{totalCount}] 正在处理: {partName}");
                    
                    // 打开零件文档
                    int openErrors = 0;
                    int openWarnings = 0;
                    ModelDoc2 partDoc = (ModelDoc2)swApp.OpenDoc6(
                        partPath, 
                        (int)swDocumentTypes_e.swDocPART, 
                        (int)swOpenDocOptions_e.swOpenDocOptions_Silent, 
                        "", 
                        ref openErrors, 
                        ref openWarnings
                    );
                    
                    if (partDoc != null)
                    {
                        partDoc.Visible = true;
                        
                        // 执行操作
                        int exportedCount = action.Invoke(partDoc, swApp);
                        
                        // 记录统计信息
                        if (!docnameCount.ContainsKey(partPath))
                        {
                            docnameCount[partPath] = 0;
                        }
                        docnameCount[partPath]++;
                        
                        if (!dwgExportCount.ContainsKey(partPath))
                        {
                            dwgExportCount[partPath] = 0;
                        }
                        dwgExportCount[partPath] = exportedCount;
                        
                        // 关闭文档
                        swApp.CloseDoc(partPath);
                        
                        Console.WriteLine($"进度: {processedCount}/{totalCount} ({(processedCount * 100.0 / totalCount):F1}%)");
                    }
                    else
                    {
                        Console.WriteLine($"警告：无法打开零件 {partName}，路径: {partPath}，errors={openErrors}, warnings={openWarnings}");
                    }
                }

                Console.WriteLine("\n========== 零件统计信息 ==========");
                foreach (var kvp in docnameCount.OrderBy(x => x.Key))
                {
                    string partName = Path.GetFileName(kvp.Key);
                    int refCount = kvp.Value;
                    int exportCount = dwgExportCount.ContainsKey(kvp.Key) ? dwgExportCount[kvp.Key] : 0;
                    Console.WriteLine($"{partName}: 引用 {refCount} 次，导出 {exportCount} 个文件");
                }
                Console.WriteLine($"\n总计：{docnameCount.Count} 个零件，共导出 {dwgExportCount.Values.Sum()} 个文件");
                Console.WriteLine("====================================\n");

                List<string> resultList = new List<string>(docnameCount.Keys);
                return resultList.ToArray();

            }
            catch (Exception ex)
            {
                Console.WriteLine($"发生错误: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }

            return null;
        }

        /// <summary>
        /// 清理文件路径：去掉末尾的"-数字"后缀
        /// </summary>
        static private string ResolvePartPath(string pathName)
        {
            if (string.IsNullOrWhiteSpace(pathName))
            {
                return string.Empty;
            }

            string originalPath = pathName.Trim().Replace(@"\\", "/");
            if (File.Exists(originalPath))
            {
                return originalPath;
            }

            // 兼容历史逻辑：仅在原始路径不存在时，尝试去掉末尾 "-数字" 后缀
            string fileName = Path.GetFileName(originalPath);
            string directory = Path.GetDirectoryName(originalPath);
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            string extension = Path.GetExtension(fileName);

            int lastDashIndex = fileNameWithoutExt.LastIndexOf('-');
            if (lastDashIndex > 0 && lastDashIndex < fileNameWithoutExt.Length - 1)
            {
                string suffix = fileNameWithoutExt.Substring(lastDashIndex + 1);
                if (int.TryParse(suffix, out _))
                {
                    string compatName = fileNameWithoutExt.Substring(0, lastDashIndex) + extension;
                    string compatPath = Path.Combine(directory ?? string.Empty, compatName).Replace(@"\\", "/");
                    if (File.Exists(compatPath))
                    {
                        return compatPath;
                    }
                }
            }

            // 文件都不存在时，返回原始路径，便于上层输出准确错误定位
            return originalPath;
        }
        
        /// <summary>
        /// 根据零件名称查找对应的组件
        /// </summary>
        static private Component2 FindComponentByName(AssemblyDoc swAssembly, string partName)
        {
            object[] allComponents = (object[])swAssembly.GetComponents(false);
            
            foreach (object compObj in allComponents)
            {
                Component2 component = (Component2)compObj;
                string componentName = component.Name2;
                
                // 去掉"/"号及之前的文字
                int slashIndex = componentName.LastIndexOf('/');
                if (slashIndex >= 0 && slashIndex < componentName.Length - 1)
                {
                    componentName = componentName.Substring(slashIndex + 1);
                }
                
                // 去掉末尾的"-数字"部分
                int lastDashIndex = componentName.LastIndexOf('-');
                if (lastDashIndex > 0 && lastDashIndex < componentName.Length - 1)
                {
                    string suffix = componentName.Substring(lastDashIndex + 1);
                    if (int.TryParse(suffix, out _))
                    {
                        componentName = componentName.Substring(0, lastDashIndex);
                    }
                }
                
                // 比较名称
                if (componentName.Equals(partName, StringComparison.OrdinalIgnoreCase))
                {
                    return component;
                }
            }
            
            return null;
        }
    }
}

