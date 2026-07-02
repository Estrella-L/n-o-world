using CsCheck;
using Xjdl.Core.Hex;
using Xjdl.Core.Random;
using Xjdl.Core.State;
using Xjdl.Core.Turn;

namespace Xjdl.Core.Tests.Turn;

/// <summary>
/// 机动阶段「接敌即锁定」的属性测试（CsCheck，一属性一测试，至少 100 次迭代）。
/// 见 design.md〈Property 12〉与 <see cref="TurnPipeline.ManeuverPhase"/>（Req 4.2、4.3）。
/// <para>
/// 事实来源 TurnPipeline.Maneuver.cs：锁定通过向 <see cref="GameState.TurnLog"/> 追加
/// 一条 <c>Kind == "Locked"</c> 的 <see cref="TurnRecordEntry"/> 表达；位置冻结由「单位停在
/// 接敌格」这一落点直接体现（<c>LockedKind</c> 常量为私有，此处断言可观察的字符串 "Locked"
/// 与落点冻结后果）。命令性质（姿态）在落点写回时保持不变（<c>unit with { Position = pos }</c>）。
/// </para>
/// <para>
/// 两个确定性场景族（固定小地图、无非预期接触）：
/// </para>
/// <list type="bullet">
/// <item>(a) 进攻准备单位沿路径首次到达与目标六角距离 1 → 落点相邻且产生 "Locked" 日志。
/// 目标敌军领 <see cref="Command.Move"/> 命令且不移动，故不产生控制区，锁定纯由「进攻准备接敌」触发。</item>
/// <item>(b) 移动单位路径穿越敌方控制区格 → 停在首个控制区格（<see cref="ZoneOfControl.StopAtZoc"/>）
/// 且产生 "Locked" 日志。敌军领 <see cref="Command.Hold"/> 命令，全向产生控制区。</item>
/// </list>
/// </summary>
// Feature: core-rules-engine, Property 12: 接敌即锁定
public class ManeuverLockProperties
{
    /// <summary>移动/进攻准备单位的起点（原地）。</summary>
    private static readonly HexCoord Origin = new(0, 0);

    private static readonly UnitId Mover = new(0);
    private static readonly UnitId Enemy = new(1);

    private const string LockedKind = "Locked";

    // ── 场景 (a) 生成器：进攻准备接敌锁定 ──────────────────────────────────

    // 目标格：与起点六角距离 ∈ [2, 6]，保证开局未相邻（不触发开局锁定），且落在地图构造范围内。
    private static readonly Gen<HexCoord> GenTarget =
        Gen.Select(Gen.Int[-5, 5], Gen.Int[-5, 5], (q, r) => new HexCoord(q, r))
            .Where(t => HexCoord.Distance(Origin, t) is >= 2 and <= 6);

    // 目标的相邻方向下标（0..5）：决定进攻准备单位的落点（与目标距离 1 的接敌格）。
    private static readonly Gen<int> GenNeighborIndex = Gen.Int[0, 5];

    // 机动点预算 ∈ [1, 4]：单格路径下均足以推进到落点（变化仅为增加输入多样性）。
    private static readonly Gen<int> GenMovement = Gen.Int[1, 4];

    // ── 场景 (b) 生成器：移动穿越控制区锁定 ────────────────────────────────

    // 敌方据守单位所在列 m ∈ [2, 6]：其相邻格覆盖水平路径 y=0 上的格，形成可穿越的控制区。
    private static readonly Gen<int> GenBlockColumn = Gen.Int[2, 6];

    // 路径越过控制区后仍延伸的额外格数 ∈ [1, 4]：保证计划终点在控制区之外，从而可验证「提前停下」。
    private static readonly Gen<int> GenExtraLength = Gen.Int[1, 4];

    // 敌方相对路径的行偏移：+1 或 -1，两种几何均使控制区跨越 y=0 路径。
    private static readonly Gen<int> GenRow = Gen.Int[0, 1];

    /// <summary>
    /// Property 12（分支一）：进攻准备单位沿路径首次到达与目标六角距离 1 时被锁定。
    /// 对任意目标位置与接敌方向：进攻准备单位落在与目标相邻的接敌格，回合日志出现该单位的
    /// "Locked" 条目，且命令性质（进攻准备）保持冻结不变。
    /// **Validates: Requirements 4.2**
    /// </summary>
    [Fact]
    public void AttackPrep_ReachingDistanceOne_IsLocked()
    {
        Gen.Select(GenTarget, GenNeighborIndex, GenMovement)
            .Sample(
                t =>
                {
                    var (target, dir, movement) = t;

                    // 落点：目标的第 dir 个相邻格（与目标距离恒为 1）。
                    var contactCell = target.Neighbor(dir);

                    // 排除落点与起点重合（当距离≥2 时不会发生，但显式跳过以防边角）。
                    if (contactCell == Origin)
                    {
                        return;
                    }

                    IReadOnlyList<HexCoord> path = new[] { contactCell };

                    var state = BuildAttackPrepScenario(target, contactCell, movement);
                    var cmds = new TurnCommands(
                        new[] { new UnitOrder(Mover, Command.AttackPrep, path, target) },
                        Array.Empty<RepositionCommand>(),
                        Array.Empty<CardPlay>());

                    var next = TurnPipeline.ManeuverPhase(state, cmds, new PcgRng(20250607UL));

                    var moved = next.Units.Single(u => u.Id == Mover);

                    // 落点冻结：单位停在接敌格，与目标恰好相邻（Req 4.2）。
                    Assert.Equal(contactCell, moved.Position);
                    Assert.Equal(1, HexCoord.Distance(moved.Position, target));

                    // 姿态/命令冻结：命令性质保持进攻准备不变（Req 4.3）。
                    Assert.Equal(Command.AttackPrep, moved.Command);

                    // 可观察锁定信号：回合日志出现该单位的 "Locked" 条目（Req 4.2/4.3）。
                    Assert.Contains(
                        next.TurnLog,
                        e => e.Kind == LockedKind && e.Unit == Mover);
                },
                iter: 200);
    }

    /// <summary>
    /// Property 12（分支二）：移动单位路径穿越敌方控制区时被迫停在首个控制区格并被锁定。
    /// 对任意据守敌军位置与路径长度：移动单位停在其路径上首个进入的敌方控制区格（早于计划终点），
    /// 回合日志出现该单位的 "Locked" 条目，且命令性质（移动）保持冻结不变。
    /// **Validates: Requirements 4.3**
    /// </summary>
    [Fact]
    public void Move_StoppedByEnemyZoc_IsLocked()
    {
        Gen.Select(GenBlockColumn, GenExtraLength, GenRow)
            .Sample(
                t =>
                {
                    var (m, extra, rowSel) = t;
                    var row = rowSel == 0 ? 1 : -1;

                    // 据守敌军位置：与起点距离 = m + 1 ≥ 3，故起点不在其控制区内。
                    var blocker = new HexCoord(m, row);

                    // 水平路径 (1,0)..(L,0)，L = m + extra，越过控制区后仍有格。
                    var length = m + extra;
                    var pathCells = new List<HexCoord>(length);
                    for (var i = 1; i <= length; i++)
                    {
                        pathCells.Add(new HexCoord(i, 0));
                    }

                    IReadOnlyList<HexCoord> path = pathCells;

                    // 期望落点：路径上首个属于敌方控制区（据守敌军 6 邻格）的格。
                    var zoc = new HashSet<HexCoord>(blocker.Neighbors());
                    var expected = pathCells.First(c => zoc.Contains(c));

                    var state = BuildMoveThroughZocScenario(blocker, pathCells);
                    var cmds = new TurnCommands(
                        new[] { new UnitOrder(Mover, Command.Move, path, null) },
                        Array.Empty<RepositionCommand>(),
                        Array.Empty<CardPlay>());

                    var next = TurnPipeline.ManeuverPhase(state, cmds, new PcgRng(20250607UL));

                    var moved = next.Units.Single(u => u.Id == Mover);

                    // 落点冻结：停在首个进入的控制区格，早于计划终点（Req 4.3/11.4）。
                    Assert.Equal(expected, moved.Position);
                    Assert.Equal(1, HexCoord.Distance(moved.Position, blocker));
                    Assert.NotEqual(pathCells[^1], moved.Position);

                    // 姿态/命令冻结：命令性质保持移动不变（Req 4.3）。
                    Assert.Equal(Command.Move, moved.Command);

                    // 可观察锁定信号：回合日志出现该单位的 "Locked" 条目（Req 4.3）。
                    Assert.Contains(
                        next.TurnLog,
                        e => e.Kind == LockedKind && e.Unit == Mover);
                },
                iter: 200);
    }

    /// <summary>
    /// 构造场景 (a)：蓝方进攻准备单位在起点，红方目标敌军在 <paramref name="target"/> 且领
    /// <see cref="Command.Move"/>（不移动、不产生控制区）。地图覆盖起点、落点与目标。
    /// </summary>
    private static GameState BuildAttackPrepScenario(HexCoord target, HexCoord contactCell, int movement)
    {
        var cells = new Dictionary<HexCoord, MapCell>
        {
            [Origin] = new MapCell(Origin, TerrainType.Plain),
            [contactCell] = new MapCell(contactCell, TerrainType.Plain),
            [target] = new MapCell(target, TerrainType.Plain),
        };

        var attacker = MakeUnit(Mover, Side.Blue, Origin, movement, Command.AttackPrep);
        // 目标敌军领移动命令但无路径 → 静止且不产生控制区，避免非预期锁定来源。
        var defender = MakeUnit(Enemy, Side.Red, target, 0, Command.Move);

        return BuildState(cells, attacker, defender);
    }

    /// <summary>
    /// 构造场景 (b)：蓝方移动单位在起点，红方据守敌军在 <paramref name="blocker"/>（全向产生控制区）。
    /// 地图覆盖起点、全部路径格与敌军位置。
    /// </summary>
    private static GameState BuildMoveThroughZocScenario(HexCoord blocker, IReadOnlyList<HexCoord> pathCells)
    {
        var cells = new Dictionary<HexCoord, MapCell>
        {
            [Origin] = new MapCell(Origin, TerrainType.Plain),
            [blocker] = new MapCell(blocker, TerrainType.Plain),
        };
        foreach (var c in pathCells)
        {
            cells[c] = new MapCell(c, TerrainType.Plain);
        }

        // 机动点预算取整条路径长度：足以推进到控制区截断后的末格。
        var mover = MakeUnit(Mover, Side.Blue, Origin, pathCells.Count, Command.Move);
        var holder = MakeUnit(Enemy, Side.Red, blocker, 0, Command.Hold);

        return BuildState(cells, mover, holder);
    }

    private static UnitState MakeUnit(UnitId id, Side owner, HexCoord pos, int movement, Command command) =>
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
            Movement: movement,
            Vision: 1,
            SupportRange: 0,
            Position: pos,
            Command: command,
            Flags: NightFlags.None);

    private static GameState BuildState(
        IReadOnlyDictionary<HexCoord, MapCell> cells,
        params UnitState[] units)
    {
        var map = new GameMap(cells, MapScale.Small);
        var cards = new Dictionary<Side, CardState>
        {
            [Side.Blue] = new CardState(0, 0, Array.Empty<CardId>(), Array.Empty<CardId>()),
            [Side.Red] = new CardState(0, 0, Array.Empty<CardId>(), Array.Empty<CardId>()),
        };

        return new GameState(
            SchemaVersion: 1,
            Map: map,
            Units: units,
            DayIndex: 0,
            Phase: DayNightPhase.Morning,
            Cards: cards,
            RngState: 0UL,
            TurnLog: Array.Empty<TurnRecordEntry>());
    }
}
