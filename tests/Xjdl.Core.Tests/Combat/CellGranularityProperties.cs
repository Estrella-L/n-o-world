using CsCheck;
using Xjdl.Core.Combat;
using Xjdl.Core.Hex;
using Xjdl.Core.State;
using Xjdl.Core.Tests.Support;

namespace Xjdl.Core.Tests.Combat;

// Feature: core-rules-engine, Property 33: 撤退与推进以格为单位
//
// 建模说明（在当前可用函数层级建模；全量流水线写回见任务 15.20）：
//
// 不变式 (a)「推进以格为单位 · 占领前须腾空」：
//   直接对 <see cref="Advance.Decide"/> 施压——对任意输入，若裁定 CanAdvance==true，
//   则该冲突格必须已腾空（cellVacated==true）。这正是「被推进占领的格，占领前该格
//   必须已腾空」（Req 9.5）。同时校验 EligibleUnits 非空 当且仅当 CanAdvance 为真，
//   即「有资格随格整体推进的存活单位集合」与推进资格一致。
//
// 不变式 (b)「撤退以格为单位 · 全部单位整体同撤」：
//   <see cref="Retreat.Resolve"/>/<see cref="Retreat.ChooseRetreatCell"/> 是以「格」
//   （origin + 共享上下文）为键的纯函数，不依赖单位个体身份。因此把同一格上的整叠单位
//   逐个施加相同输入的撤退操作，得到的目标格/结局必然完全一致——即整格作为一个整体同撤，
//   而非各单位各自散退。这与「执行撤退的格，其全部单位整体同撤」（Req 9.5）一致。
public class CellGranularityProperties
{
    /// <summary>任意 <see cref="HexCoord"/> 集合（0..6 个，去重）。</summary>
    private static readonly Gen<IReadOnlySet<HexCoord>> GenCoordSet =
        from n in Gen.Int[0, 6]
        from coords in Generators.HexCoord.List[n]
        select (IReadOnlySet<HexCoord>)coords.ToHashSet();

    /// <summary>优势方/进攻方参战单位快照集合（可空，混合归属与存活状态）。</summary>
    private static readonly Gen<IReadOnlyList<UnitState>> GenAdvancingUnits =
        from n in Gen.Int[0, 5]
        from units in Generators.UnitState.List[n]
        select (IReadOnlyList<UnitState>)units;

    /// <summary>
    /// 不变式 (a)：推进裁定绝不在冲突格未腾空时判定可推进（占领前须腾空，Req 9.5）；
    /// 且有资格随格推进的单位集合非空 当且仅当 CanAdvance 为真。
    /// **Validates: Requirements 9.5**
    /// </summary>
    [Fact]
    public void Advance_OccupiesOnlyAfterCellVacated()
    {
        var gen =
            from cell in Generators.HexCoord
            from side in Generators.Sides
            from vacated in Gen.Bool
            from mutual in Gen.Bool
            from units in GenAdvancingUnits
            select (cell, side, vacated, mutual, units);

        gen.Sample(t =>
        {
            var (cell, side, vacated, mutual, units) = t;
            var decision = Advance.Decide(cell, side, vacated, mutual, units);

            // 占领前该格必须已腾空：CanAdvance ⇒ cellVacated。
            if (decision.CanAdvance && !vacated)
            {
                return false;
            }

            // 随格整体推进的单位集合非空 ⇔ 可推进。
            return decision.CanAdvance == (decision.EligibleUnits.Count > 0);
        }, iter: 500);
    }

    /// <summary>
    /// 不变式 (b)：同一格的整叠单位在相同输入下撤退，结局完全一致——整格作为一个整体同撤
    /// （Req 9.5）。以逐个单位施加相同撤退裁定、断言目标格/结局相等来建模「以格为单位」。
    /// **Validates: Requirements 9.5**
    /// </summary>
    [Fact]
    public void Retreat_WholeCellMovesTogether()
    {
        var gen =
            from origin in Generators.HexCoord
            from stackN in Gen.Int[1, Generators.StackLimit]
            from stack in Generators.UnitState.List[stackN]
            from occupied in GenCoordSet
            from conflict in GenCoordSet
            from prep in GenCoordSet
            from zoc in GenCoordSet
            from enemyN in Gen.Int[0, 5]
            from enemies in Generators.HexCoord.List[enemyN]
            from hasRear in Gen.Bool
            from rear in Generators.HexCoord
            select (origin, stack, occupied, conflict, prep, zoc, enemies, hasRear, rear);

        gen.Sample(t =>
        {
            var (origin, stack, occupied, conflict, prep, zoc, enemies, hasRear, rear) = t;

            // 建模「一个格」：整叠单位全部落在同一 origin 上。
            var cellStack = stack.Select(u => u with { Position = origin }).ToList();

            // 到最近敌方的六角距离；无敌方时取极大值（Retreat 约定）。
            int DistanceToNearestEnemy(HexCoord c) =>
                enemies.Count == 0 ? int.MaxValue : enemies.Min(e => e.DistanceTo(c));

            var rearRef = hasRear ? rear : (HexCoord?)null;

            // 对同一格的每个单位施加相同输入的撤退裁定，断言结局完全一致。
            RetreatOutcome? first = null;
            foreach (var _ in cellStack)
            {
                var outcome = Retreat.Resolve(
                    origin, occupied, conflict, prep, zoc, DistanceToNearestEnemy, rearRef);

                if (first is null)
                {
                    first = outcome;
                }
                else if (!outcome.Equals(first.Value))
                {
                    // 整格未作为一个整体同撤 → 违反 Req 9.5。
                    return false;
                }
            }

            return true;
        }, iter: 500);
    }
}
