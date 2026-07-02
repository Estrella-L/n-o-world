using CsCheck;
using Xjdl.Core.Hex;
using Xjdl.Core.Random;
using Xjdl.Core.State;
using Xjdl.Core.Turn;

namespace Xjdl.Core.Tests.Turn;

/// <summary>
/// 机动阶段「移动消耗按格累加并受机动点限制」的属性测试（CsCheck，一属性一测试，至少 100 次迭代）。
/// 见 design.md〈Property 49〉与 <see cref="TurnPipeline.ManeuverPhase"/>（Req 15.2）。
/// <para>
/// 被验证的移动模型（事实来源 TurnPipeline.Maneuver.cs）：统一消耗模型，每进入一格消耗 1 点
/// （<c>DefaultMoveCostPerCell = 1</c>），预算取整回合机动力 <see cref="UnitState.Movement"/>，
/// 逐格推进，余量不足以进入下一格即止步。构造「单方单位、无敌军」场景使控制区为空、无接触干扰，
/// 令落点纯由机动点预算决定：单位停在路径第 <c>min(Movement, path.Count)</c> 格
/// （0 时保持原地）。
/// </para>
/// </summary>
// Feature: core-rules-engine, Property 49: 移动消耗按格累加并受机动点限制
public class ManeuverMoveCostProperties
{
    /// <summary>移动单位的起点（原地）。路径生成时排除此格以免与起点混淆。</summary>
    private static readonly HexCoord Origin = new(0, 0);

    // 有界坐标：q、r ∈ [-6, 6]，充分覆盖不同长度的路径且不会越出地图构造范围。
    private static readonly Gen<HexCoord> GenCoord =
        Gen.Select(Gen.Int[-6, 6], Gen.Int[-6, 6], (q, r) => new HexCoord(q, r));

    // 随机路径：长度 0..8 的坐标列表，去重且排除起点（统一模型下无需严格相邻，
    // 去重仅为让期望落点无歧义）。
    private static readonly Gen<IReadOnlyList<HexCoord>> GenPath =
        Gen.Int[0, 8].SelectMany(n =>
            GenCoord.List[n].Select(list =>
                (IReadOnlyList<HexCoord>)list
                    .Where(c => c != Origin)
                    .Distinct()
                    .ToList()));

    /// <summary>
    /// Property 49: 移动消耗按格累加并受机动点限制。
    /// 对任意机动点预算 <c>movement ∈ [0, 8]</c> 与任意路径：单方单位（无敌军、无控制区）
    /// 下达 <see cref="Command.Move"/> 后，落点恰为路径第 <c>min(movement, path.Count)</c> 格；
    /// 当该值为 0（机动点为 0 或路径为空）时保持起点不动。
    /// **Validates: Requirements 15.2**
    /// </summary>
    [Fact]
    public void Maneuver_StopsAtFarthestCellWithinMovementBudget()
    {
        Gen.Select(Gen.Int[0, 8], GenPath)
            .Sample(
                t =>
                {
                    var (movement, path) = t;

                    var state = BuildSingleMoverState(movement, path);
                    var cmds = new TurnCommands(
                        new[] { new UnitOrder(new UnitId(0), Command.Move, path, null) },
                        Array.Empty<RepositionCommand>(),
                        Array.Empty<CardPlay>());

                    var next = TurnPipeline.ManeuverPhase(state, cmds, new PcgRng(12345UL));

                    // 期望落点：逐格 1 点消耗、预算 = movement，故推进 min(movement, path.Count) 格。
                    var advanced = Math.Min(movement, path.Count);
                    var expected = advanced == 0 ? Origin : path[advanced - 1];

                    var moved = next.Units.Single(u => u.Id.Value == 0);
                    Assert.Equal(expected, moved.Position);
                },
                iter: 200);
    }

    /// <summary>
    /// 构造仅含一个蓝方移动单位、无敌军的 <see cref="GameState"/>；
    /// 地图覆盖起点与全部路径格，Cards 两方齐备（本场景不消费指挥点）。
    /// </summary>
    private static GameState BuildSingleMoverState(int movement, IReadOnlyList<HexCoord> path)
    {
        var cells = new Dictionary<HexCoord, MapCell>
        {
            [Origin] = new MapCell(Origin, TerrainType.Plain),
        };
        foreach (var c in path)
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
            [Side.Blue] = new CardState(0, 0, Array.Empty<CardId>(), Array.Empty<CardId>()),
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
}
