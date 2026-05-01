using System;
using System.Collections.Generic;
using System.Linq;

namespace cad_tools
{
    public sealed class CADWLRecommendationSettings
    {
        public int TopK { get; set; } = 10;
        public double MinSimilarity { get; set; } = 0.6;
        public int MinDistinctGraphs { get; set; } = 3;
        public double AutoApplySimilarity { get; set; } = 0.85;
    }

    public static class CADWLRecommendationPolicy
    {
        public static List<(string RuleName, string DimensionValue, string DimensionType, double Similarity, double Confidence, string AnnotationStyle, string RuleType, string SourceGraph)>
            Recommend(
                CADDimensionDatabase database,
                List<Dictionary<string, int>> wlFrequencies,
                CADWLRecommendationSettings settings)
        {
            return database.FindRecommendedRules(
                wlFrequencies: wlFrequencies,
                topK: settings.TopK,
                minSimilarity: settings.MinSimilarity,
                minDistinctGraphs: settings.MinDistinctGraphs);
        }

        public static bool CanAutoApply(
            List<(string RuleName, string DimensionValue, string DimensionType, double Similarity, double Confidence, string AnnotationStyle, string RuleType, string SourceGraph)> rules,
            CADWLRecommendationSettings settings)
        {
            if (rules.Count == 0)
            {
                return false;
            }

            double bestSimilarity = rules.Max(r => r.Similarity);
            return bestSimilarity >= settings.AutoApplySimilarity;
        }
    }

    public static class CADWLEvaluator
    {
        /// <summary>
        /// 基于数据库历史规则做简单离线覆盖评估。
        /// </summary>
        public static (double Top1, double Top3) EvaluateHistoricalHitRate(
            List<(string RuleName, string DimensionValue, string DimensionType, double Similarity, double Confidence, string AnnotationStyle, string RuleType, string SourceGraph)> recommendations)
        {
            if (recommendations.Count == 0)
            {
                return (0, 0);
            }

            // 这里用相似度排序后的前K覆盖作为轻量评估代理指标。
            var ordered = recommendations.OrderByDescending(r => r.Similarity).ToList();
            double top1 = ordered[0].Similarity;
            double top3 = ordered.Take(3).Average(r => r.Similarity);
            return (top1, top3);
        }
    }
}
