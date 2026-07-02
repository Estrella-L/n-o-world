namespace Xjdl.Core.Combat;

/// <summary>
/// 单个单位在同一回合被多场战斗结果同时要求「退」与「不退/推进」时的冲突消解结果
/// （Req 3.7）。
/// </summary>
public enum ConflictOutcome
{
    /// <summary>无任何位移要求：单位停在原地。</summary>
    StayInPlace,

    /// <summary>被要求据守/推进（不退），且未同时被要求撤退。</summary>
    StayOrAdvance,

    /// <summary>被要求撤退/撤离；只要有撤退要求即以此优先（Req 3.7）。</summary>
    Retreat,
}

/// <summary>
/// 退优先冲突消解（Req 3.7）：当同一单位被不同战斗结果同时要求「退」与「不退/推进」时，
/// 以退/撤离优先处理。见 docs/01-战斗机制.md〈撤退与推进〉、design.md〈Property 29〉。
///
/// <para>本类为纯函数、无副作用、不触碰 <c>GameState</c>（全量流水线接线见任务 15.20）。
/// 决策仅取决于两个布尔输入，故结果确定、可重放（Req 2.6）。</para>
/// </summary>
public static class ConflictResolution
{
    /// <summary>
    /// 消解一个单位的位移冲突（Req 3.7）。撤离优先：只要 <paramref name="mustRetreat"/> 为真，
    /// 无论是否同时被要求不退/推进，结果恒为 <see cref="ConflictOutcome.Retreat"/>；
    /// 仅当 <paramref name="mustRetreat"/> 为假时，才由「不退/推进」要求决定结果。
    /// </summary>
    /// <param name="mustRetreat">是否有战斗结果要求该单位撤退/撤离。</param>
    /// <param name="mustStayOrAdvance">是否有战斗结果要求该单位据守或推进（不退）。</param>
    /// <returns>
    /// <list type="bullet">
    /// <item><see cref="ConflictOutcome.Retreat"/>——当 <paramref name="mustRetreat"/> 为真（退优先，覆盖不退/推进）。</item>
    /// <item><see cref="ConflictOutcome.StayOrAdvance"/>——当 <paramref name="mustRetreat"/> 为假且 <paramref name="mustStayOrAdvance"/> 为真。</item>
    /// <item><see cref="ConflictOutcome.StayInPlace"/>——两者皆为假，无位移要求。</item>
    /// </list>
    /// </returns>
    public static ConflictOutcome Resolve(bool mustRetreat, bool mustStayOrAdvance)
    {
        // 退优先：撤退要求覆盖一切不退/推进要求（Req 3.7）。
        if (mustRetreat)
        {
            return ConflictOutcome.Retreat;
        }

        return mustStayOrAdvance
            ? ConflictOutcome.StayOrAdvance
            : ConflictOutcome.StayInPlace;
    }
}
