using Xjdl.Core.State;

namespace Xjdl.Core.Doctrine;

/// <summary>
/// 一种作战学说（阵地防御/装甲突击/火力支援/特种渗透之一，Req 16）。
/// 学说承载一组加法修正 <see cref="Modifiers"/> 与一组签名夜战标志 <see cref="SignatureFlags"/>，
/// 组队时与所选专精分支（<see cref="Specialization"/>）叠加后作用于中立兵种模板。
/// <para>
/// 预算约束：学说修正与所选专精修正的预算点数合计须恰为 4（Req 16.2、16.5），
/// 由 <see cref="DoctrineSystem.Validate"/> 校验；签名标志在组队时经
/// <see cref="DoctrineSystem.ApplySignatureFlags"/> 批量赋予对应兵种（Req 16.4）。
/// </para>
/// 不可变 record，无文案字段（<see cref="Key"/> 仅作 id，i18n 由表现层负责）。
/// </summary>
/// <param name="Key">学说 id，如 "doctrine.positional-defense"（无文案）。</param>
/// <param name="Modifiers">学说自身的加法修正集合（可为空，仅提供签名标志）。</param>
/// <param name="SignatureFlags">该学说的签名夜战标志，如据守单位的 <see cref="NightFlags.NightZocKeep"/>。</param>
public sealed record Doctrine(
    string Key,
    IReadOnlyList<StatModifier> Modifiers,
    NightFlags SignatureFlags);
