using CsCheck;
using Xjdl.Core.Hex;
using Xjdl.Core.State;
using Xjdl.Core.Turn;

namespace Xjdl.Core.Tests.Turn;

/// <summary>
/// Property 40（出局单位控制区消失）的属性测试。见 design.md〈Property 40〉与 docs/01〈控制区〉。
/// <para>
/// 控制区由 <see cref="ZoneOfControl.ZocCells"/> 依「当前单位集合」纯函数重算（Req 11.1–11.3、11.7）。
/// 一旦某产生方在阶段 6 被歼灭（从单位集合移除）或在阶段 7 被迫撤退（移动到远处），
/// 以更新后的单位集合重算控制区，其原相邻格控制区随之消失（在无其它产生方覆盖时）。
/// </para>
/// <para>
/// 建模采用单一据守产生方 U：据守恒产生控制区（Req 11.1），故其原点 6 相邻格即控制区。
/// 移除 U（歼灭）后重算得空集；将 U 迁至远处（撤退，距离 ≥ 20，六相邻格与原点相邻格必然不相交）
/// 后重算仅得新点相邻格，原相邻格全部消失。伴随一个下达移动命令的旁观单位（不产生控制区，Req 11.3），
/// 确认其不影响结论。
/// </para>
/// </summary>
public class ZocDisappearsAfterOutOfPlayProperties
{
    // 有界坐标：覆盖正负与零，避免整数溢出。
    private static readonly Gen<HexCoord> GenCoord =
        Gen.Select(Gen.Int[-8, 8], Gen.Int[-8, 8], (q, r) => new HexCoord(q, r));

    private static readonly IReadOnlyDictionary<UnitId, HexCoord> NoSnapshots =
        new Dictionary<UnitId, HexCoord>();

    private static readonly IReadOnlyDictionary<UnitId, UnitOrder> NoOrders =
        new Dictionary<UnitId, UnitOrder>();

    private static readonly IReadOnlySet<UnitId> NoMoved = new HashSet<UnitId>();

    /// <summary>构造一个纯净的据守单位（据守恒产生控制区，Req 11.1）。攻/防整除韧性以满足域不变量。</summary>
    private static UnitState HoldUnit(int id, Side side, HexCoord pos) =>
        new(
            new UnitId(id),
            side,
            "unit.infantry",
            UnitClass.LineHold,
            6, // InitAttack
            6, // InitDefense
            3, // Resilience0
            6, // Attack（= 衰减量 2 × 剩余韧性 3）
            6, // Defense
            3, // ResilienceLeft
            4, // Movement
            3, // Vision
            2, // SupportRange
            pos,
            Command.Hold,
            NightFlags.None);

    /// <summary>构造一个下达移动命令的旁观单位（不产生控制区，Req 11.3）。</summary>
    private static UnitState MoveUnit(int id, Side side, HexCoord pos) =>
        HoldUnit(id, side, pos) with { Command = Command.Move };

    // Feature: core-rules-engine, Property 40: 出局单位控制区消失
    // 产生方 U（据守）在阶段 6 被歼灭（移出单位集合）或阶段 7 被迫撤退（迁往远处）后，
    // 以更新后的单位集合重算 ZocCells，其原相邻格控制区消失（无其它产生方覆盖时）。
    // Validates: Requirements 11.6
    [Fact]
    public void OutOfPlayProducerZocDisappearsAfterRecompute()
    {
        var gen =
            from origin in GenCoord
            from annihilate in Gen.Bool // true：歼灭（阶段 6）；false：撤退（阶段 7）
            from dq in Gen.Int[10, 20] // 撤退位移，保证与原点距离 ≥ 20
            from dr in Gen.Int[10, 20]
            from bystander in GenCoord // 下达移动命令的旁观单位位置（不产生控制区）
            select (origin, annihilate, dq, dr, bystander);

        gen.Sample(
            t =>
            {
                const Side side = Side.Blue;

                var producer = HoldUnit(0, side, t.origin);
                var mover = MoveUnit(1, side, t.bystander);

                // 出局前：单一据守产生方 + 一个不产生控制区的移动单位。
                var before = new List<UnitState> { producer, mover };

                var zocBefore = ZoneOfControl.ZocCells(before, side, NoSnapshots, NoOrders, NoMoved);

                // 前置事实：控制区恰为产生方原点的 6 相邻格（据守全向，Req 11.1）。
                var originalNeighbors = t.origin.Neighbors().ToHashSet();
                Assert.Equal(originalNeighbors, zocBefore.ToHashSet());
                Assert.NotEmpty(zocBefore);

                // 更新单位集合：歼灭 = 移除；撤退 = 迁往远处（距离 ≥ 20，相邻格必不相交）。
                List<UnitState> after;
                if (t.annihilate)
                {
                    after = new List<UnitState> { mover }; // 阶段 6：U 出局，退出单位集合。
                }
                else
                {
                    var retreatPos = new HexCoord(t.origin.Q + t.dq, t.origin.R + t.dr);
                    after = new List<UnitState> { producer with { Position = retreatPos }, mover };
                }

                var zocAfter = ZoneOfControl.ZocCells(after, side, NoSnapshots, NoOrders, NoMoved);

                // 核心断言：U 的原相邻格控制区全部消失（无其它产生方覆盖）。
                foreach (var cell in originalNeighbors)
                {
                    Assert.DoesNotContain(cell, zocAfter);
                }

                if (t.annihilate)
                {
                    // 唯一产生方出局后，控制区归空。
                    Assert.Empty(zocAfter);
                }
                else
                {
                    // 撤退后控制区平移至新位置的 6 相邻格，与原相邻格不相交。
                    var retreatPos = new HexCoord(t.origin.Q + t.dq, t.origin.R + t.dr);
                    Assert.Equal(retreatPos.Neighbors().ToHashSet(), zocAfter.ToHashSet());
                }
            },
            iter: 200);
    }
}
