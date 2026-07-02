using Xjdl.Core.State;

namespace Xjdl.Data.Units;

/// <summary>
/// 兵种模板的可加载配置（JSON 友好 DTO，Req 20.1）。映射到核心不可变类型
/// <see cref="UnitTemplate"/>（<see cref="ToModel"/>）。纯数据、无文案（<see cref="TypeKey"/> 仅作 id）。
/// <para>
/// 加载期约束（由 DataLoader 校验，fail-fast）：<see cref="Resilience"/> 须为正；初始
/// <see cref="Attack"/> / <see cref="Defense"/> 须能被 <see cref="Resilience"/> 整除，
/// 以保证每次战损的整数衰减（Req 8.1、8.3）。
/// </para>
/// </summary>
public sealed record UnitTemplateData(
    string TypeKey,
    UnitClass Class,
    int Attack,
    int Defense,
    int Movement,
    int Resilience,
    int Vision,
    int SupportRange,
    int? SupportShift,
    NightFlags DefaultFlags = NightFlags.None)
{
    /// <summary>映射为核心 <see cref="UnitTemplate"/>（不做校验，校验由 DataLoader 负责）。</summary>
    public UnitTemplate ToModel() => new(
        TypeKey,
        Class,
        Attack,
        Defense,
        Movement,
        Resilience,
        Vision,
        SupportRange,
        SupportShift,
        DefaultFlags);
}
