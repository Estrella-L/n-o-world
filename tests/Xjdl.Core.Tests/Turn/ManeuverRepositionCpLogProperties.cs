using CsCheck;
using Xjdl.Core.Hex;
using Xjdl.Core.Random;
using Xjdl.Core.State;
using Xjdl.Core.Turn;

namespace Xjdl.Core.Tests.Turn;

/// <summary>
/// 机动阶段「临机机动扣指挥点并记入日志」的属性测试（CsCheck，一属性一测试，至少 100 次迭代）。
/// 见 design.md〈Property 15〉与 <see cref="TurnPipeline.ManeuverPhase"/>（Req 4.7/21.6）。
/// <para>
/// 被验证语义（事实来源 TurnPipeline.Maneuver.cs）：一次被接受的临机机动
/// （新路径消耗不超过机动力 <see cref="UnitState.Movement"/> 且该方指挥点 ≥ <c>RepositionCpCost = 1</c>）
/// 会从该单位所属方扣 1 点指挥点，并向 <see cref="GameState.TurnLog"/> 追加一条
/// <c>Kind == "Reposition"</c>、关联该单位、携带触发时点 <see cref="RepositionCommand.TriggerTick"/> 的条目；
/// 反之，若指挥点为 0（不足），则拒绝：不扣点、不记该单位的机动日志。
/// </para>
/// <para>
/// 场景构造为「单方单位、无敌军」，使控制区为空、无接触/遭遇干扰，令指挥点变化与日志追加
/// 纯由临机机动的接受/拒绝决定（唯一改变指挥点的路径即临机机动扣点）。
/// </para>
/// </summary>
// Feature: core-rules-engine, Property 15: 临机机动扣 CP 并记入日志
public class ManeuverRepositionCpLogProperties
{
    /// <summary>移动单位的起点（原地）。路径生成时排除此格以免与起点混淆。</summary>
    private static readonly HexCoord Origin = new(0, 0);

    private const int RepositionCpCost = 1;

    // 有界坐标：q、r ∈ [-6, 6]，覆盖不同长度的路径且不越出地图构造范围。
    private static readonly Gen<HexCoord> GenCoord =
        Gen.Select(Gen.Int[-6, 6], Gen.Int[-6, 6], (q, r) => new HexCoord(q, r));

    // 随机新路径：长度 0..6 的坐标列表，去重且排除起点（消耗即格数，去重让消耗无歧义）。
    private static readonly Gen<IReadOnlyList<HexCoord>> GenNewPath =
        Gen.Int[0, 6].SelectMany(n =>
            GenCoord.List[n].Select(list =>
                (IReadOnlyList<HexCoord>)list
                    .Where(c => c != Origin)
                    .Distinct()
                    .ToList()));

    /// <summary>
    /// Property 15（正向）：被接受的临机机动扣 1 指挥点并记入含触发时点的日志。
    /// 对任意新路径、任意 ≥1 的指挥点结余与任意触发时点：构造预算充足（机动力 ≥ 路径消耗）
    /// 的单方单位并下达该临机机动后，该方 <see cref="CardState.Cp"/> 恰好减少
    /// <c>RepositionCpCost = 1</c>，且 <see cref="GameState.TurnLog"/> 恰含一条
    /// <c>Kind == "Reposition"</c>、<c>Unit == 该单位</c>、<c>TriggerTick == 该临机机动触发时点</c> 的条目。
    /// **Validates: Requirements 4.7, 21.6**
    /// </summary>
    [Fact]
    public void AcceptedReposition_SpendsOneCp_AndAppendsLogWithTriggerTick()
    {
        Gen.Select(GenNewPath, Gen.Int[RepositionCpCost, 20], Gen.Int[0, 100_000])
            .Sample(
                t =>
                {
                    var (newPath, cp, tick) = t;

                    // 机动力充足（≥ 新路径消耗）以确保临机机动被接受。
                    var movement = newPath.Count + 2;
                    var state = BuildSingleUnitState(movement, cp, newPath);

                    // 原计划为原地 Move（空路径），临机机动改走 newPath。
                    var cmds = new TurnCommands(
                        new[] { new UnitOrder(new UnitId(0), Command.Move, Array.Empty<HexCoord>(), null) },
                        new[] { new RepositionCommand(new UnitId(0), newPath, tick) },
                        Array.Empty<CardPlay>());

                    var next = TurnPipeline.ManeuverPhase(state, cmds, new PcgRng(12345UL));

                    // 扣 1 指挥点（Req 4.7）。
                    Assert.Equal(cp - RepositionCpCost, next.Cards[Side.Blue].Cp);

                    // 恰有一条 Reposition 日志，关联该单位且携带触发时点（Req 4.7/21.6）。
                    var repEntries = next.TurnLog
                        .Where(e => e.Kind == "Reposition" && e.Unit == new UnitId(0))
                        .ToArray();
                    Assert.Single(repEntries);
                    Assert.Equal(tick, repEntries[0].TriggerTick);
                },
                iter: 200);
    }

    /// <summary>
    /// Property 15（反向）：指挥点不足（为 0）时拒绝临机机动，不扣点、不记该单位机动日志。
    /// 对任意新路径与触发时点：单方单位所属方 <see cref="CardState.Cp"/> 为 0 时，临机机动被拒绝，
    /// 结算后该方指挥点仍为 0，且 <see cref="GameState.TurnLog"/> 不含关联该单位的 <c>"Reposition"</c> 条目。
    /// **Validates: Requirements 4.7, 21.6**
    /// </summary>
    [Fact]
    public void RejectedReposition_WhenNoCp_LeavesCpUnchanged_AndAddsNoLog()
    {
        Gen.Select(GenNewPath, Gen.Int[0, 100_000])
            .Sample(
                t =>
                {
                    var (newPath, tick) = t;

                    var movement = newPath.Count + 2;
                    var state = BuildSingleUnitState(movement, cp: 0, newPath);

                    var cmds = new TurnCommands(
                        new[] { new UnitOrder(new UnitId(0), Command.Move, Array.Empty<HexCoord>(), null) },
                        new[] { new RepositionCommand(new UnitId(0), newPath, tick) },
                        Array.Empty<CardPlay>());

                    var next = TurnPipeline.ManeuverPhase(state, cmds, new PcgRng(12345UL));

                    // 指挥点不变（仍为 0），且无该单位的 Reposition 日志（Req 4.7/4.8）。
                    Assert.Equal(0, next.Cards[Side.Blue].Cp);
                    Assert.DoesNotContain(
                        next.TurnLog,
                        e => e.Kind == "Reposition" && e.Unit == new UnitId(0));
                },
                iter: 200);
    }

    /// <summary>
    /// 构造仅含一个蓝方单位、无敌军的 <see cref="GameState"/>；蓝方指挥点为 <paramref name="cp"/>。
    /// 地图覆盖起点与新路径全部格；Cards 两方齐备（红方本场景不参与）。
    /// </summary>
    private static GameState BuildSingleUnitState(int movement, int cp, IReadOnlyList<HexCoord> newPath)
    {
        var cells = new Dictionary<HexCoord, MapCell>
        {
            [Origin] = new MapCell(Origin, TerrainType.Plain),
        };
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
            Command: Command.Move,
            Flags: NightFlags.None);

        var cards = new Dictionary<Side, CardState>
        {
            [Side.Blue] = new CardState(cp, 20, Array.Empty<CardId>(), Array.Empty<CardId>()),
            [Side.Red] = new CardState(0, 20, Array.Empty<CardId>(), Array.Empty<CardId>()),
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
}
