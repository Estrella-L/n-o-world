using Xjdl.Core.Hex;
using Xjdl.Core.State;

namespace Xjdl.Core.Combat;

/// <summary>
/// 一场经接触合并后的战斗（Req 5.5）：多个相邻进攻格对同一目标格的进攻准备被合并为单场战斗。
/// 见 docs/01-战斗机制.md〈选表〉与 design.md〈CombatResolver〉。
/// </summary>
/// <param name="Target">被进攻的目标格（防守方所在格）。</param>
/// <param name="AttackingCells">
/// 参与本场进攻的各进攻格坐标，按 <c>(Q, R)</c> 字典序稳定排序（Req 2.6）。
/// 当 <see cref="AdvanceOnly"/> 为真（目标格已空）时仍列出，用于推进占领裁定。
/// </param>
/// <param name="MainAttackers">
/// 各进攻格的主攻单位（与 <see cref="AttackingCells"/> 一一对应、同序）。
/// 主攻单位由主攻选择器决定（见 <see cref="ContactBuilder"/> 默认策略）。
/// 当 <see cref="AdvanceOnly"/> 为真时为空。
/// </param>
/// <param name="AttackNumerator">
/// 合并后火力比的分子 = 各进攻格主攻单位进攻战斗力之和（Req 5.5）。
/// 当 <see cref="AdvanceOnly"/> 为真时为 0（不触发战斗）。
/// </param>
/// <param name="AdvanceOnly">
/// 目标格在机动后已空：不触发战斗，进攻方可直接推进占领该格（Req 5.4）。
/// </param>
public sealed record MergedContact(
    HexCoord Target,
    IReadOnlyList<HexCoord> AttackingCells,
    IReadOnlyList<UnitState> MainAttackers,
    int AttackNumerator,
    bool AdvanceOnly);

/// <summary>
/// 阶段 3 接触合并：把多个相邻格对同一目标的进攻准备合并为一场战斗（Req 5.5），
/// 并对目标已空的进攻标记为「直接推进、不触发战斗」（Req 5.4）。
/// </summary>
/// <remarks>
/// <para>
/// 目标信息取自 <see cref="UnitOrder.Target"/>（<see cref="UnitState"/> 本身不携带目标格），
/// 因此本合并以 <c>state.Units</c> 提供单位现状、以 <see cref="UnitOrder"/> 列表提供进攻准备目标。
/// </para>
/// <para>
/// <b>主攻单位选择（与任务 9.15 堆叠一致）：</b>同一进攻格内可有多个进攻准备单位（堆叠），
/// 火力比仅计入该格「主攻单位」（Req 9.2）。任务 9.15 尚未落地正式的主攻指定，
/// 故本类接受一个可选的主攻选择器 <c>mainUnitSelector</c>；未提供时采用确定性默认策略：
/// 取该格进攻准备单位中当前进攻战斗力最高者，并列时取 <see cref="UnitId"/> 最小者。
/// 待 9.15 落地后可传入统一的主攻选择器以保持一致。
/// </para>
/// </remarks>
public static class ContactBuilder
{
    /// <summary>
    /// 依 <c>(Q, R)</c> 字典序对六角格排序，保证遍历与输出的确定性（Req 2.6）。
    /// </summary>
    private static readonly IComparer<HexCoord> HexOrder =
        Comparer<HexCoord>.Create(static (a, b) =>
        {
            var byQ = a.Q.CompareTo(b.Q);
            return byQ != 0 ? byQ : a.R.CompareTo(b.R);
        });

    /// <summary>
    /// 合并全场进攻准备为若干场战斗（Req 5.5），并标记目标已空的直接推进（Req 5.4）。
    /// </summary>
    /// <param name="state">当前 <see cref="GameState"/>，提供各单位现状与位置（机动后）。</param>
    /// <param name="orders">
    /// 本回合单位命令列表，提供每个进攻准备单位的目标格（<see cref="UnitOrder.Target"/>）。
    /// 仅 <see cref="Command.AttackPrep"/> 且 <see cref="UnitOrder.Target"/> 非空的条目参与合并。
    /// </param>
    /// <param name="mainUnitSelector">
    /// 可选的主攻单位选择器：给定同一进攻格内针对同一目标的进攻准备单位（已按 <see cref="UnitId"/> 稳定排序，非空），
    /// 返回该格主攻单位。未提供时采用确定性默认（进攻力最高、并列取 <see cref="UnitId"/> 最小）。
    /// </param>
    /// <returns>
    /// 合并后的战斗列表，按目标格 <c>(Q, R)</c> 字典序稳定排序（Req 2.6）。
    /// 目标格已空者以 <see cref="MergedContact.AdvanceOnly"/> == true 表示（Req 5.4）。
    /// </returns>
    public static IReadOnlyList<MergedContact> Build(
        GameState state,
        IReadOnlyList<UnitOrder> orders,
        Func<IReadOnlyList<UnitState>, UnitState>? mainUnitSelector = null)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(orders);

        var selectMain = mainUnitSelector ?? DefaultMainUnit;

        // 按 id 建立单位索引（确定性查询），并按位置分组以判定目标格是否为空。
        var unitsById = new Dictionary<UnitId, UnitState>();
        var occupied = new HashSet<HexCoord>();
        foreach (var unit in state.Units)
        {
            unitsById[unit.Id] = unit;
            occupied.Add(unit.Position);
        }

        // 收集进攻准备：目标格 → 进攻格 → 该格针对此目标的进攻准备单位集合。
        // 用 SortedDictionary + 稳定排序保证遍历与输出确定（Req 2.6）。
        var byTarget = new SortedDictionary<HexCoord, SortedDictionary<HexCoord, List<UnitState>>>(HexOrder);

        foreach (var order in orders)
        {
            if (order.Command != Command.AttackPrep || order.Target is not { } target)
            {
                continue;
            }

            if (!unitsById.TryGetValue(order.Unit, out var attacker))
            {
                // 命令引用的单位已不在场（如已被移除），跳过。
                continue;
            }

            if (!byTarget.TryGetValue(target, out var byCell))
            {
                byCell = new SortedDictionary<HexCoord, List<UnitState>>(HexOrder);
                byTarget[target] = byCell;
            }

            if (!byCell.TryGetValue(attacker.Position, out var cellUnits))
            {
                cellUnits = new List<UnitState>();
                byCell[attacker.Position] = cellUnits;
            }

            cellUnits.Add(attacker);
        }

        var result = new List<MergedContact>(byTarget.Count);

        foreach (var (target, byCell) in byTarget)
        {
            // 目标格已空：不触发战斗，允许进攻方直接推进（Req 5.4）。
            if (!occupied.Contains(target))
            {
                var advanceCells = byCell.Keys.ToArray();
                result.Add(new MergedContact(
                    Target: target,
                    AttackingCells: advanceCells,
                    MainAttackers: Array.Empty<UnitState>(),
                    AttackNumerator: 0,
                    AdvanceOnly: true));
                continue;
            }

            // 目标格有防守方：合并各进攻格为一场战斗，分子为各格主攻单位攻击力之和（Req 5.5）。
            var attackingCells = new List<HexCoord>(byCell.Count);
            var mainAttackers = new List<UnitState>(byCell.Count);
            var numerator = 0;

            foreach (var (cell, cellUnits) in byCell)
            {
                // 同格候选按 id 稳定排序后交给选择器，保证确定性（Req 2.6、9.2）。
                cellUnits.Sort(static (a, b) => a.Id.Value.CompareTo(b.Id.Value));
                var main = selectMain(cellUnits);

                attackingCells.Add(cell);
                mainAttackers.Add(main);
                numerator += main.Attack;
            }

            result.Add(new MergedContact(
                Target: target,
                AttackingCells: attackingCells,
                MainAttackers: mainAttackers,
                AttackNumerator: numerator,
                AdvanceOnly: false));
        }

        return result;
    }

    /// <summary>
    /// 默认主攻单位策略：取进攻战斗力最高者，并列时取 <see cref="UnitId"/> 最小者（确定性，Req 2.6）。
    /// 候选集合非空且已按 id 稳定排序。
    /// </summary>
    private static UnitState DefaultMainUnit(IReadOnlyList<UnitState> candidates)
    {
        var main = candidates[0];
        for (var i = 1; i < candidates.Count; i++)
        {
            var c = candidates[i];
            if (c.Attack > main.Attack ||
                (c.Attack == main.Attack && c.Id.Value < main.Id.Value))
            {
                main = c;
            }
        }

        return main;
    }
}
