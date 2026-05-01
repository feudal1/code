using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace tools
{
    public class Create_new_assembly
    {
        public const int Ok = 0;
        public const int ErrTemplate = 1;
        public const int ErrCreateDoc = 2;
        public const int ErrSave = 3;
        public const int ErrInsert = 4;
        public const int ErrSplit = 5;
        public static string LastSplitErrorSummary { get; private set; } = string.Empty;

        public static int run(SldWorks swApp, string assemblyName, List<string> partNames)
        {
            if (swApp == null || string.IsNullOrWhiteSpace(assemblyName) || partNames == null || partNames.Count == 0)
            {
                return ErrInsert;
            }

            try
            {
                assemblyName = NormalizePathSafe(assemblyName);
                string templateFolder = @"C:\ProgramData\SolidWorks\SOLIDWORKS 2025\templates";
                string templatePath = ResolveTemplatePath(templateFolder, "*.asmdot");

                if (string.IsNullOrWhiteSpace(templatePath))
                {
                    templatePath = swApp.GetDocumentTemplate((int)swDocumentTypes_e.swDocASSEMBLY, "", 0, 0, 0);
                }

                if (string.IsNullOrWhiteSpace(templatePath) || !File.Exists(templatePath))
                {
                    Debug.WriteLine("Create assembly failed: assembly template not found.");
                    return ErrTemplate;
                }

                string outputDir = Path.GetDirectoryName(assemblyName) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(outputDir))
                {
                    Directory.CreateDirectory(outputDir);
                }

                ModelDoc2 asmModel = swApp.NewDocument(templatePath, 0, 0, 0) as ModelDoc2;
                if (asmModel == null || asmModel.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
                {
                    Debug.WriteLine("Create assembly failed: cannot create assembly document.");
                    return ErrCreateDoc;
                }

                int errors = 0;
                int warnings = 0;
                int saveResult = asmModel.SaveAs3(assemblyName, (int)swSaveAsOptions_e.swSaveAsOptions_Silent, 0);
                if (saveResult == 0)
                {
                    Debug.WriteLine($"Create assembly failed: cannot save file {assemblyName}.");
                    return ErrSave;
                }

                AssemblyDoc assemblyDoc = asmModel as AssemblyDoc;
                if (assemblyDoc == null)
                {
                    Debug.WriteLine("Create assembly failed: cast to AssemblyDoc failed.");
                    return ErrCreateDoc;
                }

                int insertedCount = 0;
                foreach (string partPath in partNames)
                {
                    if (string.IsNullOrWhiteSpace(partPath) || !File.Exists(partPath))
                    {
                        continue;
                    }

                    Component2 component = assemblyDoc.AddComponent5(partPath, 0, "", false, "", 0, 0, 0);
                    if (component == null)
                    {
                        Debug.WriteLine($"Create assembly warning: failed to insert part {partPath}");
                        continue;
                    }

                    insertedCount++;
                }

                if (insertedCount == 0)
                {
                    Debug.WriteLine("Create assembly failed: no parts inserted.");
                    return ErrInsert;
                }

                asmModel.ClearSelection2(true);
                asmModel.ShowNamedView2("*Isometric", (int)swStandardViews_e.swIsometricView);
                asmModel.ViewZoomtofit2();
                asmModel.EditRebuild3();
                asmModel.Save3((int)swSaveAsOptions_e.swSaveAsOptions_Silent, ref errors, ref warnings);

                return Ok;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Create_new_assembly.run failed: {ex.Message}");
                return ErrInsert;
            }
        }

        public static List<string> SplitBodiesToPartFiles(SldWorks swApp, ModelDoc2 sourcePartModel)
        {
            LastSplitErrorSummary = string.Empty;
            var result = new List<string>();
            if (swApp == null || sourcePartModel == null || sourcePartModel.GetType() != (int)swDocumentTypes_e.swDocPART)
            {
                LastSplitErrorSummary = "输入无效：当前文档不是零件或SolidWorks未初始化。";
                return result;
            }

            string sourcePath = sourcePartModel.GetPathName();
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
            {
                LastSplitErrorSummary = "源零件未保存或文件不存在。";
                return result;
            }

            PartDoc sourcePartDoc = sourcePartModel as PartDoc;
            if (sourcePartDoc == null)
            {
                LastSplitErrorSummary = "当前活动文档无法转换为 PartDoc。";
                return result;
            }

            object[] bodies = sourcePartDoc.GetBodies2((int)swBodyType_e.swSolidBody, true) as object[];
            if (bodies == null || bodies.Length == 0)
            {
                LastSplitErrorSummary = "未检测到可导出的实体（solid body）。";
                return result;
            }

            // ??????????????????
            if (bodies.Length == 1)
            {
                result.Add(sourcePath);
                return result;
            }

            string sourceTitle = sourcePartModel.GetTitle();
            string sourceName = SanitizeFileName(Path.GetFileNameWithoutExtension(sourcePath));
            string sourceDir = Path.GetDirectoryName(sourcePath) ?? string.Empty;
            string splitDir = Path.Combine(sourceDir, sourceName + "_split");
            Directory.CreateDirectory(splitDir);

            string partTemplate = ResolvePartTemplatePath(swApp);
            if (string.IsNullOrWhiteSpace(partTemplate) || !File.Exists(partTemplate))
            {
                Debug.WriteLine("Split body failed: part template not found.");
                LastSplitErrorSummary = "未找到零件模板（swDocPART）。";
                return result;
            }

            swApp.ActivateDoc3(sourceTitle, true, 0, 0);
            sourcePartModel = swApp.ActiveDoc as ModelDoc2 ?? sourcePartModel;
            sourcePartDoc = sourcePartModel as PartDoc ?? sourcePartDoc;

            int successCount = 0;
            int selectFailCount = 0;
            int exportFailCount = 0;
            string firstFailedBody = string.Empty;
            string firstFailedReason = string.Empty;

            foreach (object obj in bodies)
            {
                Body2 body = obj as Body2;
                if (body == null)
                {
                    continue;
                }

                string bodyName = string.IsNullOrWhiteSpace(body.Name) ? "Body" : body.Name;
                string safeBodyName = SanitizeFileName(bodyName);
                string targetPartPath = NormalizePathSafe(Path.Combine(splitDir, $"{sourceName}_{safeBodyName}.sldprt"));

                int suffix = 2;
                while (File.Exists(targetPartPath))
                {
                    targetPartPath = NormalizePathSafe(Path.Combine(splitDir, $"{sourceName}_{safeBodyName}_{suffix}.sldprt"));
                    suffix++;
                }

                swApp.ActivateDoc3(sourceTitle, true, 0, 0);
                sourcePartModel = swApp.ActiveDoc as ModelDoc2 ?? sourcePartModel;
                sourcePartDoc = sourcePartModel as PartDoc ?? sourcePartDoc;
                sourcePartModel.ClearSelection2(true);
                bool selected = sourcePartModel.Extension.SelectByID2(bodyName, "SOLIDBODY", 0, 0, 0, false, 0, null, 0);
                if (!selected)
                {
                    Entity bodyEntity = body as Entity;
                    selected = bodyEntity != null && bodyEntity.Select4(false, null);
                }
                if (!selected)
                {
                    object[] selectObjs = { body };
                    selected = sourcePartModel.Extension.MultiSelect2(selectObjs, false, null) == 1;
                }
                if (!selected)
                {
                    Debug.WriteLine($"Split body warning: cannot select body {bodyName}");
                    selectFailCount++;
                    if (string.IsNullOrWhiteSpace(firstFailedBody))
                    {
                        firstFailedBody = bodyName;
                        firstFailedReason = "实体选择失败";
                    }
                    continue;
                }

                bool inserted = ExportBodyToPartFile(swApp, sourcePartDoc, body, targetPartPath, partTemplate);
                sourcePartModel.ClearSelection2(true);
                swApp.ActivateDoc3(sourceTitle, true, 0, 0);

                if (inserted && File.Exists(targetPartPath))
                {
                    result.Add(targetPartPath);
                    successCount++;
                }
                else
                {
                    Debug.WriteLine($"Split body warning: failed to export body {bodyName} to {targetPartPath}");
                    exportFailCount++;
                    if (string.IsNullOrWhiteSpace(firstFailedBody))
                    {
                        firstFailedBody = bodyName;
                        firstFailedReason = "实体导出失败";
                    }
                }
            }

            if (result.Count == 0 && bodies.Length > 1)
            {
                LastSplitErrorSummary = string.IsNullOrWhiteSpace(firstFailedBody)
                    ? $"拆分失败：共 {bodies.Length} 个实体，全部导出失败。"
                    : $"拆分失败：共 {bodies.Length} 个实体，成功 0；首个失败实体={firstFailedBody}，原因={firstFailedReason}；选择失败={selectFailCount}，导出失败={exportFailCount}。";
            }
            else
            {
                LastSplitErrorSummary = $"拆分统计：总实体={bodies.Length}，成功={successCount}，选择失败={selectFailCount}，导出失败={exportFailCount}。";
            }

            return result;
        }

        private static string ResolveTemplatePath(string folderPath, string searchPattern)
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                return string.Empty;
            }

            string[] files = Directory.GetFiles(folderPath, searchPattern, SearchOption.TopDirectoryOnly);
            if (files == null || files.Length == 0)
            {
                return string.Empty;
            }

            return files[0];
        }

        private static string ResolvePartTemplatePath(SldWorks swApp)
        {
            string[] candidatePaths =
            {
                @"C:\ProgramData\SolidWorks\SOLIDWORKS 2025\templates\零件.prtdot",
                @"C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2025\templates\零件.prtdot",
                @"C:\ProgramData\SolidWorks\SOLIDWORKS 2025\templates\Part.prtdot",
                @"C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2025\templates\Part.prtdot"
            };

            foreach (string candidate in candidatePaths)
            {
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            string[] candidateFolders =
            {
                @"C:\ProgramData\SolidWorks\SOLIDWORKS 2025\templates",
                @"C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2025\templates"
            };

            foreach (string folder in candidateFolders)
            {
                if (!Directory.Exists(folder))
                {
                    continue;
                }

                string[] files = Directory.GetFiles(folder, "*.prtdot", SearchOption.TopDirectoryOnly);
                if (files == null || files.Length == 0)
                {
                    continue;
                }

                string zhPart = files.FirstOrDefault(f => Path.GetFileName(f).Contains("零件"));
                if (!string.IsNullOrWhiteSpace(zhPart))
                {
                    return zhPart;
                }

                string enPart = files.FirstOrDefault(f => Path.GetFileName(f).IndexOf("part", StringComparison.OrdinalIgnoreCase) >= 0);
                if (!string.IsNullOrWhiteSpace(enPart))
                {
                    return enPart;
                }

                return files[0];
            }

            string fromSw = swApp?.GetDocumentTemplate((int)swDocumentTypes_e.swDocPART, "", 0, 0, 0);
            if (!string.IsNullOrWhiteSpace(fromSw) && File.Exists(fromSw))
            {
                return fromSw;
            }

            return string.Empty;
        }

        private static bool ExportBodyToPartFile(SldWorks swApp, PartDoc sourcePartDoc, Body2 sourceBody, string targetPartPath, string partTemplate)
        {
            try
            {
                if (swApp == null || sourcePartDoc == null || sourceBody == null || string.IsNullOrWhiteSpace(targetPartPath) || string.IsNullOrWhiteSpace(partTemplate))
                {
                    return false;
                }

                if (!File.Exists(partTemplate))
                {
                    Debug.WriteLine("Split body failed: part template not found.");
                    return false;
                }

                int errors;
                int warnings;
                bool exported = sourcePartDoc.SaveToFile3(targetPartPath, 1, 1, false, partTemplate, out errors, out warnings);
                if (!exported)
                {
                    Debug.WriteLine($"Split body warning: SaveToFile3 failed. errors={errors}, warnings={warnings}, target={targetPartPath}, fallback=CreateFeatureFromBody3");
                    return ExportBodyByFeature(swApp, sourceBody, targetPartPath, partTemplate);
                }

                return File.Exists(targetPartPath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ExportBodyToPartFile failed: {ex.Message}");
                return false;
            }
        }

        private static bool ExportBodyByFeature(SldWorks swApp, Body2 sourceBody, string targetPartPath, string partTemplate)
        {
            try
            {
                ModelDoc2 newPartModel = swApp.NewDocument(partTemplate, 0, 0, 0) as ModelDoc2;
                if (newPartModel == null || newPartModel.GetType() != (int)swDocumentTypes_e.swDocPART)
                {
                    return false;
                }

                PartDoc newPartDoc = newPartModel as PartDoc;
                if (newPartDoc == null)
                {
                    return false;
                }

                object copiedObj = null;
                try
                {
                    copiedObj = sourceBody.Copy();
                }
                catch
                {
                    copiedObj = sourceBody;
                }

                if (copiedObj == null)
                {
                    return false;
                }

                Feature importedFeature = newPartDoc.CreateFeatureFromBody3(copiedObj, false, 0) as Feature;
                if (importedFeature == null)
                {
                    return false;
                }

                int saveResult = newPartModel.SaveAs3(targetPartPath, (int)swSaveAsOptions_e.swSaveAsOptions_Silent, 0);
                bool ok = saveResult != 0 && File.Exists(targetPartPath);

                string title = newPartModel.GetTitle();
                if (!string.IsNullOrWhiteSpace(title))
                {
                    swApp.CloseDoc(title);
                }

                return ok;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ExportBodyByFeature failed: {ex.Message}");
                return false;
            }
        }

        private static string SanitizeFileName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName))
            {
                return "Body";
            }

            string safe = rawName.Trim();
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                safe = safe.Replace(c, '_');
            }
            safe = new string(safe.Where(ch => !char.IsControl(ch)).ToArray());

            return string.IsNullOrWhiteSpace(safe) ? "Body" : safe;
        }

        private static string NormalizePathSafe(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            string root = Path.GetPathRoot(path) ?? string.Empty;
            string left = path.Substring(root.Length);
            string[] parts = left.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length; i++)
            {
                parts[i] = SanitizeFileName(parts[i]);
            }

            string rebuilt = root;
            foreach (string p in parts)
            {
                rebuilt = string.IsNullOrWhiteSpace(rebuilt) ? p : Path.Combine(rebuilt, p);
            }

            return rebuilt;
        }
    }
}
