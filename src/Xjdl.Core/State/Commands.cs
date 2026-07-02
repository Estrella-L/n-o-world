using Xjdl.Core.Hex;

namespace Xjdl.Core.State;

/// <summary>
/// 卡牌标识符。值类型、不可变，作为技能卡的稳定引用键（Req 19.x）。
/// 注：卡牌子系统的完整模型将在后续任务（13.1）扩充，此处保持最小可编译定义。
/// </summary>
public readonly record struct CardId(int Value);

/// <summary>
/// 一次技能卡打出请求（最小定义）。记录打出方、卡牌与打出时机（Req 19.3）。
/// 注：卡牌子系统将在后续任务（13.1）扩充其字段（如目标、参数等），
/// 此处仅保留 <see cref="TurnCommands"/> 组合所需的最小结构。
/// </summary>
public sealed record CardPlay(Side Owner, CardId Card, CardTiming Timing);

/// <summary>
/// 阶段 0 下达的单位命令：每单位恰好一条命令（Req 3.2）。
/// <para><see cref="Command.Move"/> 使用 <see cref="Path"/> 描述移动路径；</para>
/// <para><see cref="Command.AttackPrep"/> 使用 <see cref="Target"/> 指定目标格；</para>
/// <para><see cref="Command.Hold"/> 两者均可为空。</para>
/// 不可变值语义，契合确定性核心（Req 2.7）。
/// </summary>
public sealed record UnitOrder(
    UnitId Unit,
    Command Command,
    IReadOnlyList<HexCoord>? Path,
    HexCoord? Target);

/// <summary>
/// 动画阶段的临机机动命令：仅改变移动路径（Req 4.4、4.5）。
/// <see cref="TriggerTick"/> 记录触发时点，用于确定性回放（Req 4.7、21.6）。
/// </summary>
public sealed record RepositionCommand(
    UnitId Unit,
    IReadOnlyList<HexCoord> NewPath,
    int TriggerTick);

/// <summary>
/// 单回合的全部输入命令集合：阶段 0 单位命令、动画阶段临机机动、技能卡打出。
/// 作为 <c>NextState</c> 的确定性输入（Req 3.2、4.4、4.7）。
/// </summary>
public sealed record TurnCommands(
    IReadOnlyList<UnitOrder> Orders,
    IReadOnlyList<RepositionCommand> Repositions,
    IReadOnlyList<CardPlay> CardPlays);
