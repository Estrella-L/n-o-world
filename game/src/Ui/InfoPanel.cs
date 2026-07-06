using System;
using Godot;
using Xjdl.Core.Hex;
using Xjdl.Core.State;
using Xjdl.Game.Input;
using Xjdl.Game.Presentation;
using Xjdl.Game.Presentation.ViewModels;
using Side = Xjdl.Core.State.Side;

namespace Xjdl.Game.Ui;

/// <summary>
/// 选中格/单位详情面板（节点层，Req 5.2/5.3/5.4）。
/// <para>
/// 订阅 <see cref="SelectionController.SelectionChanged"/>，按选中类别显示：
/// </para>
/// <list type="bullet">
/// <item><see cref="SelectionKind.FriendlyUnit"/>：经 <see cref="PresentationMapper.DescribeFriendly"/>
/// 显示己方完整信息（阵营、兵种、当前攻/防/韧性、机动、视野、当前命令，Req 5.2）。</item>
/// <item><see cref="SelectionKind.IdentifiedEnemy"/>：经 <see cref="PresentationMapper.DescribeEnemy"/>
/// 显示迷雾允许的字段（兵种、当前攻/防/韧性），不显示敌方命令等保密项（Req 5.3）。</item>
/// <item><see cref="SelectionKind.SpottedCell"/>：仅显示「该格有敌」未明信息，无兵种与数值（Req 5.4）。</item>
/// <item><see cref="SelectionKind.EmptyCell"/>：显示该格坐标与地形基础信息。</item>
/// <item><see cref="SelectionKind.None"/>：清空并隐藏面板。</item>
/// </list>
/// <para>
/// 信息裁剪一律经 <see cref="PresentationMapper"/>：敌方保密字段在映射层 DTO 源头即为 <c>null</c>，
/// 本面板只消费已裁剪的视图数据，不直接读取敌方隐藏字段（Req 5.3/5.4、17.2）。
/// </para>
/// <para>
/// 依赖注入：本面板需要 <see cref="SelectionController"/>、<see cref="PresentationMapper"/>、
/// 当前 <see cref="GameState"/> 供给器与观察方 <see cref="Side"/>。
/// <c>Match.tscn</c> 的完整节点树组装在任务 15.2 统一完成，届时由 <c>TurnController</c>
/// 调用 <see cref="Initialize"/> 注入这些引用。<see cref="GameState"/> 以 <see cref="Func{TResult}"/>
/// 供给，使面板每次渲染都读取当前回合末的唯一事实状态。
/// </para>
/// </summary>
public partial class InfoPanel : PanelContainer
{
    private const int TitleFontSize = 16;
    private const int BodyFontSize = 13;

    private SelectionController? _selection;
    private PresentationMapper? _mapper;
    private Func<GameState>? _stateProvider;
    private Side _viewer = Side.Blue;

    private Label? _titleLabel;
    private Label? _bodyLabel;

    // Initialize 可能早于 _Ready 触发订阅，先缓存最近一次选中，待 UI 就绪后补渲染。
    private Selection _pending = Selection.None;
    private bool _hasPending;

    /// <summary>
    /// 注入面板所需引用（由任务 15.2 的场景组装 / <c>TurnController</c> 调用）。
    /// </summary>
    /// <param name="selection">选中控制器，本面板订阅其 <see cref="SelectionController.SelectionChanged"/>。</param>
    /// <param name="mapper">Core→视图 DTO 映射层，负责己方完整信息与敌方可见度裁剪。</param>
    /// <param name="stateProvider">当前对局状态供给器（返回唯一事实来源）。</param>
    /// <param name="viewer">观察方阵营。</param>
    public void Initialize(
        SelectionController selection,
        PresentationMapper mapper,
        Func<GameState> stateProvider,
        Side viewer)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(stateProvider);

        if (_selection is not null)
        {
            _selection.SelectionChanged -= OnSelectionChanged;
        }

        _selection = selection;
        _mapper = mapper;
        _stateProvider = stateProvider;
        _viewer = viewer;

        _selection.SelectionChanged += OnSelectionChanged;

        // 立即按控制器的当前选中渲染一次，避免注入时错过既有选中。
        OnSelectionChanged(selection.Current);
    }

    /// <summary>更新观察方阵营并按当前选中重渲染。</summary>
    public void SetViewer(Side viewer)
    {
        _viewer = viewer;
        if (_selection is not null)
        {
            OnSelectionChanged(_selection.Current);
        }
    }

    public override void _Ready()
    {
        BuildUi();

        if (_hasPending)
        {
            _hasPending = false;
            Render(_pending);
        }
    }

    public override void _ExitTree()
    {
        if (_selection is not null)
        {
            _selection.SelectionChanged -= OnSelectionChanged;
        }
    }

    private void BuildUi()
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 4);

        _titleLabel = new Label { Text = string.Empty };
        _titleLabel.AddThemeFontSizeOverride("font_size", TitleFontSize);

        _bodyLabel = new Label
        {
            Text = string.Empty,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        };
        _bodyLabel.AddThemeFontSizeOverride("font_size", BodyFontSize);

        box.AddChild(_titleLabel);
        box.AddChild(_bodyLabel);
        AddChild(box);
    }

    private void OnSelectionChanged(Selection selection)
    {
        // UI 尚未构建（Initialize 早于 _Ready）时缓存，待 _Ready 补渲染。
        if (_titleLabel is null || _bodyLabel is null)
        {
            _pending = selection;
            _hasPending = true;
            return;
        }

        Render(selection);
    }

    private void Render(Selection selection)
    {
        if (_titleLabel is null || _bodyLabel is null)
        {
            return;
        }

        // 无选中：清空并隐藏（Req 5.5 的清除结果）。
        if (selection.Kind == SelectionKind.None || _mapper is null || _stateProvider is null)
        {
            _titleLabel.Text = string.Empty;
            _bodyLabel.Text = string.Empty;
            Visible = false;
            return;
        }

        Visible = true;
        var state = _stateProvider();

        switch (selection.Kind)
        {
            case SelectionKind.FriendlyUnit:
                RenderFriendly(state, selection);
                break;

            case SelectionKind.IdentifiedEnemy:
                RenderIdentifiedEnemy(state, selection);
                break;

            case SelectionKind.SpottedCell:
                RenderSpotted(selection);
                break;

            case SelectionKind.EmptyCell:
                RenderEmptyCell(state, selection);
                break;

            default:
                _titleLabel.Text = string.Empty;
                _bodyLabel.Text = string.Empty;
                Visible = false;
                break;
        }
    }

    /// <summary>己方单位：完整信息含当前命令（Req 5.2）。</summary>
    private void RenderFriendly(GameState state, Selection selection)
    {
        if (selection.Unit is not { } id)
        {
            RenderUnavailable();
            return;
        }

        UnitDetailView? detail = _mapper!.DescribeFriendly(state, id);
        if (detail is null)
        {
            RenderUnavailable();
            return;
        }

        _titleLabel!.Text = $"己方单位 #{id.Value}";
        _bodyLabel!.Text = string.Join(
            '\n',
            $"阵营：{SideName(detail.Side)}",
            $"兵种：{ClassName(detail.Class)}",
            $"攻/防/韧：{detail.Attack}/{detail.Defense}/{detail.ResilienceLeft}",
            $"机动：{detail.Movement}",
            $"视野：{detail.Vision}",
            $"当前命令：{CommandName(detail.CurrentCommand)}");
    }

    /// <summary>Identified 敌方：迷雾允许的字段，不含敌方命令（Req 5.3）。</summary>
    private void RenderIdentifiedEnemy(GameState state, Selection selection)
    {
        if (selection.Unit is not { } id)
        {
            RenderUnavailable();
            return;
        }

        EnemyDetailView? detail = _mapper!.DescribeEnemy(state, _viewer, id);
        if (detail is null || detail.Visibility != Visibility.Identified)
        {
            RenderUnavailable();
            return;
        }

        _titleLabel!.Text = $"敌方单位 #{id.Value}（已显形）";
        _bodyLabel!.Text = string.Join(
            '\n',
            $"位置：{CoordText(detail.Position)}",
            $"兵种：{(detail.Class is { } cls ? ClassName(cls) : "未知")}",
            $"攻/防/韧：{StatText(detail.Attack)}/{StatText(detail.Defense)}/{StatText(detail.ResilienceLeft)}");
    }

    /// <summary>Spotted 敌格：仅「该格有敌」未明信息，无兵种与数值（Req 5.4）。</summary>
    private void RenderSpotted(Selection selection)
    {
        _titleLabel!.Text = "未明";
        var coord = selection.Cell is { } cell ? CoordText(cell) : "未知";
        _bodyLabel!.Text = string.Join(
            '\n',
            $"位置：{coord}",
            "该格有敌（未明）");
    }

    /// <summary>空格：坐标与地形基础信息。</summary>
    private void RenderEmptyCell(GameState state, Selection selection)
    {
        if (selection.Cell is not { } coord)
        {
            RenderUnavailable();
            return;
        }

        _titleLabel!.Text = "空格";
        _bodyLabel!.Text = string.Join(
            '\n',
            $"位置：{CoordText(coord)}",
            $"地形：{TerrainNameAt(state, coord)}");
    }

    private void RenderUnavailable()
    {
        _titleLabel!.Text = "无信息";
        _bodyLabel!.Text = "所选目标已失效";
    }

    /// <summary>复用映射层的中文地形名（经 <see cref="PresentationMapper.MapCells"/>），避免重复文案映射。</summary>
    private string TerrainNameAt(GameState state, HexCoord coord)
    {
        foreach (CellView cell in _mapper!.MapCells(state))
        {
            if (cell.Coord == coord)
            {
                return cell.TerrainDisplayName;
            }
        }

        return "未知";
    }

    private static string CoordText(HexCoord coord) => $"({coord.Q}, {coord.R})";

    private static string StatText(int? value) => value?.ToString() ?? "?";

    private static string SideName(Side side) => side switch
    {
        Side.Blue => "蓝方",
        Side.Red => "红方",
        _ => side.ToString(),
    };

    private static string ClassName(UnitClass cls) => cls switch
    {
        UnitClass.LineHold => "抗线",
        UnitClass.Elite => "精锐",
        UnitClass.FireSupport => "火力支援",
        UnitClass.Special => "特殊",
        _ => cls.ToString(),
    };

    private static string CommandName(Command command) => command switch
    {
        Command.Move => "移动",
        Command.AttackPrep => "进攻准备",
        Command.Hold => "据守",
        _ => command.ToString(),
    };
}
