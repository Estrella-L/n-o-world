using CsCheck;
using Xjdl.Core;
using Xjdl.Core.Combat;
using Xjdl.Core.Hex;
using Xjdl.Core.Random;
using Xjdl.Core.State;
using Xjdl.Core.Tests.Support;
using Xjdl.Core.Turn;

namespace Xjdl.Core.Tests.Turn;

// Feature: core-rules-engine, Property 30: 堆叠上限不变量
/// <summary>
/// Property 30（堆叠上限不变量）的属性测试。见 design.md〈Property 30〉与 docs/01-战斗机制.md〈堆叠规则〉。
/// <para>
/// 语义（事实来源 Req 9.1/9.6/13.4，实现 <see cref="Stacking.AdmitIntoCell"/> 与
/// <see cref="TurnPipeline.ResolveManeuverConflicts"/>）：
/// </para>
/// <list type="bullet">
/// <item>任意合法 <see cref="GameState"/> 经 <see cref="RulesEngine.NextState"/> 后，
/// 每格单位数不超过堆叠上限 <see cref="Stacking.DefaultStackLimit"/>（＝3，Req 9.1）。</item>
/// <item>友方移入使某格超限时，超出的单位止步于前一格（其出发格，Req 9.6/13.4），
/// 记入 <see cref="TurnPipeline.ManeuverConflictResolution.StoppedByStacking"/>。</item>
/// </list>
/// <para>
/// 方法 (A)（主属性，最强可观测量）：以合法状态 + 每单位一条 <see cref="Command.Hold"/> 命令运行整回合，
/// 断言结果每格单位数 ≤ 上限。方法 (B)：直接驱动纯裁定助手，断言超限友方移入不被接纳且止步于出发格。
/// </para>
/// **Validates: Requirements 9.1, 9.6, 13.4**
/// </summary>
public class StackingLimitInvariantProperties
{
    private const int StackLimit = Stacking.DefaultStackLimit;

    /// <summary>
    /// Property 30 (A)：对任意合法 <see cref="GameState"/>，以「每单位恰好一条 <see cref="Command.Hold"/>」命令、
    /// 空临机机动、空技能卡运行整回合 <see cref="RulesEngine.NextState"/> 后，
    /// 结果中每格的单位数均不超过堆叠上限（Req 9.1）。这是「堆叠上限不变量」的最强可观测量。
    /// **Validates: Requirements 9.1**
    /// </summary>
    [Fact]
    public void NextState_NeverLeavesAnyCellOverStackLimit()
    {
        var gen =
            from state in Generators.GameState
            from seed in Gen.ULong
            select (state, seed);

        gen.Sample(
            t =>
            {
                var (state, seed) = t;

                // 每个在场单位恰好一条 Hold 命令（合法输入，Req 3.2）；空临机机动、空技能卡，
                // 使 NextState 不因非法命令抛出，从而聚焦于「结果每格 ≤ 上限」这一不变量。
                var orders = state.Units
                    .Select(u => new UnitOrder(u.Id, Command.Hold, null, null))
                    .ToArray();

                var commands = new TurnCommands(
                    orders,
                    System.Array.Empty<RepositionCommand>(),
                    System.Array.Empty<CardPlay>());

                ISeededRng rng = new PcgRng(seed);

                var next = RulesEngine.NextState(state, commands, rng);

                // 结果按格聚合单位数，断言无一格超限（Req 9.1）。
                var perCell = next.Units
                    .GroupBy(u => u.Position)
                    .Select(g => g.Count());

                foreach (var count in perCell)
                {
                    Assert.True(
                        count <= StackLimit,
                        $"结果某格单位数 {count} 超过堆叠上限 {StackLimit}（Req 9.1）。");
                }
            },
            iter: 100);
    }

    /// <summary>
    /// Property 30 (B)：友方移入使某格超限时，超出的单位止步于前一格（Req 9.6/13.4）。
    /// 构造：目标空格 <c>D</c> 有 <paramref name="occupantCount"/> 个友方原住单位，另有若干友方单位从相异出发格
    /// 移入 <c>D</c>。断言裁定后 <c>D</c> 的占据数恰为 <c>min(总数, 上限)</c>，超出者均记入
    /// <see cref="TurnPipeline.ManeuverConflictResolution.StoppedByStacking"/> 且其最终落点为各自出发格（前一格）。
    /// **Validates: Requirements 9.6, 13.4**
    /// </summary>
    [Fact]
    public void ResolveManeuverConflicts_ExcessFriendlyMovers_StopAtPreviousCell()
    {
        // 原住单位 0..上限；移入单位 1..上限+3，保证足以覆盖「不超限」与「超限止步」两侧。
        var gen =
            from cellQ in Gen.Int[-20, 20]
            from cellR in Gen.Int[-20, 20]
            from occupantCount in Gen.Int[0, StackLimit]
            from moverCount in Gen.Int[1, StackLimit + 3]
            select (cellQ, cellR, occupantCount, moverCount);

        gen.Sample(
            t =>
            {
                var (cellQ, cellR, occupantCount, moverCount) = t;
                var destination = new HexCoord(cellQ, cellR);

                var intents = new List<TurnPipeline.ManeuverIntent>();
                var nextId = 0;

                // 目标格的友方原住单位（据守，不离开）——占用堆叠名额（Req 9.6）。
                for (var i = 0; i < occupantCount; i++)
                {
                    var occupant = BuildUnit(new UnitId(nextId++), Side.Blue, destination);
                    intents.Add(new TurnPipeline.ManeuverIntent(
                        occupant, destination, destination, LeavesOrigin: false));
                }

                // 友方移入单位：各自从相异出发格（均 ≠ D）成功离开并移入 D。
                var moverOrigins = new Dictionary<UnitId, HexCoord>();
                for (var i = 0; i < moverCount; i++)
                {
                    // 出发格确定性偏离目标格且两两互异（沿 q 轴远离），保证均为独立的「前一格」。
                    var origin = new HexCoord(destination.Q + 100 + i, destination.R);
                    var id = new UnitId(nextId++);
                    var mover = BuildUnit(id, Side.Blue, origin);
                    moverOrigins[id] = origin;
                    intents.Add(new TurnPipeline.ManeuverIntent(
                        mover, origin, destination, LeavesOrigin: true));
                }

                var res = TurnPipeline.ResolveManeuverConflicts(intents);

                // 目标格占据数不超过上限，且恰为 min(原住 + 移入, 上限)（Req 9.1/9.6）。
                var expectedAdmitted = System.Math.Min(occupantCount + moverCount, StackLimit);
                var occupants = res.Occupancy.TryGetValue(destination, out var ids)
                    ? ids.Count
                    : 0;
                Assert.Equal(expectedAdmitted, occupants);
                Assert.True(occupants <= StackLimit);

                // 超出上限的移入单位数 == 被止步单位数（Req 9.6/13.4）。
                var expectedStopped = System.Math.Max(
                    0, occupantCount + moverCount - StackLimit);
                Assert.Equal(expectedStopped, res.StoppedByStacking.Count);

                // 每个被止步单位：意图入 D，实际止步于其出发格（前一格），落点回到出发格。
                foreach (var stop in res.StoppedByStacking)
                {
                    Assert.Equal(destination, stop.IntendedCell);
                    Assert.Equal(moverOrigins[stop.Unit], stop.StoppedAt);
                    Assert.Equal(moverOrigins[stop.Unit], res.FinalPositions[stop.Unit]);
                }
            },
            iter: 100);
    }

    /// <summary>构造一个位于 <paramref name="pos"/> 的最小合法单位；裁定纯助手只读取 Id/Owner/Position 等。</summary>
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
