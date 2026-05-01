using System;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;
using SolidWorks.Interop.sldworks;

namespace tools
{
    public sealed class DwgOutputPathSettings
    {
        public string OutputRootTemplate { get; set; } = "钣金";
        public string FallbackRootName { get; set; } = "钣金";
        public string LegacyTestRootFolder { get; set; } = "测试";
        public string WeldmentFolder { get; set; } = "焊接图";
        public string CncFolder { get; set; } = "CNC";
        public string EngineeringFolder { get; set; } = "工程图";
        public string SheetMetalFolder { get; set; } = "下料";
        public string SusThicknessPrefix { get; set; } = "sus";
    }

    public static class DwgOutputPaths
    {
        private static readonly object SyncLock = new object();
        private static DwgOutputPathSettings? _cached;

        public static DwgOutputPathSettings Get()
        {
            if (_cached != null)
            {
                return _cached;
            }

            lock (SyncLock)
            {
                if (_cached != null)
                {
                    return _cached;
                }

                _cached = LoadSettings();
                return _cached;
            }
        }

        public static string ResolveOutputRoot(string modelDirectory)
        {
            var settings = Get();
            string currentFolder = Path.GetFileName(modelDirectory ?? string.Empty) ?? string.Empty;
            string template = settings.OutputRootTemplate ?? string.Empty;
            string rootName = template.Replace("{CurrentFolder}", currentFolder).Trim();
            if (string.IsNullOrWhiteSpace(rootName))
            {
                rootName = settings.FallbackRootName;
            }

            return Path.Combine(modelDirectory, rootName);
        }

        public static string BuildMaterialThicknessFolderName(ModelDoc2 modelDoc, string thickness)
        {
            var settings = Get();
            if (modelDoc is not PartDoc partDoc)
            {
                return thickness;
            }

            try
            {
                string materialDb = string.Empty;
                string materialName = partDoc.GetMaterialPropertyName(out materialDb) ?? string.Empty;
                string normalized = materialName.ToLowerInvariant();
                if (normalized.Contains("sus") || materialName.Contains("不锈钢"))
                {
                    return (settings.SusThicknessPrefix ?? "sus") + thickness;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"读取材质失败，按普通厚度目录处理: {ex.Message}");
            }

            return thickness;
        }

        private static DwgOutputPathSettings LoadSettings()
        {
            var defaults = new DwgOutputPathSettings();
            try
            {
                string? dllDir = Path.GetDirectoryName(typeof(DwgOutputPaths).Assembly.Location);
                if (string.IsNullOrWhiteSpace(dllDir))
                {
                    return defaults;
                }

                string configPath = Path.Combine(dllDir, "dwg_output_paths.json");
                if (!File.Exists(configPath))
                {
                    return defaults;
                }

                string json = File.ReadAllText(configPath);
                DwgOutputPathSettings? loaded = JsonConvert.DeserializeObject<DwgOutputPathSettings>(json);
                if (loaded == null)
                {
                    return defaults;
                }

                return MergeWithDefaults(loaded, defaults);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"读取 dwg_output_paths.json 失败，使用默认配置: {ex.Message}");
                return defaults;
            }
        }

        private static DwgOutputPathSettings MergeWithDefaults(DwgOutputPathSettings loaded, DwgOutputPathSettings defaults)
        {
            loaded.OutputRootTemplate = NormalizeOrDefault(loaded.OutputRootTemplate, defaults.OutputRootTemplate);
            loaded.FallbackRootName = NormalizeOrDefault(loaded.FallbackRootName, defaults.FallbackRootName);
            loaded.LegacyTestRootFolder = NormalizeOrDefault(loaded.LegacyTestRootFolder, defaults.LegacyTestRootFolder);
            loaded.WeldmentFolder = NormalizeOrDefault(loaded.WeldmentFolder, defaults.WeldmentFolder);
            loaded.CncFolder = NormalizeOrDefault(loaded.CncFolder, defaults.CncFolder);
            loaded.EngineeringFolder = NormalizeOrDefault(loaded.EngineeringFolder, defaults.EngineeringFolder);
            loaded.SheetMetalFolder = NormalizeOrDefault(loaded.SheetMetalFolder, defaults.SheetMetalFolder);
            loaded.SusThicknessPrefix = NormalizeOrDefault(loaded.SusThicknessPrefix, defaults.SusThicknessPrefix);
            return loaded;
        }

        private static string NormalizeOrDefault(string? value, string fallback)
        {
            string cleaned = (value ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(cleaned) ? fallback : cleaned;
        }
    }
}
