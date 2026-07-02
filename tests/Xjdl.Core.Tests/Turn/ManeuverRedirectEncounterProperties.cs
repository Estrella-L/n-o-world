using CsCheck;
using Xjdl.Core.Hex;
using Xjdl.Core.Random;
using Xjdl.Core.State;
using Xjdl.Core.Turn;

namespace Xjdl.Core.Tests.Turn;

/// <summary>
/// 机动阶段「被改向撞敌打成遭遇战」的属性测试（CsCheck，一属性一测试，至少 100 次迭代）。
/// 见 design.md〈Property 14〉与 <see cref="TurnPipeline.ManeuverPhase"/>（Req 4.6）。
/// <para>
/// 语义（事实来源 TurnPipeline.Maneuver.cs）：当一个单位的临机机动被接受（指挥点足够、
/// 路径消耗不超剩余机动力），且其被改向后的落点与敌方单位六角相邻（<c>EnemyWithin</c> 半径 1）时，
/// 该接触按遭遇战（表三）处理——向 <see cref="GameState.TurnLog"/> 追加一条
/// <c>Kind == "Encounter"</c>（<c>EncounterKind</c> 常量）的条目，而非按预设进攻处理。
/// </para>
/// <para>
/// 构造方式：一个蓝方移动单位，其临机机动新路径为单格 <c>[L]</c>，其中 <c>L</c> 恰为某红方单位
/// 快照位置 <c>E</c> 的一个相邻格（六角距离 1，非同格）。红方单位领 <see cref="Command.Move"/> 命令
/// 故不产生控制区（Req 11.3），蓝方路径不被截断；蓝方机动力≥1 且指挥点≥1 保证临机机动被接受
/// 且落点恰为 <c>L</c>。断言该单位在回合日志中存在 <c>Kind == "Encounter"</c> 的条目。
/// </para>
/// </summary>
// Feature: core-rules-engine, Property 14: 被改向撞敌打成遭遇战
public class ManeuverRedirectEncounterProperties
{
    /// <summary>蓝方移动单位起点：远离敌军区域（[-6,6]）以免意外相邻造成同格接触干扰。</summary>
    private static readonly HexCoord Origin = new(20, 20);

    private static readonly UnitId Mover = new(0);
    private static readonly UnitId Enemy = new(1);

    // 敌方快照位置：q、r ∈ [-6, 6]。
    private static readonly Gen<HexCoord> GenEnemyPos =
        Gen.Select(Gen.Int[-6, 6], Gen.Int[-6, 6], (q, r) => new HexCoord(q, r));

    /// <summary>
    /// Property 14: 被改向撞敌打成遭遇战。
    /// 对任意敌方位置 <c>E</c>、任意方向 <c>dir ∈ [0,5]</c> 与机动力 <c>movement ∈ [1,6]</c>：
    /// 蓝方单位的临机机动新路径落点 <c>L = E.Neighbor(dir)</c>（与敌相邻）被接受后，
    /// 回合日志必含该单位的 <c>Kind == "Encounter"</c> 条目（遭遇战，表三），而非预设进攻。
    /// **Validates: Requirements 4.6**
    /// </summary>
    [Fact]
    public void RepositionedUnitCollidingWithEnemyIsResolvedAsEncounter()
    {
        Gen.Select(GenEnemyPos, Gen.Int[0, 5], Gen.Int[1, 6])
            .Sample(
                t =>
                {
                    var (enemyPos, dir, movement) = t;

                    // 落点 L：敌方位置的一个相邻格，六角距离恰为 1（不与敌同格）。
                    var landing = enemyPos.Neighbor(dir);

                    var state = BuildState(enemyPos, movement);

                    // 临机机动新路径 = 单格 [L]；消耗 1 ≤ movement，指挥点充足 → 被接受。
                    var cmds = new TurnCommands(
                        new[] { new UnitOrder(Mover, Command.Move, Array.Empty<HexCoord>(), null) },
                        new[] { new RepositionCommand(Mover, new[] { landing }, TriggerTick: 3) },
                        Array.Empty<CardPlay>());

                    var next = TurnPipeline.ManeuverPhase(state, cmds, new PcgRng(777UL));

                    // 前置校验：落点确实与敌相邻（构造正确性），且落点即临机机动终点。
                    Assert.Equal(1, HexCoord.Distance(landing, enemyPos));
                    Assert.Equal(landing, next.Units.Single(u => u.Id == Mover).Position);

                    // 核心断言：该单位在回合日志中存在遭遇战条目（可观测信号 Kind == "Encounter"）。
                    Assert.Contains(
                        next.TurnLog,
                        e => e.Kind == "Encounter" && e.Unit == Mover);
                },
                iter: 200);
    }

    /// <summary>
    /// 构造「蓝方移动单位 + 红方（Move，故不产生控制区）敌军」的 <see cref="GameState"/>。
    /// 地图覆盖起点、落点与敌方位置及其相邻格；两方指挥点齐备（蓝方≥1 以接受临机机动）。
    /// </summary>
    private static GameState BuildState(HexCoord enemyPos, int movement)
    {
        var cells = new Dictionary<HexCoord, MapCell>
        {
            [Origin] = new MapCell(Origin, TerrainType.Plain),
            [enemyPos] = new MapCell(enemyPos, TerrainType.Plain),
        };
        foreach (var n in enemyPos.Neighbors())
        {
            cells[n] = new MapCell(n, TerrainType.Plain);
        }

        var map = new GameMap(cells, MapScale.Small);

        var mover = new UnitState(
            Id: Mover,
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
            Command: Command.Move,
            Flags: NightFlags.None);

        // 敌军领 Move 命令 → 不产生控制区（Req 11.3），使蓝方路径不被截断；本回合不给其路径故原地不动。
        var enemy = new UnitState(
            Id: Enemy,
            Owner: Side.Red,
            TypeKey: "unit.infantry",
            Class: UnitClass.LineHold,
            InitAttack: 1,
            InitDefense: 1,
            Resilience0: 1,
            Attack: 1,
            Defense: 1,
            ResilienceLeft: 1,
            Movement: 0,
            Vision: 1,
            SupportRange: 0,
            Position: enemyPos,
            Command: Command.Move,
            Flags: NightFlags.None);

        var cards = new Dictionary<Side, CardState>
        {
            [Side.Blue] = new CardState(5, 0, Array.Empty<CardId>(), Array.Empty<CardId>()),
            [Side.Red] = new CardState(0, 0, Array.Empty<CardId>(), Array.Empty<CardId>()),
        };

        return new GameState(
            SchemaVersion: 1,
            Map: map,
            Units: new[] { mover, enemy },
            DayIndex: 0,
            Phase: DayNightPhase.Morning,
            Cards: cards,
            RngState: 0UL,
            TurnLog: Array.Empty<TurnRecordEntry>());
    }
}
