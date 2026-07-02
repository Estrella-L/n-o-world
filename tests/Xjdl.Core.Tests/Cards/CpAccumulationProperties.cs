using CsCheck;
using Xjdl.Core.Cards;
using Xjdl.Core.State;

namespace Xjdl.Core.Tests.Cards;

// Feature: core-rules-engine, Property 60: CP 累积受 cpMax 钳制
public class CpAccumulationProperties
{
    /// <summary>CP 上限：覆盖常见规模档区间的非负整数。</summary>
    private static readonly Gen<int> GenCpMax = Gen.Int[0, 50];

    /// <summary>
    /// 每回合 CP 产出序列（0..30 步），单步取值兼含负数以覆盖 <c>Math.Max(0, cpPerTurn)</c> 分支。
    /// </summary>
    private static readonly Gen<int[]> GenGains =
        from n in Gen.Int[0, 30]
        from gains in Gen.Int[-20, 20].Array[n]
        select gains;

    /// <summary>
    /// 生成（初始状态, 产出序列）：初始 Cp 落在 [0, cpMax]（合法起点，Req 19.2），
    /// CpMax 随机；Deck/Hand 与本属性无关，取空集合。
    /// </summary>
    private static readonly Gen<(CardState State, int[] Gains)> GenScenario =
        from cpMax in GenCpMax
        from cp in Gen.Int[0, cpMax]
        from gains in GenGains
        select (
            new CardState(
                Cp: cp,
                CpMax: cpMax,
                Deck: Array.Empty<CardId>(),
                Hand: Array.Empty<CardId>()),
            gains);

    /// <summary>
    /// Property 60: CP 累积受 cpMax 钳制。
    /// 对任意合法起点（Cp ≤ CpMax）与任意每回合产出序列（含负值），
    /// 逐步 <see cref="CardSystem.GainCp"/> 折叠后：每一步 Cp 单调不减且始终不超过 CpMax。
    /// **Validates: Requirements 19.2**
    /// </summary>
    [Fact]
    public void GainCp_IsMonotonicAndClampedByCpMax()
    {
        GenScenario.Sample(scenario =>
        {
            var (state, gains) = scenario;
            var cpMax = state.CpMax;

            foreach (var gain in gains)
            {
                var previous = state.Cp;
                state = CardSystem.GainCp(state, gain, cpMax);

                // 钳制：累积后绝不超过 cpMax。
                if (state.Cp > cpMax)
                {
                    return false;
                }

                // 单调不减：cpMax 固定、起点合法时，每步 Cp 不小于上一步。
                if (state.Cp < previous)
                {
                    return false;
                }
            }

            return true;
        }, iter: 1000);
    }
}
