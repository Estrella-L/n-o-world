using Xjdl.Core.Hex;

namespace Xjdl.Core.State;

/// <summary>
/// 单个六角格：其轴向坐标与地形类型（Req 2.6、2.7）。
/// 不可变 record，无文案字段（地形显示名由表现层按 <see cref="TerrainType"/> 负责 i18n）。
/// </summary>
public sealed record MapCell(HexCoord Coord, TerrainType Terrain);
