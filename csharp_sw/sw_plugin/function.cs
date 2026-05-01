using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swpublished;
using SolidWorksTools;
using System;
using System.IO;
using System.Runtime.InteropServices;
using tools;
using cad_tools;
using System.Diagnostics;
using SolidWorks.Interop.swconst;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Reflection;
using System.Threading.Tasks;
using System.Text;

using System.Linq;
using SolidWorksAddinStudy.Services;

   namespace SolidWorksAddinStudy
{
   
    public partial class AddinStudy 
{

    [SolidWorksAddinStudy.Command(1001, "导出展开", "装配体每个零件批量展开", "asm2export", (int)swDocumentTypes_e.swDocASSEMBLY, ShowOutputWindow = true)]
    private void Asm2export()
    {
        try
        {
            StartCancelableCommand();
            if (swApp == null)
            {
                Debug.WriteLine("SolidWorks 未初始化");
                return;
            }

            ModelDoc2 swModel = (ModelDoc2)swApp.ActiveDoc;
            
            if (swModel == null)
            {
                Debug.WriteLine("没有打开的文档");
                swApp.SendMsgToUser("请先打开一个工程图文档");
                return;
            }
           
            string assemblyPath = swModel.GetPathName();
            if (string.IsNullOrWhiteSpace(assemblyPath))
            {
                swApp.SendMsgToUser("当前装配体尚未保存，请先保存装配体");
                return;
            }
            string assemblyDirectory = Path.GetDirectoryName(assemblyPath) ?? string.Empty;
            string folderName = Path.GetFileName(assemblyDirectory);
            string outputRootName = string.IsNullOrWhiteSpace(folderName) ? "钣金" : $"{folderName}钣金";

            asm2do.run(swApp, swModel, (model, app) => {
                checkk_factor.run(app,model);
                return exportdwg2_body.run(model, outputRootName);
            });


           
         
        }
        catch (OperationCanceledException)
        {
            swApp?.SendMsgToUser("命令已中止");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"工程图转换失败：{ex.Message}");
            swApp?.SendMsgToUser($"工程图转换失败：{ex.Message}");
        }
    }
    [SolidWorksAddinStudy.Command(1002, "导出展开", "导出展开", "exportdwg", (int)swDocumentTypes_e.swDocPART, ShowOutputWindow = true)]
    private void exportdwg()
    {
        try
        {
            StartCancelableCommand();
            if (swApp == null)
            {
                Debug.WriteLine("SolidWorks 未初始化");
                return;
            }

            ModelDoc2 swModel = (ModelDoc2)swApp.ActiveDoc;
            
            if (swModel == null)
            {
                Debug.WriteLine("没有打开的文档");
                swApp.SendMsgToUser("请先打开一个工程图文档");
                return;
            }
            string modelPath = swModel.GetPathName();
            string modelDirectory = Path.GetDirectoryName(modelPath) ?? string.Empty;
            string folderName = Path.GetFileName(modelDirectory);
            string outputRootName = string.IsNullOrWhiteSpace(folderName) ? "钣金" : $"{folderName}钣金";

            checkk_factor.run(swApp,swModel);
            exportdwg2_body.run(swModel, outputRootName);






        }
        catch (OperationCanceledException)
        {
            swApp?.SendMsgToUser("命令已中止");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"工程图转换失败：{ex.Message}");
            swApp?.SendMsgToUser($"工程图转换失败：{ex.Message}");
        }
    }
  
    [SolidWorksAddinStudy.Command(1003, "工程图转 DWG", "将当前工程图转换为 DWG 格式并打开", "drw2dwg", (int)swDocumentTypes_e.swDocDRAWING)]
    private void Drw2Dwg()
    {
        try
        {
            StartCancelableCommand();
            if (swApp == null)
            {
                Debug.WriteLine("SolidWorks 未初始化");
                return;
            }

            ModelDoc2 swModel = (ModelDoc2)swApp.ActiveDoc;
            
            if (swModel == null)
            {
                Debug.WriteLine("没有打开的文档");
                swApp.SendMsgToUser("请先打开一个工程图文档");
                return;
            }
           
            // 使用 share 项目中的 drw2dwg 方法转换 DWG
            var dwgFileName = drw2dwg.run(swModel, swApp);
            
            // 更新任务窗格中对应零件的出图状态
            UpdatePartDrawnStatusFromDrawing(swModel);

           
            Debug.WriteLine($"工程图已转换为 DWG: {dwgFileName}");
        }
        catch (OperationCanceledException)
        {
            swApp?.SendMsgToUser("命令已中止");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"工程图转换失败：{ex.Message}");
            swApp?.SendMsgToUser($"工程图转换失败：{ex.Message}");
        }
    }

    [SolidWorksAddinStudy.Command(1004, "新建工程图", "为当前零件创建工程图并添加视图", "newdrw", (int)swDocumentTypes_e.swDocPART, ShowOutputWindow = true)]
    private void NewDrw()
    {
        try
        {
            StartCancelableCommand();
            if (swApp == null)
            {
                Debug.WriteLine("SolidWorks 未初始化");
                return;
            }

            ModelDoc2 swModel = (ModelDoc2)swApp.ActiveDoc;
            if (swModel == null)
            {
                Debug.WriteLine("没有打开的文档");
                swApp.SendMsgToUser("请先打开一个零件文档");
                return;
            }

            // 添加名称到自定义信息
            add_name2info.run(swModel);

            // 新建工程图前先执行 K 因子检查
            var checkResult = checkk_factor.RunWithStats(swApp, swModel);
            if (checkResult.ErrorCount > 0)
            {
                swApp.SendMsgToUser2(
                    $"K因子检查发现 {checkResult.ErrorCount} 个错误，请先处理后再出图。",
                    (int)swMessageBoxIcon_e.swMbWarning,
                    (int)swMessageBoxBtn_e.swMbOk);
            }
          
            // 创建新工程图
            New_drw.run(swApp, swModel);
            
            Debug.WriteLine("工程图已创建");
        }
        catch (OperationCanceledException)
        {
            swApp?.SendMsgToUser("命令已中止");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"创建工程图失败：{ex.Message}");
            swApp?.SendMsgToUser($"创建工程图失败：{ex.Message}");
        }
    }
    [SolidWorksAddinStudy.Command(1005, "新建工程图", "为当前零件/装配体创建工程图并添加视图", "new_drw_cnc", (int)swDocumentTypes_e.swDocPART, (int)swDocumentTypes_e.swDocASSEMBLY)]
    private void NewDrwCnc()
    {
        try
        {
            StartCancelableCommand();
            if (swApp == null)
            {
                Debug.WriteLine("SolidWorks 未初始化");
                return;
            }

            ModelDoc2 swModel = (ModelDoc2)swApp.ActiveDoc;
            if (swModel == null)
            {
                Debug.WriteLine("没有打开的文档");
                swApp.SendMsgToUser("请先打开一个零件或装配体文档");
                return;
            }

      
            
            // 创建新工程图
            New_drw_cnc.run(swApp, swModel);
            
            Debug.WriteLine("工程图已创建");
        }
        catch (OperationCanceledException)
        {
            swApp?.SendMsgToUser("命令已中止");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"创建工程图失败：{ex.Message}");
            swApp?.SendMsgToUser($"创建工程图失败：{ex.Message}");
        }
    }
    


       
    [SolidWorksAddinStudy.Command(1006, "打开 DWG工程图", "打开 DWG工程图", "opendwg", (int)swDocumentTypes_e.swDocDRAWING)]
    private void Opendwg()
    {
        try
        {
            StartCancelableCommand();
            if (swApp == null)
            {
                Debug.WriteLine("SolidWorks 未初始化");
                return;
            }

            ModelDoc2 swModel = (ModelDoc2)swApp.ActiveDoc;
            
            if (swModel == null)
            {
                Debug.WriteLine("没有打开的文档");
                swApp.SendMsgToUser("请先打开一个工程图文档");
                return;
            }
           
            // 使用 share 项目中的 drw2dwg 方法转换 DWG
            opendwg.run(swModel, swApp);


           
        }
        catch (OperationCanceledException)
        {
            swApp?.SendMsgToUser("命令已中止");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"工程图dwg打开失败：{ex.Message}");
            swApp?.SendMsgToUser($"工程图dwg打开失败：{ex.Message}");
        }
    }
    [SolidWorksAddinStudy.Command(1008, "新建装配体工程图", "为当前装配体创建工程图并添加视图和 BOM 表", "asmnewdrw", (int)swDocumentTypes_e.swDocASSEMBLY, ShowOutputWindow = true)]
    private void AsmNewDrw()
    {
        try
        {
            StartCancelableCommand();
            if (swApp == null)
            {
                Debug.WriteLine("SolidWorks 未初始化");
                return;
            }

            ModelDoc2 swModel = (ModelDoc2)swApp.ActiveDoc;
            
            if (swModel == null)
            {
                Debug.WriteLine("没有打开的文档");
                swApp.SendMsgToUser("请先打开一个装配体文档");
                return;
            }
           
            // 创建新装配体工程图
            Asm_new_drw.run(swApp, swModel);
        }
        catch (OperationCanceledException)
        {
            swApp?.SendMsgToUser("命令已中止");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"装配体工程图创建失败：{ex.Message}");
            swApp?.SendMsgToUser($"装配体工程图创建失败：{ex.Message}");
        }
    }
    [SolidWorksAddinStudy.Command(1009, "装配体批量检查展开", "检查装配体中所有零件的展开情况", "asm2check", (int)swDocumentTypes_e.swDocASSEMBLY, ShowOutputWindow = true)]
    private void Asm2check()
    {
        try
        {
            StartCancelableCommand();
            if (swApp == null)
            {
                Debug.WriteLine("SolidWorks 未初始化");
                return;
            }

            ModelDoc2 swModel = (ModelDoc2)swApp.ActiveDoc;

            if (swModel == null)
            {
                Debug.WriteLine("没有打开的文档");
                swApp.SendMsgToUser("请先打开一个装配体文档");
                return;
            }

            if (swModel.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                swApp.SendMsgToUser("当前文档不是装配体");
                return;
            }

            int checkedCount = 0;
            int errorCount = 0;
            int warningCount = 0;

            Console.WriteLine("\n=== 装配体批量检查展开（经 asm2do + BOM 清单，与任务窗格无关）===");
            swApp.CommandInProgress = true;

            try
            {
                string[] processed = asm2do.run(swApp, swModel, (partDoc, app) =>
                {
                    CommandCancelManager.ThrowIfCancellationRequested();
                    var checkResult = checkk_factor.RunWithStats(app, partDoc);
                    if (checkResult.IsSheetMetal)
                    {
                        checkedCount++;
                    }

                    errorCount += checkResult.ErrorCount;
                    warningCount += checkResult.WarningCount;
                    return 0;
                }, null);

                if (processed == null)
                {
                    swApp.SendMsgToUser("检查失败：无法生成 BOM 零件清单（请先保存装配体，或查看输出窗口）。");
                    return;
                }

                if (processed.Length == 0)
                {
                    swApp.SendMsgToUser("BOM 清单中未找到需要检查的零件。");
                    return;
                }

                Console.WriteLine($"\n=== 检查完成 ===");
                Console.WriteLine($"共检查 {checkedCount} 个钣金件，{errorCount} 个错误，{warningCount} 个提醒");
                swApp.SendMsgToUser($"检查完成：钣金件 {checkedCount} 个，错误 {errorCount}，提醒 {warningCount}。");
            }
            finally
            {
                swApp.CommandInProgress = false;
            }
        }
        catch (OperationCanceledException)
        {
            swApp?.SendMsgToUser("命令已中止");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"装配体检查展开失败：{ex.Message}");
            swApp?.SendMsgToUser($"装配体检查展开失败：{ex.Message}");
        }
    }

    [SolidWorksAddinStudy.Command(1010, "检查K因子", "检查当前零件的K因子设置是否正确", "checkkfactor", (int)swDocumentTypes_e.swDocPART, ShowOutputWindow = true)]
    private void CheckKFactor()
    {
        try
        {
            StartCancelableCommand();
            if (swApp == null)
            {
                Debug.WriteLine("SolidWorks 未初始化");
                return;
            }

            ModelDoc2 swModel = (ModelDoc2)swApp.ActiveDoc;
            
            if (swModel == null)
            {
                Debug.WriteLine("没有打开的文档");
                swApp.SendMsgToUser("请先打开一个零件文档");
                return;
            }
            
            checkk_factor.run(swApp, swModel);
            
            Debug.WriteLine("K因子检查完成");
        }
        catch (OperationCanceledException)
        {
            swApp?.SendMsgToUser("命令已中止");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"K因子检查失败：{ex.Message}");
            swApp?.SendMsgToUser($"K因子检查失败：{ex.Message}");
        }
    }

    [SolidWorksAddinStudy.Command(1011, "导出BOM（装配体）", "导出装配体层级BOM到Excel（仅装配体项）", "export_bom_assembly", (int)swDocumentTypes_e.swDocASSEMBLY, ShowOutputWindow = true)]
    private async void Asm2bom()
    {
        try
        {
            StartCancelableCommand();
            if (swApp == null)
            {
                Debug.WriteLine("SolidWorks 未初始化");
                return;
            }

            ModelDoc2 swModel = (ModelDoc2)swApp.ActiveDoc;
            
            if (swModel == null)
            {
                Debug.WriteLine("没有打开的文档");
                swApp.SendMsgToUser("请先打开一个装配体文档");
                return;
            }
           
            await asm2bom.run(swApp, swModel, "装配体");
        }
        catch (OperationCanceledException)
        {
            swApp?.SendMsgToUser("命令已中止");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"装配体 BOM 导出失败：{ex.Message}");
            swApp?.SendMsgToUser($"装配体 BOM 导出失败：{ex.Message}");
        }
    }

    [SolidWorksAddinStudy.Command(1012, "导出BOM（零件）", "导出装配体中的全部零件BOM到Excel", "export_bom_parts", (int)swDocumentTypes_e.swDocASSEMBLY, ShowOutputWindow = true)]
    private async void Part2bom()
    {
        try
        {
            StartCancelableCommand();
            if (swApp == null)
            {
                Debug.WriteLine("SolidWorks 未初始化");
                return;
            }

            ModelDoc2 swModel = (ModelDoc2)swApp.ActiveDoc;
            
            if (swModel == null)
            {
                Debug.WriteLine("没有打开的文档");
                swApp.SendMsgToUser("请先打开一个零件文档");
                return;
            }
           
            await asm2bom.run(swApp, swModel, "零件");
        }
        catch (OperationCanceledException)
        {
            swApp?.SendMsgToUser("命令已中止");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"零件 BOM 导出失败：{ex.Message}");
            swApp?.SendMsgToUser($"零件 BOM 导出失败：{ex.Message}");
        }
    }

    [SolidWorksAddinStudy.Command(1021, "导出BOM（方管）", "仅导出装配体中的管件BOM到Excel", "export_bom_tube", (int)swDocumentTypes_e.swDocASSEMBLY, ShowOutputWindow = true)]
    private async void Tube2bom()
    {
        try
        {
            StartCancelableCommand();
            if (swApp == null)
            {
                Debug.WriteLine("SolidWorks 未初始化");
                return;
            }

            ModelDoc2 swModel = (ModelDoc2)swApp.ActiveDoc;

            if (swModel == null)
            {
                Debug.WriteLine("没有打开的文档");
                swApp.SendMsgToUser("请先打开一个装配体文档");
                return;
            }

            await asm2bom.run(swApp, swModel, "管件");
        }
        catch (OperationCanceledException)
        {
            swApp?.SendMsgToUser("命令已中止");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"方管 BOM 导出失败：{ex.Message}");
            swApp?.SendMsgToUser($"方管 BOM 导出失败：{ex.Message}");
        }
    }

    [SolidWorksAddinStudy.Command(1022, "导出BOM（钣金）", "仅导出装配体中的钣金件BOM到Excel", "export_bom_sheet", (int)swDocumentTypes_e.swDocASSEMBLY, ShowOutputWindow = true)]
    private async void SheetMetal2bom()
    {
        try
        {
            StartCancelableCommand();
            if (swApp == null)
            {
                Debug.WriteLine("SolidWorks 未初始化");
                return;
            }

            ModelDoc2 swModel = (ModelDoc2)swApp.ActiveDoc;

            if (swModel == null)
            {
                Debug.WriteLine("没有打开的文档");
                swApp.SendMsgToUser("请先打开一个装配体文档");
                return;
            }

            await asm2bom.run(swApp, swModel, "钣金件");
        }
        catch (OperationCanceledException)
        {
            swApp?.SendMsgToUser("命令已中止");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"钣金 BOM 导出失败：{ex.Message}");
            swApp?.SendMsgToUser($"钣金 BOM 导出失败：{ex.Message}");
        }
    }

    [SolidWorksAddinStudy.Command(1013, "导出STEP（全部零件）", "将装配体中的全部零件批量导出为STEP", "export_step_all_parts", (int)swDocumentTypes_e.swDocASSEMBLY, ShowOutputWindow = true)]
    private void AsmBatchStep()
    {
        try
        {
            StartCancelableCommand();
            if (swApp == null)
            {
                Debug.WriteLine("SolidWorks 未初始化");
                return;
            }

            ModelDoc2 swModel = (ModelDoc2)swApp.ActiveDoc;
            
            if (swModel == null)
            {
                Debug.WriteLine("没有打开的文档");
                swApp.SendMsgToUser("请先打开一个装配体文档");
                return;
            }
           
            asm2do.run(swApp, swModel, (model, app) =>
            {
                CommandCancelManager.ThrowIfCancellationRequested();
                return one2step.run(model);
            });
            
            Debug.WriteLine("装配体零件 STEP 导出完成");
        }
        catch (OperationCanceledException)
        {
            swApp?.SendMsgToUser("命令已中止");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"装配体 STEP 导出失败：{ex.Message}");
            swApp?.SendMsgToUser($"装配体 STEP 导出失败：{ex.Message}");
        }
    }

    [SolidWorksAddinStudy.Command(1020, "导出STEP（方管）", "仅导出装配体中名称包含“方管”的零件为STEP", "export_step_square_tube", (int)swDocumentTypes_e.swDocASSEMBLY, ShowOutputWindow = true)]
    private void AsmSquareTubeStep()
    {
       
        try
        {
            StartCancelableCommand();
            if (swApp == null)
            {
                Debug.WriteLine("SolidWorks 未初始化");
                return;
            }
            
         
            ModelDoc2 swModel = (ModelDoc2)swApp.ActiveDoc;
            
            if (swModel == null)
            {
                Debug.WriteLine("没有打开的文档");
                swApp.SendMsgToUser("请先打开一个装配体文档");
                return;
            }
           
            // 使用过滤器只处理包含"方管"的零件
            string[] filterKeywords = new string[] { "方管" };
            asm2do.run(swApp, swModel, (model, app) =>
            {
                CommandCancelManager.ThrowIfCancellationRequested();
                return one2step.run(model);
            }, filterKeywords);
            
            Debug.WriteLine("方管件 STEP 导出完成");
        }
        catch (OperationCanceledException)
        {
            swApp?.SendMsgToUser("命令已中止");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"方管 STEP 导出失败：{ex.Message}");
            swApp?.SendMsgToUser($"方管 STEP 导出失败：{ex.Message}");
        }
       
    }


    [SolidWorksAddinStudy.Command(1019, "工程图转DWG（全部DRW）", "按零件同名SLDDRW直接打开并批量转DWG（不再打开零件）", "asm_all_drw2dwg", (int)swDocumentTypes_e.swDocASSEMBLY, ShowOutputWindow = true)]
    private void AsmAllDrw2Dwg()
    {
        try
        {
            StartCancelableCommand();
            if (swApp == null)
            {
                Debug.WriteLine("SolidWorks 未初始化");
                return;
            }

            ModelDoc2 swModel = (ModelDoc2)swApp.ActiveDoc;
            if (swModel == null)
            {
                Debug.WriteLine("没有打开的文档");
                swApp.SendMsgToUser("请先打开一个装配体文档");
                return;
            }

            if (swModel.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                swApp.SendMsgToUser("当前文档不是装配体");
                return;
            }

            if (string.IsNullOrWhiteSpace(swModel.GetPathName()))
            {
                swApp.SendMsgToUser("当前装配体尚未保存，请先保存装配体");
                return;
            }

            if (!asm2bom.HasGeneratedPartList())
            {
                int bomResult = asm2bom.run(swApp, swModel, "零件", false).GetAwaiter().GetResult();
                if (bomResult != 0)
                {
                    swApp.SendMsgToUser("无法生成零件BOM缓存，终止批量转DWG");
                    return;
                }
            }

            List<BomPartExportInfo> bomParts = asm2bom.GetLastBomParts();
            if (bomParts == null || bomParts.Count == 0)
            {
                swApp.SendMsgToUser("BOM缓存为空，未找到可处理零件");
                return;
            }

            int successCount = 0;
            int missingDrwCount = 0;
            int failedCount = 0;
            HashSet<string> processedDrwPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var bomPart in bomParts)
            {
                CommandCancelManager.ThrowIfCancellationRequested();
                string rawPartPath = bomPart?.PartPath ?? string.Empty;
                if (string.IsNullOrWhiteSpace(rawPartPath))
                {
                    failedCount++;
                    continue;
                }

                string partPath = NormalizePartPath(rawPartPath);
                string drwPath = Path.ChangeExtension(partPath, ".slddrw");
                if (string.IsNullOrWhiteSpace(drwPath) || !processedDrwPaths.Add(drwPath))
                {
                    continue;
                }

                if (!File.Exists(drwPath))
                {
                    missingDrwCount++;
                    Debug.WriteLine($"未找到工程图: {drwPath}");
                    continue;
                }

                int openErrors = 0;
                int openWarnings = 0;
                ModelDoc2 drwModel = swApp.OpenDoc6(
                    drwPath,
                    (int)swDocumentTypes_e.swDocDRAWING,
                    (int)swOpenDocOptions_e.swOpenDocOptions_Silent,
                    "",
                    ref openErrors,
                    ref openWarnings) as ModelDoc2;

                if (drwModel == null)
                {
                    failedCount++;
                    Debug.WriteLine($"打开工程图失败: {drwPath}, errors={openErrors}, warnings={openWarnings}");
                    continue;
                }

                try
                {
                    drw2dwg.run(drwModel, swApp);
                    successCount++;
                }
                catch (Exception ex)
                {
                    failedCount++;
                    Debug.WriteLine($"转换失败: {drwPath}, {ex.Message}");
                }
                finally
                {
                    swApp.CloseDoc(drwModel.GetTitle());
                }
            }

            string resultMessage = $"批量完成：成功 {successCount}，缺少工程图 {missingDrwCount}，失败 {failedCount}";
            swApp.SendMsgToUser(resultMessage);
            Debug.WriteLine(resultMessage);
        }
        catch (OperationCanceledException)
        {
            swApp?.SendMsgToUser("命令已中止");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"装配体批量 DRW 转 DWG 失败：{ex.Message}");
            swApp?.SendMsgToUser($"装配体批量 DRW 转 DWG 失败：{ex.Message}");
        }
    }

    private static string NormalizePartPath(string pathName)
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

        return originalPath;
    }


  
    [SolidWorksAddinStudy.Command(1015, "复制DWG文件到剪贴板", "将当前文档对应的DWG文件路径复制到剪贴板", "copyfile", 0, ShowOutputWindow = true)]
    private void CopyFileToClipboard()
    {
        try
        {
            StartCancelableCommand();
            if (swApp == null)
            {
                Console.WriteLine("SolidWorks 未初始化");
                Debug.WriteLine("SolidWorks 未初始化");
                return;
            }

            ModelDoc2 swModel = (ModelDoc2)swApp.ActiveDoc;
            
            if (swModel == null)
            {
                Console.WriteLine("没有打开的文档");
                Debug.WriteLine("没有打开的文档");
                swApp.SendMsgToUser("请先打开一个文档");
                return;
            }

            string filePath = swModel.GetPathName();
            
            if (string.IsNullOrEmpty(filePath))
            {
                Console.WriteLine("当前文档尚未保存，请先保存文档");
                swApp.SendMsgToUser("当前文档尚未保存，请先保存文档");
                return;
            }

            // 如果是DRW文件，直接导出DWG
            if (filePath.ToLower().EndsWith(".slddrw"))
            {
                Console.WriteLine($"检测到DRW文件: {filePath}");
                try
                {
                    Console.WriteLine($"正在导出DWG...");
                    string exportedDwgPath = drw2dwg.run(swModel, swApp);
                    if (!string.IsNullOrEmpty(exportedDwgPath) && File.Exists(exportedDwgPath))
                    {
                        filePath = exportedDwgPath;
                        Console.WriteLine($"导出DWG成功: {filePath}");
                    }
                    else
                    {
                        Console.WriteLine($"导出DWG失败");
                        swApp.SendMsgToUser("导出DWG文件失败");
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"导出DWG异常: {ex.Message}");
                    swApp.SendMsgToUser($"导出DWG文件失败: {ex.Message}");
                    return;
                }
            }
            if (filePath.ToLower().EndsWith(".sldprt") || filePath.ToLower().EndsWith(".prt"))
            {
                string partname = Path.GetFileNameWithoutExtension(filePath);
                string? currentDirectory = Path.GetDirectoryName(filePath);
                filePath = Path.Combine(currentDirectory, "step", $"{partname}.STEP");
            }
   

            if (!File.Exists(filePath))
            {
                Console.WriteLine($"DWG文件不存在: {filePath}");
                swApp.SendMsgToUser($"DWG文件不存在: {filePath}");
                return;
            }

            // 清空剪贴板
            System.Windows.Forms.Clipboard.Clear();
            Console.WriteLine("已清空剪贴板");
            Debug.WriteLine("已清空剪贴板");

            System.Collections.Specialized.StringCollection fileList = new System.Collections.Specialized.StringCollection();
            fileList.Add(filePath);
            
            System.Windows.Forms.Clipboard.SetFileDropList(fileList);
            
            string okMsg =
                $"已将文件复制到剪贴板: {Path.GetFileName(filePath)}\n完整路径: {filePath}";
            Console.WriteLine(okMsg);
            Debug.WriteLine($"DWG文件已复制到剪贴板: {filePath}");
            swApp.SendMsgToUser($"已将DWG文件复制到剪贴板:\n{Path.GetFileName(filePath)}");
        }
        catch (OperationCanceledException)
        {
            swApp?.SendMsgToUser("命令已中止");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"复制文件到剪贴板失败: {ex.Message}");
            Debug.WriteLine($"复制文件到剪贴板失败: {ex.Message}");
            swApp?.SendMsgToUser($"复制文件到剪贴板失败: {ex.Message}");
        }
    }

    [SolidWorksAddinStudy.Command(1017, "复制当前零件", "复制当前sldprt为新文件", "copy_current_sldprt", (int)swDocumentTypes_e.swDocPART, ShowOutputWindow = true)]
    private void CopyCurrentSldprt()
    {
        try
        {
            StartCancelableCommand();
            if (swApp == null)
            {
                Debug.WriteLine("SolidWorks 未初始化");
                return;
            }

            ModelDoc2 swModel = (ModelDoc2)swApp.ActiveDoc;
            if (swModel == null)
            {
                swApp.SendMsgToUser("请先打开一个零件文档");
                return;
            }

            if (swModel.GetType() != (int)swDocumentTypes_e.swDocPART)
            {
                swApp.SendMsgToUser("当前文档不是零件文档");
                return;
            }

            string sourcePath = swModel.GetPathName();
            if (string.IsNullOrWhiteSpace(sourcePath))
            {
                swApp.SendMsgToUser("当前零件尚未保存，请先保存后再复制");
                return;
            }

            if (!File.Exists(sourcePath))
            {
                swApp.SendMsgToUser($"源文件不存在: {sourcePath}");
                return;
            }

            string targetPath = BuildUniquePartCopyPath(sourcePath);
            File.Copy(sourcePath, targetPath, false);

            string targetName = Path.GetFileName(targetPath);
            swApp.SendMsgToUser($"复制完成: {targetName}");
            Debug.WriteLine($"已复制零件: {sourcePath} -> {targetPath}");
        }
        catch (OperationCanceledException)
        {
            swApp?.SendMsgToUser("命令已中止");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"复制当前零件失败: {ex.Message}");
            swApp?.SendMsgToUser($"复制当前零件失败: {ex.Message}");
        }
    }

    private string BuildUniquePartCopyPath(string sourcePath)
    {
        string directory = Path.GetDirectoryName(sourcePath) ?? string.Empty;
        string fileNameWithoutExtension = Path.GetFileNameWithoutExtension(sourcePath);
        string extension = Path.GetExtension(sourcePath);

        string candidate = Path.Combine(directory, $"{fileNameWithoutExtension}_副本{extension}");
        if (!File.Exists(candidate))
        {
            return candidate;
        }

        int index = 2;
        while (true)
        {
            CommandCancelManager.ThrowIfCancellationRequested();
            candidate = Path.Combine(directory, $"{fileNameWithoutExtension}_副本{index}{extension}");
            if (!File.Exists(candidate))
            {
                return candidate;
            }

            index++;
        }
    }

    [SolidWorksAddinStudy.Command(1018, "名称尺寸匹配替换", "检查名称尺寸与GetBox尺寸并自动替换", "check_name_size_match", (int)swDocumentTypes_e.swDocASSEMBLY, ShowOutputWindow = true)]
    private void CheckAssemblyNameSizeMatch()
    {
        try
        {
            StartCancelableCommand();
            if (swApp == null)
            {
                Debug.WriteLine("SolidWorks 未初始化");
                return;
            }

            ModelDoc2 swModel = (ModelDoc2)swApp.ActiveDoc;
            if (swModel == null || swModel.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                swApp.SendMsgToUser("请先打开一个装配体文档");
                return;
            }

            Console.WriteLine("=== 名称尺寸匹配/替换开始 ===");
            var result = NameSizeMatchService.Run(swModel);
            Console.WriteLine($"=== 完成: 识别 {result.ParsedCount} 个, 不匹配 {result.MismatchCount} 个, 已替换 {result.ReplacedCount} 个, 替换失败 {result.ReplaceFailCount} 个 ===");
            swModel.EditRebuild3();
            swApp.SendMsgToUser($"名称尺寸匹配完成：识别{result.ParsedCount}，不匹配{result.MismatchCount}，已替换{result.ReplacedCount}，失败{result.ReplaceFailCount}");
        }
        catch (OperationCanceledException)
        {
            swApp?.SendMsgToUser("命令已中止");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"名称尺寸匹配检查失败: {ex.Message}");
            swApp?.SendMsgToUser($"名称尺寸匹配检查失败: {ex.Message}");
        }
    }



    [SolidWorksAddinStudy.Command(1016, "标折弯尺寸", "自动标注选中视图的折弯尺寸", "dimension_bends", (int)swDocumentTypes_e.swDocDRAWING, ShowOutputWindow = true)]
    private void DimensionBends()
    {
        try
        {
            StartCancelableCommand();
            if (swApp == null)
            {
                Debug.WriteLine("SolidWorks 未初始化");
                return;
            }

            ModelDoc2 swModel = (ModelDoc2)swApp.ActiveDoc;
            
            if (swModel == null)
            {
                Debug.WriteLine("没有打开的文档");
                swApp.SendMsgToUser("请先打开一个工程图文档");
                return;
            }

            // 调用 benddim 标折弯尺寸
            benddim.AddBendDimensions(swApp);
            
            Debug.WriteLine("折弯尺寸标注完成");
        }
        catch (OperationCanceledException)
        {
            swApp?.SendMsgToUser("命令已中止");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"AddBendDimensions failed: {ex.Message}");
            swApp?.SendMsgToUser($"AddBendDimensions failed: {ex.Message}");
        }
    }

    [SolidWorksAddinStudy.Command(1023, "选中视图注解字号", "仅修改选中视图注解文字大小", "set_selected_view_note_size", (int)swDocumentTypes_e.swDocDRAWING, ShowOutputWindow = true)]
    private void SetSelectedViewNoteSize()
    {
        try
        {
            StartCancelableCommand();
            if (swApp == null)
            {
                Debug.WriteLine("SolidWorks 未初始化");
                return;
            }

            ModelDoc2 swModel = (ModelDoc2)swApp.ActiveDoc;
            if (swModel == null)
            {
                swApp.SendMsgToUser("请先打开一个工程图文档");
                return;
            }

            // 只处理当前选中视图里的注解，按 0.85m 设置
            selected_view_note_text_size.run(swApp, swModel, 0.85);
        }
        catch (OperationCanceledException)
        {
            swApp?.SendMsgToUser("命令已中止");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"设置选中视图注解字号失败: {ex.Message}");
            swApp?.SendMsgToUser($"设置选中视图注解字号失败: {ex.Message}");
        }
    }

    [SolidWorksAddinStudy.Command(1024, "关闭并删除工程图", "关闭当前工程图并删除对应drw文件", "close_delete_drw", (int)swDocumentTypes_e.swDocDRAWING, ShowOutputWindow = true)]
    private void CloseDeleteDrw()
    {
        try
        {
            StartCancelableCommand();
            if (swApp == null)
            {
                Debug.WriteLine("SolidWorks 未初始化");
                return;
            }

            ModelDoc2 swModel = (ModelDoc2)swApp.ActiveDoc;
            if (swModel == null)
            {
                swApp.SendMsgToUser("请先打开一个工程图文档");
                return;
            }

            close_delete_drw.run(swApp, swModel);
        }
        catch (OperationCanceledException)
        {
            swApp?.SendMsgToUser("命令已中止");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"关闭并删除工程图失败: {ex.Message}");
            swApp?.SendMsgToUser($"关闭并删除工程图失败: {ex.Message}");
        }
    }

    [SolidWorksAddinStudy.Command(1025, "导出选中子装配体为SLDPRT", "将当前装配体中选中的子装配体导出为SLDPRT", "export_selected_subasm_sldprt", (int)swDocumentTypes_e.swDocASSEMBLY, ShowOutputWindow = true)]
    private void ExportSelectedSubAssemblyToSldprt()
    {
        try
        {
            StartCancelableCommand();
            if (swApp == null)
            {
                Debug.WriteLine("SolidWorks 未初始化");
                return;
            }

            ModelDoc2 swModel = (ModelDoc2)swApp.ActiveDoc;
            if (swModel == null || swModel.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                swApp.SendMsgToUser("请先打开一个装配体文档");
                return;
            }

            var control = AddinStudy.GetEquationModelTaskPaneControl();
            if (control == null)
            {
                swApp.SendMsgToUser("机型管理任务窗格未初始化");
                return;
            }

            bool ok = control.TryExportSelectedSubAssemblyToSldprt(out string message);
            if (!ok && !string.IsNullOrWhiteSpace(message))
            {
                swApp.SendMsgToUser(message);
            }
        }
        catch (OperationCanceledException)
        {
            swApp?.SendMsgToUser("命令已中止");
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"导出选中子装配体为SLDPRT失败: {ex.Message}");
            swApp?.SendMsgToUser($"导出选中子装配体为SLDPRT失败: {ex.Message}");
        }
    }

    /// <summary>
    /// 从工程图更新对应零件的出图状态
    /// </summary>
    private void UpdatePartDrawnStatusFromDrawing(ModelDoc2 drawingModel)
    {
        try
        {
            var taskPaneControl = AddinStudy.GetTaskPaneControl();
            if (taskPaneControl == null) return;
            
            // 获取工程图引用的零件/装配体名称
            DrawingDoc drawingDoc = (DrawingDoc)drawingModel;
            object[] views = (object[])drawingDoc.GetViews();
            
            if (views == null || views.Length == 0) return;
            
            // 遍历所有视图
            foreach (object viewObj in views)
            {
                CommandCancelManager.ThrowIfCancellationRequested();
                object[] viewArray = (object[])viewObj;
                foreach (object vObj in viewArray)
                {
                    CommandCancelManager.ThrowIfCancellationRequested();
                    SolidWorks.Interop.sldworks.View view = (SolidWorks.Interop.sldworks.View)vObj;
                    ModelDoc2 referencedModel = (ModelDoc2)view.ReferencedDocument;
                    
                    if (referencedModel != null)
                    {
                        string refPath = referencedModel.GetPathName();
                        if (!string.IsNullOrEmpty(refPath))
                        {
                            string partName = System.IO.Path.GetFileNameWithoutExtension(refPath);
                            
                            // 更新任务窗格中的出图状态
                            taskPaneControl.UpdatePartDrawnStatus(partName, "已出图");
                            Debug.WriteLine($"已更新零件 '{partName}' 的出图状态");
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"更新出图状态失败: {ex.Message}");
        }
    }

    private void StartCancelableCommand()
    {
        CommandCancelManager.Reset();
        CommandCancelManager.ThrowIfCancellationRequested();
    }


}}