//https://help.solidworks.com/2023/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.ITableAnnotation_members.html
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;

namespace tools
{
    public class BomPartExportInfo
    {
        public string PartName { get; set; } = string.Empty;
        public string PartPath { get; set; } = string.Empty;
    }

    /// <summary>任务窗格零件列表行（与装配体 BOM 导出逻辑同源，但不经过 asm2bom 的缓存与任务窗格回调）。</summary>
    public class TaskPaneBomPartRow
    {
        public string PartName { get; set; } = string.Empty;
        public string PartType { get; set; } = string.Empty;
        public string Dimension { get; set; } = string.Empty;
        public string IsDrawn { get; set; } = "未出图";
        public string Quantity { get; set; } = string.Empty;
    }

    public class asm2bom
    {
        private static readonly object _bomCacheLock = new object();
        private static readonly object _topologyMatchLock = new object();
        private static readonly object _topologyResultCacheLock = new object();
        private static readonly Dictionary<string, bool> _tubeTopologyMatchCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        /// <summary>任务窗格专用采集模式结束时，由 <see cref="CollectPartRowsForTaskPaneAsync"/> 读取。</summary>
        private static List<TaskPaneBomPartRow>? _lastTaskPaneCollection;

        private static List<BomPartExportInfo> _lastBomParts = new List<BomPartExportInfo>();
        private static bool _hasGeneratedPartList = false;

        public static bool HasGeneratedPartList()
        {
            lock (_bomCacheLock)
            {
                return _hasGeneratedPartList;
            }
        }

        public static List<BomPartExportInfo> GetLastBomParts()
        {
            lock (_bomCacheLock)
            {
                return new List<BomPartExportInfo>(_lastBomParts);
            }
        }

        private static void SetLastBomParts(List<BomPartExportInfo> parts)
        {
            lock (_bomCacheLock)
            {
                _lastBomParts = parts ?? new List<BomPartExportInfo>();
                _hasGeneratedPartList = true;
            }
        }

        public static void ResetPartListCache()
        {
            lock (_bomCacheLock)
            {
                _lastBomParts = new List<BomPartExportInfo>();
                _hasGeneratedPartList = false;
            }
        }
        
        /// <summary>
        /// 运行BOM导出
        /// </summary>
        /// <param name="swApp">SolidWorks应用</param>
        /// <param name="swModel">模型文档</param>
        /// <param name="pardoc">导出类型：钣金件/管件/装配体/零件</param>
        /// <param name="exportExcel">是否导出并打开Excel（默认true）</param>
        static public async Task<int> run(
            SldWorks swApp,
            ModelDoc2 swModel,
            string pardoc,
            bool exportExcel = true,
            bool taskPaneCollectionMode = false,
            bool writePartCustomPropertiesInTaskPaneMode = false)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                swApp.CommandInProgress = true;
                if (!taskPaneCollectionMode)
                {
                    ResetPartListCache();
                }
                var bomPartsForExport = new Dictionary<string, BomPartExportInfo>(StringComparer.OrdinalIgnoreCase);
                Console.WriteLine("=== 开始BOM导出 ===");
                // 确保是装配体
                if (swModel.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
                {
                    Console.WriteLine("错误：当前文档不是装配体。");
                    return -1;
                }
                string fullPath = swModel.GetPathName();

                if (string.IsNullOrEmpty(fullPath))
                {
                    Console.WriteLine("错误：文档尚未保存，请先保存文件。");
                    return -1;
                }
                string assemblyDirectory = Path.GetDirectoryName(fullPath) ?? string.Empty;
                string assemblyFolderName = Path.GetFileName(assemblyDirectory);
                string outputRootName = string.IsNullOrWhiteSpace(assemblyFolderName) ? "钣金" : $"{assemblyFolderName}钣金";

                var drawingSearchRoots = new List<string>();
                string currentDrawingRoot = Path.Combine(assemblyDirectory, outputRootName);
                drawingSearchRoots.Add(currentDrawingRoot);

                ModelDocExtension swModelDocExt = (ModelDocExtension)swModel.Extension;
                BomTableAnnotation swBOMAnnotation;
                TableAnnotation swTableAnnotation;
                string tableTemplate;
                int bomType;

                // 使用默认的 BOM 模板路径
                tableTemplate = "C:\\Program Files\\SOLIDWORKS Corp\\SOLIDWORKS\\lang\\chinese-simplified\\bom-standard.sldbomtbt";
                
                string Configuration = swApp.GetActiveConfigurationName(swModel.GetPathName());
                string exportType = string.IsNullOrWhiteSpace(pardoc) ? "零件" : pardoc.Trim();
                bool isAssemblyMode = string.Equals(exportType, "装配体", StringComparison.OrdinalIgnoreCase);
                bool filterSheetMetal = string.Equals(exportType, "钣金件", StringComparison.OrdinalIgnoreCase);
                bool filterTube = string.Equals(exportType, "管件", StringComparison.OrdinalIgnoreCase);
                bool filterPart = string.Equals(exportType, "零件", StringComparison.OrdinalIgnoreCase);
                bool isValidType = isAssemblyMode || filterSheetMetal || filterTube || filterPart;
                List<string> suspectedUnconvertedSheetMetalParts = new List<string>();
                int materialPropWriteCount = 0;
                int quantityPropWriteCount = 0;
                if (!isValidType)
                {
                    Console.WriteLine($"错误：不支持的导出类型 '{exportType}'，仅支持：钣金件/管件/装配体/零件");
                    return -1;
                }

                // 根据导出类型选择 BOM 类型
                bool DetailedCutList=false;
                if (isAssemblyMode)
                {
                    bomType = (int)swBomType_e.swBomType_Indented;  // 顶层装配体 BOM（包含子装配体）
                }
                else
                {
                    bomType = (int)swBomType_e.swBomType_PartsOnly;  // 仅零件式 BOM
                }
              
                swBOMAnnotation = (BomTableAnnotation)swModelDocExt.InsertBomTable3(
                    tableTemplate, 
                    1260, 
                    556, 
                    bomType, 
                    Configuration, 
                    true, 
                    (int)swNumberingType_e.swNumberingType_Detailed, 
                    DetailedCutList
                );

                if (swBOMAnnotation == null)
                {
                    Console.WriteLine("错误：无法插入 BOM 表。");
                    return -1;
                }

                // 获取 BOM 表注释信息
                swTableAnnotation = (TableAnnotation)swBOMAnnotation;
                var swBOMAnnotation2 = swBOMAnnotation;
                var colunmname = swTableAnnotation.GetColumnTitle2(2, false);
                var deleteresult=swTableAnnotation.DeleteColumn2(2, false);
                swModel.EditRebuild3();
                Console.WriteLine($"deleteresult:{deleteresult},colunmname{colunmname}");
                swTableAnnotation.InsertColumn2((int)swTableItemInsertPosition_e.swTableItemInsertPosition_After,2,"规格尺寸",(int)swInsertTableColumnWidthStyle_e.swInsertColumn_DefaultWidth);
                swTableAnnotation.SetColumnTitle(2,"单套数量");
                swTableAnnotation.InsertColumn2((int)swTableItemInsertPosition_e.swTableItemInsertPosition_After,3,"类别",(int)swInsertTableColumnWidthStyle_e.swInsertColumn_DefaultWidth);
                swTableAnnotation.InsertColumn2((int)swTableItemInsertPosition_e.swTableItemInsertPosition_After,4,"是否出图",(int)swInsertTableColumnWidthStyle_e.swInsertColumn_DefaultWidth);
                swTableAnnotation.InsertColumn2((int)swTableItemInsertPosition_e.swTableItemInsertPosition_After,5,"总数",(int)swInsertTableColumnWidthStyle_e.swInsertColumn_DefaultWidth);
                var count = swTableAnnotation.RowCount;
   
                // 获取零件文档 - 使用 GetComponents 方法遍历所有组件来查找
                AssemblyDoc swAssembly = (AssemblyDoc)swModel;
                Component2 targetComponent = null;
                                
                // 获取所有组件
                object[] allComponents = (object[])swAssembly.GetComponents(false);
                
                // 创建字典用于累加每个零件的数量
                Dictionary<string, int> partCountDict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                
                // 优化：预先构建组件名称到组件的映射字典，避免每次都遍历所有组件
                Console.WriteLine($"正在构建组件索引，共 {allComponents.Length} 个组件...");
                Dictionary<string, Component2> componentMap = new Dictionary<string, Component2>(StringComparer.OrdinalIgnoreCase);
                foreach (object compObj in allComponents)
                {
                    Component2 component = (Component2)compObj;
                    string componentName = component.Name2;
                    
                    // 去掉"/"号及之前的文字，只保留后面的部分
                    int slashIndex = componentName.LastIndexOf('/');
                    if (slashIndex >= 0 && slashIndex < componentName.Length - 1)
                    {
                        componentName = componentName.Substring(slashIndex + 1);
                    }
                    
                    // 去掉末尾的"-数字"部分（例如："零件名-1" -> "零件名"）
                    int lastDashIndex = componentName.LastIndexOf('-');
                    if (lastDashIndex > 0 && lastDashIndex < componentName.Length - 1)
                    {
                        string suffix = componentName.Substring(lastDashIndex + 1);
                        // 检查后缀是否为纯数字
                        if (int.TryParse(suffix, out _))
                        {
                            componentName = componentName.Substring(0, lastDashIndex);
                        }
                    }
                    
                    // 存储到字典中（如果已存在，保留第一个）
                    if (!componentMap.ContainsKey(componentName))
                    {
                        componentMap[componentName] = component;
                    }
                }
                Console.WriteLine($"组件索引构建完成，共 {componentMap.Count} 个唯一组件，耗时: {stopwatch.ElapsedMilliseconds}ms");

                // 优化1: 预先扫描DWG文件，构建快速查找字典
                Console.WriteLine("正在预扫描DWG文件...");
                Dictionary<string, bool> dwgFileCache = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
                foreach (string searchRoot in drawingSearchRoots)
                {
                    if (!Directory.Exists(searchRoot))
                    {
                        continue;
                    }

                    string[] allDwgFiles = Directory.GetFiles(searchRoot, "*.dwg", SearchOption.AllDirectories);
                    foreach (string dwgFile in allDwgFiles)
                    {
                        string fileNameWithoutExt = Path.GetFileNameWithoutExtension(dwgFile);
                        // 存储原始名称和清理后的名称
                        dwgFileCache[fileNameWithoutExt] = true;
                        
                        // 也存储清理后的版本（去除空格、连字符等）
                        string cleanName = fileNameWithoutExt.Replace(" ", "").Replace("-", "").Replace("_", "");
                        dwgFileCache[cleanName] = true;
                    }
                    Console.WriteLine($"预扫描目录: {searchRoot}，新增 {allDwgFiles.Length} 个DWG文件，耗时: {stopwatch.ElapsedMilliseconds}ms");
                }
                Console.WriteLine($"预扫描结束，DWG索引共 {dwgFileCache.Count} 条，耗时: {stopwatch.ElapsedMilliseconds}ms");
                
                // 正向遍历BOM表行
                int currentRowCount = count;
                int asmfactor = 1;
                for (int i = 1; i <= currentRowCount; i++)
                {
                
                    var cellText = swTableAnnotation.get_Text(i, 1);
                    if (cellText == null)
                    {
                        Console.WriteLine($"警告：第 {i} 行第 1 列的单元格为空，跳过。");
                        continue;
                    }
                    var partname = cellText.Trim();
                    swTableAnnotation.set_Text(i, 1, partname.Replace("=", ""));
                    Debug.WriteLine($"partname:{partname}");


                    
                    // 优化2: 使用缓存的DWG文件字典快速查找
                    bool dwgExists = false;
                    if (dwgFileCache.ContainsKey(partname))
                    {
                        dwgExists = true;
                    }
                    else
                    {
                        // 尝试清理后的名称匹配
                        string cleanPartName = partname.Replace(" ", "").Replace("-", "").Replace("_", "");
                        dwgExists = dwgFileCache.ContainsKey(cleanPartName);
                    }
                    
         
                        if (dwgExists)
                        {
                            swTableAnnotation.set_Text(i, 5, "已出图");
                        }
                        // 安全地获取并解析数量值
                        string itemCountText = swTableAnnotation.get_Text(i, 2);
                        int itemcount = 0;
                        if (!string.IsNullOrEmpty(itemCountText))
                        {
                            if (!int.TryParse(itemCountText, out itemcount))
                            {
                                Console.WriteLine($"警告：{partname}第 {i} 行第 2 列的数量值 '{itemCountText}' 无法转换为整数，使用默认值 0。");
                                itemcount = 0;
                            }
                        }
                        else
                        {
                            Console.WriteLine($"警告：第 {i} 行第 2 列的数量值为空，使用默认值 0。");
                        }
                 
    
     
                    
                            try
                            {
                                // 优化：直接从字典中查找组件，而不是遍历所有组件
                                if (!componentMap.TryGetValue(partname, out Component2? foundComponent))
                                {
                                    Console.WriteLine($"警告：未找到组件 '{partname}'，直接删除该行。");
                                    bool deleted = swTableAnnotation.DeleteRow2(i, false);
                                    Console.WriteLine($"删除未匹配组件行: row={i}, part={partname}, result={deleted}");
                                    currentRowCount--;
                                    i--;
                                    continue;
                                }

                                targetComponent = foundComponent;
                                ModelDoc2 partDoc = (ModelDoc2)targetComponent.GetModelDoc2();
                                int rowDocType = partDoc?.GetType() ?? -1;

                                // 装配体模式时，只保留该行对应组件文档类型为装配体的行
                                if (isAssemblyMode && rowDocType != (int)swDocumentTypes_e.swDocASSEMBLY)
                                {
                                    bool deleted = swTableAnnotation.DeleteRow2(i, false);
                                    Console.WriteLine($"删除非装配体行: row={i}, part={partname}, docType={rowDocType}, result={deleted}");
                                    currentRowCount--;
                                    i--;
                                    continue;
                                }

                                if (partDoc != null && partDoc.GetType() == (int)swDocumentTypes_e.swDocPART)
                                {
                                    string partPath = partDoc.GetPathName();
                                    if (!string.IsNullOrEmpty(partPath))
                                    {
                                        if (!bomPartsForExport.ContainsKey(partPath))
                                        {
                                            bomPartsForExport[partPath] = new BomPartExportInfo
                                            {
                                                PartName = partname,
                                                PartPath = partPath
                                            };
                                        }
                                    }

                                    PartDoc part = (PartDoc)partDoc;
                                    
                                    // 优化3: 批量获取自定义属性，减少COM调用次数
                                    Feature swFeature = (Feature)part.FirstFeature();
                                    bool isSheetMetal = false;
                                    double thickness = 0;
                                    double boundingBoxLength = 0;
                                    double boundingBoxWidth = 0;
                                    
                                    // 先尝试从切割清单直接获取属性
                                    Feature cutListFolder = null;
                                    while (swFeature != null)
                                    {
                                        if (swFeature.GetTypeName2() == "SolidBodyFolder")
                                        {
                                            cutListFolder = swFeature;
                                            break;
                                        }
                                        swFeature = (Feature)swFeature.GetNextFeature();
                                    }
                                    
                                    if (cutListFolder != null)
                                    {
                                        BodyFolder swBodyFolder = (BodyFolder)cutListFolder.GetSpecificFeature2();
                                        swBodyFolder.SetAutomaticCutList(true);
                                        swBodyFolder.SetAutomaticUpdate(true);
                                        
                                        Feature subfeat = (Feature)cutListFolder.GetFirstSubFeature();
                                        
                                        while (subfeat != null)
                                        {
                                            IBodyFolder solidBodyFolder = (IBodyFolder)subfeat.GetSpecificFeature2();
                                            
                                            var manger = subfeat.CustomPropertyManager;
                                            object vPropNames = null;
                                            object vPropTypes = null;
                                            object vPropValues = null;
                                            object resolved = null;
                                            object linkProp = null;
                                            
                                            manger.GetAll3(ref vPropNames, ref vPropTypes, ref vPropValues, ref resolved, ref linkProp);
                                            
                                            if (vPropValues != null && vPropNames != null)
                                            {
                                                string[] propValues = (string[])vPropValues;
                                                string[] propNames = (string[])vPropNames;
                                                
                                                for (int j = 0; j < propNames.Length; j++)
                                                {
                                                    if ((propNames[j] == "边界框长度" || propNames[j] == "Bounding Box Length") && 
                                                        double.TryParse(propValues[j], out double length))
                                                    {
                                                        boundingBoxLength = length;
                                                    }
                                                    if ((propNames[j] == "边界框宽度" || propNames[j] == "Bounding Box Width") && 
                                                        double.TryParse(propValues[j], out double width))
                                                    {
                                                        boundingBoxWidth = width;
                                                    }
                                                    if ((propNames[j] == "钣金厚度" || propNames[j] == "Thickness") && 
                                                        double.TryParse(propValues[j], out double thicknessValue))
                                                    {
                                                        thickness = thicknessValue;
                                                        if (thicknessValue > 0.1) 
                                                        {
                                                            isSheetMetal = true;
                                                            
                                                            string material = "SPPC " + thickness.ToString("F2");
                                                            string materialdataname = "";
                                                            string materialConfig = targetComponent.ReferencedConfiguration;
                                                            if (string.IsNullOrWhiteSpace(materialConfig))
                                                            {
                                                                materialConfig = "Default";
                                                            }
                                                            string materialname = part.GetMaterialPropertyName2(materialConfig, out materialdataname) ?? string.Empty;
                                                            if (string.IsNullOrWhiteSpace(materialname) && !string.Equals(materialConfig, "Default", StringComparison.OrdinalIgnoreCase))
                                                            {
                                                                materialname = part.GetMaterialPropertyName2("Default", out materialdataname) ?? string.Empty;
                                                            }
                                                            if (IsStainlessMaterial(materialname) || IsStainlessMaterial(materialdataname))
                                                            {
                                                                material = "SUS " + thickness.ToString("F2");
                                                            }
                                                          if (!taskPaneCollectionMode || writePartCustomPropertiesInTaskPaneMode)
                                                            {
                                                                partDoc.DeleteCustomInfo2("", "材料");
                                                                bool materialResult = partDoc.AddCustomInfo2("材料", (int)swCustomInfoType_e.swCustomInfoText, material);
                                                                if (materialResult)
                                                                {
                                                                    materialPropWriteCount++;
                                                                }
                                                                Console.WriteLine($"添加材料自定义信息结果: {materialResult},partname:{partname},material:{material}");
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                            
                                            subfeat = (Feature)subfeat.GetNextSubFeature();
                                        }
                                    }
                                    
                                    string dimensionStr = "";
                                    string categoryStr = ""; // 类别：钣金件/管件/其他
                                    
                                    if (isSheetMetal && boundingBoxLength > 0 && boundingBoxWidth > 0)
                                    {
                                        // 使用切割清单中的边界框尺寸作为下料尺寸
                                        double maxLength = Math.Max(boundingBoxLength, boundingBoxWidth);
                                        double minLength = Math.Min(boundingBoxLength, boundingBoxWidth);
                                        // 将尺寸放入数组并排序
                                        double[] dimensions = new double[] { maxLength, minLength, thickness };
                                        Array.Sort(dimensions);
                                        dimensionStr = $"{FormatDimension(dimensions[0])}x{FormatDimension(dimensions[1])}x{FormatDimension(dimensions[2])}";
                                    categoryStr = "钣金件";
                                        
                                        Debug.WriteLine($"钣金件 '{partname}' 下料尺寸: {dimensionStr}, 类别: {categoryStr}");
                                    }
                                    else if (isSheetMetal)
                                    {
                                        // 如果没有找到切割清单信息，使用普通边界框
                                        var dimensions = PartDimensionHelper.GetPartDimensions(part);
                                        dimensionStr = $"000";
                                        categoryStr = "钣金件展开错误";
                                    }
                                    else
                                    {
                                        double minDimensionMm = double.MaxValue;

                                        // 非钣金件统一使用零件文档尺寸计算，避免 GetBox 受全局坐标方向影响
                                        var dimensions = PartDimensionHelper.GetPartDimensions(part);

                                        // 将尺寸放入数组并排序
                                        double[] dims = new double[] { dimensions.length, dimensions.width, dimensions.height };
                                        Array.Sort(dims);
                                        minDimensionMm = dims[0];
                                        dimensionStr = $"{FormatDimension(dims[0])}x{FormatDimension(dims[1])}x{FormatDimension(dims[2])}";

                                        bool isTubePart = IsTubePart(partDoc);
                                        if (isTubePart)
                                        {
                                            categoryStr = "管件";
                                            Console.WriteLine($"管件 '{partname}' 尺寸: {dimensionStr}");
                                        }
                                        else
                                        {
                                            // 检查是否有子组件，判断是否为子装配体
                                            object[] children = (object[])targetComponent.GetChildren();
                                            if (children != null && children.Length > 0)
                                            {
                                                categoryStr = "组合件";
                                                Console.WriteLine($"组合件 '{partname}' 尺寸: {dimensionStr}");
                                            }
                                            else
                                            {
                                                categoryStr = "其他";
                                            }
                                        }

                                        // 钣金BOM模式下，非钣金件若最小尺寸<10mm，提示可能存在未转换的钣金件
                                        if (filterSheetMetal && categoryStr != "钣金件" && minDimensionMm < 10)
                                        {
                                            string suspect = $"{partname}({FormatDimension(minDimensionMm)}mm)";
                                            if (!suspectedUnconvertedSheetMetalParts.Contains(suspect))
                                            {
                                                suspectedUnconvertedSheetMetalParts.Add(suspect);
                                            }
                                        }
                                    }
                                    
                                    // 过滤模式：只保留匹配类型
                                    bool shouldKeepRow = true;
                                    if (filterSheetMetal)
                                    {
                                        shouldKeepRow = categoryStr == "钣金件";
                                    }
                                    else if (filterTube)
                                    {
                                        shouldKeepRow = categoryStr == "管件";
                                    }

                                    if (!shouldKeepRow)
                                    {
                                        bool deleted = swTableAnnotation.DeleteRow2(i, false);
                                        Console.WriteLine($"删除不匹配类型行: row={i}, part={partname}, category={categoryStr}, target={exportType}, result={deleted}");
                                        currentRowCount--;
                                        i--;
                                        continue;
                                    }

                                    // 优化4: 批量设置表格文本，减少COM调用
                                    List<(int row, int col, string text)> pendingUpdates = new List<(int, int, string)>();
                                    
                                    pendingUpdates.Add((i, 3, dimensionStr));
                                    pendingUpdates.Add((i, 4, categoryStr));
                                
                                    itemcount = itemcount*asmfactor;
                                    pendingUpdates.Add((i, 2, itemcount.ToString()));
                                    
                                    if (!taskPaneCollectionMode || writePartCustomPropertiesInTaskPaneMode)
                                    {
                                        partDoc.DeleteCustomInfo2("", "数量");
                                        // 使用字典中累加后的数量值
                                        int accumulatedCount = partCountDict.ContainsKey(partname) ? partCountDict[partname] : itemcount;
                                        bool result = partDoc.AddCustomInfo2("数量", (int)swCustomInfoType_e.swCustomInfoText, accumulatedCount.ToString());
                                        if (result)
                                        {
                                            quantityPropWriteCount++;
                                        }
                                        Console.WriteLine($"添加数量自定义信息结果: {result},partcount:{accumulatedCount},partname:{partname}");
                                    }
                                    
                                    // 批量应用表格更新
                                    foreach (var update in pendingUpdates)
                                    {
                                        swTableAnnotation.set_Text(update.row, update.col, update.text);
                                    }
                                }
                                
                                
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"获取管件 '{partname}' 尺寸时出错: {ex.Message}");
                            }
                    
                    
                       
                    
                            // 累加零件数量到字典
                            if (!partCountDict.ContainsKey(partname))
                            {
                                partCountDict[partname] = 0;
                            }
                            partCountDict[partname] += itemcount;
                    }
                  
            
                

               
                
                string excelpath = swModel.GetPathName().Replace("SLDASM", "xlsx");
                if (filterPart) excelpath = swModel.GetPathName().Replace(".SLDASM", "_part.xlsx");
                if (filterSheetMetal) excelpath = swModel.GetPathName().Replace(".SLDASM", "_sheetmetal.xlsx");
                if (filterTube) excelpath = swModel.GetPathName().Replace(".SLDASM", "_tube.xlsx");
                if (isAssemblyMode) excelpath = swModel.GetPathName().Replace(".SLDASM", "_asm.xlsx");
                
                // 只在需要导出Excel时保存
                if (exportExcel)
                {
                    swBOMAnnotation.SaveAsExcel(excelpath, false, true);
                }
            
            swBOMAnnotation=swBOMAnnotation2;

                if (taskPaneCollectionMode)
                {
                    _lastTaskPaneCollection = ExtractTaskPaneRowsFromBomTable(swTableAnnotation);
                }
                else
                {
                    _lastTaskPaneCollection = null;
                }

                // 同步保存 BOM 零件清单（供 asm2do 等流程复用，避免重复扫描/重复建表）
                if (!taskPaneCollectionMode)
                {
                    SetLastBomParts(new List<BomPartExportInfo>(bomPartsForExport.Values));
                }
                
                // 只在需要时启动 Excel 文件
                if (exportExcel)
                {
                    ProcessStartInfo startInfo = new ProcessStartInfo(excelpath)
                    {
                        UseShellExecute = true
                    };
                    Process.Start(startInfo);
                }

                if (filterSheetMetal && suspectedUnconvertedSheetMetalParts.Count > 0)
                {
                    string preview = string.Join("，", suspectedUnconvertedSheetMetalParts.GetRange(0, Math.Min(8, suspectedUnconvertedSheetMetalParts.Count)));
                    string tip = $"检测到{suspectedUnconvertedSheetMetalParts.Count}个非钣金件最小尺寸小于10mm，可能有未转化的钣金件。\n示例：{preview}";
                    swApp.SendMsgToUser2(tip, (int)swMessageBoxIcon_e.swMbWarning, (int)swMessageBoxBtn_e.swMbOk);
                }
                
                Console.WriteLine($"\n=== BOM导出完成 ===");
                Console.WriteLine($"属性写入统计: 材料 {materialPropWriteCount} 项, 数量 {quantityPropWriteCount} 项");
                Console.WriteLine($"总耗时: {stopwatch.ElapsedMilliseconds}ms ({stopwatch.Elapsed.TotalSeconds:F2}秒)");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"发生错误：{ex.Message}");
                Console.WriteLine(ex.StackTrace);
                return -1;
            }
            finally
            {
                swApp.CommandInProgress = false;
            }
        }

        /// <summary>
        /// 格式化尺寸值，如果能取整则返回整数形式，否则保留一位小数
        /// </summary>
        /// <param name="value">尺寸值</param>
        /// <returns>格式化后的字符串</returns>
        static private string FormatDimension(double value)
        {
            if (Math.Abs(value - Math.Round(value)) < 1e-9)
            {
                return ((int)Math.Round(value)).ToString();
            }
            else
            {
                return value.ToString("F1");
            }
        }

        static private bool IsStainlessMaterial(string materialText)
        {
            if (string.IsNullOrWhiteSpace(materialText))
            {
                return false;
            }

            string normalized = materialText.Trim().ToLowerInvariant();
            return normalized.Contains("不锈钢")
                || normalized.Contains("sus")
                || normalized.Contains("stainless");
        }

        static private bool IsTubePart(ModelDoc2 partDoc)
        {
            if (partDoc != null)
            {
                // 仅通过拓扑匹配识别“拉伸建模的方管/管件”。
                if (IsTubePartByTopology(partDoc))
                {
                    return true;
                }
            }

            return false;
        }

        static private bool IsTubePartByTopology(ModelDoc2 partDoc)
        {
            try
            {
                if (partDoc == null || partDoc.GetType() != (int)swDocumentTypes_e.swDocPART)
                {
                    return false;
                }

                string cacheKey = BuildTopologyCacheKey(partDoc);
                if (!string.IsNullOrWhiteSpace(cacheKey))
                {
                    lock (_topologyResultCacheLock)
                    {
                        if (_tubeTopologyMatchCache.TryGetValue(cacheKey, out bool cachedResult))
                        {
                            return cachedResult;
                        }
                    }
                }

                TopologyDatabase? db = TopologyLabeler.GetDatabase();
                if (db == null)
                {
                    return false;
                }

                List<BodyGraph> graphs;
                lock (_topologyMatchLock)
                {
                    graphs = FaceGraphBuilder.BuildGraphs(partDoc);
                }

                if (graphs == null || graphs.Count == 0)
                {
                    return false;
                }

                const double exactMatchThreshold = 0.999999;
                bool matched = false;
                foreach (var graph in graphs)
                {
                    List<Dictionary<string, int>> wlFreq;
                    lock (_topologyMatchLock)
                    {
                        wlFreq = WLGraphKernel.PerformWLIterations(graph, iterations: 1);
                    }

                    if (wlFreq == null || wlFreq.Count == 0)
                    {
                        continue;
                    }

                    var querySw = Stopwatch.StartNew();
                    var matches = db.FindBodiesByWLTags(
                        wlFreq,
                        topK: 5,
                        minSimilarity: exactMatchThreshold,
                        labelValueKeyword: "方管");
                    querySw.Stop();
                    Console.WriteLine($"[性能] 方管拓扑查询耗时: {querySw.ElapsedMilliseconds}ms, part={partDoc.GetTitle()}, body={graph.FullBodyName}");
                    foreach (var match in matches)
                    {
                        if (match.Similarity < exactMatchThreshold)
                        {
                            continue;
                        }

                        string labelValue = match.LabelValue?.Trim() ?? string.Empty;
                        if (labelValue.Contains("方管") || labelValue.Contains("管件"))
                        {
                            Console.WriteLine($"拓扑识别管件命中: part={partDoc.GetTitle()}, body={graph.FullBodyName}, label={labelValue}, sim={match.Similarity:F6}");
                            matched = true;
                            break;
                        }
                    }

                    if (matched)
                    {
                        break;
                    }
                }

                if (!string.IsNullOrWhiteSpace(cacheKey))
                {
                    lock (_topologyResultCacheLock)
                    {
                        _tubeTopologyMatchCache[cacheKey] = matched;
                    }
                }

                return matched;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"拓扑识别管件失败: {ex.Message}");
            }

            return false;
        }

        static private string BuildTopologyCacheKey(ModelDoc2 partDoc)
        {
            string path = partDoc.GetPathName();
            if (!string.IsNullOrWhiteSpace(path))
            {
                if (File.Exists(path))
                {
                    DateTime lastWrite = File.GetLastWriteTimeUtc(path);
                    return $"{path}|{lastWrite.Ticks}";
                }
                return $"{path}|nosave";
            }

            string title = partDoc.GetTitle() ?? string.Empty;
            return string.IsNullOrWhiteSpace(title) ? string.Empty : $"title:{title}";
        }

        /// <summary>从 BOM 表提取任务窗格用零件行（不触发任务窗格 UI）。</summary>
        static private List<TaskPaneBomPartRow> ExtractTaskPaneRowsFromBomTable(TableAnnotation swTableAnnotation)
        {
            var rows = new List<TaskPaneBomPartRow>();
            try
            {
                int rowCount = swTableAnnotation.RowCount;
                for (int i = 1; i < rowCount; i++)
                {
                    string partName = swTableAnnotation.get_Text(i, 1)?.Trim() ?? "";
                    string quantity = swTableAnnotation.get_Text(i, 2)?.Trim() ?? "";
                    string dimension = swTableAnnotation.get_Text(i, 3)?.Trim() ?? "";
                    string partType = swTableAnnotation.get_Text(i, 4)?.Trim() ?? "";
                    string isDrawn = swTableAnnotation.get_Text(i, 5)?.Trim() ?? "未出图";

                    if (!string.IsNullOrEmpty(partName))
                    {
                        rows.Add(new TaskPaneBomPartRow
                        {
                            PartName = partName,
                            PartType = partType,
                            Dimension = dimension,
                            IsDrawn = isDrawn,
                            Quantity = quantity
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ExtractTaskPaneRowsFromBomTable: {ex.Message}");
            }

            return rows;
        }

        /// <summary>
        /// 仅为任务窗格采集「零件」式 BOM 行：不刷新 <see cref="GetLastBomParts"/> 缓存、不写零件自定义属性、不回调任务窗格。
        /// </summary>
        public static async Task<List<TaskPaneBomPartRow>?> CollectPartRowsForTaskPaneAsync(SldWorks swApp, ModelDoc2 swModel)
        {
            _lastTaskPaneCollection = null;
            int code = await run(
                swApp,
                swModel,
                "零件",
                false,
                taskPaneCollectionMode: true,
                writePartCustomPropertiesInTaskPaneMode: true);
            if (code != 0)
            {
                return null;
            }

            return _lastTaskPaneCollection ?? new List<TaskPaneBomPartRow>();
        }
    }
}