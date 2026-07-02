using Xjdl.Core.State;

namespace Xjdl.Data.Scale;

/// <summary>
/// 地图规模档位的可加载配置（JSON 友好 DTO，Req 19.1、20.1）。相较核心
/// <see cref="MapScaleProfile"/>，本 DTO 额外携带其 <see cref="Scale"/> 键，
/// 由 DataLoader 汇聚为按规模索引的字典。
/// </summary>
/// <param name="Scale">该档位对应的地图规模（字典键）。</param>
/// <param name="CpPerTurn">每回合获得的指挥点。</param>
/// <param name="CpMax">指挥点上限。</param>
/// <param name="DeckSize">牌库大小。</param>
/// <param name="HandLimit">手牌上限。</param>
public sealed record MapScaleProfileData(
    MapScale Scale,
    int CpPerTurn,
    int CpMax,
    int DeckSize,
    int HandLimit)
{
    /// <summary>映射为核心 <see cref="MapScaleProfile"/>（不含规模键）。</summary>
    public MapScaleProfile ToModel() => new(CpPerTurn, CpMax, DeckSize, HandLimit);
}
