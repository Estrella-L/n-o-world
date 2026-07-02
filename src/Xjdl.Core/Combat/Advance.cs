using Xjdl.Core.Hex;
using Xjdl.Core.State;

namespace Xjdl.Core.Combat;

/// <summary>
/// 阶段 8 推进裁定：冲突格因撤退或歼灭腾空后，由存活的优势方/进攻方可选推进占领
/// （Req 12.4、12.5、12.6、9.5）。
/// 纯函数、确定性、不原地修改输入（Req 2.1、2.7）；仅裁定「谁有资格推进」，
/// 推进本身为可选，故返回资格而不强制移动（Req 12.4）。
/// 撤退/推进以「格」为单位，被推进占领的格须先腾空（Req 9.5）。
/// 见 docs/01-战斗机制.md〈撤退与推进〉与 design.md〈CombatResolver〉Property 42。
/// 实际写回 GameState、堆叠上限校验由回合流水线串接（任务 15.20 / Stacking）。
/// </summary>
public static class Advance
{
    /// <summary>
    /// 裁定一个冲突格的推进资格（Req 12.4、12.5、12.6、9.5）。
    /// 规则：
    /// <list type="bullet">
    /// <item>互-N 致双方同时出局（<paramref name="mutualElimination"/> 为真）时，该冲突格无人占据，
    /// 任何一方都不推进（Req 12.6）。</item>
    /// <item>已被歼灭或因互损同场出局的单位不推进：以「快照剩余韧性 &gt; 0」为存活判据，
    /// 过滤掉出局单位（Req 12.5）。</item>
    /// <item>冲突格须先腾空（<paramref name="cellVacated"/> 为真）方可占领（Req 9.5）。</item>
    /// <item>满足以上条件且优势方/进攻方尚有存活单位时，允许其可选推进占领（Req 12.4）。</item>
    /// </list>
    /// 存活单位按 <see cref="UnitId"/> 稳定排序以保证确定性遍历（Req 2.6）。
    /// </summary>
    /// <param name="contestedCell">被裁定的冲突格坐标。</param>
    /// <param name="advancingSide">拥有推进资格的一方（优势方/进攻方）。</param>
    /// <param name="cellVacated">该冲突格是否已因撤退或歼灭腾空。</param>
    /// <param name="mutualElimination">是否为互-N 致双方同时出局。</param>
    /// <param name="advancingSideUnits">优势方/进攻方参与该场战斗的（结算后）单位快照集合。</param>
    /// <returns>推进裁定结果；<see cref="AdvanceDecision.EligibleUnits"/> 非空当且仅当 <see cref="AdvanceDecision.CanAdvance"/> 为真。</returns>
    public static AdvanceDecision Decide(
        HexCoord contestedCell,
        Side advancingSide,
        bool cellVacated,
        bool mutualElimination,
        IReadOnlyList<UnitState> advancingSideUnits)
    {
        ArgumentNullException.ThrowIfNull(advancingSideUnits);

        // 互-N 双方同时出局：冲突格无人占据（Req 12.6）。
        if (mutualElimination)
        {
            return new AdvanceDecision(false, contestedCell, []);
        }

        // 出局单位不推进：仅保留本方、快照剩余韧性 > 0 的存活单位（Req 12.5）。
        // 按单位 id 稳定排序，保证结算次序无关与遍历确定性（Req 2.6）。
        var survivors = advancingSideUnits
            .Where(u => u.Owner == advancingSide && u.ResilienceLeft > 0)
            .OrderBy(u => u.Id.Value)
            .ToList();

        // 需冲突格已腾空（Req 9.5）且尚有存活单位方可推进；推进为可选（Req 12.4）。
        var canAdvance = cellVacated && survivors.Count > 0;

        return canAdvance
            ? new AdvanceDecision(true, contestedCell, survivors)
            : new AdvanceDecision(false, contestedCell, []);
    }
}

/// <summary>
/// 推进裁定结果（不可变；Req 2.7）。
/// <see cref="CanAdvance"/> 表示优势方/进攻方是否可（可选）推进占领 <see cref="ContestedCell"/>；
/// <see cref="EligibleUnits"/> 为有资格随格整体推进的存活单位，按单位 id 稳定排序。
/// 不变式：<see cref="EligibleUnits"/> 非空当且仅当 <see cref="CanAdvance"/> 为真。
/// </summary>
/// <param name="CanAdvance">是否允许推进（为可选行为，调用方可放弃占领，Req 12.4）。</param>
/// <param name="ContestedCell">被裁定的冲突格坐标。</param>
/// <param name="EligibleUnits">有资格推进占领该格的存活单位集合（以「格」为单位整体推进，Req 9.5）。</param>
public readonly record struct AdvanceDecision(
    bool CanAdvance,
    HexCoord ContestedCell,
    IReadOnlyList<UnitState> EligibleUnits);
