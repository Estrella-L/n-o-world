using CsCheck;
using Xjdl.Core.Hex;
using Xjdl.Core.State;
using Xjdl.Core.Tests.Support;
using Xjdl.Core.Turn;

namespace Xjdl.Core.Tests.Turn;

/// <summary>
/// Property 38（控制区产生条件）的属性测试。见 design.md〈Property 38〉与 docs/01〈控制区〉。
/// 覆盖 <see cref="ZoneOfControl.ProducesZoc"/> 的产生判定（Req 11.1–11.3、11.7）
/// 与 <see cref="ZoneOfControl.ZocCells"/> 的全向 6 相邻格并集（Req 11.1、11.7）。
/// </summary>
public class ZoneOfControlProperties
{
    // 有界坐标：避免整数溢出，覆盖正负与零。
    private static readonly Gen<HexCoord> GenCoord =
        Gen.Select(Gen.Int[-8, 8], Gen.Int[-8, 8], (q, r) => new HexCoord(q, r));

    /// <summary>
    /// 独立复刻的产生规则（作为断言基准，不调用被测实现，避免循环论证）。
    /// Hold 恒真；AttackPrep 当且仅当本回合未移动且目标与快照相邻（距离 1）；Move 恒假。
    /// </summary>
    private static bool Oracle(Command command, HexCoord origin, HexCoord? target, bool moved) =>
        command switch
        {
            Command.Hold => true,
            Command.AttackPrep => !moved && target is { } t && HexCoord.Distance(origin, t) == 1,
            _ => false,
        };

    // Feature: core-rules-engine, Property 38: 控制区产生条件
    // ProducesZoc: Hold → 恒真; Move → 恒假;
    // AttackPrep → 当且仅当 !movedThisTurn 且目标与快照距离为 1。
    // Validates: Requirements 11.1, 11.2, 11.3, 11.7
    [Fact]
    public void ProducesZoc_MatchesStationaryProductionRule()
    {
        var gen =
            from snapshot in GenCoord
            from command in Generators.Commands
            from moved in Gen.Bool
            from targetKind in Gen.Int[0, 2]
            from dir in Gen.Int[0, 5]
            from arbitrary in GenCoord
            select (snapshot, command, moved, targetKind, dir, arbitrary);

        gen.Sample(
            t =>
            {
                HexCoord? target = t.targetKind switch
                {
                    1 => t.snapshot.Neighbor(t.dir), // 保证距离恰为 1
                    2 => t.arbitrary,
                    _ => null,
                };

                var actual = ZoneOfControl.ProducesZoc(t.command, t.snapshot, target, t.moved);
                var expected = Oracle(t.command, t.snapshot, target, t.moved);

                Assert.Equal(expected, actual);

                // 直接锁定各命令的语义，独立于 oracle。
                switch (t.command)
                {
                    case Command.Hold:
                        Assert.True(actual); // 据守恒产生（Req 11.1）
                        break;
                    case Command.Move:
                        Assert.False(actual); // 移动命令不产生（Req 11.3）
                        break;
                    case Command.AttackPrep:
                        var adjacentUnmoved = !t.moved && target is { } tt &&
                                              HexCoord.Distance(t.snapshot, tt) == 1;
                        Assert.Equal(adjacentUnmoved, actual); // Req 11.2 / 11.3
                        break;
                }
            },
            iter: 200);
    }

    // Feature: core-rules-engine, Property 38: 控制区产生条件
    // ZocCells 恰为「本回合产生控制区的己方单位」按机动开始快照位置的 6 相邻格之并集，
    // 去重且按 (Q, R) 字典序稳定排序（Req 11.1、11.7、2.6）。
    // Validates: Requirements 11.1, 11.2, 11.3, 11.7
    [Fact]
    public void ZocCells_IsUnionOfSixNeighborsOfProducingUnits()
    {
        var gen =
            from n in Gen.Int[0, 5]
            from bases in Generators.UnitState.List[n]
            from snaps in GenCoord.List[n]
            from useSnap in Gen.Bool.List[n]
            from targetKind in Gen.Int[0, 2].List[n]
            from dirs in Gen.Int[0, 5].List[n]
            from arbits in GenCoord.List[n]
            from moved in Gen.Bool.List[n]
            select (bases, snaps, useSnap, targetKind, dirs, arbits, moved);

        gen.Sample(
            t =>
            {
                var count = t.bases.Count;
                var units = new List<UnitState>(count);
                var snapshots = new Dictionary<UnitId, HexCoord>();
                var orders = new Dictionary<UnitId, UnitOrder>();
                var movedUnits = new HashSet<UnitId>();

                for (var i = 0; i < count; i++)
                {
                    var id = new UnitId(i);
                    var unit = t.bases[i] with { Id = id };
                    units.Add(unit);

                    if (t.useSnap[i])
                    {
                        snapshots[id] = t.snaps[i];
                    }

                    // 判定原点：与实现一致，取快照否则回退当前位置。
                    var origin = t.useSnap[i] ? t.snaps[i] : unit.Position;

                    HexCoord? target = t.targetKind[i] switch
                    {
                        1 => origin.Neighbor(t.dirs[i]), // 与原点相邻
                        2 => t.arbits[i],
                        _ => (HexCoord?)null,
                    };

                    orders[id] = new UnitOrder(id, unit.Command, null, target);

                    if (t.moved[i])
                    {
                        movedUnits.Add(id);
                    }
                }

                const Side side = Side.Blue;

                var actual = ZoneOfControl.ZocCells(units, side, snapshots, orders, movedUnits);

                // 独立计算期望并集。
                var expected = new HashSet<HexCoord>();
                foreach (var unit in units)
                {
                    if (unit.Owner != side)
                    {
                        continue;
                    }

                    var origin = snapshots.TryGetValue(unit.Id, out var snap) ? snap : unit.Position;
                    var target = orders[unit.Id].Target;
                    var didMove = movedUnits.Contains(unit.Id);

                    if (!Oracle(unit.Command, origin, target, didMove))
                    {
                        continue;
                    }

                    foreach (var neighbor in origin.Neighbors())
                    {
                        expected.Add(neighbor);
                    }
                }

                // 集合内容一致（恰好为并集，不多不少）。
                Assert.Equal(expected, actual.ToHashSet());

                // 去重。
                Assert.Equal(actual.Count, actual.Distinct().Count());

                // 按 (Q, R) 字典序稳定排序（确定性输出，Req 2.6）。
                var sorted = actual
                    .OrderBy(c => c.Q)
                    .ThenBy(c => c.R)
                    .ToArray();
                Assert.Equal(sorted, actual.ToArray());
            },
            iter: 200);
    }
}
