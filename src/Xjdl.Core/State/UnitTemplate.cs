namespace Xjdl.Core.State;

/// <summary>
/// 兵种模板（中立基准，Req 16.1）。学说 + 专精在此基础上叠加修正。
/// 不可变 record，无文案字段（仅 <see cref="TypeKey"/> 作为兵种 id，i18n 由表现层负责）。
/// 约束：初始攻 / 防必须整除韧性 <see cref="Resilience"/>，以保证每次战损的整数衰减（Req 8.1、8.3）；
/// 该约束由 DataLoader / DoctrineSystem 在加载期校验（fail-fast），此处仅承载数据。
/// </summary>
public sealed record UnitTemplate(
    string TypeKey,           // 兵种 id，如 "unit.infantry-battalion"（无文案，Req i18n）
    UnitClass Class,
    int Attack,
    int Defense,
    int Movement,
    int Resilience,
    int Vision,
    int SupportRange,         // 仅火力支援单位有意义
    int? SupportShift,        // 支援移档档数（如 +1）
    NightFlags DefaultFlags);
