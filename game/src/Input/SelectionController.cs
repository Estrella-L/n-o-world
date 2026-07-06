using System;
using System.Collections.Generic;
using Godot;
using Xjdl.Core.Hex;
using Xjdl.Core.State;
using Xjdl.Game.Presentation;
using Xjdl.Game.Presentation.ViewModels;
using Xjdl.Game.Render;
using Side = Xjdl.Core.State.Side;

namespace Xjdl.Game.Input;

/// <summary>
/// 当前选中目标的类别（Req 5.1–5.4）。任一时刻至多一个选中目标（Req 5.7）。
/// </summary>
public enum SelectionKind
{
    /// <summary>无选中（点击图外后清除，Req 5.5）。</summary>
    None,

    /// <summary>选中一个地图范围内的空格（无可见单位，Req 5.1）。</summary>
    EmptyCell,

    /// <summary>选中一个己方单位（Req 5.2）。</summary>
    FriendlyUnit,

    /// <summary>选中一个 <see cref="Visibility.Identified"/> 的敌方单位（Req 5.3）。</summary>
    IdentifiedEnemy,

    /// <summary>选中一个 <see cref="Visibility.Spotted"/> 敌方单位所在的格，仅"有敌"未明信息（Req 5.4）。</summary>
    SpottedCell,
}

/// <summary>
/// 当前选中快照（值语义，Req 5.7 任一时刻至多一个）。
/// <see cref="Cell"/> 为选中格坐标（<see cref="SelectionKind.None"/> 时为 <c>null</c>）；
/// <see cref="Unit"/> 仅在选中具体单位（己方 / <see cref="Visibility.Identified"/> 敌方）时有值。
/// </summary>
public readonly record struct Selection(HexCoord? Cell, UnitId? Unit, SelectionKind Kind)
{
    /// <summary>无选中的空快照。</summary>
    public static Selection None { get; } = new(null, null, SelectionKind.None);
}

/// <summary>
/// 格与单位选中输入控制器（节点层，Req 5.1–5.7）。
/// <para>
/// 处理鼠标左键点选：经 <see cref="HexLayout"/> 由鼠标世界坐标反解六角格，再据当前
/// <see cref="GameState"/> 判定该格内容——
/// </para>
/// <list type="bullet">
/// <item>图外 → 清除选中且不报错（Req 5.5）。</item>
/// <item>空格（无可见单位）→ 选中并高亮该格（Req 5.1）。</item>
/// <item>己方单位 → 选中并触发信息面板；高亮其可移动/可攻击格（Req 5.2/5.6）。</item>
/// <item><see cref="Visibility.Identified"/> 敌方 → 选中并显示迷雾允许的信息（Req 5.3）。</item>
/// <item><see cref="Visibility.Spotted"/> 敌格 → 仅"该格有敌"未明信息（Req 5.4）。</item>
/// </list>
/// <para>
/// 信息裁剪一律经 <see cref="PresentationMapper"/>（<see cref="PresentationMapper.EnemyUnits"/>），
/// 本控制器不直接读敌方隐藏字段——保密由映射层在 DTO 源头保证（Req 5.3/5.4、17.2）。
/// 选中结果经 <see cref="SelectionChanged"/> 通知信息面板（任务 14.1）与高亮层（任务 8.3）。
/// </para>
/// <para>
/// 依赖注入：本控制器需要当前 <see cref="GameState"/>、<see cref="PresentationMapper"/>、
/// <see cref="HexLayout"/>、<see cref="HighlightLayer"/> 与观察方 <see cref="Side"/>。
/// <c>Match.tscn</c> 的完整节点树组装在任务 15.2 统一完成，届时由 <c>TurnController</c>
/// 调用 <see cref="Initialize"/> 注入这些引用、并在每次状态刷新时调用 <see cref="SetState"/>。
/// </para>
/// </summary>
public partial class SelectionController : Node2D
{
    /// <summary>选中变化通知（供 <c>InfoPanel</c> 与 <see cref="HighlightLayer"/> 消费）。</summary>
    public event Action<Selection>? SelectionChanged;

    private GameState? _state;
    private PresentationMapper? _mapper;
    private HexLayout? _layout;
    private HighlightLayer? _highlightLayer;
    private Side _viewer = Side.Blue;

    private Selection _current = Selection.None;

    /// <summary>当前选中快照（任一时刻至多一个，Req 5.7）。</summary>
    public Selection Current => _current;

    /// <summary>
    /// 注入控制器所需的引用（由任务 15.2 的场景组装 / <c>TurnController</c> 调用）。
    /// </summary>
    /// <param name="state">当前对局状态（唯一事实来源）。</param>
    /// <param name="mapper">Core→视图 DTO 映射层，负责可见度裁剪。</param>
    /// <param name="layout">六角坐标 ↔ 像素换算工具，用于由鼠标世界坐标反解格。</param>
    /// <param name="highlightLayer">高亮层，展示选中格/可移动格/可攻击目标（Req 5.1/5.6）。</param>
    /// <param name="viewer">观察方阵营。</param>
    public void Initialize(
        GameState state,
        PresentationMapper mapper,
        HexLayout layout,
        HighlightLayer highlightLayer,
        Side viewer)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(highlightLayer);

        _state = state;
        _mapper = mapper;
        _layout = layout;
        _highlightLayer = highlightLayer;
        _viewer = viewer;
    }

    /// <summary>
    /// 更新到新的状态快照（每回合结算后由 <c>TurnController</c> 调用）。
    /// 若当前选中的目标在新快照中已失效（单位阵亡 / 格越界），则清除选中；
    /// 否则依新状态重刷高亮，使显示与快照一致。
    /// </summary>
    public void SetState(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state = state;
        RefreshFromCurrent();
    }

    /// <summary>更新观察方阵营并按当前选中重刷高亮。</summary>
    public void SetViewer(Side viewer)
    {
        _viewer = viewer;
        RefreshFromCurrent();
    }

    /// <summary>
    /// 处理未被 UI 消费的鼠标左键按下：取世界坐标反解格并执行选中（Req 5.1–5.5）。
    /// 相机的屏幕↔世界变换由 <see cref="Node2D.GetGlobalMousePosition"/> 承担。
    /// </summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            return;
        }

        if (_state is null || _mapper is null || _layout is null)
        {
            return;
        }

        var world = GetGlobalMousePosition();
        SelectAt(new Vector2D(world.X, world.Y));
    }

    /// <summary>
    /// 由世界像素坐标反解格并按内容更新选中（可脱离鼠标事件独立调用，便于测试/联调）。
    /// </summary>
    public void SelectAt(Vector2D worldPixel)
    {
        if (_state is null || _mapper is null || _layout is null)
        {
            return;
        }

        var coord = _layout.CoordAt(worldPixel);
        ApplySelection(ResolveSelection(coord));
    }

    /// <summary>清除当前选中（图外点击等，Req 5.5）。</summary>
    public void ClearSelection() => ApplySelection(Selection.None);

    /// <summary>
    /// 依据点击的格坐标解析出选中目标：图外→None；己方单位→FriendlyUnit；
    /// <see cref="Visibility.Identified"/> 敌方→IdentifiedEnemy；<see cref="Visibility.Spotted"/>
    /// 敌格→SpottedCell；地图内空格→EmptyCell。敌方可见度一律经 <see cref="PresentationMapper"/>。
    /// </summary>
    private Selection ResolveSelection(HexCoord coord)
    {
        // 图外：清除且不报错（Req 5.5）。
        if (!_state!.Map.Contains(coord))
        {
            return Selection.None;
        }

        // 己方单位优先（Req 5.2）：同格堆叠时取稳定 id 最小者。
        var friendly = FindFriendlyAt(coord);
        if (friendly is not null)
        {
            return new Selection(coord, friendly.Id, SelectionKind.FriendlyUnit);
        }

        // 敌方按可见度裁剪，仅消费映射层已过滤的视图（Req 5.3/5.4、17.2）。
        var enemies = _mapper!.EnemyUnits(_state, _viewer);
        UnitId? identifiedId = null;
        var hasSpotted = false;
        foreach (var enemy in enemies)
        {
            if (enemy.Position != coord)
            {
                continue;
            }

            if (enemy.Visibility == Visibility.Identified)
            {
                // 取稳定 id 最小的 Identified 敌方作为选中单位。
                if (identifiedId is null || enemy.Id.Value < identifiedId.Value.Value)
                {
                    identifiedId = enemy.Id;
                }
            }
            else
            {
                hasSpotted = true;
            }
        }

        if (identifiedId is not null)
        {
            return new Selection(coord, identifiedId, SelectionKind.IdentifiedEnemy);   // Req 5.3
        }

        if (hasSpotted)
        {
            return new Selection(coord, null, SelectionKind.SpottedCell);               // Req 5.4
        }

        // 地图内空格（无可见单位）：选中并高亮该格（Req 5.1）。
        return new Selection(coord, null, SelectionKind.EmptyCell);
    }

    /// <summary>存下选中、驱动高亮层并广播 <see cref="SelectionChanged"/>（Req 5.7 至多一个）。</summary>
    private void ApplySelection(Selection selection)
    {
        _current = selection;
        DriveHighlight(selection);
        SelectionChanged?.Invoke(selection);
    }

    /// <summary>依当前存储的选中重刷高亮（状态/观察方变化后调用）。</summary>
    private void RefreshFromCurrent()
    {
        if (_state is null || _current.Kind == SelectionKind.None)
        {
            return;
        }

        // 选中格越界（地图更换）则清除。
        if (_current.Cell is { } cell && !_state.Map.Contains(cell))
        {
            ApplySelection(Selection.None);
            return;
        }

        DriveHighlight(_current);
    }

    /// <summary>
    /// 驱动 <see cref="HighlightLayer"/>：己方单位显示选中格 + 可移动/可攻击格（Req 5.6）；
    /// 其余在图内的选中仅高亮选中格（Req 5.1）；None 清除。
    /// </summary>
    private void DriveHighlight(Selection selection)
    {
        if (_highlightLayer is null || _layout is null || _state is null)
        {
            return;
        }

        if (selection.Kind == SelectionKind.None || selection.Cell is not { } cell)
        {
            _highlightLayer.Clear();
            return;
        }

        var selectedCorners = _layout.CornersOf(cell);

        if (selection.Kind == SelectionKind.FriendlyUnit && selection.Unit is { } unitId)
        {
            var unit = FindUnit(unitId);
            if (unit is not null)
            {
                var (movable, attackable) = ComputeReachHighlights(unit);
                _highlightLayer.Show(selectedCorners, movable, attackable);
                return;
            }
        }

        _highlightLayer.Show(
            selectedCorners,
            Array.Empty<IReadOnlyList<Vector2D>>(),
            Array.Empty<IReadOnlyList<Vector2D>>());
    }

    /// <summary>
    /// 计算选中己方单位的可移动格与可攻击目标格顶点（Req 5.6）。
    /// <para>
    /// <b>近似实现（有意为之）</b>：可移动格取以单位位置为中心、半径为其机动点的六角范围内、
    /// 位于地图且未被可见敌方占据的格；可攻击目标取处于机动点 +1 范围内的可见敌方所在格。
    /// 这是不重算规则的<b>粗略距离近似</b>——未计入地形移动消耗、ZOC 等，仅用于给玩家一个
    /// 直观的候选提示。真正的路径合法性与可达性最终仍由 Core（结算）与
    /// <see cref="PathPlanner"/>（计划阶段拖拽）委托 <c>TerrainSystem</c> 判定。
    /// </para>
    /// <para>
    /// TODO（任务 15.2 接线后可选增强）：为获得计入地形消耗的精确可达集合，可注入
    /// <c>GameData</c> 并复用 <see cref="PathPlanner"/> 对每个候选格试探 <c>Extend</c> 的落点，
    /// 以替换此处的距离近似；本控制器保持依赖精简，不在选中阶段引入规则重算。
    /// </para>
    /// </summary>
    private (IReadOnlyList<IReadOnlyList<Vector2D>> Movable, IReadOnlyList<IReadOnlyList<Vector2D>> Attackable)
        ComputeReachHighlights(UnitState unit)
    {
        var move = Math.Max(0, unit.Movement);

        var enemies = _mapper!.EnemyUnits(_state!, _viewer);
        var enemyCells = new HashSet<HexCoord>();
        foreach (var enemy in enemies)
        {
            enemyCells.Add(enemy.Position);
        }

        var movable = new List<IReadOnlyList<Vector2D>>();
        foreach (var candidate in HexGrid.Range(unit.Position, move))
        {
            if (candidate == unit.Position || !_state!.Map.Contains(candidate) || enemyCells.Contains(candidate))
            {
                continue;
            }

            movable.Add(_layout!.CornersOf(candidate));
        }

        var attackable = new List<IReadOnlyList<Vector2D>>();
        foreach (var enemy in enemies)
        {
            if (enemy.Position != unit.Position && unit.Position.DistanceTo(enemy.Position) <= move + 1)
            {
                attackable.Add(_layout!.CornersOf(enemy.Position));
            }
        }

        return (movable, attackable);
    }

    /// <summary>取指定格上稳定 id 最小的己方单位；无则返回 <c>null</c>。</summary>
    private UnitState? FindFriendlyAt(HexCoord coord)
    {
        UnitState? best = null;
        foreach (var unit in _state!.Units)
        {
            if (unit.Owner != _viewer || unit.Position != coord)
            {
                continue;
            }

            if (best is null || unit.Id.Value < best.Id.Value)
            {
                best = unit;
            }
        }

        return best;
    }

    /// <summary>按 id 查单位；不存在返回 <c>null</c>。</summary>
    private UnitState? FindUnit(UnitId id)
    {
        foreach (var unit in _state!.Units)
        {
            if (unit.Id == id)
            {
                return unit;
            }
        }

        return null;
    }
}
