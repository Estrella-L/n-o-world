using Xjdl.Core.Modifiers;
using Xjdl.Core.State;

namespace Xjdl.Core.Cards;

/// <summary>
/// 当一张「指向敌方且要求目标显形」的卡在目标未显形（<see cref="Visibility.Spotted"/>/
/// <see cref="Visibility.Hidden"/>）时的处置方式（Req 19.6，「无效或减效，按卡面」）。
/// </summary>
public enum UnrevealedEffect
{
    /// <summary>无效：拒绝打出，不消耗 CP、不产生效果。</summary>
    Void,

    /// <summary>减效：仍可打出，但火力比移档按卡面折减（此处取整数减半、向零取整）。</summary>
    Reduce,
}

/// <summary>
/// 单张技能卡的定义（不可变、纯数据，Req 19.x）。
/// <para>本记录为核心引擎侧的最小可玩模型：卡牌的实际内容池由数据层加载（任务 18.1）后
/// 以 <see cref="CardId"/> 为键注入 <see cref="PlayContext.Cards"/>。此处仅承载
/// <see cref="CardSystem"/> 校验与结算所需字段，后续任务可在此扩充目标类型、参数等。</para>
/// </summary>
/// <param name="Id">卡牌稳定标识符。</param>
/// <param name="Timing">可打出的 WEGO 时机标签（Req 19.3）。</param>
/// <param name="CpCost">打出所需指挥点，非负（Req 19.7）。</param>
/// <param name="TargetsEnemy">该卡是否指向某个敌方单位。</param>
/// <param name="RequiresRevealedTarget">
/// 是否要求目标已显形（<see cref="Visibility.Identified"/>）。仅当 <see cref="TargetsEnemy"/> 为真时有意义（Req 19.6）。
/// </param>
/// <param name="FirePowerShift">
/// 该卡产生的火力比移档档数（正右负左，0 表示不涉及火力比）。以 <see cref="ModifierSource.Card"/>
/// 走统一移档管线并受 ±2 档总封顶约束（Req 19.4）。
/// </param>
/// <param name="OnUnrevealed">
/// 当 <see cref="RequiresRevealedTarget"/> 为真且目标未显形时的处置（无效/减效，Req 19.6）。
/// </param>
public sealed record Card(
    CardId Id,
    CardTiming Timing,
    int CpCost,
    bool TargetsEnemy,
    bool RequiresRevealedTarget,
    int FirePowerShift,
    UnrevealedEffect OnUnrevealed);

/// <summary>
/// 一次 <see cref="CardSystem.Play"/> 打出请求的上下文（不可变）。
/// 因 <see cref="CardState"/> 仅承载 CP 经济与牌堆，卡牌定义通过 <see cref="Cards"/> 注册表注入，
/// 而非扩展 <see cref="CardState"/>（Req 19.x）。
/// </summary>
/// <param name="Cards">卡牌定义注册表（由数据层提供，任务 18.1）。</param>
/// <param name="TargetVisibility">
/// 当卡指向敌方时，目标当前的可见度；不指向敌方时为 <c>null</c>（Req 19.6）。
/// </param>
public sealed record PlayContext(
    IReadOnlyDictionary<CardId, Card> Cards,
    Visibility? TargetVisibility);

/// <summary>
/// <see cref="CardSystem.Play"/> 的结果（不可变）。
/// <para>成功时 <see cref="Success"/> 为真、<see cref="State"/> 为扣除 CP 并移出该卡后的新状态；
/// 拒绝时 <see cref="Success"/> 为假、<see cref="State"/> 原样返回且 <see cref="RejectReason"/> 说明原因
/// （拒绝为规则内结果，不抛异常，Req 19.7）。</para>
/// </summary>
/// <param name="Success">是否成功打出。</param>
/// <param name="State">打出后的新卡牌经济状态；拒绝时与输入一致。</param>
/// <param name="RejectReason">拒绝原因；成功时为 <c>null</c>。</param>
/// <param name="FirePowerShift">
/// 该卡产生的火力比移档（带 <see cref="ModifierSource.Card"/> 来源，交由 <see cref="ModifierPipeline"/> 累加封顶）；
/// 不涉及火力比或折减后为 0 时为 <c>null</c>（Req 19.4）。
/// </param>
/// <param name="Reduced">是否因目标未显形而按减效打出（Req 19.6）。</param>
public sealed record PlayResult(
    bool Success,
    CardState State,
    string? RejectReason,
    ColumnShift? FirePowerShift,
    bool Reduced);
