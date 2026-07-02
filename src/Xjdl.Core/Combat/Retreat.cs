using Xjdl.Core.Hex;
using Xjdl.Core.State;

namespace Xjdl.Core.Combat;

/// <summary>
/// 一次撤退裁定的结果（阶段 7，Req 12.1、12.2、12.3）。
/// 三种互斥情形：
/// <list type="bullet">
/// <item>成功撤退：<see cref="Destination"/> 有值、<see cref="ExtraCasualty"/> 与 <see cref="Annihilated"/> 均为 <c>false</c>。</item>
/// <item>被包围就地歼灭：<see cref="Destination"/> 为 <c>null</c>、<see cref="ExtraCasualty"/> 为 <c>true</c>（额外承受 1 次战损）、<see cref="Annihilated"/> 为 <c>true</c>。</item>
/// </list>
/// 说明：Req 12.3 描述为「无合法格 → 额外 1 次战损；仍无合法格 → 就地歼灭」两步。
/// 在<b>同一快照</b>下，额外战损作用于撤退单位自身，并不会腾出任何新的合法撤退格，
/// 故合法格集合在两步之间不变——因此本纯函数在无合法格时同时置位两个标志。
/// 若上层流水线（任务 15.20）在两步之间因其他单位撤退/歼灭而改变了占位，
/// 可用新快照重新调用本裁定以体现「仍无合法格」的再判定。
/// </summary>
/// <param name="Destination">选中的撤退目标格；被歼灭时为 <c>null</c>。</param>
/// <param name="ExtraCasualty">是否因无合法撤退格而额外承受 1 次战损（Req 12.3）。</param>
/// <param name="Annihilated">是否就地歼灭（Req 12.3）。</param>
public readonly record struct RetreatOutcome(
    HexCoord? Destination,
    bool ExtraCasualty,
    bool Annihilated)
{
    /// <summary>构造一次成功撤退到 <paramref name="destination"/> 的结果。</summary>
    public static RetreatOutcome ToCell(HexCoord destination) => new(destination, false, false);

    /// <summary>被包围无路可退：额外 1 次战损并就地歼灭（Req 12.3）。</summary>
    public static RetreatOutcome AnnihilatedInPlace { get; } = new(null, true, true);

    /// <summary>是否成功退往某格（未被歼灭）。</summary>
    public bool Retreated => Destination is not null;
}

/// <summary>
/// 撤退目标格选择与撤退合法性裁定（阶段 7，Req 12.1、12.2、12.3）。
/// 见 docs/01-战斗机制.md〈撤退与推进〉、design.md〈CombatResolver · ChooseRetreatCell〉与 Property 41。
///
/// <para><b>合法撤退格</b>（Req 12.1、12.2）须同时满足：</para>
/// <list type="number">
/// <item>为空——不在 <c>occupiedCells</c> 中（涵盖「敌占格」，Req 12.1、12.2）。</item>
/// <item>不是冲突格——不在 <c>conflictCells</c> 中（Req 12.2）。</item>
/// <item>不被敌进攻准备指向——不在 <c>enemyAttackPrepTargets</c> 中（Req 12.2）。</item>
/// <item>不在敌方控制区——不在 <c>enemyZocCells</c> 中（Req 12.2）。</item>
/// <item>不比原位更靠近敌方——到最近敌方的六角距离不小于原位（Req 12.1，与 Property 41 一致）。</item>
/// </list>
///
/// <para><b>确定性择优</b>（Req 12.1）：在多个合法格中「优先己方后方/补给方向」。
/// 以调用方给定的后方/补给参考点 <c>rearReference</c> 为准，依次按
/// ①离参考点更近 → ②离敌方更远 → ③ <see cref="HexCoord.Directions"/> 固定方向序
/// 三级键择优，保证结算次序无关与可重放（Req 2.6）。</para>
///
/// <para><b>输入约定</b>：本类为纯函数、整数运算、不触碰 <see cref="GameState"/>
/// （全量流水线接线见任务 15.20）。调用方以显式集合与委托提供裁定所需的最小上下文，
/// 由上层从快照中预先算好并传入。</para>
/// </summary>
public static class Retreat
{
    /// <summary>
    /// 判定单个候选格对某撤退单位是否为合法撤退格（Req 12.1、12.2）。
    /// </summary>
    /// <param name="origin">撤退单位当前所在格。</param>
    /// <param name="candidate">候选撤退格（通常为 <paramref name="origin"/> 的相邻格）。</param>
    /// <param name="occupiedCells">全部非空格（含敌占/友占）；候选须不在其中方为「空」。</param>
    /// <param name="conflictCells">冲突格集合（Req 12.2）。</param>
    /// <param name="enemyAttackPrepTargets">被敌方进攻准备指向的格集合（Req 12.2）。</param>
    /// <param name="enemyZocCells">敌方控制区格集合（Req 12.2）。</param>
    /// <param name="distanceToNearestEnemy">
    /// 给定格到最近敌方单位的六角距离；若场上无敌方，调用方可返回一个很大的值（如 <see cref="int.MaxValue"/>）。
    /// </param>
    /// <returns>合法返回 <c>true</c>，否则 <c>false</c>。</returns>
    public static bool IsLegalRetreatCell(
        HexCoord origin,
        HexCoord candidate,
        IReadOnlySet<HexCoord> occupiedCells,
        IReadOnlySet<HexCoord> conflictCells,
        IReadOnlySet<HexCoord> enemyAttackPrepTargets,
        IReadOnlySet<HexCoord> enemyZocCells,
        Func<HexCoord, int> distanceToNearestEnemy)
    {
        ArgumentNullException.ThrowIfNull(occupiedCells);
        ArgumentNullException.ThrowIfNull(conflictCells);
        ArgumentNullException.ThrowIfNull(enemyAttackPrepTargets);
        ArgumentNullException.ThrowIfNull(enemyZocCells);
        ArgumentNullException.ThrowIfNull(distanceToNearestEnemy);

        // 必须为空，且不落入任一禁入类别（Req 12.1、12.2）。
        if (occupiedCells.Contains(candidate)
            || conflictCells.Contains(candidate)
            || enemyAttackPrepTargets.Contains(candidate)
            || enemyZocCells.Contains(candidate))
        {
            return false;
        }

        // 不比原位更靠近敌方（Req 12.1 / Property 41）。
        return distanceToNearestEnemy(candidate) >= distanceToNearestEnemy(origin);
    }

    /// <summary>
    /// 枚举某撤退单位的全部合法相邻撤退格（Req 12.1、12.2），
    /// 按 <see cref="HexCoord.Directions"/> 固定方向序返回以保证确定性遍历（Req 2.6）。
    /// </summary>
    /// <returns>合法撤退格列表，可能为空。</returns>
    public static IReadOnlyList<HexCoord> LegalRetreatCells(
        HexCoord origin,
        IReadOnlySet<HexCoord> occupiedCells,
        IReadOnlySet<HexCoord> conflictCells,
        IReadOnlySet<HexCoord> enemyAttackPrepTargets,
        IReadOnlySet<HexCoord> enemyZocCells,
        Func<HexCoord, int> distanceToNearestEnemy)
    {
        var legal = new List<HexCoord>();
        foreach (var candidate in origin.Neighbors())
        {
            if (IsLegalRetreatCell(
                    origin,
                    candidate,
                    occupiedCells,
                    conflictCells,
                    enemyAttackPrepTargets,
                    enemyZocCells,
                    distanceToNearestEnemy))
            {
                legal.Add(candidate);
            }
        }

        return legal;
    }

    /// <summary>
    /// 确定性地选出最佳撤退格（Req 12.1）。在合法格中「优先己方后方/补给方向」，
    /// 依次按 ①离 <paramref name="rearReference"/> 更近 → ②离敌方更远 →
    /// ③ <see cref="HexCoord.Directions"/> 固定方向序 三级键择优。
    /// </summary>
    /// <param name="rearReference">
    /// 己方后方/补给方向的参考点：越靠近它的合法格越优先。为 <c>null</c> 时跳过该级键，
    /// 直接按「离敌方更远 → 固定方向序」择优。
    /// </param>
    /// <returns>最佳撤退格；若无合法格则返回 <c>null</c>。</returns>
    public static HexCoord? ChooseRetreatCell(
        HexCoord origin,
        IReadOnlySet<HexCoord> occupiedCells,
        IReadOnlySet<HexCoord> conflictCells,
        IReadOnlySet<HexCoord> enemyAttackPrepTargets,
        IReadOnlySet<HexCoord> enemyZocCells,
        Func<HexCoord, int> distanceToNearestEnemy,
        HexCoord? rearReference = null)
    {
        var legal = LegalRetreatCells(
            origin,
            occupiedCells,
            conflictCells,
            enemyAttackPrepTargets,
            enemyZocCells,
            distanceToNearestEnemy);

        if (legal.Count == 0)
        {
            return null;
        }

        // legal 已按固定方向序排列，因此「保留先到者」即以方向序为最末级稳定决胜键（Req 2.6）。
        var best = legal[0];
        for (var i = 1; i < legal.Count; i++)
        {
            if (IsBetter(legal[i], best, rearReference, distanceToNearestEnemy))
            {
                best = legal[i];
            }
        }

        return best;
    }

    /// <summary>
    /// 完整撤退裁定（Req 12.1、12.2、12.3）：优先退往最佳合法格；
    /// 若无合法格，则额外承受 1 次战损并就地歼灭（同一快照下合法格集合不变，见 <see cref="RetreatOutcome"/> 说明）。
    /// </summary>
    /// <returns>撤退结果。</returns>
    public static RetreatOutcome Resolve(
        HexCoord origin,
        IReadOnlySet<HexCoord> occupiedCells,
        IReadOnlySet<HexCoord> conflictCells,
        IReadOnlySet<HexCoord> enemyAttackPrepTargets,
        IReadOnlySet<HexCoord> enemyZocCells,
        Func<HexCoord, int> distanceToNearestEnemy,
        HexCoord? rearReference = null)
    {
        var cell = ChooseRetreatCell(
            origin,
            occupiedCells,
            conflictCells,
            enemyAttackPrepTargets,
            enemyZocCells,
            distanceToNearestEnemy,
            rearReference);

        // 无合法格 → 额外 1 次战损；仍无合法格 → 就地歼灭（Req 12.3）。
        return cell is { } destination
            ? RetreatOutcome.ToCell(destination)
            : RetreatOutcome.AnnihilatedInPlace;
    }

    /// <summary>
    /// 择优比较：候选是否严格优于当前最佳。
    /// 级键①离后方参考点更近；级键②离敌方更远；均相等则不替换（由方向序稳定决胜）。
    /// </summary>
    private static bool IsBetter(
        HexCoord candidate,
        HexCoord current,
        HexCoord? rearReference,
        Func<HexCoord, int> distanceToNearestEnemy)
    {
        // 级键①：优先撤向己方后方/补给方向——离参考点更近者优先（Req 12.1）。
        if (rearReference is { } rear)
        {
            var candRear = candidate.DistanceTo(rear);
            var currRear = current.DistanceTo(rear);
            if (candRear != currRear)
            {
                return candRear < currRear;
            }
        }

        // 级键②：离敌方更远者优先。
        var candEnemy = distanceToNearestEnemy(candidate);
        var currEnemy = distanceToNearestEnemy(current);
        if (candEnemy != currEnemy)
        {
            return candEnemy > currEnemy;
        }

        // 级键③：并列 → 保留先到者（方向序），返回 false 不替换（Req 2.6）。
        return false;
    }
}
