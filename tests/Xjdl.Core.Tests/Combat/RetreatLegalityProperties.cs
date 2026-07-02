using CsCheck;
using Xjdl.Core.Combat;
using Xjdl.Core.Hex;

namespace Xjdl.Core.Tests.Combat;

// Feature: core-rules-engine, Property 41: 撤退目标合法性
public class RetreatLegalityProperties
{
    /// <summary>坐标生成边界，保持在小范围内以便复现与聚焦邻域裁定。</summary>
    private const int CoordMin = -8;

    private const int CoordMax = 8;

    private static readonly Gen<HexCoord> GenHexCoord =
        from q in Gen.Int[CoordMin, CoordMax]
        from r in Gen.Int[CoordMin, CoordMax]
        select new HexCoord(q, r);

    /// <summary>
    /// 一个撤退裁定场景：原位、一个用于定义「到最近敌方距离」的敌方位置、
    /// 以及四类禁入集合（均为原位相邻格的任意子集，用 6 位掩码选取），外加后方参考点。
    /// </summary>
    private sealed record Scenario(
        HexCoord Origin,
        HexCoord Enemy,
        IReadOnlySet<HexCoord> Occupied,
        IReadOnlySet<HexCoord> Conflict,
        IReadOnlySet<HexCoord> AttackPrep,
        IReadOnlySet<HexCoord> Zoc,
        HexCoord? RearReference);

    /// <summary>由 6 位掩码从原位的 6 个相邻格中选取一个子集。</summary>
    private static IReadOnlySet<HexCoord> SubsetOfNeighbors(HexCoord origin, int mask)
    {
        var neighbors = origin.Neighbors().ToList();
        var set = new HashSet<HexCoord>();
        for (var i = 0; i < neighbors.Count; i++)
        {
            if ((mask & (1 << i)) != 0)
            {
                set.Add(neighbors[i]);
            }
        }

        return set;
    }

    private static readonly Gen<Scenario> GenScenario =
        from origin in GenHexCoord
        from enemy in GenHexCoord
        from occupiedMask in Gen.Int[0, 63]
        from conflictMask in Gen.Int[0, 63]
        from attackMask in Gen.Int[0, 63]
        from zocMask in Gen.Int[0, 63]
        from hasRear in Gen.Bool
        from rear in GenHexCoord
        select new Scenario(
            origin,
            enemy,
            SubsetOfNeighbors(origin, occupiedMask),
            SubsetOfNeighbors(origin, conflictMask),
            SubsetOfNeighbors(origin, attackMask),
            SubsetOfNeighbors(origin, zocMask),
            hasRear ? rear : (HexCoord?)null);

    /// <summary>
    /// Property 41: 撤退目标合法性。
    /// 对任意执行撤退的单位，<see cref="Retreat.LegalRetreatCells"/> 与
    /// <see cref="Retreat.ChooseRetreatCell"/> 返回的每个撤退格都满足全部合法性约束：
    /// 为空、不属于冲突格/被敌进攻准备指向的格/敌方控制区格，且到最近敌方的六角距离不小于原位；
    /// 被选中的格必在合法格集合内；且相同输入必得相同选择（确定性）（Req 12.1、12.2）。
    /// **Validates: Requirements 12.1, 12.2**
    /// </summary>
    [Fact]
    public void RetreatTargets_AreLegalAndDeterministic()
    {
        GenScenario.Sample(
            s =>
            {
                int Distance(HexCoord c) => c.DistanceTo(s.Enemy);

                var originDistance = Distance(s.Origin);

                bool IsLegal(HexCoord cell) =>
                    !s.Occupied.Contains(cell)
                    && !s.Conflict.Contains(cell)
                    && !s.AttackPrep.Contains(cell)
                    && !s.Zoc.Contains(cell)
                    && Distance(cell) >= originDistance;

                var legal = Retreat.LegalRetreatCells(
                    s.Origin, s.Occupied, s.Conflict, s.AttackPrep, s.Zoc, Distance);

                // 每个「合法格」确实是原位相邻格且满足全部约束。
                Assert.All(legal, cell =>
                {
                    Assert.Equal(1, s.Origin.DistanceTo(cell));
                    Assert.True(IsLegal(cell));
                });

                var chosen = Retreat.ChooseRetreatCell(
                    s.Origin, s.Occupied, s.Conflict, s.AttackPrep, s.Zoc, Distance, s.RearReference);

                if (chosen is { } cell)
                {
                    // 被选中的格必满足合法性并出现在合法格集合中。
                    Assert.True(IsLegal(cell));
                    Assert.Contains(cell, legal);
                }
                else
                {
                    // 无选中格 ⇔ 没有任何合法格。
                    Assert.Empty(legal);
                }

                // 确定性：相同输入必得相同选择。
                var chosenAgain = Retreat.ChooseRetreatCell(
                    s.Origin, s.Occupied, s.Conflict, s.AttackPrep, s.Zoc, Distance, s.RearReference);
                Assert.Equal(chosen, chosenAgain);
            },
            iter: 200);
    }
}
