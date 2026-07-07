using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Xjdl.Core;
using Xjdl.Core.Random;
using Xjdl.Core.State;
using Xjdl.Game.Input;
using Xjdl.Game.Presentation;
using Xjdl.Game.Render;

// Godot 亦定义了 Godot.Side（Left/Top/Right/Bottom），与规则引擎的阵营枚举同名。
// 本文件的“阵营”一律指 Xjdl.Core.State.Side，显式别名消歧（与 UnitRenderer/RepositionController 同款约定）。
using Side = Xjdl.Core.State.Side;

namespace Xjdl.Game.Turn;

/// <summary>
/// 回合控制器（对局根节点，Req 7.4、8.1/8.2/8.3/8.6/8.8）。
/// <para>
/// 持有当前对局 <see cref="GameState"/> 作为<b>唯一事实来源</b>（<see cref="Current"/>），并注入一个确定性
/// 随机源 <see cref="PcgRng"/>（跨回合连续推进以支持可重放，Req 8.2）。<see cref="SubmitTurn"/> 将
/// 己方 <see cref="UnitOrder"/> 与 <see cref="PlaceholderOpponent"/> 的对手命令、以及本回合已收集的
/// <c>Repositions</c> 合并为一个 <see cref="TurnCommands"/>，以注入的 <see cref="PcgRng"/> <b>单次调用</b>
/// <see cref="RulesEngine.NextState"/> 原子结算整个 WEGO 回合（阶段 0–9），得回合末 <see cref="GameState"/>
/// 与其 <c>TurnLog</c>（Req 8.1/8.3）。
/// </para>
/// <para>
/// <b>不修改返回状态</b>（Req 8.8）：<see cref="RulesEngine.NextState"/> 返回的状态仅作显示之用，本控制器
/// 不改动其内容——只把它设为新的 <see cref="Current"/>。结算成功后，若已接线 <see cref="IPhaseAnimator"/>
/// （任务 13.2 的 <c>PhaseAnimator</c>）则驱动其 <c>PlayAsync</c> 重建阶段动画，动画结束后调用各渲染器
/// <c>Render(after)</c> 使显示与结算后状态一致（Req 8.6）；未接线动画器时直接同步显示。
/// </para>
/// <para>
/// <b>Core 异常回滚</b>（Req 8.8）：<see cref="RulesEngine.NextState"/> 调用置于 try/catch 中，一旦 Core 抛出
/// （意外的非法命令组合等），回滚——保持 <see cref="Current"/> 不变、不推进 RNG 之外的任何显示，并经
/// <see cref="SettlementFailed"/> 事件向玩家提示“结算失败”，绝不崩溃。
/// </para>
/// <para>
/// <b>接线（供任务 15.2 组装 <c>Match.tscn</c> 时完成）</b>：通过 <see cref="Initialize"/> 注入渲染器
/// （<see cref="MapRenderer"/>/<see cref="UnitRenderer"/>/<see cref="FogView"/>）、<see cref="PresentationMapper"/>、
/// <see cref="PlaceholderOpponent"/>、<see cref="RepositionController"/>、观察方 <see cref="Side"/> 与
/// <see cref="PcgRng"/>；可选注入 <see cref="IPhaseAnimator"/>（13.2 就绪前可为 <c>null</c>）与本地热座对手
/// 命令来源。全部规则计算委托 Core，本控制器不重算任何规则（Req 1.5）。
/// </para>
/// </summary>
public partial class TurnController : Node2D
{
    // ── 注入依赖（Req 7.4/8.x；任务 15.2 场景组装接线）─────────────────
    private GameState _current = null!;
    private ISeededRng _rng = null!;
    private PresentationMapper _mapper = null!;
    private PlaceholderOpponent _opponent = null!;
    private Side _viewer;
    private OpponentMode _opponentMode = OpponentMode.Scripted;

    private MapRenderer? _mapRenderer;
    private UnitRenderer? _unitRenderer;
    private FogView? _fogView;
    private RepositionController? _reposition;
    private IPhaseAnimator? _animator;

    // 本地热座模式下，读取玩家为对手方（红方）逐单位下达命令的来源（Req 7.2）。
    private Func<IReadOnlyList<UnitOrder>>? _hotSeatOrdersSource;

    private bool _initialized;

    // 防止动画进行中重复提交导致状态错乱（同一时刻仅结算/播放一回合）。
    private bool _settling;

    // 上一回合处于据守/布防姿态的单位：本回合若移动，动画起步前先"挖出"停顿（Req 8.5 扩展）。
    private IReadOnlySet<UnitId> _prevDefenders = new HashSet<UnitId>();

    /// <summary>当前对局状态（唯一事实来源，Req 8.8）。结算前后由本控制器统一持有与推进。</summary>
    public GameState Current => _current;

    /// <summary>是否正在结算或播放阶段动画。此间拒绝新的 <see cref="SubmitTurn"/>。</summary>
    public bool IsSettling => _settling;

    /// <summary>
    /// 单次 <see cref="RulesEngine.NextState"/> 成功返回后立即触发，携带回合末状态（Req 8.1）。
    /// 早于阶段动画播放；供需要“已结算但未播完动画”时机的订阅者使用。
    /// </summary>
    public event Action<GameState>? TurnSettled;

    /// <summary>
    /// 阶段动画播放完毕、显示已与结算后状态一致后触发，携带新的 <see cref="Current"/>（Req 8.6）。
    /// 供 UI/输入控制器在新回合开始时刷新（如重新进入计划阶段）。
    /// </summary>
    public event Action<GameState>? TurnAdvanced;

    /// <summary>
    /// 结算失败（Core 抛出异常）时触发，携带面向玩家的提示原因（Req 8.8）。
    /// 触发时 <see cref="Current"/> 保持提交前状态不变。由 UI 订阅显示提示。
    /// </summary>
    public event Action<string>? SettlementFailed;

    /// <summary>
    /// 注入依赖并设置初始对局状态（供任务 15.2 组装场景时调用）。
    /// </summary>
    /// <param name="initialState">合法的初始对局状态（唯一事实来源）。</param>
    /// <param name="rng">注入的确定性随机源（<see cref="PcgRng"/>），跨回合连续推进以支持可重放（Req 8.2）。</param>
    /// <param name="mapper">Core → 视图 DTO 的纯层映射器，用于结算后同步显示。</param>
    /// <param name="opponent">占位对手命令来源（Req 7.1/7.2）。</param>
    /// <param name="viewer">当前观察方阵营；其对手为占位对手所属阵营。</param>
    /// <param name="mapRenderer">可选：地图渲染器。</param>
    /// <param name="unitRenderer">可选：单位渲染器。</param>
    /// <param name="fogView">可选：迷雾呈现层。</param>
    /// <param name="reposition">可选：临机机动控制器，提供本回合待提交 <c>Repositions</c>（Req 13.2）。</param>
    /// <param name="animator">可选：阶段动画器（任务 13.2 的 <c>PhaseAnimator</c>）；为 <c>null</c> 时直接同步显示（Req 8.6）。</param>
    /// <param name="opponentMode">占位对手模式（脚本化/本地热座，默认脚本化，Req 7.1）。</param>
    /// <param name="hotSeatOrdersSource">本地热座模式下读取对手方玩家命令的来源；脚本化模式可为 <c>null</c>（Req 7.2）。</param>
    public void Initialize(
        GameState initialState,
        ISeededRng rng,
        PresentationMapper mapper,
        PlaceholderOpponent opponent,
        Side viewer,
        MapRenderer? mapRenderer = null,
        UnitRenderer? unitRenderer = null,
        FogView? fogView = null,
        RepositionController? reposition = null,
        IPhaseAnimator? animator = null,
        OpponentMode opponentMode = OpponentMode.Scripted,
        Func<IReadOnlyList<UnitOrder>>? hotSeatOrdersSource = null)
    {
        _current = initialState ?? throw new ArgumentNullException(nameof(initialState));
        _rng = rng ?? throw new ArgumentNullException(nameof(rng));
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _opponent = opponent ?? throw new ArgumentNullException(nameof(opponent));
        _viewer = viewer;
        _mapRenderer = mapRenderer;
        _unitRenderer = unitRenderer;
        _fogView = fogView;
        _reposition = reposition;
        _animator = animator;
        _opponentMode = opponentMode;
        _hotSeatOrdersSource = hotSeatOrdersSource;
        _initialized = true;

        // 初始同步一次显示，使地图/单位/迷雾与初始状态一致（Req 3.6/4.5/10.5）。
        SyncDisplay(_current);
    }

    /// <summary>可选后接线：设置或更换阶段动画器（任务 13.2 就绪后调用）。</summary>
    public void SetPhaseAnimator(IPhaseAnimator? animator) => _animator = animator;

    /// <summary>
    /// 提交本回合并结算（Req 7.4、8.1/8.2/8.3/8.6/8.8）。
    /// <para>
    /// 合并 <paramref name="friendlyOrders"/> 与占位对手命令（按 <see cref="OpponentMode"/> 取脚本化/热座）、
    /// 以及 <see cref="RepositionController.Pending"/> 中已收集的临机机动为一个 <see cref="TurnCommands"/>，
    /// 以注入的 <see cref="PcgRng"/> <b>单次调用</b> <see cref="RulesEngine.NextState"/>（Req 8.1/8.2/8.3）。
    /// </para>
    /// <para>
    /// 结算成功：将回合末状态设为新的 <see cref="Current"/>（不修改其内容，Req 8.8），驱动
    /// <see cref="IPhaseAnimator"/>（若有）后同步各渲染器至结算后状态（Req 8.6）；结算抛异常：回滚、
    /// 保持 <see cref="Current"/> 不变、经 <see cref="SettlementFailed"/> 提示，不崩溃（Req 8.8）。
    /// </para>
    /// </summary>
    /// <param name="friendlyOrders">本回合全部己方 <see cref="UnitOrder"/> 集合（由 <c>PlanningController</c> 提交）。</param>
    public void SubmitTurn(IReadOnlyList<UnitOrder> friendlyOrders)
    {
        if (!_initialized)
        {
            throw new InvalidOperationException(
                $"{nameof(TurnController)} 尚未 {nameof(Initialize)}，无法提交回合。");
        }

        ArgumentNullException.ThrowIfNull(friendlyOrders);

        // 动画/结算进行中拒绝新提交，避免并发推进状态（Req 8.6 播放期间不接受新回合）。
        if (_settling)
        {
            return;
        }

        var before = _current;

        // Req 7.1/7.2/7.4：取对手命令并与己方命令合并。
        var opponentOrders = ResolveOpponentOrders(before);
        var mergedOrders = MergeOrders(friendlyOrders, opponentOrders);

        // Req 13.2：在调用 NextState 之前纳入本回合已收集的临机机动。
        IReadOnlyList<RepositionCommand> repositions =
            _reposition?.Pending is { Count: > 0 } pending
                ? new List<RepositionCommand>(pending)
                : Array.Empty<RepositionCommand>();

        var commands = new TurnCommands(mergedOrders, repositions, Array.Empty<CardPlay>());

        GameState after;
        try
        {
            // Req 8.1/8.2/8.3：以注入的确定性 RNG 单次调用，原子结算整个回合。
            after = RulesEngine.NextState(before, commands, _rng);
        }
        catch (Exception ex)
        {
            // Req 8.8：捕获 Core 异常 → 回滚（Current 保持不变）、提示、不崩溃。
            SettlementFailed?.Invoke($"结算失败：{ex.Message}");
            return;
        }

        // Req 8.8：不修改返回状态，仅将其设为新的唯一事实来源。
        _current = after;
        _settling = true;
        TurnSettled?.Invoke(after);

        // Req 8.5 扩展：上回合据守/布防的单位本回合起步前"挖出"停顿；随后记录本回合据守/布防单位供下一回合。
        _animator?.SetDigOutUnits(_prevDefenders);
        _prevDefenders = CollectDefenders(mergedOrders);

        if (_animator is null)
        {
            // 未接线动画器：直接同步显示到结算后状态（Req 8.6）。
            SyncDisplay(after);
            FinishTurn(after);
            return;
        }

        // 有动画器：播放阶段动画，结束后再同步显示（Req 8.6）。
        _ = PlayThenSyncAsync(before, after, mergedOrders);
    }

    /// <summary>
    /// 跳过当前阶段动画，直接呈现结算后的最终状态（Req 8.7）。仅在已接线动画器且正在结算时有意义。
    /// </summary>
    public void SkipAnimation() => _animator?.Skip();

    /// <summary>
    /// 播放阶段动画，播完（或异常）后同步显示到结算后状态并收尾（Req 8.6）。
    /// 动画属于表现层的“重建/近似”，其异常不得影响权威状态：无论如何都以 <paramref name="after"/> 收尾。
    /// </summary>
    private async Task PlayThenSyncAsync(
        GameState before,
        GameState after,
        IReadOnlyList<UnitOrder> orders)
    {
        try
        {
            await _animator!.PlayAsync(before, after, after.TurnLog, orders);
        }
        catch (Exception ex)
        {
            // 动画重建失败不影响权威状态：记录告警并仍以结算后状态收尾（Req 8.4/8.6）。
            GD.PushWarning($"阶段动画重建失败，直接同步到结算后状态：{ex.Message}");
        }

        SyncDisplay(after);
        FinishTurn(after);
    }

    /// <summary>
    /// 结算收尾：刷新临机机动/CP 状态、清空本回合待提交命令，并广播新回合状态（Req 12.2、13.2）。
    /// </summary>
    private void FinishTurn(GameState after)
    {
        // 刷新到结算后快照并清空上一回合待提交的临机机动（避免跨回合泄漏），同时刷新 CP 显示。
        _reposition?.UpdateState(after);

        _settling = false;
        TurnAdvanced?.Invoke(after);
    }

    /// <summary>
    /// 使地图/单位/迷雾显示与给定状态一致（Req 3.6/4.5/8.6/10.5）。敌方按可见度经
    /// <see cref="PresentationMapper.EnemyUnits"/> 过滤后交渲染器与迷雾层。
    /// </summary>
    private void SyncDisplay(GameState state)
    {
        _mapRenderer?.Render(_mapper.MapCells(state));

        var friendly = _mapper.FriendlyUnits(state, _viewer);
        var enemies = _mapper.EnemyUnits(state, _viewer);

        _unitRenderer?.Render(friendly, enemies);
        _fogView?.ApplyFog(enemies);
    }

    /// <summary>取占位对手本回合命令：按模式选择脚本化或本地热座（Req 7.1/7.2）。</summary>
    private IReadOnlyList<UnitOrder> ResolveOpponentOrders(GameState state)
    {
        var opponentSide = Opposite(_viewer);

        if (_opponentMode == OpponentMode.HotSeat && _hotSeatOrdersSource is not null)
        {
            return _opponent.HotSeatOrders(_hotSeatOrdersSource.Invoke());
        }

        // 默认脚本化（含热座但未接入命令来源的兜底）：敌方全部据守，无智能决策（Req 7.1/7.3）。
        return _opponent.ScriptedOrders(state, opponentSide);
    }

    /// <summary>合并己方与对手命令为单一集合（Req 7.4）。</summary>
    private static IReadOnlyList<UnitOrder> MergeOrders(
        IReadOnlyList<UnitOrder> friendly,
        IReadOnlyList<UnitOrder> opponent)
    {
        var merged = new List<UnitOrder>(friendly.Count + opponent.Count);
        merged.AddRange(friendly);
        merged.AddRange(opponent);
        return merged;
    }

    private static Side Opposite(Side side) => side == Side.Blue ? Side.Red : Side.Blue;

    /// <summary>收集本回合处于据守/布防姿态的单位（供下一回合动画的"挖出"停顿判定）。</summary>
    private static IReadOnlySet<UnitId> CollectDefenders(IReadOnlyList<UnitOrder> orders)
    {
        var defenders = new HashSet<UnitId>();
        foreach (var order in orders)
        {
            if (order.Command is Command.Hold or Command.MoveHold)
            {
                defenders.Add(order.Unit);
            }
        }

        return defenders;
    }
}
