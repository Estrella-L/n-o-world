using CsCheck;
using Xjdl.Core.Hex;
using Xjdl.Core.Random;
using Xjdl.Core.State;
using Xjdl.Core.Turn;

namespace Xjdl.Core.Tests.Turn;

/// <summary>
/// 机动阶段「临机机动只改移动且守恒」的属性测试（CsCheck，一属性一测试，至少 100 次迭代）。
/// 见 design.md〈Property 13〉与 <see cref="TurnPipeline.ManeuverPhase"/>（Req 4.4/4.5）。
/// <para>
/// 被验证语义（事实来源 TurnPipeline.Maneuver.cs）：临机机动仅在
/// 「新路径移动消耗 ≤ 剩余机动力」且「指挥点 ≥ RepositionCpCost（=1）」时被接受；被接受后
/// <b>只改移动路径、不改命令性质（Req 4.4）、不增机动力（Req 4.5）</b>。统一移动模型下每进入一格
/// 消耗 1 点、预算取整回合机动力，逐格推进至第 <c>min(Movement, path.Count)</c> 格。
/// 构造「单方单位、无敌军」场景使控制区为空、无接触干扰，令落点纯由被选路径与机动点预算决定，
/// 从而使断言确定可判定。
/// </para>
/// </summary>
// Feature: core-rules-engine, Property 13: 临机机动只改移动且守恒
public class ManeuverRepositionConservationProperties
{
    /// <summary>单位起点（原地）。路径生成排除此格以免与起点混淆。</summary>
    private static readonly HexCoord Origin = new(0, 0);

    /// <summary>进攻准备命令的远距目标：与起点六角距离远大于 1，故开局不锁定，允许临机改向。</summary>
    private static readonly HexCoord DistantTarget = new(20, -20);

    /// <summary>实现内固定的单次临机机动指挥点代价（RepositionCpCost = 1）。</summary>
    private const int RepositionCpCost = 1;

    /// <summary>实现内统一的每格移动消耗（DefaultMoveCostPerCell = 1）。</summary>
    private const int MoveCostPerCell = 1;

    // 有界坐标：q、r ∈ [-6, 6]，覆盖不同长度路径且不越出地图构造范围。
    private static readonly Gen<HexCoord> GenCoord =
        Gen.Select(Gen.Int[-6, 6], Gen.Int[-6, 6], (q, r) => new HexCoord(q, r));

    // 随机路径：去重且排除起点（去重仅为让期望落点无歧义）。
    private static Gen<IReadOnlyList<HexCoord>> GenPath(int max) =>
        Gen.Int[0, max].SelectMany(n =>
            GenCoord.List[n].Select(list =>
                (IReadOnlyList<HexCoord>)list
                    .Where(c => c != Origin)
                    .Distinct()
                    .ToList()));

    // 完整场景：机动力预算、原计划路径、临机新路径、蓝方指挥点结余、是否为进攻准备命令。
    private static readonly Gen<Scenario> GenScenario =
        Gen.Int[0, 8].SelectMany(movement =>
        GenPath(6).SelectMany(planned =>
        GenPath(8).SelectMany(newPath =>
        Gen.Int[0, 3].SelectMany(cp =>
        Gen.Bool.Select(isPrep =>
            new Scenario(movement, planned, newPath, cp, isPrep))))));

    /// <summary>
    /// Property 13: 临机机动只改移动且守恒。
    /// 对任意未锁定单位的临机机动：其命令性质保持不变（Req 4.4）；机动力字段不增（Req 4.5）；
    /// 被接受时落点与新路径按 <c>min(Movement, path.Count)</c> 截断一致，绝不多走超过机动力允许的格数；
    /// 新路径消耗 &gt; 机动力时不被当作额外移动实施（仍按原计划推进）。
    /// **Validates: Requirements 4.4, 4.5**
    /// </summary>
    [Fact]
    public void Reposition_PreservesCommandAndMovement_AndNeverExceedsBudget()
    {
        GenScenario.Sample(
            sc =>
            {
                var command = sc.IsAttackPrep ? Command.AttackPrep : Command.Move;
                var target = sc.IsAttackPrep ? (HexCoord?)DistantTarget : null;

                var state = BuildSingleMoverState(sc.Movement, command, sc.Planned, sc.NewPath, sc.Cp);
                var cmds = new TurnCommands(
                    new[] { new UnitOrder(new UnitId(0), command, sc.Planned, target) },
                    new[] { new RepositionCommand(new UnitId(0), sc.NewPath, TriggerTick: 3) },
                    Array.Empty<CardPlay>());

                var next = TurnPipeline.ManeuverPhase(state, cmds, new PcgRng(12345UL));
                var moved = next.Units.Single(u => u.Id.Value == 0);

                // 临机机动被接受的条件：新路径消耗 ≤ 剩余机动力，且指挥点足够（Req 4.5/4.8）。
                var newPathCost = sc.NewPath.Count * MoveCostPerCell;
                var accepted = newPathCost <= sc.Movement && sc.Cp >= RepositionCpCost;
                var chosen = accepted ? sc.NewPath : sc.Planned;

                // 逐格 1 点消耗、预算 = movement，推进 min(movement, chosen.Count) 格。
                var advanced = Math.Min(sc.Movement, chosen.Count);
                var expected = advanced == 0 ? Origin : chosen[advanced - 1];

                // (1) 命令性质不变（Req 4.4）。
                Assert.Equal(command, moved.Command);

                // (2) 机动力字段不变，临机机动不增机动力（Req 4.5）。
                Assert.Equal(sc.Movement, moved.Movement);

                // (3) 落点与被选路径按机动力预算截断一致：绝不多走超过机动力允许的格数。
                Assert.Equal(expected, moved.Position);
                Assert.True(advanced <= sc.Movement);

                // (4) 新路径消耗 > 机动力时不被接受、不作为额外移动实施（仍按原计划推进）。
                if (newPathCost > sc.Movement)
                {
                    Assert.False(accepted);
                    var plannedAdvanced = Math.Min(sc.Movement, sc.Planned.Count);
                    var plannedExpected = plannedAdvanced == 0 ? Origin : sc.Planned[plannedAdvanced - 1];
                    Assert.Equal(plannedExpected, moved.Position);
                }
            },
            iter: 200);
    }

    /// <summary>
    /// 构造仅含一个蓝方单位、无敌军的 <see cref="GameState"/>（控制区为空、无接触干扰）；
    /// 地图覆盖起点与两条路径的全部格；蓝方指挥点结余 = <paramref name="cp"/>。
    /// </summary>
    private static GameState BuildSingleMoverState(
        int movement,
        Command command,
        IReadOnlyList<HexCoord> planned,
        IReadOnlyList<HexCoord> newPath,
        int cp)
    {
        var cells = new Dictionary<HexCoord, MapCell>
        {
            [Origin] = new MapCell(Origin, TerrainType.Plain),
        };
        foreach (var c in planned)
        {
            cells[c] = new MapCell(c, TerrainType.Plain);
        }

        foreach (var c in newPath)
        {
            cells[c] = new MapCell(c, TerrainType.Plain);
        }

        var map = new GameMap(cells, MapScale.Small);

        var unit = new UnitState(
            Id: new UnitId(0),
            Owner: Side.Blue,
            TypeKey: "unit.infantry",
            Class: UnitClass.LineHold,
            InitAttack: 1,
            InitDefense: 1,
            Resilience0: 1,
            Attack: 1,
            Defense: 1,
            ResilienceLeft: 1,
            Movement: movement,
            Vision: 1,
            SupportRange: 0,
            Position: Origin,
            Command: command,
            Flags: NightFlags.None);

        var cards = new Dictionary<Side, CardState>
        {
            [Side.Blue] = new CardState(cp, cp, Array.Empty<CardId>(), Array.Empty<CardId>()),
            [Side.Red] = new CardState(0, 0, Array.Empty<CardId>(), Array.Empty<CardId>()),
        };

        return new GameState(
            SchemaVersion: 1,
            Map: map,
            Units: new[] { unit },
            DayIndex: 0,
            Phase: DayNightPhase.Morning,
            Cards: cards,
            RngState: 0UL,
            TurnLog: Array.Empty<TurnRecordEntry>());
    }

    /// <summary>单个采样场景的不可变载体。</summary>
    private sealed record Scenario(
        int Movement,
        IReadOnlyList<HexCoord> Planned,
        IReadOnlyList<HexCoord> NewPath,
        int Cp,
        bool IsAttackPrep);
}
