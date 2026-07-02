namespace Xjdl.Core.Doctrine;

/// <summary>
/// 一条专精分支（A/B 二选一，Req 16.5）。每种学说提供两条专精分支，
/// 玩家组队时选定其一与学说叠加。专精承载一组加法修正 <see cref="Modifiers"/>，
/// 其占用的预算点数记于 <see cref="Budget"/>。
/// <para>
/// 一致性约束：<see cref="Budget"/> 必须等于 <see cref="Modifiers"/> 各条
/// <see cref="StatModifier.PointCost"/> 之和；且学说 + 专精的点数合计须恰为 4
/// （Req 16.2、16.5）。两条分支适用同一预算规则，均由
/// <see cref="DoctrineSystem.Validate"/> 校验。
/// </para>
/// 不可变 record，无文案字段（<see cref="Key"/> 仅作 id）。
/// </summary>
/// <param name="Key">专精分支 id，如 "doctrine.armor-assault.spec-a"（无文案）。</param>
/// <param name="Modifiers">该分支的加法修正集合。</param>
/// <param name="Budget">该分支声明的预算点数，须与 <see cref="Modifiers"/> 点数合计一致。</param>
public sealed record Specialization(
    string Key,
    IReadOnlyList<StatModifier> Modifiers,
    int Budget);
