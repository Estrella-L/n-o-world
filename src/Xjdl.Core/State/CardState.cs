namespace Xjdl.Core.State;

// 注：CardId 定义位于 Commands.cs（任务 3.4）。此处仅承载卡牌经济状态。

/// <summary>
/// 单方卡牌经济状态（不可变，Req 19.1）。
/// <see cref="Cp"/> 为当前指挥点，累积但不超 <see cref="CpMax"/>（Req 19.2）；
/// <see cref="Deck"/>/<see cref="Hand"/> 分别为牌库与手牌，按稳定序遍历以保证确定性（Req 2.6）。
/// 结构保持最小，任务 13.1 将进一步扩展。
/// </summary>
public sealed record CardState(
    int Cp,
    int CpMax,
    IReadOnlyList<CardId> Deck,
    IReadOnlyList<CardId> Hand);
