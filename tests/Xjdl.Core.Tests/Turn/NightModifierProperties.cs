using CsCheck;
using Xjdl.Core.Modifiers;
using Xjdl.Core.State;
using Xjdl.Core.Turn;

namespace Xjdl.Core.Tests.Turn;

/// <summary>
/// Property 56（夜晚修正与豁免）的属性测试。见 design.md〈Property 56〉与 docs/01〈昼夜机制〉。
/// 覆盖夜晚修正助手 <see cref="TurnPipeline.EffectiveVision"/>（Req 18.2）、
/// <see cref="TurnPipeline.EffectiveSupportRange"/>（Req 18.3）、
/// <see cref="TurnPipeline.NightAttackColumnShift"/>（Req 18.4）、
/// <see cref="TurnPipeline.EffectiveMovement"/>（Req 18.5）：
/// 夜晚且不持对应 keep 标志时按规则缩减/移档（含 min-1 钳制），
/// 白天或持有 keep 标志时原样返回（豁免）。
/// </summary>
public class NightModifierProperties
{
    // 夜晚机动惩罚：与实现常量一致（Req 18.5，机动减 1）。
    private const int NightMovementPenalty = -1;

    // 任意 NightFlags 组合：6 个位 -> 0..63。
    private static readonly Gen<NightFlags> GenFlags =
        Gen.Int[0, 63].Select(i => (NightFlags)i);

    // 昼夜阶段：上午 / 下午 / 晚上。
    private static readonly Gen<DayNightPhase> GenPhase =
        Gen.Int[0, 2].Select(i => (DayNightPhase)i);

    // 夜战配置：进攻移档（含 0 与正负）、射程修正（含正负）、视野除数（含 <1 边界）。
    private static readonly Gen<NightConfig> GenConfig =
        from attackShift in Gen.Int[-3, 3]
        from rangeMod in Gen.Int[-3, 3]
        from visionDivisor in Gen.Int[0, 5]
        select new NightConfig(attackShift, rangeMod, visionDivisor);

    // Feature: core-rules-engine, Property 56: 夜晚修正与豁免
    // EffectiveVision: Night & !NightVisionKeep → max(1, floor(V/divisor))（divisor 至少为 1）;
    // 白天或持 NightVisionKeep → 原样（豁免）。
    // Validates: Requirements 18.2
    [Fact]
    public void EffectiveVision_ReducedAtNightUnlessKept()
    {
        var gen =
            from baseVision in Gen.Int[1, 20]
            from flags in GenFlags
            from phase in GenPhase
            from config in GenConfig
            select (baseVision, flags, phase, config);

        gen.Sample(
            t =>
            {
                var actual = TurnPipeline.EffectiveVision(t.baseVision, t.flags, t.phase, t.config);

                var isNight = t.phase == DayNightPhase.Night;
                var kept = t.flags.HasFlag(NightFlags.NightVisionKeep);

                if (!isNight || kept)
                {
                    Assert.Equal(t.baseVision, actual); // 白天或豁免 → 原样
                }
                else
                {
                    var divisor = t.config.NightVisionDivisor < 1 ? 1 : t.config.NightVisionDivisor;
                    var expected = Math.Max(1, t.baseVision / divisor);
                    Assert.Equal(expected, actual);
                    Assert.True(actual >= 1); // min-1 钳制
                }
            },
            iter: 200);
    }

    // Feature: core-rules-engine, Property 56: 夜晚修正与豁免
    // EffectiveSupportRange: Night & !NightRangeKeep → max(1, range + NightRangeMod);
    // 白天或持 NightRangeKeep → 原样（豁免）。
    // Validates: Requirements 18.3
    [Fact]
    public void EffectiveSupportRange_ModifiedAtNightUnlessKept()
    {
        var gen =
            from baseRange in Gen.Int[1, 20]
            from flags in GenFlags
            from phase in GenPhase
            from config in GenConfig
            select (baseRange, flags, phase, config);

        gen.Sample(
            t =>
            {
                var actual = TurnPipeline.EffectiveSupportRange(t.baseRange, t.flags, t.phase, t.config);

                var isNight = t.phase == DayNightPhase.Night;
                var kept = t.flags.HasFlag(NightFlags.NightRangeKeep);

                if (!isNight || kept)
                {
                    Assert.Equal(t.baseRange, actual); // 白天或豁免 → 原样
                }
                else
                {
                    var expected = Math.Max(1, t.baseRange + t.config.NightRangeMod);
                    Assert.Equal(expected, actual);
                    Assert.True(actual >= 1); // min-1 钳制
                }
            },
            iter: 200);
    }

    // Feature: core-rules-engine, Property 56: 夜晚修正与豁免
    // EffectiveMovement: Night & !NightMoveKeep → max(1, move - 1);
    // 白天或持 NightMoveKeep → 原样（豁免）。
    // Validates: Requirements 18.5
    [Fact]
    public void EffectiveMovement_ReducedAtNightUnlessKept()
    {
        var gen =
            from baseMovement in Gen.Int[1, 20]
            from flags in GenFlags
            from phase in GenPhase
            select (baseMovement, flags, phase);

        gen.Sample(
            t =>
            {
                var actual = TurnPipeline.EffectiveMovement(t.baseMovement, t.flags, t.phase);

                var isNight = t.phase == DayNightPhase.Night;
                var kept = t.flags.HasFlag(NightFlags.NightMoveKeep);

                if (!isNight || kept)
                {
                    Assert.Equal(t.baseMovement, actual); // 白天或豁免 → 原样
                }
                else
                {
                    var expected = Math.Max(1, t.baseMovement + NightMovementPenalty);
                    Assert.Equal(expected, actual);
                    Assert.True(actual >= 1); // min-1 钳制
                }
            },
            iter: 200);
    }

    // Feature: core-rules-engine, Property 56: 夜晚修正与豁免
    // NightAttackColumnShift: Night & !NightAttackKeep & shift!=0 → ColumnShift(NightAttackShift, Night);
    // 否则（白天 / 持 NightAttackKeep / shift==0）→ null（豁免/无修正）。
    // Validates: Requirements 18.4
    [Fact]
    public void NightAttackColumnShift_AppliedAtNightUnlessKeptOrZero()
    {
        var gen =
            from flags in GenFlags
            from phase in GenPhase
            from config in GenConfig
            select (flags, phase, config);

        gen.Sample(
            t =>
            {
                var actual = TurnPipeline.NightAttackColumnShift(t.flags, t.phase, t.config);

                var isNight = t.phase == DayNightPhase.Night;
                var kept = t.flags.HasFlag(NightFlags.NightAttackKeep);
                var applies = isNight && !kept && t.config.NightAttackShift != 0;

                if (applies)
                {
                    Assert.NotNull(actual);
                    Assert.Equal(new ColumnShift(t.config.NightAttackShift, ModifierSource.Night), actual!.Value);
                }
                else
                {
                    Assert.Null(actual); // 白天 / 豁免 / 档数为 0 → 无修正
                }
            },
            iter: 200);
    }
}
