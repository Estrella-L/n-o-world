using CsCheck;
using Xjdl.Core.State;
using Xjdl.Core.Tests.Support;
using Xjdl.Core.Turn;

namespace Xjdl.Core.Tests.Turn;

/// <summary>
/// Property 10（回合末清进攻准备并按固定序切昼夜）的属性测试。
/// 见 design.md〈Property 10〉与 docs/01〈回合结束 / 昼夜〉。
/// 覆盖 <see cref="TurnPipeline.EndTurnPhase"/>：清除全部「进攻准备」残留（Req 3.10）、
/// 按固定序 上午→下午→晚上→上午 推进昼夜并在跨夜时递增天数（Req 18.1）。
/// </summary>
public class EndTurnProperties
{
    /// <summary>
    /// 独立复刻的固定序后继（作为断言基准，不调用被测实现，避免循环论证）。
    /// 上午→下午、下午→晚上、晚上→上午。
    /// </summary>
    private static DayNightPhase ExpectedNext(DayNightPhase phase) => phase switch
    {
        DayNightPhase.Morning => DayNightPhase.Afternoon,
        DayNightPhase.Afternoon => DayNightPhase.Night,
        DayNightPhase.Night => DayNightPhase.Morning,
        _ => DayNightPhase.Morning,
    };

    // Feature: core-rules-engine, Property 10: 回合末清进攻准备并按固定序切昼夜
    // EndTurnPhase: (1) 结束后无单位命令为 AttackPrep（清进攻准备，Req 3.10）;
    // (2) 结果昼夜恰为输入昼夜的固定序后继 上午→下午→晚上→上午（Req 18.1）;
    // (3) 仅当输入昼夜为晚上（折回上午）时天数 +1，否则天数不变;
    // 且非进攻准备命令（Move/Hold）逐位保持不变。
    // Validates: Requirements 3.10, 18.1
    [Fact]
    public void EndTurnPhase_ClearsAttackPrep_AndAdvancesDayNightInFixedCycle()
    {
        Generators.GameState.Sample(
            state =>
            {
                var before = state.Units;
                var result = TurnPipeline.EndTurnPhase(state);

                // (1) 无进攻准备残留（Req 3.10）。
                Assert.DoesNotContain(result.Units, u => u.Command == Command.AttackPrep);

                // (2) 昼夜为输入的固定序后继（Req 18.1）。
                Assert.Equal(ExpectedNext(state.Phase), result.Phase);

                // (3) 天数仅在跨夜（晚上→上午）时递增 1，否则不变。
                var expectedDayIndex = state.Phase == DayNightPhase.Night
                    ? state.DayIndex + 1
                    : state.DayIndex;
                Assert.Equal(expectedDayIndex, result.DayIndex);

                // 单位数量与顺序保持一致（仅命令可能被归位）。
                Assert.Equal(before.Count, result.Units.Count);

                // 非进攻准备命令逐位保持不变；进攻准备被归位为据守（Hold）。
                for (var i = 0; i < before.Count; i++)
                {
                    var original = before[i];
                    var after = result.Units[i];

                    // 除命令外的其余字段不变（同一单位，仅命令可能改变）。
                    Assert.Equal(original.Id, after.Id);

                    if (original.Command == Command.AttackPrep)
                    {
                        Assert.Equal(Command.Hold, after.Command);
                    }
                    else
                    {
                        Assert.Equal(original.Command, after.Command);
                    }
                }
            },
            iter: 200);
    }
}
