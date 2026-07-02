using Xjdl.Core.Hex;

namespace Xjdl.Core.State;

/// <summary>
/// 游戏地图：格子集合与规模档位（Req 2.6、2.7）。
/// 不可变 record。<see cref="Cells"/> 仅用于按坐标 O(1) 查询；由于
/// <see cref="IReadOnlyDictionary{TKey,TValue}"/> 的枚举顺序不确定，任何需要遍历的
/// 场景都必须使用 <see cref="OrderedCells"/> 以获得字节级可重放的确定性次序（Req 2.6）。
/// </summary>
public sealed record GameMap(
    IReadOnlyDictionary<HexCoord, MapCell> Cells,   // 查询用；遍历时按稳定序排序（Req 2.6）
    MapScale Scale)
{
    /// <summary>
    /// 按稳定序（先 Q 升序、再 R 升序）遍历全部格子。
    /// 为确定性核心提供与插入/哈希顺序无关的枚举次序（Req 2.6）。
    /// </summary>
    public IEnumerable<MapCell> OrderedCells =>
        Cells.Values
            .OrderBy(c => c.Coord.Q)
            .ThenBy(c => c.Coord.R);

    /// <summary>按坐标查询格子；不存在时返回 <c>null</c>。</summary>
    public MapCell? TryGet(HexCoord coord) =>
        Cells.TryGetValue(coord, out var cell) ? cell : null;

    /// <summary>指定坐标是否在地图范围内。</summary>
    public bool Contains(HexCoord coord) => Cells.ContainsKey(coord);
}
