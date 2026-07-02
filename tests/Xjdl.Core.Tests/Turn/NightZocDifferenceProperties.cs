using CsCheck;
using Xjdl.Core.State;
using Xjdl.Core.Turn;

namespace Xjdl.Core.Tests.Turn;

/// <summary>
/// Property 57（夜晚控制区差异）的属性测试。见 design.md〈Property 57〉与 docs/01〈昼夜机制〉。
/// 覆盖 <see cref="TurnPipeline.ProducesZocConsideringNight"/>（Req 18.6）：
/// 夜晚仅保留据守（<see cref="Command.Hold"/>）单位的控制区，进攻准备/移动的控制区一律失效；
/// 白天原样返回白天判定结果。持 <see cref="NightFlags.NightZocKeep"/> 的据守单位在夜晚同样不受影响
/// （据守控制区本就在夜晚保留，该标志为 no-op）。
/// </summary>
public class NightZocDifferenceProperties
{
    // 任意 NightFlags 组合：6 个位 -> 0..63。
    private static readonly Gen<NightFlags> GenFlags =
        Gen.Int[0, 63].Select(i => (NightFlags)i);

    // 命令：移动 / 进攻准备 / 据守。
    private static readonly Gen<Command> GenCommand =
        Gen.Int[0, 2].Select(i => (Command)i);

    // 昼夜阶段：上午 / 下午 / 晚上。
    private static readonly Gen<DayNightPhase> GenPhase =
        Gen.Int[0, 2].Select(i => (DayNightPhase)i);

    // Feature: core-rules-engine, Property 57: 夜晚控制区差异
    // ProducesZocConsideringNight:
    //   白天 → 原样返回 daytimeProducesZoc（不受命令/标志影响）;
    //   夜晚 → (Command==Hold && daytimeProducesZoc)——据守控制区保留，进攻准备/移动控制区失效;
    //   夜晚据守单位持 NightZocKeep 仍产生控制区（该标志为 no-op，不改变结果）。
    // Validates: Requirements 18.6
    [Fact]
    public void ProducesZoc_OnlyHoldPersistsAtNight()
    {
        var gen =
            from daytimeProducesZoc in Gen.Bool
            from command in GenCommand
            from flags in GenFlags
            from phase in GenPhase
            select (daytimeProducesZoc, command, flags, phase);

        gen.Sample(
            t =>
            {
                var actual = TurnPipeline.ProducesZocConsideringNight(
                    t.daytimeProducesZoc, t.command, t.flags, t.phase);

                var isNight = t.phase == DayNightPhase.Night;

                if (!isNight)
                {
                    // 白天：原样返回白天判定，命令与标志均不影响。
                    Assert.Equal(t.daytimeProducesZoc, actual);
                }
                else
                {
                    // 夜晚：仅据守单位保留控制区；进攻准备/移动控制区失效。
                    var expected = t.command == Command.Hold && t.daytimeProducesZoc;
                    Assert.Equal(expected, actual);

                    // 夜晚：进攻准备 / 移动控制区一律失效。
                    if (t.command != Command.Hold)
                    {
                        Assert.False(actual);
                    }
                }
            },
            iter: 200);
    }

    // Feature: core-rules-engine, Property 57: 夜晚控制区差异
    // 夜晚据守单位（白天产生控制区）无论是否持 NightZocKeep 都不受夜晚影响，仍产生控制区。
    // NightZocKeep 在据守单位上为 no-op：加/不加该标志结果一致。
    // Validates: Requirements 18.6
    [Fact]
    public void ProducesZoc_HoldWithNightZocKeepUnaffectedAtNight()
    {
        var gen =
            from otherFlags in GenFlags
            select otherFlags;

        gen.Sample(
            otherFlags =>
            {
                // 白天产生控制区、命令为据守、夜晚阶段的据守单位。
                var withoutKeep = otherFlags & ~NightFlags.NightZocKeep;
                var withKeep = withoutKeep | NightFlags.NightZocKeep;

                var resultWithout = TurnPipeline.ProducesZocConsideringNight(
                    daytimeProducesZoc: true, Command.Hold, withoutKeep, DayNightPhase.Night);
                var resultWith = TurnPipeline.ProducesZocConsideringNight(
                    daytimeProducesZoc: true, Command.Hold, withKeep, DayNightPhase.Night);

                // 据守控制区在夜晚保留：两者均为 true，NightZocKeep 为 no-op。
                Assert.True(resultWithout);
                Assert.True(resultWith);
                Assert.Equal(resultWithout, resultWith);
            },
            iter: 200);
    }
}
