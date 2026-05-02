namespace tools;

using System;
using System.Text;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using System.Diagnostics;

/// <summary>
/// 钣金特征统一按板厚写入 Type4 折弯扣除（表 type4_deduction_table.json，关闭折弯表并 SetOverrideDefaultParameter2）。
/// 折弯：仅当静默 K 检查报错才处理——使用钣金默认且为约 90°、R&lt;6 时按 <see cref="checkk_factor.Process_CustomBendAllowance"/> 等价分支改钣金特征；
/// 使用默认但非 90°（或 R≥6）：全局钣金默认无法与 90°Type4 等混用，改为对该子折弯（OneBend）取消默认并按 kcheck 写回。
/// 未使用默认时按是否约 90° 分支：非 90°/大半径走 kcheck，约 90° 自定义走 Type4/Type3/Type2 与 checkk_factor 一致。
/// </summary>
public static class fix_bends_kcheck
{
    private const double BendRadiusLimitMm = 6.0;
    private const double Angle90TolDeg = 0.5;

    /// <summary>
    /// 子折弯勾选「使用钣金默认」时，若错误来自非 90° 的 K 规则或 R≥6 的大半径规则，只改钣金特征会把整件默认改成 Type2，
    /// 与仍需 Type4 扣除的 90° 折弯冲突；此时应对该折弯单独取消默认并按 kcheck 写回。
    /// </summary>
    private static bool PreferPerBendKcheckOverSheetMetalDefault(
        bool useDefaultBendAllowance,
        double angleDeg,
        double bendRadiusMm) =>
        useDefaultBendAllowance
        && (bendRadiusMm >= BendRadiusLimitMm || Math.Abs(angleDeg - 90.0) > Angle90TolDeg);
    /// <summary>防止异常特征链或失效 COM 引用导致 GetNextSubFeature 死循环。</summary>
    private const int MaxSubFeatureWalkSteps = 100_000;

    private abstract class KcheckFixJob
    {
    }

    private sealed class SmDefaultInsteadJob : KcheckFixJob
    {
        /// <summary>勿存 <see cref="CustomBendAllowance"/> COM 引用：队列中其它任务 ModifyDefinition 后 RCW 可能失效导致 NRE。</summary>
        public SmDefaultInsteadJob(
            string topFeatureName,
            string bendNameLog,
            double angleDeg,
            double bendRadiusMm,
            int cbaType,
            double cbaKFactor,
            double cbaBendDeduction,
            double cbaBendAllowance)
        {
            TopFeatureName = topFeatureName;
            BendNameLog = bendNameLog;
            AngleDeg = angleDeg;
            BendRadiusMm = bendRadiusMm;
            CbaType = cbaType;
            CbaKFactor = cbaKFactor;
            CbaBendDeduction = cbaBendDeduction;
            CbaBendAllowance = cbaBendAllowance;
        }

        public string TopFeatureName { get; }
        public string BendNameLog { get; }
        public double AngleDeg { get; }
        public double BendRadiusMm { get; }
        public int CbaType { get; }
        public double CbaKFactor { get; }
        public double CbaBendDeduction { get; }
        public double CbaBendAllowance { get; }
    }

    private sealed class OneBendJob : KcheckFixJob
    {
        public OneBendJob(string topFeatureName, string bendNameLog)
        {
            TopFeatureName = topFeatureName;
            BendNameLog = bendNameLog;
        }

        public string TopFeatureName { get; }
        public string BendNameLog { get; }
    }

    /// <summary>一层子链上的折弯：类型名为 OneBend，或定义为 <see cref="OneBendFeatureData"/>（基体/斜接子链上 API 名常为 SketchBend）。</summary>
    private static bool IsWorkshopLinearBendSub(Feature sub) =>
        string.Equals(sub.GetTypeName(), "OneBend", StringComparison.Ordinal)
        || string.Equals(sub.GetTypeName2(), "OneBend", StringComparison.Ordinal)
        || FeatureDefinesOneBendData(sub);

    private static bool IsWorkshopLinearBendParentType(string? parentTypeName2) =>
        parentTypeName2 is "SMBaseFlange" or "SMMiteredFlange" or "SolidToSheetMetal" or "LPattern" or "EdgeFlange";

    /// <summary>仅一层 GetFirstSubFeature 链（与车间特征读取相同），不递归子特征的子特征。</summary>
    private static void ForEachLinearSubFeature(Feature top, Action<Feature> onSub)
    {
        int steps = 0;
        Feature? sub = (Feature?)top.GetFirstSubFeature();
        while (sub != null && steps < MaxSubFeatureWalkSteps)
        {
            steps++;
            onSub(sub);
            sub = (Feature?)sub.GetNextSubFeature();
        }
    }

    /// <summary>单元素数组，供 lambda 内累加探测次数（不能用 ref 参数）。</summary>
    private static void TryEnqueueKcheckJobFromSubBend(
        string modelName,
        double thicknessMm,
        FixStats stats,
        List<KcheckFixJob> jobs,
        int[] bendsProbedBox,
        Feature topOwner,
        Feature bendFeat)
    {
        try
        {
            if (bendFeat.GetDefinition() is not OneBendFeatureData obDef)
            {
                return;
            }

            double angleDegProbe = Math.Round(obDef.BendAngle * 180.0 / Math.PI, 2);
            double bendRadiusMmProbe = obDef.BendRadius * 1000.0;
            bool useDefaultProbe = obDef.UseDefaultBendAllowance;
            CustomBendAllowance cbaProbe = obDef.GetCustomBendAllowance();
            if (cbaProbe == null)
            {
                stats.Errors++;
                Console.WriteLine(
                    $"fix_bends_kcheck: 错误 折弯子特征 GetCustomBendAllowance 为空，跳过 ({bendFeat.Name ?? "?"} [{bendFeat.GetTypeName2()}])");
                return;
            }

            bendsProbedBox[0]++;
            bool hasKcheckErr = checkk_factor.Process_CustomBendAllowance(
                modelName,
                cbaProbe,
                bendRadiusMmProbe,
                bendFeat.Name ?? string.Empty,
                thicknessMm,
                angleDegProbe,
                quiet: true);

            if (!hasKcheckErr)
            {
                stats.BendsSkipped++;
                return;
            }

            if (useDefaultProbe
                && !PreferPerBendKcheckOverSheetMetalDefault(useDefaultProbe, angleDegProbe, bendRadiusMmProbe))
            {
                jobs.Add(new SmDefaultInsteadJob(
                    topOwner.Name ?? string.Empty,
                    bendFeat.Name ?? string.Empty,
                    angleDegProbe,
                    bendRadiusMmProbe,
                    cbaProbe.Type,
                    cbaProbe.KFactor,
                    cbaProbe.BendDeduction,
                    cbaProbe.BendAllowance));
            }
            else
            {
                jobs.Add(new OneBendJob(topOwner.Name ?? string.Empty, bendFeat.Name ?? string.Empty));
            }
        }
        catch (Exception ex)
        {
            stats.Errors++;
            Console.WriteLine($"fix_bends_kcheck: 折弯 '{bendFeat?.Name ?? "?"}' 失败: {ex.Message}");
        }
    }

    /// <summary>与车间 <c>特征读取()</c> 相同：顶层 FirstFeature→GetNextFeature；折弯只在若干钣金父特征的一层子链上枚举。</summary>
    private static void CollectKcheckJobsWorkshopStyleWalk(
        ModelDoc2 swModel,
        string modelName,
        double thicknessMm,
        FixStats stats,
        List<KcheckFixJob> jobs,
        int[] bendsProbedBox)
    {
        Feature? f = (Feature?)swModel.FirstFeature();
        int topSteps = 0;
        while (f != null && topSteps < MaxSubFeatureWalkSteps)
        {
            topSteps++;
            string t2 = f.GetTypeName2() ?? string.Empty;
            if (IsWorkshopLinearBendParentType(t2))
            {
                ForEachLinearSubFeature(f, sub =>
                {
                    if (IsWorkshopLinearBendSub(sub))
                    {
                        TryEnqueueKcheckJobFromSubBend(modelName, thicknessMm, stats, jobs, bendsProbedBox, f, sub);
                    }
                });
            }

            f = (Feature?)f.GetNextFeature();
        }
    }

    /// <summary>执行阶段：与收集阶段相同，先在车间所列父类型的一层子链上按名称找子折弯，否则再递归子树。</summary>
    private static Feature? FindBendSubForOneBendJob(ModelDoc2 swModel, string topFeatureName, string bendNameLog)
    {
        Feature? top = FindTopFeatureInPartByName(swModel, topFeatureName);
        if (top == null)
        {
            return null;
        }

        string t2 = top.GetTypeName2() ?? string.Empty;
        if (IsWorkshopLinearBendParentType(t2))
        {
            Feature? found = null;
            ForEachLinearSubFeature(top, sub =>
            {
                if (found != null)
                {
                    return;
                }

                if (!string.Equals(sub.Name, bendNameLog, StringComparison.Ordinal))
                {
                    return;
                }

                if (IsWorkshopLinearBendSub(sub))
                {
                    found = sub;
                }
            });

            return found;
        }

        return FindOneBendInTopSubtree(top, bendNameLog);
    }

    public sealed class FixStats
    {
        public int SheetMetalFeaturesUpdated { get; set; }
        public int BendsUpdated { get; set; }
        public int BendsSkipped { get; set; }
        public int Errors { get; set; }
    }

    /// <summary>修正当前零件：钣金默认仍扫顶层 SheetMetal；折弯 K 检查与车间 <c>特征读取()</c> 一致——顶层 <see cref="ModelDoc2.FirstFeature"/>/<see cref="Feature.GetNextFeature"/>，仅在 SMBaseFlange/SMMiteredFlange/EdgeFlange/SolidToSheetMetal/LPattern 的一层 <see cref="Feature.GetFirstSubFeature"/> 链上枚举 OneBend（含 <see cref="OneBendFeatureData"/> 子特征）。</summary>
    public static FixStats Run(SldWorks? swApp, ModelDoc2? swModel)
    {
        var stats = new FixStats();
        if (swModel == null || swModel.GetType() != (int)swDocumentTypes_e.swDocPART)
        {
            Console.WriteLine("fix_bends_kcheck: 跳过非零件文档");
            return stats;
        }

        string modelName = swModel.GetTitle();

        var featNamesLine = new StringBuilder();
        Feature? firstSheetMetal = null;
        Feature? f = (Feature?)swModel.FirstFeature();
        int topSteps = 0;
        while (f != null && topSteps < MaxSubFeatureWalkSteps)
        {
            topSteps++;
            if (featNamesLine.Length > 0)
            {
                featNamesLine.Append(" | ");
            }

            featNamesLine.Append(f.Name ?? "?");
            featNamesLine.Append('[');
            featNamesLine.Append(f.GetTypeName2());
            featNamesLine.Append(']');

            if (f.GetTypeName2() == "SheetMetal" && firstSheetMetal == null)
            {
                firstSheetMetal = f;
            }

            f = (Feature?)f.GetNextFeature();
        }

        if (firstSheetMetal == null)
        {
            Console.WriteLine($"fix_bends_kcheck: 零件 '{modelName}' 无顶层 SheetMetal 特征，跳过");
            return stats;
        }

        Console.WriteLine($"fix_bends_kcheck: 零件顶层特征: {featNamesLine}");

        string smKey = firstSheetMetal.Name ?? firstSheetMetal.GetTypeName2();
        if (!TryGetSheetMetalThicknessMm(firstSheetMetal, out double thicknessMm))
        {
            stats.Errors++;
            Console.WriteLine($"fix_bends_kcheck: 无法读取钣金厚度（{modelName}+{smKey}），中止。");
            return stats;
        }

        var sheetMetalSeen = new HashSet<string>(StringComparer.Ordinal);
        f = (Feature?)swModel.FirstFeature();
        topSteps = 0;
        while (f != null && topSteps < MaxSubFeatureWalkSteps)
        {
            topSteps++;
            if (f.GetTypeName2() == "SheetMetal")
            {
                string key = f.Name ?? f.GetTypeName2();
                if (sheetMetalSeen.Add(key))
                {
                    if (!TryGetSheetMetalThicknessMm(f, out double th))
                    {
                        stats.Errors++;
                        Console.WriteLine($"fix_bends_kcheck: 跳过钣金特征（无法读厚度）: {modelName}+{key}");
                        continue;
                    }

                    if (TryUpdateSheetMetalDefault(swModel, f, th, out double dedMm, out double tabThMm))
                    {
                        stats.SheetMetalFeaturesUpdated++;
                        Console.WriteLine(
                            $"fix_bends_kcheck: 已更新钣金默认 Type4 折弯扣除≈{dedMm:F2}mm（表列板厚≈{tabThMm:F2}mm，零件≈{Math.Round(th, 2)}mm） ({modelName}+{key})");
                    }
                }
            }

            f = (Feature?)f.GetNextFeature();
        }

        Feature? smAfterDefault = FindSheetMetalInPart(swModel, smKey);
        if (smAfterDefault != null && TryGetSheetMetalThicknessMm(smAfterDefault, out double thAfterSm))
        {
            thicknessMm = thAfterSm;
        }

        // 先只读收集任务再逐项修改：与车间特征读取() 相同——顶层 GetNextFeature + 基体/斜接/边线等的一层子链；不扫平板型式。
        var jobs = new List<KcheckFixJob>();
        int[] bendsProbedBox = new int[1];
        CollectKcheckJobsWorkshopStyleWalk(swModel, modelName, thicknessMm, stats, jobs, bendsProbedBox);
        int bendsProbed = bendsProbedBox[0];

        Console.WriteLine(
            $"fix_bends_kcheck: 按车间特征读取路径共探测 {bendsProbed} 处折弯（一层子链：OneBend / OneBendFeatureData），静默 K 通过跳过 {stats.BendsSkipped}，入队待写回 {jobs.Count}。");

        foreach (KcheckFixJob job in jobs)
        {
            try
            {
                Feature? smLive = FindSheetMetalInPart(swModel, smKey);
                if (smLive == null)
                {
                    stats.Errors++;
                    Console.WriteLine($"fix_bends_kcheck: 执行阶段找不到钣金特征 ({smKey})，跳过一条任务。");
                    continue;
                }

                if (!TryGetSheetMetalThicknessMm(smLive, out double thicknessLiveMm))
                {
                    stats.Errors++;
                    Console.WriteLine(
                        $"fix_bends_kcheck: 错误 执行阶段无法读钣金厚度，跳过任务 ({smLive.Name ?? smKey})。");
                    continue;
                }

                if (job is SmDefaultInsteadJob smj)
                {
                    Console.WriteLine(
                        $"fix_bends_kcheck: {smj.BendNameLog} 使用钣金默认且 K 检查不通过 → 仅按 kcheck 修正钣金「{smLive.Name}」（不改子折弯）");
                    if (TryFixSheetMetalInsteadOfOneBend(
                            swModel,
                            smLive,
                            thicknessLiveMm,
                            smj.AngleDeg,
                            smj.BendRadiusMm,
                            smj.CbaType,
                            smj.CbaKFactor,
                            smj.CbaBendDeduction,
                            smj.CbaBendAllowance))
                    {
                        stats.SheetMetalFeaturesUpdated++;
                        Console.WriteLine(
                            $"fix_bends_kcheck: 已按 kcheck 修正钣金默认 ({modelName}+{smLive.Name})，触发检查的子折弯 {smj.TopFeatureName}+{smj.BendNameLog}");
                    }
                    else
                    {
                        stats.Errors++;
                        Console.WriteLine(
                            $"fix_bends_kcheck: 错误 按 kcheck 修正钣金默认失败 ({modelName}+{smLive.Name})，触发子折弯 {smj.TopFeatureName}+{smj.BendNameLog}。");
                    }
                }
                else if (job is OneBendJob ob)
                {
                    Feature? bendLive = FindBendSubForOneBendJob(swModel, ob.TopFeatureName, ob.BendNameLog);
                    Feature? topLive = FindTopFeatureInPartByName(swModel, ob.TopFeatureName);
                    if (bendLive == null || topLive == null)
                    {
                        stats.Errors++;
                        Console.WriteLine(
                            $"fix_bends_kcheck: 执行阶段找不到折弯 ({ob.TopFeatureName} / {ob.BendNameLog})，可能已改名或重建。");
                    }
                    else if (TryUpdateOneBend(
                                 swModel,
                                 bendLive,
                                 topLive,
                                 thicknessLiveMm,
                                 out bool changedBend))
                    {
                        if (changedBend)
                        {
                            stats.BendsUpdated++;
                            Console.WriteLine(
                                $"fix_bends_kcheck: 已更新折弯 {modelName}+{ob.TopFeatureName}+{ob.BendNameLog}");
                        }
                        else
                        {
                            stats.BendsSkipped++;
                        }
                    }
                    else
                    {
                        stats.Errors++;
                        Console.WriteLine(
                            $"fix_bends_kcheck: 错误 折弯 kcheck 写回失败 ({modelName}+{ob.TopFeatureName}+{ob.BendNameLog})。");
                    }
                }
            }
            catch (Exception ex)
            {
                stats.Errors++;
                Console.WriteLine($"fix_bends_kcheck: 执行任务时异常（已跳过该条）: {ex.Message}");
            }
        }

        try
        {
            swModel.EditRebuild3();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"fix_bends_kcheck: 错误 EditRebuild3: {ex.Message}");
        }

        Console.WriteLine(
            $"fix_bends_kcheck 完成: 钣金默认更新 {stats.SheetMetalFeaturesUpdated}, 折弯更新 {stats.BendsUpdated}, 跳过 {stats.BendsSkipped}, 错误 {stats.Errors}");
        return stats;
    }

    /// <summary>不依赖类型名字符串：子折弯定义可为 <see cref="OneBendFeatureData"/>（含 API 类型名为 SketchBend 的情形）。</summary>
    private static bool FeatureDefinesOneBendData(Feature feat)
    {
        if (feat == null)
        {
            return false;
        }

        try
        {
            return feat.GetDefinition() is OneBendFeatureData;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 沿 FeatureManager 顺序遍历零件顶层链（FirstFeature / GetNextFeature），按名称匹配 SheetMetal；未命中名称时返回第一个 SheetMetal。
    /// </summary>
    private static Feature? FindSheetMetalInPart(ModelDoc2 swModel, string smKey)
    {
        Feature? feat = (Feature?)swModel.FirstFeature();
        Feature? firstSm = null;
        int steps = 0;
        while (feat != null && steps < MaxSubFeatureWalkSteps)
        {
            steps++;
            if (feat.GetTypeName2() == "SheetMetal")
            {
                firstSm ??= feat;
                string key = feat.Name ?? feat.GetTypeName2();
                if (string.Equals(key, smKey, StringComparison.Ordinal))
                {
                    return feat;
                }
            }

            feat = (Feature?)feat.GetNextFeature();
        }

        return firstSm;
    }

    private static bool TryGetSheetMetalThicknessMm(Feature smFeat, out double thicknessMm)
    {
        thicknessMm = 0;
        if (smFeat == null)
        {
            return false;
        }

        var smData = smFeat.GetDefinition() as SheetMetalFeatureData;
        if (smData == null)
        {
            Console.WriteLine(
                $"fix_bends_kcheck: SheetMetal GetDefinition 为空或非 SheetMetalFeatureData ({smFeat.Name ?? "?"})");
            return false;
        }

        thicknessMm = smData.Thickness * 1000.0;
        return true;
    }

    /// <summary>与车间用「特征读取」一致：关闭折弯表并强制使用当前自定义折弯扣除/K 设置。</summary>
    private static void ApplySheetMetalPersistFlags(SheetMetalFeatureData data)
    {
        try
        {
            data.SetUseGaugeTable(false, string.Empty);
        }
        catch
        {
        }

        try
        {
            data.SetOverrideDefaultParameter2(1, true);
        }
        catch
        {
        }
    }

    /// <summary>在零件顶层链（FirstFeature / GetNextFeature）中按名称查找特征（与原先 body 顶层列表一致）。</summary>
    private static Feature? FindTopFeatureInPartByName(ModelDoc2 swModel, string topFeatureName)
    {
        Feature? feat = (Feature?)swModel.FirstFeature();
        int steps = 0;
        while (feat != null && steps < MaxSubFeatureWalkSteps)
        {
            steps++;
            if (string.Equals(feat.Name, topFeatureName, StringComparison.Ordinal))
            {
                return feat;
            }

            feat = (Feature?)feat.GetNextFeature();
        }

        return null;
    }

    /// <summary>在顶层特征的子树中按名称递归查找折弯特征（定义须为 <see cref="OneBendFeatureData"/>）。</summary>
    private static Feature? FindOneBendInTopSubtree(Feature top, string bendName)
    {
        int steps = 0;
        return FindOneBendInSubtreeRecursive(top, bendName, ref steps);
    }

    private static Feature? FindOneBendInSubtreeRecursive(Feature parent, string bendName, ref int steps)
    {
        Feature? ch = (Feature?)parent.GetFirstSubFeature();
        while (ch != null && steps < MaxSubFeatureWalkSteps)
        {
            steps++;
            if (FeatureDefinesOneBendData(ch)
                && string.Equals(ch.Name, bendName, StringComparison.Ordinal))
            {
                return ch;
            }

            Feature? nested = null;
            if (!string.Equals(ch.GetTypeName2(), "FlatPattern", StringComparison.Ordinal))
            {
                nested = FindOneBendInSubtreeRecursive(ch, bendName, ref steps);
            }

            if (nested != null)
            {
                return nested;
            }

            ch = (Feature?)ch.GetNextSubFeature();
        }

        return null;
    }

    private static bool TryUpdateSheetMetalDefault(
        ModelDoc2 swModel,
        Feature smFeat,
        double thicknessMm,
        out double deductionMmLogged,
        out double tableThicknessMmLogged)
    {
        deductionMmLogged = 0;
        tableThicknessMmLogged = 0;
        SheetMetalFeatureData? data = null;
        try
        {
            if (!Type4BendTable.TryGetNearest(thicknessMm, out double tableThicknessMm, out double deductionMm))
            {
                Console.WriteLine(
                    $"fix_bends_kcheck: 无法按板厚设置默认折弯扣除（缺少 type4_deduction_table.json 或表为空），跳过钣金特征 ({smFeat.Name})");
                return false;
            }

            deductionMmLogged = deductionMm;
            tableThicknessMmLogged = tableThicknessMm;

            data = (SheetMetalFeatureData)smFeat.GetDefinition();
            if (data == null)
            {
                return false;
            }

            if (!data.AccessSelections(swModel, null))
            {
                Console.WriteLine($"fix_bends_kcheck: SheetMetal AccessSelections 失败 ({smFeat.Name})");
                return false;
            }

            ApplySheetMetalPersistFlags(data);
            CustomBendAllowance cba = data.GetCustomBendAllowance();
            if (cba == null)
            {
                Console.WriteLine($"fix_bends_kcheck: 错误 SheetMetal GetCustomBendAllowance 为空 ({smFeat.Name})");
                return false;
            }

            cba.Type = 4;
            cba.BendDeduction = deductionMm / 1000.0;
            data.SetCustomBendAllowance(cba);
            bool ok = smFeat.ModifyDefinition(data, swModel, null);
            if (!ok)
            {
                Console.WriteLine($"fix_bends_kcheck: 错误 SheetMetal ModifyDefinition 失败 ({smFeat.Name})");
            }

            return ok;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"fix_bends_kcheck: 更新钣金特征失败 ({smFeat.Name}): {ex.Message}");
            return false;
        }
        finally
        {
            try
            {
                data?.ReleaseSelectionAccess();
            }
            catch
            {
                // 部分版本接口差异，忽略
            }
        }
    }

    /// <summary>子折弯勾选了「使用钣金默认」且仍报 K 检查错误时，只改钣金特征的默认折弯（与 Process_CustomBendAllowance 分支一致）。</summary>
    /// <param name="cbaType">入队时从子折弯 <see cref="CustomBendAllowance"/> 拷贝的 Type，勿在执行阶段再读 COM 引用。</param>
    private static bool TryFixSheetMetalInsteadOfOneBend(
        ModelDoc2 swModel,
        Feature smFeat,
        double thicknessMm,
        double angleDeg,
        double bendRadiusMm,
        int cbaType,
        double cbaKFactor,
        double cbaBendDeduction,
        double cbaBendAllowance)
    {
        const double tableTolMm = 0.2;
        var debuctFactorMm = Math.Round(cbaBendDeduction * 1000.0, 2);
        var allowFactorMm = Math.Round(cbaBendAllowance * 1000.0, 2);

        if (bendRadiusMm >= BendRadiusLimitMm)
        {
            return TryUpdateSheetMetalType2K(swModel, smFeat, 0.5);
        }

        if (Math.Abs(angleDeg - 90.0) > Angle90TolDeg && Math.Abs(cbaKFactor - 0.35) > 0.05)
        {
            return TryUpdateSheetMetalType2K(swModel, smFeat, 0.35);
        }

        if (cbaType == 4)
        {
            return TryUpdateSheetMetalDefault(swModel, smFeat, thicknessMm, out _, out _);
        }

        if (cbaType == 3)
        {
            if (!Type4BendTable.TryGetNearest(thicknessMm, out _, out double deduction))
            {
                return TryUpdateSheetMetalDefault(swModel, smFeat, thicknessMm, out _, out _);
            }

            double expectAllowMm = 2.0 * thicknessMm - deduction + 2.0 * bendRadiusMm;
            if (Math.Abs(allowFactorMm - expectAllowMm) > tableTolMm)
            {
                return TryUpdateSheetMetalType3Allowance(swModel, smFeat, thicknessMm, bendRadiusMm, deduction);
            }

            return TryUpdateSheetMetalDefault(swModel, smFeat, thicknessMm, out _, out _);
        }

        if (bendRadiusMm < BendRadiusLimitMm && cbaType == 2 && Math.Abs(cbaKFactor - 0.35) > 0.05)
        {
            return TryUpdateSheetMetalType2K(swModel, smFeat, 0.35);
        }

        return TryUpdateSheetMetalDefault(swModel, smFeat, thicknessMm, out _, out _);
    }

    private static bool TryUpdateSheetMetalType2K(ModelDoc2 swModel, Feature smFeat, double kFactor)
    {
        SheetMetalFeatureData? data = null;
        try
        {
            data = (SheetMetalFeatureData)smFeat.GetDefinition();
            if (data == null)
            {
                Console.WriteLine($"fix_bends_kcheck: 错误 SheetMetal GetDefinition 为空 ({smFeat.Name})，Type2 K");
                return false;
            }

            if (!data.AccessSelections(swModel, null))
            {
                Console.WriteLine($"fix_bends_kcheck: SheetMetal AccessSelections 失败 ({smFeat.Name})，Type2 K");
                return false;
            }

            ApplySheetMetalPersistFlags(data);
            CustomBendAllowance cba = data.GetCustomBendAllowance();
            if (cba == null)
            {
                Console.WriteLine($"fix_bends_kcheck: 错误 SheetMetal GetCustomBendAllowance 为空 ({smFeat.Name})，Type2 K");
                return false;
            }

            cba.Type = 2;
            cba.KFactor = kFactor;
            data.SetCustomBendAllowance(cba);
            bool okType2 = smFeat.ModifyDefinition(data, swModel, null);
            if (!okType2)
            {
                Console.WriteLine($"fix_bends_kcheck: 错误 SheetMetal ModifyDefinition 失败 ({smFeat.Name})，Type2 K={kFactor}");
            }

            return okType2;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"fix_bends_kcheck: 钣金 Type2 K 更新失败 ({smFeat.Name}): {ex.Message}");
            return false;
        }
        finally
        {
            try
            {
                data?.ReleaseSelectionAccess();
            }
            catch
            {
            }
        }
    }

    /// <param name="deductionMm">表中扣除值（mm）。</param>
    private static bool TryUpdateSheetMetalType3Allowance(
        ModelDoc2 swModel,
        Feature smFeat,
        double thicknessMm,
        double bendRadiusMm,
        double deductionMm)
    {
        SheetMetalFeatureData? data = null;
        try
        {
            data = (SheetMetalFeatureData)smFeat.GetDefinition();
            if (data == null)
            {
                Console.WriteLine($"fix_bends_kcheck: 错误 SheetMetal GetDefinition 为空 ({smFeat.Name})，Type3");
                return false;
            }

            if (!data.AccessSelections(swModel, null))
            {
                Console.WriteLine($"fix_bends_kcheck: SheetMetal AccessSelections 失败 ({smFeat.Name})，Type3");
                return false;
            }

            ApplySheetMetalPersistFlags(data);
            double tM = thicknessMm / 1000.0;
            double rM = bendRadiusMm / 1000.0;
            double dedM = deductionMm / 1000.0;
            CustomBendAllowance cba = data.GetCustomBendAllowance();
            if (cba == null)
            {
                Console.WriteLine($"fix_bends_kcheck: 错误 SheetMetal GetCustomBendAllowance 为空 ({smFeat.Name})，Type3");
                return false;
            }

            cba.Type = 3;
            cba.BendAllowance = 2.0 * tM - dedM + 2.0 * rM;
            data.SetCustomBendAllowance(cba);
            bool okType3 = smFeat.ModifyDefinition(data, swModel, null);
            if (!okType3)
            {
                Console.WriteLine($"fix_bends_kcheck: 错误 SheetMetal ModifyDefinition 失败 ({smFeat.Name})，Type3");
            }

            return okType3;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"fix_bends_kcheck: 钣金 Type3 更新失败 ({smFeat.Name}): {ex.Message}");
            return false;
        }
        finally
        {
            try
            {
                data?.ReleaseSelectionAccess();
            }
            catch
            {
            }
        }
    }

    /// <summary>按约 90°/非 90° 对已有自定义折弯系数结构体套用 kcheck 修正规则（与 <see cref="checkk_factor.Process_CustomBendAllowance"/> 一致）。</summary>
    private static void ApplyKcheckBendRulesToCba(
        CustomBendAllowance cba,
        double angleDeg,
        double bendRadiusMm,
        double thicknessMm)
    {
        if (Math.Abs(angleDeg - 90.0) > Angle90TolDeg)
        {
            ApplyNon90OrLargeRadius(cba, bendRadiusMm);
        }
        else
        {
            ApplyKcheckForCustom90(cba, thicknessMm, bendRadiusMm);
        }
    }

    /// <param name="ownerFeat">子折弯所属的顶层特征（如边线折弯 EdgeFlange），用于选择上下文；可为 null。</param>
    /// <remarks>调用方已保证 K 检查报错；子折弯可仍为「使用钣金默认」（由 <see cref="PreferPerBendKcheckOverSheetMetalDefault"/> 转入本路径），此处按约 90°/非 90° 与 <see cref="checkk_factor.Process_CustomBendAllowance"/> 对齐写回并取消默认。</remarks>
    private static bool TryUpdateOneBend(ModelDoc2 swModel, Feature bendFeat, Feature? ownerFeat, double thicknessMm, out bool changed)
    {
        changed = false;
        OneBendFeatureData? def = null;
        bool accessOk = false;
        try
        {
            def = (OneBendFeatureData)bendFeat.GetDefinition();
            if (def == null)
            {
                Console.WriteLine($"fix_bends_kcheck: 错误 OneBend GetDefinition 为空 ({bendFeat.Name})");
                return false;
            }

            double angleDeg = Math.Round(def.BendAngle * 180.0 / Math.PI, 2);
            double bendRadiusMm = def.BendRadius * 1000.0;
            bool useDefault = def.UseDefaultBendAllowance;

            CustomBendAllowance cba = def.GetCustomBendAllowance();
            if (cba == null)
            {
                Console.WriteLine($"fix_bends_kcheck: 错误 OneBend GetCustomBendAllowance 为空 ({bendFeat.Name})");
                return false;
            }

            var before = (cba.Type, cba.KFactor, cba.BendDeduction, cba.BendAllowance, useDefault);

            ApplyKcheckBendRulesToCba(cba, angleDeg, bendRadiusMm, thicknessMm);

            var desired = (cba.Type, cba.KFactor, cba.BendDeduction, cba.BendAllowance, false);
            changed = before != desired;
            if (!changed)
            {
                return true;
            }

            accessOk = TryOneBendAccessSelections(swModel, bendFeat, ownerFeat, def);
            if (!accessOk)
            {
                Console.WriteLine(
                    $"fix_bends_kcheck: OneBend AccessSelections 未通过，将直接 SetCustomBendAllowance/ModifyDefinition ({bendFeat.Name})");
                def = (OneBendFeatureData)bendFeat.GetDefinition();
                if (def == null)
                {
                    return false;
                }
            }

            def.UseDefaultBendAllowance = false;
            def.SetCustomBendAllowance(cba);
            bool ok = bendFeat.ModifyDefinition(def, swModel, null);
            if (!ok && accessOk)
            {
                try
                {
                    def.ReleaseSelectionAccess();
                }
                catch
                {
                }

                accessOk = false;
                try
                {
                    swModel.ClearSelection2(true);
                }
                catch
                {
                }

                def = (OneBendFeatureData)bendFeat.GetDefinition();
                if (def != null)
                {
                    def.UseDefaultBendAllowance = false;
                    def.SetCustomBendAllowance(cba);
                    ok = bendFeat.ModifyDefinition(def, swModel, null);
                }
            }

            if (!ok)
            {
                try
                {
                    swModel.ClearSelection2(true);
                }
                catch
                {
                }

                def = (OneBendFeatureData)bendFeat.GetDefinition();
                if (def != null)
                {
                    def.UseDefaultBendAllowance = false;
                    def.SetCustomBendAllowance(cba);
                    ok = bendFeat.ModifyDefinition(def, swModel, null);
                }
            }

            if (!ok)
            {
                Console.WriteLine($"fix_bends_kcheck: OneBend ModifyDefinition 失败 ({bendFeat.Name})");
            }

            return ok;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"fix_bends_kcheck: ModifyDefinition 失败 ({bendFeat.Name}): {ex.Message}");
            return false;
        }
        finally
        {
            if (accessOk)
            {
                try
                {
                    def?.ReleaseSelectionAccess();
                }
                catch
                {
                }
            }

            try
            {
                swModel.ClearSelection2(true);
            }
            catch
            {
            }
        }
    }

    /// <summary>边线折弯等父特征下的 OneBend 常需先清空选择并选中特征，AccessSelections 才能成功。</summary>
    private static bool TryOneBendAccessSelections(ModelDoc2 swModel, Feature bendFeat, Feature? ownerFeat, OneBendFeatureData def)
    {
        try
        {
            swModel.ClearSelection2(true);
            if (def.AccessSelections(swModel, null))
            {
                return true;
            }
        }
        catch
        {
        }

        try
        {
            swModel.ClearSelection2(true);
            bendFeat.Select2(false, 0);
            if (def.AccessSelections(swModel, null))
            {
                return true;
            }
        }
        catch
        {
        }

        if (ownerFeat != null)
        {
            try
            {
                swModel.ClearSelection2(true);
                ownerFeat.Select2(false, 0);
                bendFeat.Select2(true, 0);
                if (def.AccessSelections(swModel, null))
                {
                    return true;
                }
            }
            catch
            {
            }
        }

        return false;
    }

    /// <summary>与 check 顺序一致：R≥6 → 0.5K；否则非 90° → 0.35K。</summary>
    private static void ApplyNon90OrLargeRadius(CustomBendAllowance cba, double bendRadiusMm)
    {
        if (bendRadiusMm >= BendRadiusLimitMm)
        {
            cba.Type = 2;
            cba.KFactor = 0.5;
            return;
        }

        cba.Type = 2;
        cba.KFactor = 0.35;
    }

    /// <summary>约 90° 且已自定义：按原 Type 与板厚套用 kcheck。</summary>
    private static void ApplyKcheckForCustom90(CustomBendAllowance cba, double thicknessMm, double bendRadiusMm)
    {
        if (bendRadiusMm >= BendRadiusLimitMm)
        {
            cba.Type = 2;
            cba.KFactor = 0.5;
            return;
        }

        if (cba.Type == 4)
        {
            if (Type4BendTable.TryGetNearest(thicknessMm, out _, out double deductionMm))
            {
                cba.Type = 4;
                cba.BendDeduction = deductionMm / 1000.0;
            }

            return;
        }

        if (cba.Type == 3)
        {
            if (Type4BendTable.TryGetNearest(thicknessMm, out _, out double deductionMm))
            {
                cba.Type = 3;
                double tM = thicknessMm / 1000.0;
                double rM = bendRadiusMm / 1000.0;
                double dedM = deductionMm / 1000.0;
                cba.BendAllowance = 2.0 * tM - dedM + 2.0 * rM;
            }

            return;
        }

        cba.Type = 2;
        cba.KFactor = 0.35;
    }
}
