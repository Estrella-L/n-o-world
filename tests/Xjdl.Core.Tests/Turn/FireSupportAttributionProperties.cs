using System.Collections.Generic;
using CsCheck;
using Xjdl.Core.Combat;
using Xjdl.Core.Hex;
using Xjdl.Core.Modifiers;
using Xjdl.Core.State;
using Xjdl.Core.Turn;

namespace Xjdl.Core.Tests.Turn;

// Feature: core-rules-engine, Property 16: 支援归属按回合末位置
/// <summary>
/// Property 16（支援归属按回合末位置）的属性测试。见 design.md〈Property 16〉与 docs/01〈火力支援〉。
/// 覆盖 <see cref="TurnPipeline.ResolveFireSupport"/> 的归属判定（Req 4.9）：
/// 一个火力支援单位支援哪场战斗，完全由其 <em>回合末位置</em> 决定——
/// 当其回合末位置落在某场战斗支援范围内时，该场获得一次 <c>+1</c> 档、来源
/// <see cref="ModifierSource.Support"/> 的移档；范围外的战斗则得不到该单位的支援。
/// </summary>
public class FireSupportAttributionProperties
{
    /// <summary>被支援方（两场战斗的进攻方）。</summary>
    private const Side Supporter = Side.Blue;

    /// <summary>火力支援单位的支援射程（回合末位置到战斗任一格的最小六角距离阈值）。</summary>
    private const int SupportRange = 3;

    /// <summary>战斗 A 的目标格。</summary>
    private static readonly HexCoord TargetA = new(0, 0);

    /// <summary>战斗 A 的唯一进攻格（与目标相邻）。</summary>
    private static readonly HexCoord AttackCellA = new(1, 0);

    /// <summary>战斗 B 的目标格：与战斗 A 相距甚远，远超 2×SupportRange，保证「在一场范围内 ⇒ 必在另一场范围外」。</summary>
    private static readonly HexCoord TargetB = new(30, 0);

    /// <summary>战斗 B 的唯一进攻格（与目标相邻）。</summary>
    private static readonly HexCoord AttackCellB = new(31, 0);

    /// <summary>
    /// Property 16: 支援归属按回合末位置。
    /// <para>
    /// 构造两场相距甚远的战斗 A、B（直接构造 <see cref="MergedContact"/>，均为真实战斗 <c>AdvanceOnly=false</c>），
    /// 并在两场战斗的目标格上各放置一个己方观察单位，使两场目标格「恒在己方视野内」——
    /// 从而把归属的唯一决定因素隔离为「支援单位的回合末位置是否落在该场支援范围内」（Req 10.4 恒满足，判定仅由 Req 4.9/10.1 主导）。
    /// </para>
    /// <para>
    /// 每轮迭代确定性地把火力支援单位的回合末位置放到 A 或 B 之一的邻域内（沿任意六角方向移动 0..SupportRange 步）：
    /// 被覆盖的一场恰好获得一次 <c>+1</c>/<see cref="ModifierSource.Support"/> 移档，另一场为空。
    /// 由于两目标间距 &gt; 2×SupportRange，覆盖一场必然使另一场出范围，归属随位置在 A/B 之间切换。
    /// </para>
    /// **Validates: Requirements 4.9**
    /// </summary>
    [Fact]
    public void ResolveFireSupport_AttributesBattleByEndOfTurnPosition()
    {
        var gen =
            from coverA in Gen.Bool          // 本轮支援单位覆盖 A（true）还是 B（false）
            from dir in Gen.Int[0, 5]        // 在覆盖目标邻域内的移动方向
            from steps in Gen.Int[0, SupportRange] // 移动步数（0..R，保证仍在范围内）
            select (coverA, dir, steps);

        gen.Sample(
            t =>
            {
                var baseTarget = t.coverA ? TargetA : TargetB;
                var endPosition = Step(baseTarget, t.dir, t.steps);

                var support = MakeUnit(
                    id: 0,
                    cls: UnitClass.FireSupport,
                    position: endPosition,
                    vision: 1,
                    supportRange: SupportRange);

                // 两场目标格各放一个己方观察单位，使两场目标恒在己方视野内（隔离 Req 10.4）。
                var observerA = MakeUnit(id: 1, cls: UnitClass.LineHold, position: TargetA, vision: 1, supportRange: 0);
                var observerB = MakeUnit(id: 2, cls: UnitClass.LineHold, position: TargetB, vision: 1, supportRange: 0);

                var state = MakeState(new[] { support, observerA, observerB });

                var battleA = MakeBattle(TargetA, AttackCellA);
                var battleB = MakeBattle(TargetB, AttackCellB);

                var engaged = new HashSet<UnitId>();

                var shiftsA = TurnPipeline.ResolveFireSupport(state, battleA, Supporter, engaged);
                var shiftsB = TurnPipeline.ResolveFireSupport(state, battleB, Supporter, engaged);

                var covered = t.coverA ? shiftsA : shiftsB;
                var uncovered = t.coverA ? shiftsB : shiftsA;

                // 被回合末位置覆盖的一场：恰好一次 +1 档、来源 Support（Req 4.9/10.1）。
                Assert.Single(covered);
                Assert.Equal(new ColumnShift(1, ModifierSource.Support), covered[0]);

                // 未被覆盖的一场：无该支援单位贡献（归属完全随回合末位置切换，Req 4.9）。
                Assert.Empty(uncovered);
            },
            iter: 200);
    }

    /// <summary>沿固定六角方向 <paramref name="dir"/> 走 <paramref name="steps"/> 步，得到距 <paramref name="origin"/> 恰为 steps 的格。</summary>
    private static HexCoord Step(HexCoord origin, int dir, int steps)
    {
        var d = HexCoord.Directions[dir];
        return new HexCoord(origin.Q + (d.Q * steps), origin.R + (d.R * steps));
    }

    /// <summary>构造一场真实战斗（<c>AdvanceOnly=false</c>）：目标格 + 单一进攻格，攻方数值不影响归属判定。</summary>
    private static MergedContact MakeBattle(HexCoord target, HexCoord attackCell)
    {
        var attacker = MakeUnit(id: 99, cls: UnitClass.LineHold, position: attackCell, vision: 1, supportRange: 0);
        return new MergedContact(
            Target: target,
            AttackingCells: new[] { attackCell },
            MainAttackers: new[] { attacker },
            AttackNumerator: attacker.Attack,
            AdvanceOnly: false);
    }

    /// <summary>构造一个满足整除不变量的己方单位（Owner=Blue），仅设置本测试关心的字段。</summary>
    private static UnitState MakeUnit(int id, UnitClass cls, HexCoord position, int vision, int supportRange) =>
        new(
            Id: new UnitId(id),
            Owner: Supporter,
            TypeKey: "unit.test",
            Class: cls,
            InitAttack: 4,
            InitDefense: 4,
            Resilience0: 4,
            Attack: 4,
            Defense: 4,
            ResilienceLeft: 4,
            Movement: 4,
            Vision: vision,
            SupportRange: supportRange,
            Position: position,
            Command: Command.Hold,
            Flags: NightFlags.None);

    /// <summary>构造承载给定单位集合的最小 <see cref="GameState"/>（地图/卡牌仅为占位，归属判定不依赖它们）。</summary>
    private static GameState MakeState(IReadOnlyList<UnitState> units)
    {
        var origin = new HexCoord(0, 0);
        var cells = new Dictionary<HexCoord, MapCell>
        {
            [origin] = new MapCell(origin, TerrainType.Plain),
        };
        var map = new GameMap(cells, MapScale.Small);

        var cards = new Dictionary<Side, CardState>
        {
            [Side.Blue] = new CardState(0, 0, System.Array.Empty<CardId>(), System.Array.Empty<CardId>()),
            [Side.Red] = new CardState(0, 0, System.Array.Empty<CardId>(), System.Array.Empty<CardId>()),
        };

        return new GameState(
            SchemaVersion: 1,
            Map: map,
            Units: units,
            DayIndex: 0,
            Phase: DayNightPhase.Morning,
            Cards: cards,
            RngState: 0UL,
            TurnLog: System.Array.Empty<TurnRecordEntry>());
    }
}
