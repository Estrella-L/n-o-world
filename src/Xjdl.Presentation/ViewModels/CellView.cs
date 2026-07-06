using Xjdl.Core.Hex;
using Xjdl.Core.State;

namespace Xjdl.Game.Presentation.ViewModels;

/// <summary>
/// 单元格视图：坐标、地形、绘制顶点与地形显示名（Req 3.1/3.4）。
/// 不可变投影，随快照更新重新映射，契合"新快照即重绘"（Req 3.6）。
/// </summary>
public sealed record CellView(
    HexCoord Coord,
    TerrainType Terrain,
    Vector2D Center,
    IReadOnlyList<Vector2D> Corners,
    string TerrainDisplayName);
