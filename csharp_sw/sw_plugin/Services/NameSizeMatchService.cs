using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace SolidWorksAddinStudy
{
    internal static class NameSizeMatchService
    {
        internal readonly struct NameSizeMatchResult
        {
            public NameSizeMatchResult(int parsedCount, int mismatchCount, int replacedCount, int replaceFailCount)
            {
                ParsedCount = parsedCount;
                MismatchCount = mismatchCount;
                ReplacedCount = replacedCount;
                ReplaceFailCount = replaceFailCount;
            }

            public int ParsedCount { get; }
            public int MismatchCount { get; }
            public int ReplacedCount { get; }
            public int ReplaceFailCount { get; }
        }

        public static NameSizeMatchResult Run(ModelDoc2 rootAssemblyModel)
        {
            int parsedCount = 0;
            int mismatchCount = 0;
            int replacedCount = 0;
            int replaceFailCount = 0;
            var visitedAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            ProcessAssembly(
                rootAssemblyModel,
                ref parsedCount,
                ref mismatchCount,
                ref replacedCount,
                ref replaceFailCount,
                visitedAssemblies);

            return new NameSizeMatchResult(parsedCount, mismatchCount, replacedCount, replaceFailCount);
        }

        private static void ProcessAssembly(
            ModelDoc2 assemblyModel,
            ref int parsedCount,
            ref int mismatchCount,
            ref int replacedCount,
            ref int replaceFailCount,
            HashSet<string> visitedAssemblies)
        {
            if (assemblyModel == null || assemblyModel.GetType() != (int)swDocumentTypes_e.swDocASSEMBLY)
            {
                return;
            }

            string asmKey = assemblyModel.GetPathName();
            if (string.IsNullOrWhiteSpace(asmKey))
            {
                asmKey = assemblyModel.GetTitle();
            }

            if (!visitedAssemblies.Add(asmKey))
            {
                return;
            }

            AssemblyDoc swAssembly = (AssemblyDoc)assemblyModel;
            // 只获取当前层级组件，递归时再进入子装配体，避免重复全量遍历导致卡顿
            object[] components = (object[])swAssembly.GetComponents(true);
            if (components == null || components.Length == 0)
            {
                return;
            }

            const double tolerance = 0.5; // mm

            foreach (object compObj in components)
            {
                Component2 comp = (Component2)compObj;
                ModelDoc2 compModel = null;

                try
                {
                    compModel = (ModelDoc2)comp.GetModelDoc2();
                }
                catch
                {
                    compModel = null;
                }

                if (compModel != null && compModel.GetType() == (int)swDocumentTypes_e.swDocASSEMBLY)
                {
                    ProcessAssembly(
                        compModel,
                        ref parsedCount,
                        ref mismatchCount,
                        ref replacedCount,
                        ref replaceFailCount,
                        visitedAssemblies);
                    continue;
                }

                string originalComponentName = comp.Name2 ?? string.Empty;
                string rawName = NormalizeComponentName(originalComponentName);

                if (!TryParseSizeFromName(rawName, out double[] nameDims))
                {
                    continue;
                }

                parsedCount++;

                if (!TryGetComponentDimensionsMm(comp, out double[] boxDims))
                {
                    Console.WriteLine($"[跳过] {rawName} -> 无法获取GetBox尺寸");
                    continue;
                }

                if (AreDimensionsClose(nameDims, boxDims, tolerance))
                {
                    continue;
                }

                mismatchCount++;
                string targetSizeToken = FormatDims(boxDims);
                string updatedComponentName = ReplaceSizeTokenInName(originalComponentName, targetSizeToken);

                if (string.Equals(updatedComponentName, originalComponentName, StringComparison.Ordinal))
                {
                    replaceFailCount++;
                    Console.WriteLine($"[不匹配-未替换] {rawName} | 名称:{FormatDims(nameDims)} | GetBox:{targetSizeToken}");
                    continue;
                }

                bool renamed = TryRenameComponent(assemblyModel, comp, updatedComponentName, targetSizeToken, out string renameMessage);
                if (renamed)
                {
                    replacedCount++;
                    Console.WriteLine($"[已替换] {rawName} | 名称:{FormatDims(nameDims)} -> {targetSizeToken}");
                }
                else
                {
                    replaceFailCount++;
                    Console.WriteLine($"[不匹配-替换失败] {rawName} | 目标:{targetSizeToken} | 原因:{renameMessage}");
                }
            }
        }

        private static bool TryRenameComponent(ModelDoc2 assemblyModel, Component2 comp, string updatedComponentName, string targetSizeToken, out string message)
        {
            message = string.Empty;
            if (assemblyModel == null)
            {
                message = "装配体文档为空";
                return false;
            }

            string targetLeafName = ExtractLeafComponentName(updatedComponentName);
            if (string.IsNullOrWhiteSpace(targetLeafName))
            {
                message = "目标名称为空";
                return false;
            }

            // 使用官方 RenameDocument 时不手动拼实例后缀，由 SW 自动处理重名实例。
            string renameTarget = targetLeafName;
            if (TryStripInstanceSuffix(targetLeafName, out string strippedTargetName))
            {
                renameTarget = strippedTargetName;
            }

            if (string.IsNullOrWhiteSpace(renameTarget))
            {
                message = "目标名称为空";
                return false;
            }

            string finalName = comp.Name2 ?? string.Empty;
            try
            {
                bool selected = comp.Select4(false, null, false);
                if (!selected)
                {
                    message = "组件选择失败";
                    return false;
                }

                ModelDocExtension modelExt = assemblyModel.Extension;
                if (modelExt == null)
                {
                    message = "ModelDocExtension 为空";
                    return false;
                }

                int renameError = modelExt.RenameDocument(renameTarget);
                finalName = comp.Name2 ?? string.Empty;
                if (renameError == (int)swRenameDocumentError_e.swRenameDocumentError_None)
                {
                    return true;
                }

                message = $"RenameDocument失败: error={renameError}, 目标[{renameTarget}], 当前[{finalName}]";
                return false;
            }
            catch (Exception ex)
            {
                message = $"RenameDocument异常: {ex.Message}";
                return false;
            }
        }

        private static bool IsRenameApplied(string currentComponentName, string targetInstanceName, string targetSizeToken, double[] targetDims)
        {
            string currentLeafName = ExtractLeafComponentName(currentComponentName ?? string.Empty);
            if (string.Equals(currentLeafName, targetInstanceName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string normalizedCurrentName = NormalizeComponentName(currentLeafName);
            if (!string.IsNullOrWhiteSpace(normalizedCurrentName))
            {
                if (!string.IsNullOrWhiteSpace(targetSizeToken) &&
                    normalizedCurrentName.IndexOf(targetSizeToken, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                if (targetDims != null &&
                    targetDims.Length == 3 &&
                    TryParseSizeFromName(normalizedCurrentName, out double[] currentDims) &&
                    AreDimensionsClose(currentDims, targetDims, 0.001))
                {
                    return true;
                }
            }

            return false;
        }

        private static string NormalizeComponentName(string componentName)
        {
            if (string.IsNullOrWhiteSpace(componentName))
            {
                return string.Empty;
            }

            int slashIndex = componentName.LastIndexOf('/');
            if (slashIndex >= 0 && slashIndex < componentName.Length - 1)
            {
                componentName = componentName.Substring(slashIndex + 1);
            }

            int lastDashIndex = componentName.LastIndexOf('-');
            if (lastDashIndex > 0 && lastDashIndex < componentName.Length - 1)
            {
                string suffix = componentName.Substring(lastDashIndex + 1);
                if (int.TryParse(suffix, out _))
                {
                    componentName = componentName.Substring(0, lastDashIndex);
                }
            }

            return componentName;
        }

        private static string ExtractLeafComponentName(string componentName)
        {
            if (string.IsNullOrWhiteSpace(componentName))
            {
                return string.Empty;
            }

            int slashIndex = componentName.LastIndexOf('/');
            if (slashIndex >= 0 && slashIndex < componentName.Length - 1)
            {
                return componentName.Substring(slashIndex + 1);
            }

            return componentName;
        }

        private static bool TryGetInstanceSuffix(string componentName, out string suffix)
        {
            suffix = string.Empty;
            string leaf = ExtractLeafComponentName(componentName);
            int lastDashIndex = leaf.LastIndexOf('-');
            if (lastDashIndex > 0 && lastDashIndex < leaf.Length - 1)
            {
                string possibleSuffix = leaf.Substring(lastDashIndex + 1);
                if (int.TryParse(possibleSuffix, out _))
                {
                    suffix = possibleSuffix;
                    return true;
                }
            }

            return false;
        }

        private static bool HasInstanceSuffix(string componentName)
        {
            return TryGetInstanceSuffix(componentName, out _);
        }

        private static bool TryStripInstanceSuffix(string componentName, out string nameWithoutSuffix)
        {
            nameWithoutSuffix = string.Empty;
            string leaf = ExtractLeafComponentName(componentName);
            int lastDashIndex = leaf.LastIndexOf('-');
            if (lastDashIndex > 0 && lastDashIndex < leaf.Length - 1)
            {
                string possibleSuffix = leaf.Substring(lastDashIndex + 1);
                if (int.TryParse(possibleSuffix, out _))
                {
                    nameWithoutSuffix = leaf.Substring(0, lastDashIndex);
                    return true;
                }
            }

            return false;
        }

        private static bool TryParseSizeFromName(string name, out double[] dims)
        {
            dims = Array.Empty<double>();
            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            var match = Regex.Match(name, @"(\d+(?:\.\d+)?)\s*[xX×]\s*(\d+(?:\.\d+)?)\s*[xX×]\s*(\d+(?:\.\d+)?)");
            if (!match.Success)
            {
                return false;
            }

            if (!double.TryParse(match.Groups[1].Value, out double d1) ||
                !double.TryParse(match.Groups[2].Value, out double d2) ||
                !double.TryParse(match.Groups[3].Value, out double d3))
            {
                return false;
            }

            dims = new[] { d1, d2, d3 };
            Array.Sort(dims);
            return true;
        }

        private static bool TryGetComponentDimensionsMm(Component2 comp, out double[] dims)
        {
            dims = Array.Empty<double>();
            object boxObj = comp.GetBox(false, false);
            if (boxObj == null || !(boxObj is double[]))
            {
                return false;
            }

            double[] box = (double[])boxObj;
            if (box.Length < 6)
            {
                return false;
            }

            double length = Math.Abs(box[3] - box[0]) * 1000.0;
            double width = Math.Abs(box[4] - box[1]) * 1000.0;
            double height = Math.Abs(box[5] - box[2]) * 1000.0;

            dims = new[] { length, width, height };
            Array.Sort(dims);
            return true;
        }

        private static bool AreDimensionsClose(double[] a, double[] b, double tolerance)
        {
            if (a == null || b == null || a.Length != 3 || b.Length != 3)
            {
                return false;
            }

            return Math.Abs(a[0] - b[0]) <= tolerance &&
                   Math.Abs(a[1] - b[1]) <= tolerance &&
                   Math.Abs(a[2] - b[2]) <= tolerance;
        }

        private static string FormatDims(double[] dims)
        {
            if (dims == null || dims.Length != 3)
            {
                return "-";
            }

            return $"{dims[0]:0.###}x{dims[1]:0.###}x{dims[2]:0.###}";
        }

        private static string ReplaceSizeTokenInName(string name, string replacementSizeToken)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(replacementSizeToken))
            {
                return name;
            }

            const string sizePattern = @"(\d+(?:\.\d+)?)\s*[xX×]\s*(\d+(?:\.\d+)?)\s*[xX×]\s*(\d+(?:\.\d+)?)";
            var regex = new Regex(sizePattern);
            return regex.Replace(name, replacementSizeToken, 1);
        }
    }
}
