using Xjdl.Core.State;

namespace Xjdl.Data.Terrain;

/// <summary>
/// 单种地形的可加载配置（JSON 友好 DTO，Req 15.x、20.1）。相较核心
/// <see cref="TerrainSpec"/>，本 DTO 额外携带其 <see cref="Terrain"/> 键，
/// 由 DataLoader 汇聚为 <see cref="TerrainProfile"/> 字典。
/// </summary>
/// <param name="Terrain">该配置对应的地形类型（字典键）。</param>
/// <param name="MoveCost">进入该地形的移动消耗（Req 15.1/15.2）。</param>
/// <param name="DefensiveDrm">该地形的防御 DRM 骰轴修正（Req 15.3）。</param>
/// <param name="ForbiddenClasses">禁入兵种类别（如沼泽禁重型，Req 15.7），可空。</param>
/// <param name="EnterAndStop">进入即停语义（Req 15.x）。</param>
public sealed record TerrainSpecData(
    TerrainType Terrain,
    int MoveCost,
    int DefensiveDrm,
    IReadOnlyList<UnitClass>? ForbiddenClasses,
    bool EnterAndStop)
{
    /// <summary>映射为核心 <see cref="TerrainSpec"/>（不含地形键）。</summary>
    public TerrainSpec ToModel() => new(
        MoveCost,
        DefensiveDrm,
        ForbiddenClasses ?? Array.Empty<UnitClass>(),
        EnterAndStop);
}
