using Autodesk.AutoCAD.Interop;
using Autodesk.AutoCAD.Interop.Common;
using System;
using System.IO;
using System.Windows.Forms;

namespace cad_tools
{
    public class draw_divider
    {

        /// <summary>
        /// 在指定位置写入文字
        /// </summary>
        /// <param name="text">要写入的文字</param>
        /// <param name="insertionPoint">插入点坐标 [x, y, z]</param>
        /// <param name="height">文字高度</param>
        static private void add_text(string text, double[] insertionPoint, double height)
        {
            AcadApplication? acadApp = CadConnect.GetOrCreateInstance();
            if (acadApp == null)
            {
                Console.WriteLine("错误：无法连接到 AutoCAD 应用程序。");
                return;
            }
            
            // 确保有活动文档
            if (acadApp.ActiveDocument == null)
            {
                Console.WriteLine("警告：当前没有活动的 AutoCAD 文档，尝试创建新文档...");
                try
                {
                    // 如果没有活动文档，创建一个新文档
                    acadApp.Documents.Add("");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"创建新文档失败：{ex.Message}");
                    return;
                }
            }

            try
            {
                var textEntity = acadApp.ActiveDocument.ModelSpace.AddText(
                    text,
                    insertionPoint,
                    height
                );
                textEntity.Alignment = AcAlignment.acAlignmentLeft;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"添加文字失败：{ex.Message}");
            }
        }

        /// <summary>
        /// 递归处理文件夹（支持多层嵌套子文件夹）
        /// </summary>
        /// <param name="folderPath">文件夹路径</param>
        /// <param name="currentX">当前 X 起始位置</param>
        /// <param name="currentY">当前 Y 起始位置</param>
        /// <param name="partSpacing">零件间距</param>
        /// <param name="folderSpacing">文件夹间距</param>
        /// <param name="textHeight">文字高度</param>
        /// <param name="textOffsetY">文字 Y 偏移</param>
        /// <returns>返回占用区域的右上角坐标 [maxX, maxY]</returns>
        static private double[] process_folder_recursive(
            string folderPath,
            double currentX,
            double currentY,
            double partSpacing,
            double folderSpacing,
            double textHeight,
            double textOffsetY)
        {
            AcadApplication? acadApp = CadConnect.GetOrCreateInstance();
            if (acadApp == null)
            {
                Console.WriteLine($"错误：无法获取 AutoCAD 实例，跳过文件夹 {folderPath}");
                return new double[] { currentX, currentY };
            }
            
            // 确保有活动文档
            if (acadApp.ActiveDocument == null)
            {
                Console.WriteLine($"警告：当前没有活动的 AutoCAD 文档，尝试创建新文档...");
                try
                {
                    acadApp.Documents.Add("");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"创建新文档失败：{ex.Message}，跳过文件夹 {folderPath}");
                    return new double[] { currentX, currentY };
                }
            }

            double folderStartX = currentX;
            double folderCurrentMaxY = currentY;
            
     
            // 2. 递归处理所有子文件夹
            var subDirectories = Directory.GetDirectories(folderPath);
            foreach (var subDir in subDirectories)
            {
                Console.WriteLine($"\n>> 进入子文件夹：{Path.GetFileName(subDir)}");
                
                var result = process_folder_recursive(
                    subDir,
                    folderStartX,  // 每个子文件夹都从相同的 X 起始位置开始
                    currentY,      // 从当前最大 Y 之后开始
                    partSpacing,
                    folderSpacing,
                    textHeight,
                    textOffsetY);
                
                // 更新全局最大 Y
                if (result[1] > folderCurrentMaxY)
                {
                    folderCurrentMaxY = result[1];
                }
                
                // 下一个子文件夹需要从当前最大 Y + 间距开始
                currentY = folderCurrentMaxY + folderSpacing;
            }
            
            // 1. 先处理当前文件夹中的 DWG 文件（如果有的话）
            var dwgFiles = Directory.GetFiles(folderPath, "*.dwg");
            if (dwgFiles.Length > 0)
            {
                Console.WriteLine($"\n处理文件夹：{Path.GetFileName(folderPath)} 中的 {dwgFiles.Length} 个 DWG 文件");
                
                double dwgCurrentX = currentX;
                double dwgCurrentMaxY = currentY;
                
                foreach (var dwgFile in dwgFiles)
                {
                    Console.WriteLine($"  插入文件：{Path.GetFileName(dwgFile)}, 位置=({dwgCurrentX:F2}, {currentY:F2})");
                    var maxPoint = merge_dwg.run(dwgFile, dwgCurrentX, currentY, true);
                    
                    if (maxPoint != null)
                    {
                        double partMaxX = maxPoint[0];
                        double partMaxY = maxPoint[1];
                        
                        if (partMaxY > dwgCurrentMaxY)
                        {
                            dwgCurrentMaxY = partMaxY;
                        }
                        
                        dwgCurrentX = partMaxX + partSpacing;
                    }
                }
                
                // 更新当前文件夹的最大 Y
                if (dwgCurrentMaxY > folderCurrentMaxY)
                {
                    folderCurrentMaxY = dwgCurrentMaxY;
                }
                
                // 在 DWG 内容下方写入文件夹名称（仅当有 DWG 文件时）
                double textX = currentX;
                double textY = currentY + textOffsetY;
                add_text(Path.GetFileName(folderPath), new double[] { textX, textY, 0.0 }, textHeight);
                
                Console.WriteLine($"已绘制文件夹 {Path.GetFileName(folderPath)} 的 DWG 区域：X({currentX:F2} 到 {dwgCurrentX:F2}), Y({currentY:F2} 到 {dwgCurrentMaxY:F2})");
                
                // 如果有子文件夹，需要从新的 Y 位置开始
                currentY = dwgCurrentMaxY + folderSpacing;
            }

            return new double[] { folderStartX, folderCurrentMaxY };
        }

        /// <summary>
        /// 处理文件夹遍历并为每个文件夹的 DWG 文件绘制边界框（支持多层嵌套子文件夹）
        /// </summary>
        /// <param name="folderPath">可选的文件夹路径，如果不提供则弹出选择对话框</param>
        static public void process_subfolders_with_divider(string folderPath = null)
        {
            string selectedFolder = folderPath;
            
            // 如果没有提供路径，则需要用户选择
            if (string.IsNullOrEmpty(selectedFolder))
            {
                // 在非 STA 线程上创建新线程来执行 UI 操作
                if (System.Threading.Thread.CurrentThread.GetApartmentState() != System.Threading.ApartmentState.STA)
                {
                    var tcs = new System.Threading.Tasks.TaskCompletionSource<string?>();
                    var thread = new System.Threading.Thread(() =>
                    {
                        try { tcs.SetResult(FolderPicker.SelectFolder()); }
                        catch (Exception ex) { tcs.SetException(ex); }
                    });
                    thread.SetApartmentState(System.Threading.ApartmentState.STA);
                    thread.Start();
                    
                    selectedFolder = tcs.Task.Result;
                }
                else
                {
                    // 直接在当前 STA 线程执行
                    selectedFolder = FolderPicker.SelectFolder();
                }
                
                if (string.IsNullOrEmpty(selectedFolder))
                {
                    Console.WriteLine("未选择文件夹，操作取消。");
                    return;
                }
            }
            
            ProcessFolderLogic(selectedFolder);
        }

        /// <summary>
        /// 实际的文件夹处理逻辑
        /// </summary>
        static private void ProcessFolderLogic(string selectedFolder)
        {
            double partSpacing = 15.0;
            double folderSpacing = 100.0;
            double textHeight = 15;
            double textOffsetY = -30.0;
            
            Console.WriteLine($"\n开始处理文件夹：{selectedFolder}");
            Console.WriteLine($"参数设置：零件间距={partSpacing}, 文件夹间距={folderSpacing}, 文字高度={textHeight}");
            
            var result = process_folder_recursive(
                selectedFolder,
                0.0, 0.0,
                partSpacing,
                folderSpacing,
                textHeight,
                textOffsetY);
            
            Console.WriteLine($"\n所有处理完成！总占用区域：X(0 到 {result[0]:F2}), Y(0 到 {result[1]:F2})");
        }

       
    }
}