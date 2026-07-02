using CsCheck;
using Xjdl.Core.Combat;
using Xjdl.Core.Hex;
using Xjdl.Core.State;
using Xjdl.Core.Tests.Support;

namespace Xjdl.Core.Tests.Combat;

// Feature: core-rules-engine, Property 42: 推进裁定
public class AdvanceDecisionProperties
{
    /// <summary>
    /// 生成一个已落位单位，其 <see cref="UnitState.ResilienceLeft"/> 可能为 0（出局）。
    /// 从合法单位派生，随机将剩余韧性归零以覆盖「已歼灭/出局」样本，并重设当前攻/防与之一致。
    /// 归零单位表达阶段 8 结算后已出局、不应参与推进的存活判据（Req 12.5）。
    /// </summary>
    private static readonly Gen<UnitState> UnitMaybeDead =
        from unit in Generators.UnitState
        from dead in Gen.Bool
        select dead
            ? unit with { ResilienceLeft = 0, Attack = 0, Defense = 0 }
            : unit;

    /// <summary>
    /// 生成完整推进裁定输入：冲突格、推进方、腾空标志、互损标志，及一组随机归属/剩余韧性的单位。
    /// 单位 id 重编为唯一值，避免因 id 重复导致「按 id 排序」判定的歧义。
    /// </summary>
    private static readonly Gen<(HexCoord Cell, Side Side, bool Vacated, bool Mutual, IReadOnlyList<UnitState> Units)> GenInput =
        from cell in Generators.HexCoord
        from side in Generators.Sides
        from vacated in Gen.Bool
        from mutual in Gen.Bool
        from n in Gen.Int[0, 8]
        from units in UnitMaybeDead.List[n]
        select (
            cell,
            side,
            vacated,
            mutual,
            (IReadOnlyList<UnitState>)units
                .Select((u, i) => u with { Id = new UnitId(i) })
                .ToList());

    /// <summary>
    /// Property 42: 推进裁定。
    /// 对任意冲突格与结算后单位快照：
    /// <list type="bullet">
    /// <item>互损致双方同时出局时，无人推进——<c>CanAdvance</c> 为假且 <c>EligibleUnits</c> 为空（Req 12.6）。</item>
    /// <item>否则，仅当冲突格已腾空且推进方尚有存活单位时方可推进（Req 12.4、9.5）。</item>
    /// <item>合格推进单位恰为推进方的存活单位（Owner==推进方 且 ResilienceLeft&gt;0），按单位 id 稳定排序；
    /// 已出局单位不推进（Req 12.5、2.6）。</item>
    /// <item><c>EligibleUnits</c> 非空当且仅当 <c>CanAdvance</c> 为真。</item>
    /// </list>
    /// **Validates: Requirements 12.4, 12.5, 12.6**
    /// </summary>
    [Fact]
    public void Advance_IsGrantedOnlyToSurvivingAdvancingSide_WhenCellVacatedAndNoMutualElimination()
    {
        GenInput.Sample(input =>
        {
            var (cell, side, vacated, mutual, units) = input;

            var decision = Advance.Decide(cell, side, vacated, mutual, units);

            // 预期的存活推进单位：本方且剩余韧性 > 0，按 id 稳定排序。
            var expectedSurvivors = units
                .Where(u => u.Owner == side && u.ResilienceLeft > 0)
                .OrderBy(u => u.Id.Value)
                .ToList();

            // 冲突格坐标恒回传原格。
            if (decision.ContestedCell != cell)
            {
                return false;
            }

            if (mutual)
            {
                // 互损：无人推进。
                return !decision.CanAdvance && decision.EligibleUnits.Count == 0;
            }

            var expectedCanAdvance = vacated && expectedSurvivors.Count > 0;
            if (decision.CanAdvance != expectedCanAdvance)
            {
                return false;
            }

            // EligibleUnits 非空 当且仅当 CanAdvance。
            if ((decision.EligibleUnits.Count > 0) != decision.CanAdvance)
            {
                return false;
            }

            if (decision.CanAdvance)
            {
                // 合格单位恰为存活推进方单位，且顺序与稳定排序一致。
                return decision.EligibleUnits.SequenceEqual(expectedSurvivors);
            }

            return decision.EligibleUnits.Count == 0;
        }, iter: 1000);
    }
}
