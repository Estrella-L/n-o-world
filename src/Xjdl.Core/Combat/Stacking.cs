using Xjdl.Core.State;

namespace Xjdl.Core.Combat;

/// <summary>
/// 一次「移入某格」的堆叠裁定结果（Req 9.6、13.4）。
/// </summary>
/// <param name="Admitted">
/// 被接纳进入目标格的单位，按稳定 id 升序排列（含原占位单位，见 <see cref="Stacking.AdmitIntoCell"/>）。
/// </param>
/// <param name="Stopped">
/// 因超出堆叠上限而被拒的移入单位，须止步于前一格（Req 9.6）。同样按稳定 id 升序排列。
/// </param>
public readonly record struct StackAdmission(
    IReadOnlyList<UnitState> Admitted,
    IReadOnlyList<UnitState> Stopped);

/// <summary>
/// 堆叠规则（Req 9.1–9.4、9.6）。见 docs/01-战斗机制.md〈堆叠规则〉。
///
/// <para>核心约定：</para>
/// <list type="bullet">
/// <item>每格堆叠单位数受上限约束（默认 <see cref="DefaultStackLimit"/>＝3，可配置，Req 9.1）。</item>
/// <item>结算时每格只指定一个「主攻单位」，仅其攻/防计入火力比，同格随从数值不计入（Req 9.2）。</item>
/// <item>主攻阵亡后从剩余单位中梯次接替，不一次清空整格（Req 9.3、9.4）。</item>
/// <item>友方移入使某格超限时，超出的单位止步于前一格（Req 9.6）。</item>
/// </list>
///
/// <para>
/// <b>主攻单位选取规则（确定性）：</b>取该格进攻战斗力 <see cref="UnitState.Attack"/> 最高者；
/// 若并列，取 <see cref="UnitId"/> 最小者。以稳定 id 作为决胜键，保证结算次序无关与可重放
/// （Req 2.6）。此规则同时约束「多格打一」的分子构成——各进攻格取各自主攻单位攻击力求和
/// （Req 5.5，见 ContactBuilder，任务 9.12 与本规则对齐）。
/// </para>
/// </summary>
public static class Stacking
{
    /// <summary>默认堆叠上限（暂定 3，可由地形/学说等配置覆盖，Req 9.1）。</summary>
    public const int DefaultStackLimit = 3;

    /// <summary>
    /// 从一格的单位集合中确定性地选出主攻单位（Req 9.2）。
    /// 规则：进攻战斗力 <see cref="UnitState.Attack"/> 最高者优先；并列时取 <see cref="UnitId"/> 最小者。
    /// 仅该主攻单位的攻/防计入火力比，同格随从不计入。
    /// </summary>
    /// <param name="unitsInCell">该格当前的全部单位。</param>
    /// <returns>选中的主攻单位；若该格为空则返回 <c>null</c>。</returns>
    public static UnitState? SelectMainUnit(IEnumerable<UnitState> unitsInCell)
    {
        ArgumentNullException.ThrowIfNull(unitsInCell);

        UnitState? main = null;
        foreach (var unit in unitsInCell)
        {
            if (main is null || IsHigherPriority(unit, main))
            {
                main = unit;
            }
        }

        return main;
    }

    /// <summary>
    /// 主攻单位被歼灭后的梯次接替（Req 9.3、9.4）。歼灭仅移除当前主攻，
    /// 若该格仍有剩余单位，则从剩余单位中按同一规则重新指定主攻承受后续战损。
    /// </summary>
    /// <param name="unitsInCell">该格歼灭事件发生前的全部单位（含即将被移除的主攻）。</param>
    /// <param name="annihilatedMain">本次被歼灭的当前主攻单位 id。</param>
    /// <returns>接替的新主攻单位；若移除后该格已空则返回 <c>null</c>。</returns>
    public static UnitState? SucceedMainUnit(
        IEnumerable<UnitState> unitsInCell,
        UnitId annihilatedMain)
    {
        ArgumentNullException.ThrowIfNull(unitsInCell);

        // 仅移除当前主攻，其余单位留在格内梯次接替（不一次清空整格，Req 9.4）。
        var survivors = unitsInCell.Where(u => u.Id != annihilatedMain);
        return SelectMainUnit(survivors);
    }

    /// <summary>
    /// 友方单位移入某格时的堆叠上限裁定（Req 9.6、13.4）。
    /// 原占位单位保留在格内，移入单位按稳定 id 升序依次占用剩余名额；
    /// 超出上限的移入单位被拒，须止步于前一格。
    /// </summary>
    /// <param name="occupants">目标格移入前已有的单位（视为已在格内，全部保留）。</param>
    /// <param name="incoming">本回合尝试移入该格的友方单位。</param>
    /// <param name="stackLimit">堆叠上限，默认 <see cref="DefaultStackLimit"/>。须为正数。</param>
    /// <returns>接纳与止步的划分结果，两组均按稳定 id 升序排列。</returns>
    /// <exception cref="ArgumentOutOfRangeException">当 <paramref name="stackLimit"/> 小于 1。</exception>
    public static StackAdmission AdmitIntoCell(
        IEnumerable<UnitState> occupants,
        IEnumerable<UnitState> incoming,
        int stackLimit = DefaultStackLimit)
    {
        ArgumentNullException.ThrowIfNull(occupants);
        ArgumentNullException.ThrowIfNull(incoming);
        if (stackLimit < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stackLimit), stackLimit, "堆叠上限必须为正数。");
        }

        // 稳定排序：以 UnitId 升序作为确定性遍历键（Req 2.6），杜绝依赖集合迭代顺序。
        var existing = occupants.OrderBy(u => u.Id.Value).ToList();
        var movers = incoming.OrderBy(u => u.Id.Value).ToList();

        var admitted = new List<UnitState>(existing);
        var stopped = new List<UnitState>();

        // 原占位单位已计入名额；移入单位按序占用剩余名额，满则止步（Req 9.6）。
        foreach (var mover in movers)
        {
            if (admitted.Count < stackLimit)
            {
                admitted.Add(mover);
            }
            else
            {
                stopped.Add(mover);
            }
        }

        return new StackAdmission(admitted, stopped);
    }

    /// <summary>
    /// 判断给定单位数是否在堆叠上限之内（Req 9.1）。
    /// </summary>
    /// <param name="unitCount">该格单位数量。</param>
    /// <param name="stackLimit">堆叠上限，默认 <see cref="DefaultStackLimit"/>。</param>
    /// <returns>不超过上限返回 <c>true</c>，否则 <c>false</c>。</returns>
    public static bool IsWithinLimit(int unitCount, int stackLimit = DefaultStackLimit) =>
        unitCount <= stackLimit;

    /// <summary>
    /// 主攻优先级比较：进攻战斗力更高者优先；并列时 <see cref="UnitId"/> 更小者优先。
    /// </summary>
    private static bool IsHigherPriority(UnitState candidate, UnitState current)
    {
        if (candidate.Attack != current.Attack)
        {
            return candidate.Attack > current.Attack;
        }

        // 攻击力并列 → 稳定 id 决胜，取更小者，保证确定性（Req 2.6）。
        return candidate.Id.Value < current.Id.Value;
    }
}
