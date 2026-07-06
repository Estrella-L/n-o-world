using System.Collections.Generic;
using Godot;
using Xjdl.Game.Presentation.ViewModels;

namespace Xjdl.Game.Render;

/// <summary>
/// 选中/可移动/可攻击高亮层（节点层，Req 5.1/5.6）。
/// 在地图之上绘制三类可区分的半透明高亮：当前选中格（Req 5.1）、
/// 已选中己方单位的可移动格与可攻击目标格（Req 5.6）。本层不做任何规则计算，
/// 仅消费由 <c>SelectionController</c>（任务 9.1）经 <c>HexLayout</c>/<c>PresentationMapper</c>
/// 换算得到的六角格顶点多边形（纯层 <see cref="Vector2D"/>），并在边界处转换为
/// Godot <see cref="Vector2"/>（Req 17.4）。与 <see cref="MapRenderer"/> 一致，采用
/// "存下输入即请求重绘、在 <see cref="_Draw"/> 中绘制"的渲染模型。
///
/// 注意：<c>Match.tscn</c> 目前尚未组装（见任务 15.2）。任务 15.2 会在场景树中新增名为
/// <c>HighlightLayer</c> 的 <see cref="Node2D"/> 节点并挂接本脚本，使其位于 <c>MapLayer</c>
/// 之上、<c>UnitLayer</c> 之下，由 <c>SelectionController</c> 驱动其 <see cref="Show"/>/<see cref="Clear"/>。
/// 当前任务仅提供该脚本与其公开 API，供 <c>SelectionController</c> 调用。
/// </summary>
public partial class HighlightLayer : Node2D
{
    // 三类高亮的占位视觉（仅为可区分的占位色，无最终美术）：
    // 选中格——淡蓝填充 + 蓝色描边（Req 5.1）。
    private static readonly Color SelectedFill = new(0.30f, 0.60f, 0.95f, 0.35f);
    private static readonly Color SelectedOutline = new(0.15f, 0.45f, 0.90f, 0.95f);

    // 可移动格——淡绿填充 + 绿色描边（Req 5.6）。
    private static readonly Color MovableFill = new(0.35f, 0.80f, 0.40f, 0.28f);
    private static readonly Color MovableOutline = new(0.20f, 0.65f, 0.25f, 0.90f);

    // 可攻击目标格——淡红填充 + 红色描边（Req 5.6）。
    private static readonly Color AttackableFill = new(0.90f, 0.30f, 0.30f, 0.30f);
    private static readonly Color AttackableOutline = new(0.80f, 0.15f, 0.15f, 0.95f);

    private const float OutlineWidth = 2.5f;

    private static readonly IReadOnlyList<IReadOnlyList<Vector2D>> EmptyCells =
        System.Array.Empty<IReadOnlyList<Vector2D>>();

    private IReadOnlyList<Vector2D>? _selectedCellCorners;
    private IReadOnlyList<IReadOnlyList<Vector2D>> _movableCellCorners = EmptyCells;
    private IReadOnlyList<IReadOnlyList<Vector2D>> _attackableCellCorners = EmptyCells;

    /// <summary>
    /// 更新三类高亮并请求重绘（Req 5.1/5.6）。各参数均为六角格顶点多边形（纯层
    /// <see cref="Vector2D"/> 序列），由 <c>SelectionController</c> 经 <c>HexLayout</c> 换算得到。
    /// </summary>
    /// <param name="selectedCellCorners">当前选中格的顶点；<c>null</c> 或空表示无选中格（Req 5.1）。</param>
    /// <param name="movableCellCorners">已选中己方单位的可移动格顶点集合（Req 5.6）；可为空。</param>
    /// <param name="attackableCellCorners">已选中己方单位的可攻击目标格顶点集合（Req 5.6）；可为空。</param>
    public void Show(
        IReadOnlyList<Vector2D>? selectedCellCorners,
        IReadOnlyList<IReadOnlyList<Vector2D>> movableCellCorners,
        IReadOnlyList<IReadOnlyList<Vector2D>> attackableCellCorners)
    {
        _selectedCellCorners = selectedCellCorners;
        _movableCellCorners = movableCellCorners ?? EmptyCells;
        _attackableCellCorners = attackableCellCorners ?? EmptyCells;
        QueueRedraw();
    }

    /// <summary>
    /// 清除全部高亮并请求重绘（配合 Req 5.5：图外点击清除选中而不报错）。
    /// </summary>
    public void Clear()
    {
        _selectedCellCorners = null;
        _movableCellCorners = EmptyCells;
        _attackableCellCorners = EmptyCells;
        QueueRedraw();
    }

    /// <summary>
    /// 绘制三类高亮。绘制顺序为可移动格 → 可攻击目标格 → 选中格，使选中格描边位于最上，
    /// 便于在重叠时仍清晰可辨（Req 5.1/5.6）。纯层 <see cref="Vector2D"/> 在此边界处转换为
    /// Godot <see cref="Vector2"/>（Req 17.4）。
    /// </summary>
    public override void _Draw()
    {
        foreach (var corners in _movableCellCorners)
        {
            DrawCell(corners, MovableFill, MovableOutline);
        }

        foreach (var corners in _attackableCellCorners)
        {
            DrawCell(corners, AttackableFill, AttackableOutline);
        }

        DrawCell(_selectedCellCorners, SelectedFill, SelectedOutline);
    }

    /// <summary>
    /// 以填充多边形 + 闭合描边绘制单个高亮格。顶点不足以成面（&lt; 3）或为空则跳过。
    /// </summary>
    private void DrawCell(IReadOnlyList<Vector2D>? corners, Color fill, Color outline)
    {
        if (corners is null)
        {
            return;
        }

        var polygon = ToGodotPolygon(corners);
        if (polygon.Length < 3)
        {
            return;
        }

        DrawColoredPolygon(polygon, fill);
        DrawPolyline(ClosePolygon(polygon), outline, OutlineWidth);
    }

    /// <summary>
    /// 把纯层顶点序列（<see cref="Vector2D"/>）转为 Godot 多边形顶点（<see cref="Vector2"/>）。
    /// 与 <see cref="MapRenderer"/> 的转换约定保持一致（Req 17.4）。
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
}
