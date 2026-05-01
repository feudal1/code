using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.Interop;
using Autodesk.AutoCAD.Interop.Common;
using Newtonsoft.Json;

namespace cad_tools
{
    /// <summary>
    /// 从CAD图纸中提取标注实体并转换为可学习规则。
    /// </summary>
    public static class CADDimensionExtractor
    {
        public static List<CADExtractedDimensionRule> ExtractRulesFromDocument(AcadDocument acadDoc)
        {
            return ExtractRulesFromEntities(acadDoc.ModelSpace.Cast<AcadEntity>());
        }

        public static List<CADExtractedDimensionRule> ExtractRulesFromEntities(IEnumerable<AcadEntity> entities)
        {
            var rules = new List<CADExtractedDimensionRule>();

            foreach (AcadEntity entity in entities)
            {
                if (!IsDimensionEntity(entity))
                {
                    continue;
                }

                try
                {
                    string objectName = GetStringProperty(entity, "ObjectName", "UnknownDimension");
                    string ruleType = NormalizeRuleType(objectName);
                    double measurement = GetDoubleProperty(entity, "Measurement", 0);
                    string textOverride = GetStringProperty(entity, "TextOverride", string.Empty);
                    string styleName = GetStringProperty(entity, "StyleName", "Standard");

                    var textPosition = GetPointProperty(entity, "TextPosition");
                    var referenceGeometry = BuildReferenceGeometry(entity);

                    string displayValue = !string.IsNullOrWhiteSpace(textOverride) ? textOverride : measurement.ToString("0.###");
                    string dimensionType = objectName;
                    string ruleName = $"{ruleType}_{rules.Count + 1}";
                    string rulePattern = JsonConvert.SerializeObject(new
                    {
                        objectName,
                        measurement = Math.Round(measurement, 3),
                        hasTextOverride = !string.IsNullOrWhiteSpace(textOverride)
                    });
                    string referenceNodes = JsonConvert.SerializeObject(referenceGeometry);
                    string notes = textPosition == null
                        ? "auto-extracted"
                        : $"text@({textPosition[0]:0.###},{textPosition[1]:0.###})";

                    rules.Add(new CADExtractedDimensionRule(
                        ruleName,
                        ruleType,
                        displayValue,
                        dimensionType,
                        styleName,
                        rulePattern,
                        referenceNodes,
                        notes));
                }
                catch
                {
                    // 忽略单条异常DIM，确保整体提取可继续。
                }
            }

            return rules;
        }

        public static int SaveExtractedRules(
            CADDimensionDatabase database,
            int graphId,
            List<CADExtractedDimensionRule> extractedRules,
            double defaultConfidence = 0.95)
        {
            int count = 0;
            foreach (var rule in extractedRules)
            {
                database.AddDimensionRule(
                    graphId: graphId,
                    ruleName: rule.RuleName,
                    ruleType: rule.RuleType,
                    dimensionValue: rule.DimensionValue,
                    dimensionType: rule.DimensionType,
                    annotationStyle: rule.AnnotationStyle,
                    confidence: defaultConfidence,
                    notes: rule.Notes,
                    rulePattern: rule.RulePattern,
                    referenceNodes: rule.ReferenceNodes);
                count++;
            }

            return count;
        }

        private static bool IsDimensionEntity(AcadEntity entity)
        {
            string objectName = GetStringProperty(entity, "ObjectName", string.Empty);
            return objectName.IndexOf("Dimension", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string NormalizeRuleType(string objectName)
        {
            if (objectName.IndexOf("Rotated", StringComparison.OrdinalIgnoreCase) >= 0
                || objectName.IndexOf("Aligned", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "linear";
            }
            if (objectName.IndexOf("Angular", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "angular";
            }
            if (objectName.IndexOf("Radial", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "radial";
            }
            if (objectName.IndexOf("Diametric", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "diameter";
            }
            return "dimension";
        }

        private static object BuildReferenceGeometry(AcadEntity entity)
        {
            var payload = new Dictionary<string, object>();
            TrySetPoint(payload, "xLine1", GetPointProperty(entity, "XLine1Point"));
            TrySetPoint(payload, "xLine2", GetPointProperty(entity, "XLine2Point"));
            TrySetPoint(payload, "center", GetPointProperty(entity, "Center"));
            TrySetPoint(payload, "dimLine", GetPointProperty(entity, "DimLinePoint"));
            return payload;
        }

        private static void TrySetPoint(Dictionary<string, object> payload, string key, double[]? point)
        {
            if (point == null || point.Length < 2)
            {
                return;
            }

            payload[key] = new[] { Math.Round(point[0], 3), Math.Round(point[1], 3), Math.Round(point.Length > 2 ? point[2] : 0, 3) };
        }

        private static string GetStringProperty(object obj, string prop, string fallback)
        {
            try
            {
                var value = obj.GetType().InvokeMember(prop, System.Reflection.BindingFlags.GetProperty, null, obj, null);
                return value?.ToString() ?? fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private static double GetDoubleProperty(object obj, string prop, double fallback)
        {
            try
            {
                var value = obj.GetType().InvokeMember(prop, System.Reflection.BindingFlags.GetProperty, null, obj, null);
                if (value == null) return fallback;
                return Convert.ToDouble(value);
            }
            catch
            {
                return fallback;
            }
        }

        private static double[]? GetPointProperty(object obj, string prop)
        {
            try
            {
                var value = obj.GetType().InvokeMember(prop, System.Reflection.BindingFlags.GetProperty, null, obj, null);
                return value as double[];
            }
            catch
            {
                return null;
            }
        }
    }

    public class CADExtractedDimensionRule
    {
        public string RuleName { get; }
        public string RuleType { get; }
        public string DimensionValue { get; }
        public string DimensionType { get; }
        public string AnnotationStyle { get; }
        public string RulePattern { get; }
        public string ReferenceNodes { get; }
        public string Notes { get; }

        public CADExtractedDimensionRule(
            string ruleName,
            string ruleType,
            string dimensionValue,
            string dimensionType,
            string annotationStyle,
            string rulePattern,
            string referenceNodes,
            string notes)
        {
            RuleName = ruleName;
            RuleType = ruleType;
            DimensionValue = dimensionValue;
            DimensionType = dimensionType;
            AnnotationStyle = annotationStyle;
            RulePattern = rulePattern;
            ReferenceNodes = referenceNodes;
            Notes = notes;
        }
    }
}
