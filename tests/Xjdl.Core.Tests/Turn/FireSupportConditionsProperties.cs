using System.Collections.Generic;
using CsCheck;
using Xjdl.Core.Combat;
using Xjdl.Core.Hex;
using Xjdl.Core.Modifiers;
using Xjdl.Core.State;
using Xjdl.Core.Turn;

namespace Xjdl.Core.Tests.Turn;

// Feature: core-rules-engine, Property 34: 单支援移一档且仅在合法条件下
/// <summary>
/// Property 34（单支援移一档且仅在合法条件下）的属性测试。见 design.md〈Property 34〉与 docs/01〈火力支援〉。
/// 覆盖 <see cref="TurnPipeline.ResolveFireSupport"/> 的单支援移档判定（Req 10.1、10.3、10.4）：
/// 一个火力支援单位当且仅当 <em>同时</em> 满足「在支援范围内、本回合未交战、且目标战斗格处于己方视野内」时，
/// 贡献恰好一次 <c>+1</c> 档、来源 <see cref="ModifierSource.Support"/> 的移档（Req 10.1）；
/// 若其自身处于交战中（Req 10.3）或目标格不在己方视野内（Req 10.4），则其贡献为 0（结果为空）。
/// <para>
/// 通过三个独立布尔维度（在范围/交战/目标可见）的正交组合，把三条合法性条件隔离：
/// 支援单位视野置 0 且置于距目标 &gt;= 2 处，使其自身从不为目标格提供视野——从而目标可见与否
/// 完全由一个独立的己方观察单位控制，与支援单位的位置/交战状态解耦。
/// </para>
/// </summary>
public class FireSupportConditionsProperties
{
    /// <summary>被支援方（该场进攻方 / 支援单位所属方）。</summary>
    private const Side Supporter = Side.Blue;

    /// <summary>火力支援单位的支援射程。</summary>
    private const int SupportRange = 3;

    /// <summary>战斗目标格（防守方所在格）。</summary>
    private static readonly HexCoord Target = new(0, 0);

    /// <summary>唯一进攻格（与目标相邻）。</summary>
    private static readonly HexCoord AttackCell = new(1, 0);

    /// <summary>支援单位「在范围内」时的回合末位置：距目标 2（&lt;= SupportRange），但视野 0 故不自见目标。</summary>
    private static readonly HexCoord InRangePosition = new(2, 0);

    /// <summary>支援单位「出范围」时的回合末位置：距目标与进攻格均 &gt; SupportRange。</summary>
    private static readonly HexCoord OutOfRangePosition = new(50, 0);

    /// <summary>观察单位「使目标可见」时的位置：正落在目标格上（vision 1 覆盖目标）。</summary>
    private static readonly HexCoord VisibleObserverPosition = Target;

    /// <summary>观察单位「使目标不可见」时的位置：远离目标，vision 1 无法覆盖。</summary>
    private static readonly HexCoord HiddenObserverPosition = new(60, 0);

    /// <summary>
    /// Property 34: 单支援移一档且仅在合法条件下。
    /// <para>
    /// 对「在范围 × 交战 × 目标可见」三布尔维度的全部正交组合采样：断言
    /// <see cref="TurnPipeline.ResolveFireSupport"/> 返回恰好一次 <c>+1</c>/<see cref="ModifierSource.Support"/>
    /// 移档 <b>当且仅当</b>（在支援范围内 AND 本回合未交战 AND 目标格在己方视野内）；
    /// 其余任一条件不满足时结果为空（贡献 0）。
    /// </para>
    /// **Validates: Requirements 10.1, 10.3, 10.4**
    /// </summary>
    [Fact]
    public void ResolveFireSupport_ShiftsOnceIffAllLegalConditionsHold()
    {
        var gen =
            from inRange in Gen.Bool
            from engaged in Gen.Bool
            from targetVisible in Gen.Bool
            select (inRange, engaged, targetVisible);

        gen.Sample(
            t =>
            {
                // 支援单位：视野 0，故永不为目标格提供视野；位置决定是否在范围内。
                var support = MakeUnit(
                    id: 0,
                    cls: UnitClass.FireSupport,
                    position: t.inRange ? InRangePosition : OutOfRangePosition,
                    vision: 0,
                    supportRange: SupportRange);

                // 观察单位（抗线，不贡献移档）：唯一决定目标格是否在己方视野内。
                var observer = MakeUnit(
                    id: 1,
                    cls: UnitClass.LineHold,
                    position: t.targetVisible ? VisibleObserverPosition : HiddenObserverPosition,
                    vision: 1,
                    supportRange: 0);

                var state = MakeState(new[] { support, observer });
                var battle = MakeBattle(Target, AttackCell);

                var engaged = t.engaged
                    ? new HashSet<UnitId> { support.Id }
                    : new HashSet<UnitId>();

                var shifts = TurnPipeline.ResolveFireSupport(state, battle, Supporter, engaged);

                var eligible = t.inRange && !t.engaged && t.targetVisible;

                if (eligible)
                {
                    // 三条合法性条件全满足：恰好一次 +1 档、来源 Support（Req 10.1）。
                    Assert.Single(shifts);
                    Assert.Equal(new ColumnShift(1, ModifierSource.Support), shifts[0]);
                }
                else
                {
                    // 出范围 / 自身交战（Req 10.3）/ 目标不在视野（Req 10.4）任一 → 贡献 0。
                    Assert.Empty(shifts);
                }
            },
            iter: 200);
    }

    /// <summary>构造一场真实战斗（<c>AdvanceOnly=false</c>）：目标格 + 单一进攻格；攻方数值不影响本判定。</summary>
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

    /// <summary>构造承载给定单位集合的最小 <see cref="GameState"/>（地图/卡牌仅为占位，本判定不依赖它们）。</summary>
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
