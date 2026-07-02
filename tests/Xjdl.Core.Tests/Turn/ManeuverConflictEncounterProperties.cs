using CsCheck;
using Xjdl.Core.Hex;
using Xjdl.Core.State;
using Xjdl.Core.Turn;

namespace Xjdl.Core.Tests.Turn;

/// <summary>
/// 机动冲突触发遭遇战的属性测试（CsCheck，一属性一测试，至少 100 次迭代）。
/// 直接驱动纯函数 <see cref="TurnPipeline.ResolveManeuverConflicts"/>（Req 13.1/13.2），
/// 见 design.md〈Property 43〉与 src/Xjdl.Core/Turn/TurnPipeline.Conflict.cs。
/// <para>
/// 语义（事实来源 TurnPipeline.Conflict.cs）：
/// </para>
/// <list type="bullet">
/// <item>(a) <b>同格</b>：两敌对单位（<see cref="UnitState.Owner"/> 不同）均<em>成功离开</em>出发格并移入
/// <em>同一空格</em> → 二者均<b>不占据</b>该格（停在各自出发格），在该格触发一处 <see cref="TurnPipeline.ManeuverConflictKind.SameCell"/>
/// 遭遇战（表三，Req 13.1），参与者含双方。</item>
/// <item>(b) <b>互换</b>：两敌对单位 A、B 满足 A.Origin==B.Destination 且 A.Destination==B.Origin、均成功离开
/// → 二者均<b>不占据</b>（停在各自出发格），触发一处 <see cref="TurnPipeline.ManeuverConflictKind.Swap"/> 遭遇战
/// （Req 13.2），代表位置取两出发格 <c>(Q, R)</c> 字典序较小者。</item>
/// </list>
/// </summary>
// Feature: core-rules-engine, Property 43: 机动冲突触发遭遇战
public class ManeuverConflictEncounterProperties
{
    private static readonly UnitId UnitA = new(0);
    private static readonly UnitId UnitB = new(1);

    /// <summary>目标 / 基准格坐标：q、r ∈ [-20, 20]（确定性生成，范围足够分散）。</summary>
    private static readonly Gen<HexCoord> GenCell =
        Gen.Select(Gen.Int[-20, 20], Gen.Int[-20, 20], (q, r) => new HexCoord(q, r));

    /// <summary>
    /// Property 43 (a): 两敌对单位移入同一空格 → 同格遭遇战，双方均不占据。
    /// 对任意空目标格 <c>D</c>：蓝、红两单位从相异出发格（均 ≠ <c>D</c>）成功离开并同时移入 <c>D</c>，
    /// 裁定结果含唯一一处 <see cref="TurnPipeline.ManeuverConflictKind.SameCell"/> 遭遇战（位置 <c>D</c>，参与双方），
    /// 且二者最终位置仍为各自出发格（不占据 <c>D</c>）。
    /// **Validates: Requirements 13.1**
    /// </summary>
    [Fact]
    public void TwoEnemiesIntoSameEmptyCellTriggerSameCellEncounterAndNeitherOccupies()
    {
        GenCell.Sample(
            destination =>
            {
                // 两相异出发格，均确定性偏离目标格：保证 D 为空（无原住单位）且两出发格互异。
                var originA = new HexCoord(destination.Q + 100, destination.R);
                var originB = new HexCoord(destination.Q, destination.R + 100);

                var intents = new[]
                {
                    new TurnPipeline.ManeuverIntent(
                        BuildUnit(UnitA, Side.Blue, originA), originA, destination, LeavesOrigin: true),
                    new TurnPipeline.ManeuverIntent(
                        BuildUnit(UnitB, Side.Red, originB), originB, destination, LeavesOrigin: true),
                };

                var res = TurnPipeline.ResolveManeuverConflicts(intents);

                // 恰有一处遭遇战，成因为同格，位置即共同意图落点 D。
                var enc = Assert.Single(res.Encounters);
                Assert.Equal(TurnPipeline.ManeuverConflictKind.SameCell, enc.Kind);
                Assert.Equal(destination, enc.Location);

                // 参与者含双方（按 UnitId 升序）。
                Assert.Equal(new[] { UnitA, UnitB }, enc.Participants);

                // 双方均不占据 D：最终位置仍为各自出发格，且 D 无占据者。
                Assert.Equal(originA, res.FinalPositions[UnitA]);
                Assert.Equal(originB, res.FinalPositions[UnitB]);
                Assert.False(res.Occupancy.ContainsKey(destination));
            },
            iter: 100);
    }

    /// <summary>
    /// Property 43 (b): 两敌对单位互换格子 → 互换遭遇战，双方均不占据。
    /// 对任意相异格对 <c>P、Q</c>：A 从 <c>P</c>→<c>Q</c>、B 从 <c>Q</c>→<c>P</c>，均成功离开，
    /// 裁定结果含唯一一处 <see cref="TurnPipeline.ManeuverConflictKind.Swap"/> 遭遇战（参与双方、位置取较小出发格），
    /// 且二者最终位置仍为各自出发格（不占据对方格）。
    /// **Validates: Requirements 13.2**
    /// </summary>
    [Fact]
    public void TwoEnemiesSwappingCellsTriggerSwapEncounterAndNeitherOccupies()
    {
        // deltaQ ≥ 1 保证 P ≠ Q（两格必相异）。
        Gen.Select(GenCell, Gen.Int[1, 20], Gen.Int[-20, 20])
            .Sample(
                t =>
                {
                    var (p, deltaQ, deltaR) = t;
                    var q = new HexCoord(p.Q + deltaQ, p.R + deltaR);

                    var intents = new[]
                    {
                        new TurnPipeline.ManeuverIntent(
                            BuildUnit(UnitA, Side.Blue, p), p, q, LeavesOrigin: true),
                        new TurnPipeline.ManeuverIntent(
                            BuildUnit(UnitB, Side.Red, q), q, p, LeavesOrigin: true),
                    };

                    var res = TurnPipeline.ResolveManeuverConflicts(intents);

                    // 恰有一处遭遇战，成因为互换。
                    var enc = Assert.Single(res.Encounters);
                    Assert.Equal(TurnPipeline.ManeuverConflictKind.Swap, enc.Kind);

                    // 代表位置：两出发格中 (Q, R) 字典序较小者。
                    var expectedLocation = Lexicographically(p, q) <= 0 ? p : q;
                    Assert.Equal(expectedLocation, enc.Location);

                    // 参与者含双方（按 UnitId 升序）。
                    Assert.Equal(new[] { UnitA, UnitB }, enc.Participants);

                    // 双方均不占据对方格：最终位置仍为各自出发格。
                    Assert.Equal(p, res.FinalPositions[UnitA]);
                    Assert.Equal(q, res.FinalPositions[UnitB]);
                },
                iter: 100);
    }

    /// <summary>六角格 (Q, R) 字典序比较，与实现的冲突排序一致（Req 2.6）。</summary>
    private static int Lexicographically(HexCoord a, HexCoord b)
    {
        var byQ = a.Q.CompareTo(b.Q);
        return byQ != 0 ? byQ : a.R.CompareTo(b.R);
    }

    /// <summary>构造一个位于 <paramref name="pos"/> 的最小单位；裁定纯助手只读取 Id/Owner，其余取合法占位值。</summary>
    private static UnitState BuildUnit(UnitId id, Side owner, HexCoord pos) =>
        new(
            Id: id,
            Owner: owner,
            TypeKey: "unit.infantry",
            Class: UnitClass.LineHold,
            InitAttack: 1,
            InitDefense: 1,
            Resilience0: 1,
            Attack: 1,
            Defense: 1,
            ResilienceLeft: 1,
            Movement: 1,
            Vision: 1,
            SupportRange: 0,
            Position: pos,
            Command: Command.Move,
            Flags: NightFlags.None);
}
