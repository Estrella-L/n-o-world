using Xjdl.Core.Cards;
using Xjdl.Core.State;

namespace Xjdl.Data.Cards;

/// <summary>
/// 单张技能卡的可加载配置（JSON 友好 DTO，Req 19.x）。相较核心 <see cref="Card"/> 使用扁平
/// 的整数 <see cref="Id"/>（而非 <see cref="CardId"/> 包装），由 DataLoader 映射为核心类型。
/// </summary>
/// <param name="Id">卡牌稳定标识符（整数，映射为 <see cref="CardId"/>）。</param>
/// <param name="Timing">可打出的 WEGO 时机标签（Req 19.3）。</param>
/// <param name="CpCost">打出所需指挥点，非负（Req 19.7）。</param>
/// <param name="TargetsEnemy">该卡是否指向某个敌方单位。</param>
/// <param name="RequiresRevealedTarget">是否要求目标已显形（Req 19.6）。</param>
/// <param name="FirePowerShift">该卡产生的火力比移档档数（正右负左，0 表示不涉及）。</param>
/// <param name="OnUnrevealed">目标未显形时的处置（无效/减效，Req 19.6）。</param>
public sealed record CardData(
    int Id,
    CardTiming Timing,
    int CpCost,
    bool TargetsEnemy,
    bool RequiresRevealedTarget,
    int FirePowerShift,
    UnrevealedEffect OnUnrevealed)
{
    /// <summary>映射为核心 <see cref="Card"/>。</summary>
    public Card ToModel() => new(
        new CardId(Id),
        Timing,
        CpCost,
        TargetsEnemy,
        RequiresRevealedTarget,
        FirePowerShift,
        OnUnrevealed);
}
