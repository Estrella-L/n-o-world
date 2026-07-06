using System.Collections.Generic;
using Godot;
using Xjdl.Core.State;
using Xjdl.Game.Presentation.ViewModels;

namespace Xjdl.Game.Render;

/// <summary>
/// 六角地图渲染器（节点层，Req 3.1/3.4/3.6）。
/// 消费纯层 <see cref="PresentationMapper"/> 产出的 <see cref="CellView"/> 列表，
/// 按 <see cref="CellView.Corners"/> 绘制六角多边形，并依 <see cref="CellView.Terrain"/>
/// 取可区分的占位色。<see cref="Render"/> 存下新快照并触发 <see cref="_Draw"/> 重绘，
/// 从而与"新快照即重绘"的渲染模型一致（Req 3.6）。
/// 规则计算一律委托 Core，本渲染器仅消费已映射的视图 DTO（Req 1.5/17.4）。
/// </summary>
public partial class MapRenderer : Node2D
{
    private static readonly Color OutlineColor = new(0.15f, 0.15f, 0.15f, 1.0f);
    private const float OutlineWidth = 2.0f;

    private IReadOnlyList<CellView> _cells = System.Array.Empty<CellView>();

    /// <summary>
    /// 载入/更新快照即重绘（Req 3.1/3.6）：存下单元格视图并请求重绘，
    /// 使显示的地图与新快照一致。
    /// </summary>
    public void Render(IReadOnlyList<CellView> cells)
    {
        _cells = cells ?? System.Array.Empty<CellView>();
        QueueRedraw();
    }

    /// <summary>
    /// 按每个 <see cref="CellView.Corners"/> 画填充六角多边形（地形占位色）+ 边框（Req 3.1/3.4）。
    /// 纯层的 <see cref="Vector2D"/> 在此边界处转换为 Godot <see cref="Vector2"/>（Req 17.4）。
    /// </summary>
    public override void _Draw()
    {
        foreach (var cell in _cells)
        {
            var polygon = ToGodotPolygon(cell.Corners);
            if (polygon.Length < 3)
            {
                continue;
            }

            var fill = ColorFor(cell.Terrain);
            DrawColoredPolygon(polygon, fill);

            // 闭合边框：把首点接到末点再连线。
            DrawPolyline(ClosePolygon(polygon), OutlineColor, OutlineWidth);
        }
    }

    /// <summary>
    /// 把纯层顶点序列（<see cref="Vector2D"/>）转为 Godot 多边形顶点（<see cref="Vector2"/>）。
    /// </summary>
    private static Vector2[] ToGodotPolygon(IReadOnlyList<Vector2D> corners)
    {
        var count = corners.Count;
        var polygon = new Vector2[count];
        for (var i = 0; i < count; i++)
        {
            polygon[i] = new Vector2((float)corners[i].X, (float)corners[i].Y);
        }

        return polygon;
    }

    /// <summary>
    /// 返回一个把首点追加到末尾的闭合折线，用于描边完整六角轮廓。
    /// </summary>
    private static Vector2[] ClosePolygon(Vector2[] polygon)
    {
        var closed = new Vector2[polygon.Length + 1];
        System.Array.Copy(polygon, closed, polygon.Length);
        closed[polygon.Length] = polygon[0];
        return closed;
    }

    /// <summary>
    /// 依地形类型取可区分的占位色（Req 3.4）。仅为占位视觉，无最终美术。
    /// </summary>
    private static Color ColorFor(TerrainType terrain) => terrain switch
    {
        TerrainType.Plain => new Color(0.78f, 0.80f, 0.55f),   // 平原：黄绿
        TerrainType.Forest => new Color(0.20f, 0.50f, 0.25f),  // 森林：深绿
        TerrainType.Hill => new Color(0.65f, 0.52f, 0.35f),    // 丘陵：土棕
        TerrainType.City => new Color(0.60f, 0.60f, 0.62f),    // 城市：灰
        TerrainType.River => new Color(0.30f, 0.55f, 0.85f),   // 河流：蓝
        TerrainType.Swamp => new Color(0.40f, 0.45f, 0.30f),   // 沼泽：暗黄绿
        _ => new Color(0.90f, 0.20f, 0.90f),                   // 未知：醒目品红（便于发现遗漏）
    };
}
