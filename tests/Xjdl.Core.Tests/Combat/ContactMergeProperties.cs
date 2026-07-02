using CsCheck;
using Xjdl.Core.Combat;
using Xjdl.Core.Hex;
using Xjdl.Core.State;

namespace Xjdl.Core.Tests.Combat;

// Feature: core-rules-engine, Property 18: 多格打一合并火力
public class ContactMergeProperties
{
    /// <summary>进攻战斗力取值范围：正整数且较小，以制造并列（考验按 UnitId 的并列裁决）。</summary>
    private static readonly Gen<int> GenAttack = Gen.Int[1, 10];

    /// <summary>有界坐标，避免与目标格及彼此过度稀疏。</summary>
    private static readonly Gen<HexCoord> GenCoord =
        from q in Gen.Int[-5, 5]
        from r in Gen.Int[-5, 5]
        select new HexCoord(q, r);

    /// <summary>单个进攻格内的进攻准备单位（堆叠）的进攻力序列：1..3 个。</summary>
    private static readonly Gen<int[]> GenCellAttacks = GenAttack.Array[1, 3];

    /// <summary>
    /// 一个「多格打一」场景：一个防守方位于目标格，若干相邻进攻格各含一个或多个
    /// 进攻准备单位，全部指向该目标格。
    /// </summary>
    private sealed record Scenario(
        GameState State,
        IReadOnlyList<UnitOrder> Orders,
        HexCoord Target,
        int ExpectedNumerator);

    private static readonly Gen<Scenario> GenScenario =
        from target in GenCoord
        from rawCells in GenCoord.Array[1, 6]
        from cellAttacks in GenCellAttacks.Array[1, 6]
        select BuildScenario(target, rawCells, cellAttacks);

    /// <summary>
    /// Property 18: 多格打一合并火力。
    /// 对任意多个相邻进攻格对同一目标格的进攻准备，接触合并后该目标的
    /// <see cref="MergedContact.AttackNumerator"/> 恰等于各进攻格「主攻单位」进攻战斗力之和
    /// （主攻单位默认策略：进攻力最高、并列取 <see cref="UnitId"/> 最小；Req 5.5）。
    /// **Validates: Requirements 5.5**
    /// </summary>
    [Fact]
    public void MergedFirePower_EqualsSumOfPerCellMainAttack()
    {
        GenScenario.Sample(scenario =>
        {
            var contacts = ContactBuilder.Build(scenario.State, scenario.Orders);

            var contact = contacts.FirstOrDefault(c => c.Target == scenario.Target);
            if (contact is null)
            {
                return false;
            }

            // 目标格有防守方：应为一场真正的战斗（非直接推进）。
            if (contact.AdvanceOnly)
            {
                return false;
            }

            return contact.AttackNumerator == scenario.ExpectedNumerator;
        }, iter: 200);
    }

    private static Scenario BuildScenario(HexCoord target, HexCoord[] rawCells, int[][] cellAttacks)
    {
        // 进攻格：去重并排除目标格；若为空则退化为目标格的一个相邻格。
        var cells = rawCells
            .Distinct()
            .Where(c => c != target)
            .ToList();

        if (cells.Count == 0)
        {
            cells.Add(new HexCoord(target.Q + 1, target.R));
        }

        var cellCount = Math.Min(cells.Count, cellAttacks.Length);

        var units = new List<UnitState>();
        var orders = new List<UnitOrder>();
        var nextId = 0;

        // 防守方占据目标格（据守，不参与进攻合并）。
        units.Add(MakeUnit(nextId++, Side.Red, target, attack: 5, Command.Hold));

        // 各进攻格的进攻准备单位（同格可堆叠多个），全部指向目标格。
        for (var i = 0; i < cellCount; i++)
        {
            var cell = cells[i];
            foreach (var attack in cellAttacks[i])
            {
                var unit = MakeUnit(nextId++, Side.Blue, cell, attack, Command.AttackPrep);
                units.Add(unit);
                orders.Add(new UnitOrder(unit.Id, Command.AttackPrep, Path: null, Target: target));
            }
        }

        // 独立复算期望分子：每个进攻格取主攻单位（攻击力最高、并列取最小 UnitId），求和。
        var expected = units
            .Where(u => u.Command == Command.AttackPrep)
            .GroupBy(u => u.Position)
            .Sum(group => group
                .OrderByDescending(u => u.Attack)
                .ThenBy(u => u.Id.Value)
                .First()
                .Attack);

        var state = BuildState(units);
        return new Scenario(state, orders, target, expected);
    }

    private static UnitState MakeUnit(int id, Side owner, HexCoord pos, int attack, Command command) =>
        new(
            new UnitId(id),
            owner,
            "unit.test",
            UnitClass.LineHold,
            attack, // InitAttack
            attack, // InitDefense
            1,      // Resilience0
            attack, // Attack
            attack, // Defense
            1,      // ResilienceLeft
            1,      // Movement
            1,      // Vision
            0,      // SupportRange
            pos,
            command,
            NightFlags.None);

    private static GameState BuildState(IReadOnlyList<UnitState> units)
    {
        var cells = new Dictionary<HexCoord, MapCell>();
        foreach (var unit in units)
        {
            cells[unit.Position] = new MapCell(unit.Position, TerrainType.Plain);
        }

        var map = new GameMap(cells, MapScale.Medium);

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
