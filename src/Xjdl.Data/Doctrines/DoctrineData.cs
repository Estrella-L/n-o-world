using Xjdl.Core.Doctrine;
using Xjdl.Core.State;
using CoreDoctrine = Xjdl.Core.Doctrine.Doctrine;

namespace Xjdl.Data.Doctrines;

/// <summary>
/// 一条学说修正的可加载配置（JSON 友好 DTO，Req 16.1）。映射到核心
/// <see cref="StatModifier"/>。
/// </summary>
/// <param name="Stat">被修正的属性维度。</param>
/// <param name="Value">对该属性的加法增量（正增益、负削弱）。</param>
/// <param name="PointCost">该修正占用的预算点数。</param>
public sealed record StatModifierData(StatKind Stat, int Value, int PointCost)
{
    /// <summary>映射为核心 <see cref="StatModifier"/>。</summary>
    public StatModifier ToModel() => new(Stat, Value, PointCost);
}

/// <summary>
/// 一条专精分支（A/B）的可加载配置（JSON 友好 DTO，Req 16.5）。映射到核心
/// <see cref="Specialization"/>。
/// </summary>
/// <param name="Key">专精分支 id（无文案）。</param>
/// <param name="Modifiers">该分支的加法修正集合。</param>
/// <param name="Budget">该分支声明的预算点数，须与修正点数合计一致（由 DoctrineSystem 校验）。</param>
public sealed record SpecializationData(
    string Key,
    IReadOnlyList<StatModifierData>? Modifiers,
    int Budget)
{
    /// <summary>映射为核心 <see cref="Specialization"/>。</summary>
    public Specialization ToModel() => new(
        Key,
        (Modifiers ?? Array.Empty<StatModifierData>()).Select(m => m.ToModel()).ToArray(),
        Budget);
}

/// <summary>
/// 一种作战学说的可加载配置（JSON 友好 DTO，Req 16）。相较核心
/// <see cref="CoreDoctrine"/>，本 DTO 额外携带其两条专精分支（A/B，Req 16.5），
/// 由 DataLoader 校验预算并汇聚为 <see cref="LoadedDoctrine"/>。
/// </summary>
/// <param name="Key">学说 id（无文案）。</param>
/// <param name="Modifiers">学说自身的加法修正集合（可空，仅提供签名标志）。</param>
/// <param name="SignatureFlags">该学说的签名夜战标志（Req 16.4）。</param>
/// <param name="Specializations">两条专精分支（A/B，Req 16.5）。</param>
public sealed record DoctrineData(
    string Key,
    IReadOnlyList<StatModifierData>? Modifiers,
    NightFlags SignatureFlags,
    IReadOnlyList<SpecializationData>? Specializations)
{
    /// <summary>映射学说本体为核心 <see cref="CoreDoctrine"/>（不含专精分支）。</summary>
    public CoreDoctrine ToModel() => new(
        Key,
        (Modifiers ?? Array.Empty<StatModifierData>()).Select(m => m.ToModel()).ToArray(),
        SignatureFlags);
}

/// <summary>
/// 已加载并校验通过的学说：核心 <see cref="CoreDoctrine"/> 本体 + 其可选专精分支列表
/// （Req 16）。核心侧 <see cref="CoreDoctrine"/> 与 <see cref="Specialization"/> 相互独立，
/// 本记录在数据层将二者分组，便于组队时按学说取用其 A/B 分支。
/// </summary>
/// <param name="Doctrine">学说本体（核心类型）。</param>
/// <param name="Specializations">该学说的专精分支（核心类型，Req 16.5）。</param>
public sealed record LoadedDoctrine(
    CoreDoctrine Doctrine,
    IReadOnlyList<Specialization> Specializations);
