using CsCheck;
using Xjdl.Core.Hex;
using Xjdl.Core.State;
using Xjdl.Core.Turn;

namespace Xjdl.Core.Tests.Turn;

/// <summary>
/// 机动冲突「移入正在离开的格」的属性测试（CsCheck，一属性一测试，至少 100 次迭代）。
/// 见 design.md〈Property 44〉与 <see cref="TurnPipeline.ResolveManeuverConflicts"/>（Req 13.3）。
/// <para>
/// 语义（事实来源 TurnPipeline.Conflict.cs）：一个单位移入敌方正在离开的格 C——
/// </para>
/// <list type="bullet">
/// <item><b>敌成功离开</b>：以敌方原住单位 <see cref="TurnPipeline.ManeuverIntent.LeavesOrigin"/> 为真
/// 且其落点不同于 C 建模（该敌确实腾空 C 前往他处）。此时 C 在裁定时为空格，移入方占据 C。</item>
/// <item><b>敌未能离开</b>：以敌方原住单位 <c>LeavesOrigin=false</c>（据守 C，落点即 C）建模。
/// 此时 C 仍被敌据守，移入方<b>不占据</b>（停在其出发格），并产生一处
/// <see cref="TurnPipeline.ManeuverContact"/>（接触，而非占据）。</item>
/// </list>
/// <para>
/// 「成功离开」与「未能离开」的建模方式：直接使用输入原子 <see cref="TurnPipeline.ManeuverIntent"/>
/// 的 <c>LeavesOrigin</c> 布尔位——真＝腾空出发格（Destination≠Origin），假＝据守出发格（Destination＝Origin）。
/// 这正是裁定内部区分 C 是否为空的唯一判据（<c>IsMover = LeavesOrigin &amp;&amp; Destination != Origin</c>）。
/// </para>
/// </summary>
// Feature: core-rules-engine, Property 44: 移入正在离开的格
public class MoveIntoLeavingCellProperties
{
    private static readonly UnitId MoverId = new(0);
    private static readonly UnitId EnemyId = new(1);

    /// <summary>争夺格 C 的坐标：q、r ∈ [-8, 8]，由此确定性派生互异的其余格。</summary>
    private static readonly Gen<HexCoord> GenCell =
        Gen.Select(Gen.Int[-8, 8], Gen.Int[-8, 8], (q, r) => new HexCoord(q, r));

    /// <summary>
    /// Property 44（敌成功离开分支）：敌方原住单位以 <c>LeavesOrigin=true</c> 腾空 C 前往他处，
    /// 移入 C 的敌对单位<b>占据</b> C（<c>FinalPositions[Mover] == C</c>），且该格<b>不产生接触</b>。
    /// **Validates: Requirements 13.3**
    /// </summary>
    [Fact]
    public void MoverOccupiesCellWhenEnemySuccessfullyLeaves()
    {
        GenCell.Sample(
            c =>
            {
                // 互异派生：移入者出发格 M（+10 于 Q），敌方离开落点 D（+10 于 R）。
                var moverOrigin = new HexCoord(c.Q + 10, c.R);
                var enemyDest = new HexCoord(c.Q, c.R + 10);

                // 敌方原住 C，成功离开（LeavesOrigin=true）前往 D。
                var enemyLeaving = new TurnPipeline.ManeuverIntent(
                    MakeUnit(EnemyId, Side.Red, c), c, enemyDest, LeavesOrigin: true);

                // 蓝方移入者：出发格 M，成功离开前往 C。
                var mover = new TurnPipeline.ManeuverIntent(
                    MakeUnit(MoverId, Side.Blue, moverOrigin), moverOrigin, c, LeavesOrigin: true);

                var result = TurnPipeline.ResolveManeuverConflicts(
                    new[] { enemyLeaving, mover });

                // 敌成功离开 → 移入方占据 C。
                Assert.Equal(c, result.FinalPositions[MoverId]);
                // 该格不触发接触（占据路径，非接触路径）。
                Assert.DoesNotContain(result.Contacts, ct => ct.Location == c);
                // 附带确证：敌方确实落在其离开的目标格 D。
                Assert.Equal(enemyDest, result.FinalPositions[EnemyId]);
            },
            iter: 200);
    }

    /// <summary>
    /// Property 44（敌未能离开分支）：敌方原住单位以 <c>LeavesOrigin=false</c> 据守 C，
    /// 移入 C 的敌对单位<b>不占据</b>（停在其出发格），并产生一处
    /// <see cref="TurnPipeline.ManeuverContact"/>(Location=C, Mover, Defender=敌)。
    /// **Validates: Requirements 13.3**
    /// </summary>
    [Fact]
    public void MoverContactsCellWhenEnemyFailsToLeave()
    {
        GenCell.Sample(
            c =>
            {
                var moverOrigin = new HexCoord(c.Q + 10, c.R);

                // 敌方原住 C，未能离开（据守）：LeavesOrigin=false 且落点即 C。
                var enemyStaying = new TurnPipeline.ManeuverIntent(
                    MakeUnit(EnemyId, Side.Red, c), c, c, LeavesOrigin: false);

                // 蓝方移入者：出发格 M，成功离开前往 C。
                var mover = new TurnPipeline.ManeuverIntent(
                    MakeUnit(MoverId, Side.Blue, moverOrigin), moverOrigin, c, LeavesOrigin: true);

                var result = TurnPipeline.ResolveManeuverConflicts(
                    new[] { enemyStaying, mover });

                // 敌未能离开 → 移入方不占据，停在其出发格 M。
                Assert.Equal(moverOrigin, result.FinalPositions[MoverId]);
                // 敌方仍据守 C。
                Assert.Equal(c, result.FinalPositions[EnemyId]);
                // 产生接触：Location=C，Mover=移入者，Defender=据守敌方。
                Assert.Contains(
                    result.Contacts,
                    ct => ct.Location == c && ct.Mover == MoverId && ct.Defender == EnemyId);
            },
            iter: 200);
    }

    /// <summary>构造用于机动裁定的最小 <see cref="UnitState"/>（数值仅需满足堆叠主攻判定即可）。</summary>
    private static UnitState MakeUnit(UnitId id, Side owner, HexCoord position) =>
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
            Position: position,
            Command: Command.Move,
            Flags: NightFlags.None);
}
