using System.Collections.Generic;
using Godot;
using Xjdl.Core.Hex;
using Xjdl.Core.Random;
using Xjdl.Core.State;
using Xjdl.Data.Loading;
using Xjdl.Game.Input;
using Xjdl.Game.Presentation;
using Xjdl.Game.Presentation.ViewModels;
using Xjdl.Game.Render;
using Xjdl.Game.Ui;
// Xjdl.Game.Ui.DayNightView（节点）与 Xjdl.Game.Presentation.ViewModels.DayNightView（DTO）同短名，显式别名消歧。
using DayNightPanel = Xjdl.Game.Ui.DayNightView;
using Side = Xjdl.Core.State.Side;

namespace Xjdl.Game.Turn;

/// <summary>
/// 对局场景根控制器（<see cref="Node2D"/>，任务 15.2 接线）。
/// <para>
/// 在 <see cref="Node._Ready"/> 时读取 <see cref="MatchBootstrap"/> 交接的初始状态/配置/种子/迷雾/
/// 观察方/对手模式，构造纯层依赖（<see cref="HexLayout"/>/<see cref="PresentationMapper"/>/
/// <see cref="PlaceholderOpponent"/>/<see cref="PcgRng"/>），并对场景树中的各渲染器、输入控制器、
/// UI 面板与阶段动画器逐一 <c>Initialize</c>、连接事件，完成一整局的装配（Req 14.4/14.5）。
/// </para>
/// <para>
/// 事件接线要点：<c>PhaseControlBar.SubmitRequested</c> → 收集 <c>PlanningController</c> 命令并经
/// <c>TurnController.SubmitTurn</c> 结算（Req 6.11/8.1）；<c>PhaseControlBar.SkipAnimationRequested</c>
/// → <c>PhaseAnimator.Skip</c>（Req 8.7）；<c>SelectionController.SelectionChanged</c> → <c>InfoPanel</c>
/// （Req 5.2–5.4）；<c>TurnLogView.LocateRequested</c> → 相机/高亮定位（Req 9.3）；
/// <c>RepositionController.CommandPointsChanged</c> → <c>CommandPointView</c> 刷新（Req 12.2）。
/// 全部规则计算委托 <c>Xjdl.Core</c>，本控制器仅做装配与显示编排（Req 1.5）。
/// </para>
/// </summary>
public partial class MatchController : Node2D
{
    // 六角布局参数（占位视觉，无规则含义）。
    private const double HexSize = 42.0;
    private static readonly Vector2D LayoutOrigin = new(160.0, 140.0);

    private static readonly IReadOnlyList<IReadOnlyList<Vector2D>> EmptyCells =
        System.Array.Empty<IReadOnlyList<Vector2D>>();

    private HexLayout _layout = null!;
    private PresentationMapper _mapper = null!;
    private Side _viewer;

    private TurnController _turnController = null!;
    private SelectionController _selection = null!;
    private PlanningController _planning = null!;
    private RepositionController _reposition = null!;
    private PhaseControlBar _phaseControlBar = null!;
    private TurnLogView _turnLogView = null!;
    private DayNightPanel _dayNightView = null!;
    private CommandPointView _commandPointView = null!;
    private HighlightLayer _highlightLayer = null!;
    private Camera2D _camera = null!;

    public override void _Ready()
    {
        if (!MatchBootstrap.IsReady)
        {
            GD.PushWarning(
                "MatchController: 未找到有效的对局交接数据（MatchBootstrap）。" +
                "请从主菜单经 MatchSetup 启动对局。");
            return;
        }

        GameData data = MatchBootstrap.Data!;
        GameState initialState = MatchBootstrap.InitialState!;
        FogConfig fog = MatchBootstrap.Fog!;
        ulong seed = MatchBootstrap.Seed;
        _viewer = MatchBootstrap.Viewer;
        OpponentMode mode = MatchBootstrap.Mode;

        // 读取后清空交接，避免跨对局泄漏。
        MatchBootstrap.Clear();

        // ── 纯层依赖 ──────────────────────────────────────────────────
        _layout = new HexLayout(HexSize, LayoutOrigin);
        _mapper = new PresentationMapper(data, fog, _layout);
        var opponent = new PlaceholderOpponent();
        ISeededRng rng = new PcgRng(seed);

        // ── 场景节点解析 ──────────────────────────────────────────────
        _turnController = GetNode<TurnController>("TurnController");
        _camera = GetNode<Camera2D>("Camera2D");
        var mapRenderer = GetNode<MapRenderer>("MapLayer");
        _highlightLayer = GetNode<HighlightLayer>("HighlightLayer");
        var zocView = GetNode<ZoneOfControlView>("ZocLayer");
        var unitRenderer = GetNode<UnitRenderer>("UnitLayer");
        var combatMarkers = GetNode<CombatMarkerLayer>("CombatMarkerLayer");
        var fogView = GetNode<FogView>("UnitLayer/FogView");
        var activeArrow = GetNode<MovementArrowNode>("ArrowLayer/ActiveArrow");
        _selection = GetNode<SelectionController>("InputRoot/Selection");
        _planning = GetNode<PlanningController>("InputRoot/Planning");
        _reposition = GetNode<RepositionController>("InputRoot/Reposition");
        var animator = GetNode<PhaseAnimator>("PhaseFx");
        var infoPanel = GetNode<InfoPanel>("UiRoot/InfoPanel");
        _turnLogView = GetNode<TurnLogView>("UiRoot/TurnLogPanel");
        _commandPointView = GetNode<CommandPointView>("UiRoot/CommandPointView");
        _dayNightView = GetNode<DayNightPanel>("UiRoot/DayNightView");
        _phaseControlBar = GetNode<PhaseControlBar>("UiRoot/PhaseControlBar");
        var promptLabel = GetNode<Label>("UiRoot/PromptLabel");

        // ── 组件初始化（注入依赖）──────────────────────────────────────
        _reposition.Initialize(_mapper, initialState, _viewer);

        animator.Initialize(
            _mapper,
            _layout,
            _viewer,
            _highlightLayer,
            promptLabel,
            unitRenderer,
            mapRenderer,
            fogView,
            combatMarkers,
            _camera);

        _turnController.Initialize(
            initialState,
            rng,
            _mapper,
            opponent,
            _viewer,
            mapRenderer,
            unitRenderer,
            fogView,
            _reposition,
            animator,
            mode);

        _selection.Initialize(initialState, _mapper, _layout, _highlightLayer, _viewer);

        _planning.Initialize(initialState, data, _layout, _mapper, activeArrow, _viewer, zocView);
        _planning.PlanningEnabled = true;

        infoPanel.Initialize(_selection, _mapper, () => _turnController.Current, _viewer);

        // ── UI 初始呈现 ───────────────────────────────────────────────
        _turnLogView.Show(_mapper.LogLines(initialState));
        _dayNightView.Show(_mapper.PhaseView(initialState));
        _commandPointView.Bind(_reposition, () => _mapper.CommandPoints(_turnController.Current, _viewer));
        _phaseControlBar.SetPlanningPhase();

        // ── 事件接线 ──────────────────────────────────────────────────
        _planning.OrdersSubmitted += OnOrdersSubmitted;
        _phaseControlBar.SubmitRequested += OnSubmitRequested;
        _phaseControlBar.SkipAnimationRequested += OnSkipRequested;
        _turnLogView.LocateRequested += OnLocateRequested;
        _turnController.TurnSettled += OnTurnSettled;
        _turnController.TurnAdvanced += OnTurnAdvanced;
        _turnController.SettlementFailed += msg => ShowPrompt(promptLabel, msg);
        _reposition.RepositionRejected += msg => ShowPrompt(promptLabel, msg);
    }

    public override void _ExitTree()
    {
        // 解除事件订阅，避免场景切换后回调悬挂。
        if (_planning is not null)
        {
            _planning.OrdersSubmitted -= OnOrdersSubmitted;
        }

        if (_phaseControlBar is not null)
        {
            _phaseControlBar.SubmitRequested -= OnSubmitRequested;
            _phaseControlBar.SkipAnimationRequested -= OnSkipRequested;
        }

        if (_turnLogView is not null)
        {
            _turnLogView.LocateRequested -= OnLocateRequested;
        }

        if (_turnController is not null)
        {
            _turnController.TurnSettled -= OnTurnSettled;
            _turnController.TurnAdvanced -= OnTurnAdvanced;
        }
    }

    // ── 事件处理 ──────────────────────────────────────────────────────

    // 计划提交（Req 6.11）：把己方命令交给 TurnController 单次结算（Req 8.1）。
    private void OnOrdersSubmitted(IReadOnlyList<UnitOrder> orders) => _turnController.SubmitTurn(orders);

    // 提交按钮 → 收集计划命令（触发 OrdersSubmitted）。
    private void OnSubmitRequested() => _planning.Submit();

    // 跳过按钮 → 直达结算后最终态（Req 8.7）。
    private void OnSkipRequested() => _turnController.SkipAnimation();

    // 结算开始：切到动画/结算阶段——禁用计划下令、允许跳过（Req 6.12/8.7）。
    private void OnTurnSettled(GameState after)
    {
        _planning.PlanningEnabled = false;
        _phaseControlBar.SetResolutionPhase();
    }

    // 动画播完、显示已与结算后状态一致：刷新各视图并回到计划阶段（Req 8.6/6.1）。
    private void OnTurnAdvanced(GameState after)
    {
        _selection.SetState(after);
        _planning.SetGameState(after);
        _turnLogView.Show(_mapper.LogLines(after));
        _dayNightView.Show(_mapper.PhaseView(after));
        _commandPointView.Bind(_reposition, () => _mapper.CommandPoints(_turnController.Current, _viewer));
        _phaseControlBar.SetPlanningPhase();
        _planning.PlanningEnabled = true;
    }

    // 日志条目定位（Req 9.3）：把相机移到该格并高亮之。
    private void OnLocateRequested(HexCoord coord)
    {
        var center = _layout.CenterOf(coord);
        _camera.Position = new Vector2((float)center.X, (float)center.Y);
        _highlightLayer.Show(_layout.CornersOf(coord), EmptyCells, EmptyCells);
    }

    private static void ShowPrompt(Label label, string text)
    {
        if (label is not null)
        {
            label.Text = text;
            label.Visible = true;
        }
    }
}
