using System.Collections.Generic;
using System.Linq;
using Godot;
using Xjdl.Core.Hex;
using Xjdl.Core.State;
using Xjdl.Data.Loading;
using Xjdl.Game.Presentation;
using Xjdl.Game.Presentation.ViewModels;
using Xjdl.Game.Render;
using Side = Xjdl.Core.State.Side;

namespace Xjdl.Game.Input;

/// <summary>
/// 计划阶段「拖拽下令」控制器（节点层，Req 6.1–6.13）。
/// <para>
/// 实现设计〈PathPlanner + PlanningController〉的鼠标输入状态机
/// （<see cref="DragState.Idle"/> → <see cref="DragState.Dragging"/> → <see cref="DragState.Committed"/>）：
/// 在己方单位棋子上按下左键并拖动，经纯层 <see cref="PathPlanner"/> 的
/// <c>Begin</c>/<c>Extend</c> 逐格吸附生成移动路径并实时预览已用消耗/剩余机动点（Req 6.2/6.3）；
/// 松手时按终点组装命令——终点落在敌方目标格 → <see cref="Command.AttackPrep"/>（Req 6.8），
/// 普通合法路径 → <see cref="Command.Move"/>（Req 6.5），仅起点/未画路径 → <see cref="Command.Hold"/>（Req 6.9）。
/// </para>
/// <para>
/// <b>棋子不随拖拽移动</b>（Req 6.6）：拖拽只在 <see cref="MovementArrowNode"/> 上画计划箭头，
/// 被规划单位的棋子停留原位；真正移动由结算/动画阶段呈现。可对同一单位重拖以替换路径，或清除为
/// <see cref="Command.Hold"/>（Req 6.7）；提交前可修改/清除任一命令（Req 6.10）。
/// </para>
/// <para>
/// <b>规则委托 Core</b>（Req 6.13）：消耗累计与地形进入限制由 <see cref="PathPlanner"/> 委托
/// <c>TerrainSystem</c> 判定；本控制器只负责鼠标事件、箭头绘制与命令组装（经 <see cref="PresentationMapper"/>），
/// 提交路径最终仍由 Core 在 <c>NextState</c> 校验。仅在计划阶段（<see cref="PlanningEnabled"/>）接受下令，
/// 动画阶段的实时改路径改由 <c>RepositionController</c> 处理（Req 6.12）。
/// </para>
/// <para>
/// 依赖经 <see cref="Initialize"/> 注入（供任务 15.2 的 <c>Match.tscn</c> 组装接线）：当前
/// <see cref="GameState"/>、配置聚合 <see cref="GameData"/>、<see cref="HexLayout"/>、
/// <see cref="PresentationMapper"/>、用于绘制拖拽预览的 <see cref="MovementArrowNode"/> 与观察方
/// <see cref="Side"/>。每次拖拽按当前状态新建一个 <see cref="PathPlanner"/>。
/// </para>
/// </summary>
public partial class PlanningController : Node2D
{
    /// <summary>拖拽下令状态机的三态（design〈PlanningController 状态机〉）。</summary>
    private enum DragState
    {
        /// <summary>空闲：等待在己方单位上按下左键发起拖拽。</summary>
        Idle,

        /// <summary>拖拽中：正沿光标经 <see cref="PathPlanner.Extend"/> 逐格延伸路径并预览消耗。</summary>
        Dragging,

        /// <summary>已提交：最近一次拖拽已组装为 <see cref="Command.Move"/>/<see cref="Command.AttackPrep"/> 并保留箭头。</summary>
        Committed,
    }

    // ── 注入依赖（Req 6.x；任务 15.2 场景组装接线）─────────────────────
    private GameState _gameState = null!;
    private GameData _data = null!;
    private HexLayout _layout = null!;
    private PresentationMapper _mapper = null!;
    private MovementArrowNode? _activeArrow;
    private Side _viewer;
    private bool _initialized;

    // ── 状态机与下令集合 ──────────────────────────────────────────────
    private DragState _dragState = DragState.Idle;

    // 每个己方单位恰好一条命令，默认据守（Req 6.1/6.9）。
    private readonly Dictionary<UnitId, UnitOrder> _orders = new();

    // 已提交命令的持久箭头（每单位一条，Req 6.5）；与拖拽预览箭头 _activeArrow 区分。
    private readonly Dictionary<UnitId, MovementArrowNode> _committedArrows = new();

    // 当前拖拽的瞬时状态。
    private PathPlanner? _planner;
    private PathDraft _draft;
    private UnitId _dragUnit;
    private bool _dragIsAttackPrep;

    /// <summary>
    /// 是否处于计划阶段并接受下令（Req 6.12）。仅当为 <c>true</c> 时处理鼠标下令输入；
    /// 置为 <c>false</c> 时取消进行中的拖拽预览。由 <c>TurnController</c>/<c>PhaseControlBar</c>
    /// 按 WEGO 阶段切换控制。
    /// </summary>
    public bool PlanningEnabled { get; set; }

    /// <summary>
    /// 提交本回合计划时触发，携带全部己方 <see cref="UnitOrder"/> 集合（Req 6.11），
    /// 供 <c>TurnController</c>（任务 13.1）订阅并结算。
    /// </summary>
    public event System.Action<IReadOnlyList<UnitOrder>>? OrdersSubmitted;

    /// <summary>
    /// 注入依赖并以当前状态初始化下令集合（全部己方单位默认据守，Req 6.1/6.9）。
    /// 供 <c>Match.tscn</c> 组装时调用（任务 15.2）。
    /// </summary>
    /// <param name="state">当前对局状态（唯一事实来源，用于命中测试与新建 <see cref="PathPlanner"/>）。</param>
    /// <param name="data">配置聚合，提供地形移动消耗规则来源。</param>
    /// <param name="layout">六角坐标 ↔ 像素换算工具。</param>
    /// <param name="mapper">命令组装映射层。</param>
    /// <param name="activeArrow">绘制拖拽预览与选中单位已提交箭头的节点；可为 <c>null</c>（无预览）。</param>
    /// <param name="viewer">当前观察方（下令一方）。</param>
    public void Initialize(
        GameState state,
        GameData data,
        HexLayout layout,
        PresentationMapper mapper,
        MovementArrowNode? activeArrow,
        Side viewer)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(mapper);

        _gameState = state;
        _data = data;
        _layout = layout;
        _mapper = mapper;
        _activeArrow = activeArrow;
        _viewer = viewer;
        _initialized = true;

        ResetOrders();
    }

    /// <summary>
    /// 切换到新的对局状态（新回合开始或读档后）：清空箭头与拖拽状态，并把全部己方单位
    /// 的命令重置为默认据守（Req 6.1/6.9）。
    /// </summary>
    public void SetGameState(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        _gameState = state;
        CancelDrag();
        ClearAllArrows();
        ResetOrders();
        _dragState = DragState.Idle;
    }

    /// <summary>
    /// 产出全部己方 <see cref="UnitOrder"/> 集合，一单位一条、缺省为据守（Req 6.9/6.11）。
    /// 按 <see cref="UnitId"/> 稳定序，保持确定性。
    /// </summary>
    public IReadOnlyList<UnitOrder> CollectOrders()
    {
        var result = new List<UnitOrder>();
        foreach (var unit in FriendlyUnitsOrdered())
        {
            result.Add(_orders.TryGetValue(unit.Id, out var order)
                ? order
                : _mapper.BuildHoldOrder(unit.Id));
        }

        return result;
    }

    /// <summary>
    /// 确认提交本回合计划（Req 6.11）：结束进行中的拖拽，产出全部己方命令集合并经
    /// <see cref="OrdersSubmitted"/> 交给 <c>TurnController</c>。返回同一集合以便直接调用。
    /// </summary>
    public IReadOnlyList<UnitOrder> Submit()
    {
        CancelDrag();
        var orders = CollectOrders();
        OrdersSubmitted?.Invoke(orders);
        return orders;
    }

    /// <summary>
    /// 修改/清除某己方单位的命令，使其回到默认据守并移除其计划箭头（Req 6.7/6.10）。
    /// 仅在计划阶段生效（Req 6.12）。
    /// </summary>
    public void ClearOrder(UnitId unit)
    {
        if (!PlanningEnabled)
        {
            return;
        }

        _orders[unit] = _mapper.BuildHoldOrder(unit);
        RemoveCommittedArrow(unit);
        if (_dragState == DragState.Committed)
        {
            _dragState = DragState.Idle;
        }
    }

    /// <inheritdoc/>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (!_initialized || !PlanningEnabled)
        {
            return;
        }

        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }:
                OnLeftPressed(GetGlobalMousePosition());
                break;

            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false }
                when _dragState == DragState.Dragging:
                EndDrag(GetGlobalMousePosition());
                break;

            case InputEventMouseButton { ButtonIndex: MouseButton.Right, Pressed: true }:
                OnRightPressed(GetGlobalMousePosition());
                break;

            case InputEventMouseMotion when _dragState == DragState.Dragging:
                ExtendDrag(GetGlobalMousePosition());
                break;
        }
    }

    // ── 输入处理 ──────────────────────────────────────────────────────

    // 左键按下：命中己方单位则发起（或替换）该单位的拖拽（Idle/Committed → Dragging，Req 6.2/6.7）。
    // 命中空格/敌方/图外则不处理（交由 SelectionController）。
    private void OnLeftPressed(Vector2 world)
    {
        var hex = _layout.CoordAt(ToVector2D(world));
        var unit = FriendlyUnitAt(hex);
        if (unit is null)
        {
            return;
        }

        BeginDrag(unit.Value);
    }

    // 右键按下：命中己方单位则清除其命令为据守（Req 6.7/6.10）。
    private void OnRightPressed(Vector2 world)
    {
        var hex = _layout.CoordAt(ToVector2D(world));
        var unit = FriendlyUnitAt(hex);
        if (unit is not null)
        {
            ClearOrder(unit.Value);
        }
    }

    // Idle/Committed → Dragging：新建 PathPlanner，从单位起点开始草案；重拖同一单位时先撤下其旧箭头（Req 6.7）。
    private void BeginDrag(UnitId unit)
    {
        RemoveCommittedArrow(unit);

        _planner = new PathPlanner(_gameState, _data, _layout);
        _draft = _planner.Begin(unit);
        _dragUnit = unit;
        _dragIsAttackPrep = false;
        _dragState = DragState.Dragging;

        RenderActivePreview();
    }

    // Dragging → Dragging：沿光标逐格延伸路径并实时预览消耗；终点悬停敌格时以进攻准备样式提示（Req 6.3/6.8）。
    private void ExtendDrag(Vector2 world)
    {
        if (_planner is null)
        {
            return;
        }

        var cursor = ToVector2D(world);
        _draft = _planner.Extend(_draft, cursor);
        _dragIsAttackPrep = EnemyAt(_layout.CoordAt(cursor));
        RenderActivePreview();
    }

    // 松开左键：按终点组装命令并转移状态（Req 6.5/6.8/6.9）。
    private void EndDrag(Vector2 world)
    {
        _activeArrow?.Render(null); // 撤下拖拽预览，交由持久箭头呈现已提交计划。

        var unit = _dragUnit;
        var start = _draft.Path.Count > 0 ? _draft.Path[0] : default;
        var hoverHex = _layout.CoordAt(ToVector2D(world));
        _planner = null;

        // 终点落在敌方目标格 → 进攻准备（Req 6.8）。
        if (EnemyAt(hoverHex))
        {
            _orders[unit] = _mapper.BuildAttackPrepOrder(unit, hoverHex);
            ShowCommittedArrow(unit, BuildAttackArrowView(unit, start, hoverHex, _draft.MovementBudget));
            _dragState = DragState.Committed;
            return;
        }

        // 有实际位移（起点之外至少一格）→ 移动（Req 6.5）。Core 期望的 Path 不含出发格，故剔除起点。
        if (_draft.Path.Count > 1)
        {
            var movePath = new List<HexCoord>(_draft.Path.Count - 1);
            for (var i = 1; i < _draft.Path.Count; i++)
            {
                movePath.Add(_draft.Path[i]);
            }

            _orders[unit] = _mapper.BuildMoveOrder(unit, movePath);
            ShowCommittedArrow(unit, BuildMoveArrowView(_draft));
            _dragState = DragState.Committed;
            return;
        }

        // 仅起点（未画出有效路径）→ 视为据守（Req 6.9）。
        _orders[unit] = _mapper.BuildHoldOrder(unit);
        RemoveCommittedArrow(unit);
        _dragState = DragState.Idle;
    }

    // 取消进行中的拖拽（不改动已提交命令），撤下预览箭头。
    private void CancelDrag()
    {
        if (_dragState == DragState.Dragging)
        {
            _activeArrow?.Render(null);
            _dragState = DragState.Idle;
        }

        _planner = null;
    }

    // ── 箭头绘制 ──────────────────────────────────────────────────────

    private void RenderActivePreview() => _activeArrow?.Render(BuildMoveArrowView(_draft, _dragIsAttackPrep));

    // 由移动草案构建箭头视图：逐格中心连成折线，携带消耗/机动点与超限/禁入提示（Req 6.2/6.5）。
    private ArrowView BuildMoveArrowView(PathDraft draft, bool isAttackPrep = false)
    {
        var points = new List<Vector2D>(draft.Path.Count);
        foreach (var hex in draft.Path)
        {
            points.Add(_layout.CenterOf(hex));
        }

        return new ArrowView(
            draft.Unit,
            points,
            draft.UsedCost,
            draft.MovementBudget,
            isAttackPrep,
            draft.BlockedAhead);
    }

    // 进攻准备箭头：由单位起点直指目标敌格，以进攻准备样式呈现（Req 6.8）。
    private ArrowView BuildAttackArrowView(UnitId unit, HexCoord start, HexCoord target, int movementBudget)
    {
        var points = new List<Vector2D>
        {
            _layout.CenterOf(start),
            _layout.CenterOf(target),
        };

        return new ArrowView(unit, points, UsedCost: 0, movementBudget, IsAttackPrep: true, BlockedAhead: false);
    }

    private void ShowCommittedArrow(UnitId unit, ArrowView view)
    {
        var node = GetOrCreateCommittedArrow(unit);
        node?.Render(view);
    }

    // 惰性创建每单位一条的持久箭头节点，挂在预览箭头的同一父层（ArrowLayer）之下。
    private MovementArrowNode? GetOrCreateCommittedArrow(UnitId unit)
    {
        if (_committedArrows.TryGetValue(unit, out var existing))
        {
            return existing;
        }

        var node = new MovementArrowNode { Name = $"CommittedArrow_{unit.Value}" };
        var parent = _activeArrow?.GetParent();
        if (parent is not null)
        {
            parent.AddChild(node);
        }
        else
        {
            AddChild(node);
        }

        _committedArrows[unit] = node;
        return node;
    }

    private void RemoveCommittedArrow(UnitId unit)
    {
        if (_committedArrows.Remove(unit, out var node))
        {
            node.Render(null);
            node.QueueFree();
        }
    }

    private void ClearAllArrows()
    {
        _activeArrow?.Render(null);
        foreach (var node in _committedArrows.Values)
        {
            node.Render(null);
            node.QueueFree();
        }

        _committedArrows.Clear();
    }

    // ── 状态查询辅助 ──────────────────────────────────────────────────

    // 全部己方单位默认据守（Req 6.1/6.9）。
    private void ResetOrders()
    {
        _orders.Clear();
        foreach (var unit in FriendlyUnitsOrdered())
        {
            _orders[unit.Id] = _mapper.BuildHoldOrder(unit.Id);
        }
    }

    private IEnumerable<UnitState> FriendlyUnitsOrdered() =>
        _gameState.Units
            .Where(u => u.Owner == _viewer)
            .OrderBy(u => u.Id.Value);

    // 命中测试：该格上的己方单位（同格堆叠取最小 id，保持确定性）。
    private UnitId? FriendlyUnitAt(HexCoord hex)
    {
        UnitId? found = null;
        foreach (var unit in _gameState.Units)
        {
            if (unit.Owner == _viewer && unit.Position == hex &&
                (found is null || unit.Id.Value < found.Value.Value))
            {
                found = unit.Id;
            }
        }

        return found;
    }

    // 该格是否有敌方单位占据（终点判定进攻准备用；直接读状态，非规则计算，Req 6.8）。
    // 注：此处以「敌格是否被敌方单位占据」的最简判据识别进攻目标；可见度呈现由 FogView 负责，
    // 提交的目标最终仍由 Core 在结算时校验。
    private bool EnemyAt(HexCoord hex)
    {
        foreach (var unit in _gameState.Units)
        {
            if (unit.Owner != _viewer && unit.Position == hex)
            {
                return true;
            }
        }

        return false;
    }

    private static Vector2D ToVector2D(Vector2 v) => new(v.X, v.Y);
}
