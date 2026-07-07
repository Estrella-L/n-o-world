using System.Collections.Generic;
using Godot;
using Xjdl.Game.Presentation.ViewModels;

namespace Xjdl.Game.Render;

/// <summary>
/// 控制区（ZOC）显示层（节点层，Req 11.1–11.3 表现）。在地图之上以半透明色块 + 描边标出
/// 己方"据守/布防"单位当前（或移动布防落点）产生的控制区格——其周围一圈六个相邻格。
/// <para>
/// 由 <c>PlanningController</c> 依<b>当前计划命令</b>驱动（据守单位以当前位置、移动布防单位以落点为锚，
/// 取其六个相邻格）：随玩家规划实时刷新，帮助其看清防线覆盖。本层不做规则计算，只消费换算好的
/// 六角格顶点多边形（纯层 <see cref="Vector2D"/>），在边界处转换为 Godot <see cref="Vector2"/>（Req 17.4）。
/// </para>
/// </summary>
public partial class ZoneOfControlView : Node2D
{
    // 控制区占位视觉：淡青填充 + 青色描边，弱于选中/可攻击高亮，避免喧宾夺主。
    private static readonly Color ZocFill = new(0.30f, 0.70f, 0.60f, 0.14f);
    private static readonly Color ZocOutline = new(0.30f, 0.75f, 0.62f, 0.55f);
    private const float OutlineWidth = 1.5f;

    private static readonly IReadOnlyList<IReadOnlyList<Vector2D>> Empty =
        System.Array.Empty<IReadOnlyList<Vector2D>>();

    private IReadOnlyList<IReadOnlyList<Vector2D>> _cells = Empty;

    /// <summary>用新的控制区格顶点集合整体替换并请求重绘。传空则清空。</summary>
    public void Show(IReadOnlyList<IReadOnlyList<Vector2D>> cells)
    {
        _cells = cells ?? Empty;
        QueueRedraw();
    }

    /// <summary>清除全部控制区显示并请求重绘。</summary>
    public void Clear()
    {
        _cells = Empty;
        QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (var corners in _cells)
        {
            DrawCell(corners);
        }
    }

    private void DrawCell(IReadOnlyList<Vector2D> corners)
    {
        if (corners is null || corners.Count < 3)
        {
            return;
        }

        var polygon = new Vector2[corners.Count];
        for (var i = 0; i < corners.Count; i++)
        {
            polygon[i] = new Vector2((float)corners[i].X, (float)corners[i].Y);
        }

        DrawColoredPolygon(polygon, ZocFill);

        var closed = new Vector2[polygon.Length + 1];
        System.Array.Copy(polygon, closed, polygon.Length);
        closed[polygon.Length] = polygon[0];
        DrawPolyline(closed, ZocOutline, OutlineWidth);
    }
}
