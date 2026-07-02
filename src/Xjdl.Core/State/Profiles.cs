namespace Xjdl.Core.State;

/// <summary>
/// 地形配置档：每种地形对应一份 <see cref="TerrainSpec"/>（Req 15.1、20.1）。
/// 不可变 record，纯数据，可直接序列化。
/// </summary>
public sealed record TerrainProfile(IReadOnlyDictionary<TerrainType, TerrainSpec> Terrains);

/// <summary>
/// 单种地形的规则参数（Req 15.x）。
/// <see cref="MoveCost"/> 移动消耗（Req 15.1/15.2）；<see cref="DefensiveDrm"/> 防御 DRM 骰轴（Req 15.3）；
/// <see cref="ForbiddenClasses"/> 禁入兵种（如沼泽禁重型，Req 15.7）；
/// <see cref="EnterAndStop"/> 进入即停语义（Req 15.x）。
/// </summary>
public sealed record TerrainSpec(
    int MoveCost,
    int DefensiveDrm,
    IReadOnlyList<UnitClass> ForbiddenClasses,
    bool EnterAndStop);

/// <summary>
/// 地图规模配置档：按规模决定 CP/牌库/手牌等参数（Req 19.1）。
/// </summary>
public sealed record MapScaleProfile(
    int CpPerTurn,
    int CpMax,
    int DeckSize,
    int HandLimit);

/// <summary>
/// 战争迷雾配置（Req 14.x）。
/// <see cref="BlipRingEnabled"/> 是否启用 V+1 环侦得（Req 14.3）；
/// <see cref="NightVisionDivisor"/> 夜晚视野除数、向下取整、最低 1（Req 18.2）。
/// </summary>
public sealed record FogConfig(
    bool BlipRingEnabled,
    int NightVisionDivisor);

/// <summary>
/// 夜战配置（可配置，Req 18.7）。
/// <see cref="NightAttackShift"/> 夜间进攻移档；<see cref="NightRangeMod"/> 夜间射程修正；
/// <see cref="NightVisionDivisor"/> 夜间视野除数（Req 18.2）。
/// </summary>
public sealed record NightConfig(
    int NightAttackShift,
    int NightRangeMod,
    int NightVisionDivisor);
