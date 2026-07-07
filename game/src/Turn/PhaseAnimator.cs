using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Xjdl.Core.Hex;
using Xjdl.Core.State;
using Xjdl.Game.Presentation;
using Xjdl.Game.Presentation.ViewModels;
using Xjdl.Game.Render;

// 与 TurnController/UnitRenderer 同款约定：Godot 亦定义了 Godot.Side（Left/Top/Right/Bottom），
// 本文件“阵营”一律指 Xjdl.Core.State.Side，显式别名消歧。
using Side = Xjdl.Core.State.Side;

namespace Xjdl.Game.Turn;

/// <summary>
/// 移动节奏策略（Req 8.5 扩展）：决定某单位在动画的<b>每个 tick</b>内前进多少格。
/// 默认每 tick 前进 1 格（<see cref="UniformMovementPace"/>）；未来的高机动单位可返回 &gt;1
/// （例如"一帧移动两格"），从而在同一 tick 内比其它单位走得更远——各单位仍在同一 tick 内同时移动。
/// 实现应为纯函数（同一命令返回同一步数），保证动画可预期。
/// </summary>
public interface IMovementPace
{
    /// <summary>该命令对应单位每个动画 tick 前进的格数（应 &gt;= 1）。</summary>
    int CellsPerTick(UnitOrder order);
}

/// <summary>默认移动节奏：所有单位每 tick 前进固定格数（默认 1）。</summary>
public sealed class UniformMovementPace : IMovementPace
{
    private readonly int _cellsPerTick;

    /// <summary>构造统一节奏。<paramref name="cellsPerTick"/> 会被钳制到至少 1。</summary>
    public UniformMovementPace(int cellsPerTick = 1) => _cellsPerTick = Math.Max(1, cellsPerTick);

    /// <inheritdoc />
    public int CellsPerTick(UnitOrder order) => _cellsPerTick;
}

/// <summary>
/// 阶段动画器（节点层，Req 8.4/8.5/8.7）。实现 <see cref="IPhaseAnimator"/>，可直接接线到
/// <see cref="TurnController"/>。
/// <para>
/// <b>三源重建</b>（Req 8.4）：本动画器不依赖 Core 的逐阶段中间快照——Core 以单次
/// <c>NextState</c> 原子结算整个 WEGO 回合，仅回吐回合末 <see cref="GameState"/> 与其
/// <see cref="GameState.TurnLog"/>。故阶段 0–9 由「回合前 <c>before</c> + 回合末 <c>after</c> +
/// 回合日志 <c>log</c>」<b>重建/近似</b>呈现。
/// </para>
/// <para>
/// <b>呈现形式</b>（Req 8.5）：默认「分步高亮 + 中文文字提示 + 可跳过」，不做精细补间。移动阶段依
/// 玩家提交的 <see cref="UnitOrder.Path"/> 逐格重放，并<b>以回合末实际落点收尾</b>（被 Core 在结算时
/// 截断/停下的落点以 <c>after</c> 中该单位的 <see cref="UnitState.Position"/> 为准）；随后按回合日志
/// 顺序逐条呈现接触/选表/快照/战斗结果/撤退/推进/临机机动的高亮与中文提示。
/// </para>
/// <para>
/// <b>文案复用</b>：中文文案<b>不</b>在本类重复实现 <c>Kind → 中文</c> 的 switch，而是复用
/// <see cref="PresentationMapper.LogLines"/>（返回按 <c>TurnLog</c> 顺序、含 <c>Turn/Kind/Text/Locate</c>
/// 的 <see cref="LogLineView"/>），与 <c>TurnLogView</c> 保持一致。由于 <see cref="PresentationMapper.LogLines"/>
/// 由 <c>after.TurnLog</c> 生成，与传入的 <c>log</c> 同源同序，按序对齐即可。
/// </para>
/// <para>
/// <b>可跳过</b>（Req 8.7）：<see cref="Skip"/> 置位跳过标志，<see cref="PlayAsync"/> 在每个步骤前检查该
/// 标志并短路，最终收尾直达结算后最终态。
/// </para>
/// <para>
/// <b>接线（供任务 15.2 组装 <c>Match.tscn</c> 时完成）</b>：经 <see cref="Initialize"/> 注入
/// <see cref="PresentationMapper"/> 与 <see cref="HexLayout"/>（用于文案与格顶点换算），以及可空的视觉
/// 驱动引用——<see cref="HighlightLayer"/>（分步高亮）、提示用 <see cref="Label"/>、以及可选的
/// <see cref="UnitRenderer"/>/<see cref="MapRenderer"/>/<see cref="FogView"/>（用于收尾/跳过时直达最终态）。
/// 全部可选依赖均做空值检查（Req 8.6/8.7）。
/// </para>
/// </summary>
public partial class PhaseAnimator : Node2D, IPhaseAnimator
{
    private static readonly IReadOnlyList<IReadOnlyList<Vector2D>> EmptyCells =
        Array.Empty<IReadOnlyList<Vector2D>>();

    // ── 注入依赖（任务 15.2 场景组装接线）─────────────────────────────
    private PresentationMapper _mapper = null!;
    private HexLayout _layout = null!;
    private Side _viewer;

    private HighlightLayer? _highlightLayer;
    private Label? _promptLabel;
    private UnitRenderer? _unitRenderer;
    private MapRenderer? _mapRenderer;
    private FogView? _fogView;
    private CombatMarkerLayer? _combatMarkers;
    private Camera2D? _camera;

    // 移动节奏策略（每 tick 前进几格）。默认每 tick 1 格；可替换以支持高机动单位一 tick 多格。
    private IMovementPace _movementPace = new UniformMovementPace();

    // 本回合起步前需"挖出"停顿的单位（上回合处于据守/布防姿态）。由 TurnController 每回合注入。
    private IReadOnlySet<UnitId> _digOutUnits = new HashSet<UnitId>();

    // 每一步之间的停顿（秒）。<=0 视为不停顿（便于测试/快速回放）。
    private double _stepSeconds = 0.6;

    // 据守/布防单位本回合移动前的"挖出"停顿 tick 数（Req 8.5 扩展）。
    private const int DigOutDelayTicks = 2;

    private bool _initialized;

    // 跳过标志：由 Skip() 置位，PlayAsync 各步骤前检查以短路（Req 8.7）。
    // volatile 以确保跨 await 续体可见。
    private volatile bool _skipped;

    /// <summary>是否已注入依赖。</summary>
    public bool IsInitialized => _initialized;

    /// <summary>
    /// 移动节奏策略（Req 8.5 扩展）：决定每个动画 tick 内各单位前进的格数。默认
    /// <see cref="UniformMovementPace"/>（每 tick 1 格）。赋 <c>null</c> 时回退到默认。
    /// 这是为未来"高机动单位一 tick 多格"预留的接口。
    /// </summary>
    public IMovementPace MovementPace
    {
        get => _movementPace;
        set => _movementPace = value ?? new UniformMovementPace();
    }

    /// <inheritdoc />
    public void SetDigOutUnits(IReadOnlySet<UnitId> units) =>
        _digOutUnits = units ?? new HashSet<UnitId>();

    /// <summary>
    /// 注入依赖（供任务 15.2 组装场景时调用）。
    /// </summary>
    /// <param name="mapper">Core → 视图 DTO 的纯层映射器，用于复用中文日志文案（<see cref="PresentationMapper.LogLines"/>）与收尾同步显示。</param>
    /// <param name="layout">六角布局，用于把格坐标换算为高亮多边形顶点。</param>
    /// <param name="viewer">当前观察方阵营，收尾/跳过时按可见度重建显示。</param>
    /// <param name="highlightLayer">可选：分步高亮层（Req 8.5）。</param>
    /// <param name="promptLabel">可选：中文文字提示标签（Req 8.5）。</param>
    /// <param name="unitRenderer">可选：单位渲染器，用于收尾/跳过时直达最终态（Req 8.6/8.7）。</param>
    /// <param name="mapRenderer">可选：地图渲染器，用于收尾/跳过时直达最终态。</param>
    /// <param name="fogView">可选：迷雾呈现层，用于收尾/跳过时直达最终态。</param>
    /// <param name="combatMarkers">可选：战斗位置标识层，逐场结算时亮出/聚焦/消除标识（Req 8.5 扩展）。</param>
    /// <param name="camera">可选：对局相机，逐场结算时把镜头移到当前战斗格居中（Req 8.5 扩展）。</param>
    /// <param name="stepSeconds">每步停顿秒数（默认 0.6；<=0 表示不停顿）。</param>
    public void Initialize(
        PresentationMapper mapper,
        HexLayout layout,
        Side viewer,
        HighlightLayer? highlightLayer = null,
        Label? promptLabel = null,
        UnitRenderer? unitRenderer = null,
        MapRenderer? mapRenderer = null,
        FogView? fogView = null,
        CombatMarkerLayer? combatMarkers = null,
        Camera2D? camera = null,
        double stepSeconds = 0.6)
    {
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        _viewer = viewer;
        _highlightLayer = highlightLayer;
        _promptLabel = promptLabel;
        _unitRenderer = unitRenderer;
        _mapRenderer = mapRenderer;
        _fogView = fogView;
        _combatMarkers = combatMarkers;
        _camera = camera;
        _stepSeconds = stepSeconds;
        _initialized = true;
    }

    /// <inheritdoc />
    public async Task PlayAsync(
        GameState before,
        GameState after,
        IReadOnlyList<TurnRecordEntry> log,
        IReadOnlyList<UnitOrder> orders)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(orders);

        if (!_initialized)
        {
            throw new InvalidOperationException(
                $"{nameof(PhaseAnimator)} 尚未 {nameof(Initialize)}，无法播放阶段动画。");
        }

        // 每次播放重置跳过标志（Req 8.7）。
        _skipped = false;

        try
        {
            // 先把棋子渲染到回合前位置，作为移动动画的起点（避免仍停在上一次渲染态）。
            RenderUnits(before);

            // 阶段 2（机动/移动）：依提交路径沿轨迹平滑移动棋子，以回合末实际落点收尾（Req 8.4）。
            await ReplayMovementAsync(before, after, orders);

            // 阶段 2–8：按回合日志顺序逐条呈现高亮 + 中文提示（文案复用 PresentationMapper）。
            await ReplayLogAsync(after, log, orders);
        }
        finally
        {
            // 无论正常播完还是中途跳过，都收尾到结算后最终态（Req 8.6/8.7）。
            PresentFinalState(after);
            ClearHighlight();
        }
    }

    /// <inheritdoc />
    public void Skip() => _skipped = true;

    // ── 阶段重建 ──────────────────────────────────────────────────────

    /// <summary>
    /// 移动重放：对本回合的 <see cref="Command.Move"/> 命令，沿其提交的 <see cref="UnitOrder.Path"/>
    /// 逐格高亮，并以回合末 <paramref name="after"/> 中该单位的实际落点收尾（Req 8.4）。
    /// Core 不回吐逐格中间快照，故轨迹依提交路径近似重放；若计划路径与实际落点不一致（被截断/停下），
    /// 以实际落点截断/收尾。
    /// </summary>
    private async Task ReplayMovementAsync(
        GameState before, GameState after, IReadOnlyList<UnitOrder> orders)
    {
        // 组装每个移动单位的像素轨迹与其每 tick 步数（Req 8.4/8.5）。
        // 任何带路径的命令都重放移动：移动 / 移动布防 / 进攻准备（接敌路径），先把接敌移动放完再播战斗。
        var movers = new List<MoverAnim>();
        foreach (var order in orders)
        {
            if (order.Path is not { Count: > 0 } path)
            {
                continue;
            }

            var startPos = FindUnitPosition(before, order.Unit);
            var landing = FindUnitPosition(after, order.Unit);
            var cells = BuildTrajectory(path, landing);

            var centers = new List<Vector2>(cells.Count + 1);
            if (startPos is { } sp)
            {
                centers.Add(ToGodot(_layout.CenterOf(sp)));
            }

            foreach (var cell in cells)
            {
                centers.Add(ToGodot(_layout.CenterOf(cell)));
            }

            if (centers.Count == 0)
            {
                continue;
            }

            var pace = Math.Max(1, _movementPace.CellsPerTick(order));

            // 上回合处于据守/布防姿态的单位本回合起步前先"挖出"停顿若干 tick（Req 8.5 扩展）。
            var startDelay = _digOutUnits.Contains(order.Unit) ? DigOutDelayTicks : 0;

            movers.Add(new MoverAnim(order.Unit, centers, pace, startDelay));

            // 起始把棋子钉在起点覆盖位，使其脱离同格分组、独立沿轨迹移动。
            _unitRenderer?.SetAnimationOverride(order.Unit, centers[0]);
        }

        if (movers.Count == 0)
        {
            return;
        }

        SetPrompt("部队机动中…");

        // 逐 tick 同时推进所有移动单位：每 tick 内每个（已过挖出延迟且未到终点的）单位前进 pace 格，
        // 各自补间在同一 tick 内并发播放（高机动单位一 tick 走更多格 → 同时移动、走得更远）。
        var tick = 0;
        while (!_skipped)
        {
            var stillActive = false;
            var tweens = new List<Tween>();

            foreach (var mover in movers)
            {
                if (tick < mover.StartDelayTicks)
                {
                    stillActive = true; // 仍在挖出停顿中，尚未起步
                    continue;
                }

                if (mover.Index >= mover.Centers.Count - 1)
                {
                    continue; // 已到终点
                }

                var next = Math.Min(mover.Index + mover.Pace, mover.Centers.Count - 1);
                var tween = StartTokenTween(mover.Unit, mover.Centers[mover.Index], mover.Centers[next]);
                mover.Index = next;
                if (next < mover.Centers.Count - 1)
                {
                    stillActive = true; // 尚有后续格要走
                }

                if (tween is not null)
                {
                    tweens.Add(tween);
                }
            }

            if (tweens.Count > 0)
            {
                await AwaitTweensAsync(tweens);
            }
            else if (stillActive)
            {
                await StepDelayAsync(); // 本 tick 无移动（都在挖出停顿）→ 等一个 tick 的时长
            }
            else
            {
                break; // 全部单位已抵达终点
            }

            tick++;
        }

        // 收尾：所有单位钉在各自落点（跳过时也就位）；随后 PresentFinalState 清覆盖并按结算态重绘。
        foreach (var mover in movers)
        {
            _unitRenderer?.SetAnimationOverride(mover.Unit, mover.Centers[^1]);
        }
    }

    /// <summary>
    /// 为单位启动一段位置补间并返回该 <see cref="Tween"/>（自动运行）；已跳过、无渲染器、不在场景树或
    /// 停顿 &lt;=0 时直接就位到 <paramref name="to"/> 并返回 <c>null</c>（Req 8.7）。
    /// </summary>
    private Tween? StartTokenTween(UnitId unit, Vector2 from, Vector2 to)
    {
        if (_unitRenderer is null)
        {
            return null;
        }

        SceneTree? tree = GetTree();
        if (_skipped || _stepSeconds <= 0.0 || tree is null)
        {
            _unitRenderer.SetAnimationOverride(unit, to);
            return null;
        }

        UnitRenderer renderer = _unitRenderer;
        Tween tween = CreateTween();
        tween.TweenMethod(
            Callable.From((Vector2 center) => renderer.SetAnimationOverride(unit, center)),
            from,
            to,
            _stepSeconds);
        return tween;
    }

    /// <summary>
    /// 等待本 tick 全部并发补间结束。各补间已在同一帧启动、时长相同（<see cref="_stepSeconds"/>），
    /// 故逐个 await 的总耗时约等于单段时长——达成"同时移动"。已跳过时立即短路（Req 8.7）。
    /// </summary>
    private async Task AwaitTweensAsync(IReadOnlyList<Tween> tweens)
    {
        foreach (var tween in tweens)
        {
            if (_skipped)
            {
                return;
            }

            await ToSignal(tween, Tween.SignalName.Finished);
        }
    }

    /// <summary>按可见度把某状态的己方/敌方单位渲染到棋子层与迷雾层（可选渲染器缺席时安全跳过）。</summary>
    private void RenderUnits(GameState state)
    {
        if (_unitRenderer is null && _fogView is null)
        {
            return;
        }

        var friendly = _mapper.FriendlyUnits(state, _viewer);
        var enemies = _mapper.EnemyUnits(state, _viewer);
        _unitRenderer?.Render(friendly, enemies);
        _fogView?.ApplyFog(enemies);
    }

    /// <summary>
    /// 日志重放：复用 <see cref="PresentationMapper.LogLines"/> 生成的中文文案，按 <c>TurnLog</c> 顺序
    /// 逐条呈现（Req 8.5、9.2）。每条设置提示文字并高亮其关联格（<see cref="LogLineView.Locate"/>）。
    /// </summary>
    private async Task ReplayLogAsync(
        GameState after, IReadOnlyList<TurnRecordEntry> log, IReadOnlyList<UnitOrder> orders)
    {
        // 1) 非战斗前置事件（临机机动 / 接敌锁定 / 战力快照）：沿用通用提示 + 高亮
        //    （复用映射层中文文案，与 TurnLogView 一致）。战斗类条目留待第 2 步逐场呈现。
        foreach (LogLineView line in _mapper.LogLines(after))
        {
            if (_skipped)
            {
                return;
            }

            if (PresentationMapper.IsCombatKind(line.Kind))
            {
                continue;
            }

            SetPrompt(line.Text);
            if (line.Locate is { } cell)
            {
                HighlightCell(cell);
            }
            else
            {
                ClearHighlight();
            }

            await StepDelayAsync();
        }

        // 2) 战斗：接触阶段一次性亮出全部战斗标识，随后一场一场结算
        //    （镜头居中 → 描述双方交战的引导文本 → 分步结果 → 消除该场标识）。
        IReadOnlyList<BattleView> battles = _mapper.Battles(after);
        if (battles.Count == 0)
        {
            return;
        }

        ShowAllCombatMarkers(battles);
        ClearHighlight();

        foreach (BattleView battle in battles)
        {
            if (_skipped)
            {
                break;
            }

            _combatMarkers?.SetFocus(battle.Cell);

            // 点亮该场各进攻格的主攻单位（多个单位进攻一个敌人时显示主攻，Req 9.2）。
            _unitRenderer?.SetMainHighlight(BattleMainAttackers(after, orders, battle.Cell));

            if (battle.Center is { } center)
            {
                await CenterCameraAsync(center);
            }

            // 一小段描述双方交战的引导文本。
            SetPrompt(battle.Narrative);
            await StepDelayAsync();

            // 该场的分步结果（选表/读表/撤退/推进）。
            foreach (string step in battle.Steps)
            {
                if (_skipped)
                {
                    break;
                }

                SetPrompt(step);
                await StepDelayAsync();
            }

            _combatMarkers?.ResolveMarker(battle.Cell);
        }

        _combatMarkers?.Clear();
        _unitRenderer?.SetMainHighlight(System.Array.Empty<UnitId>());
    }

    /// <summary>
    /// 某场战斗各进攻格的主攻单位集合（Req 9.2）：取所有进攻该目标格的进攻准备单位，按落点分格，
    /// 每格取进攻力最高（并列取最小 id）者。用于逐场结算时点亮主攻。
    /// </summary>
    private static IReadOnlyCollection<UnitId> BattleMainAttackers(
        GameState after, IReadOnlyList<UnitOrder> orders, HexCoord? cell)
    {
        var result = new HashSet<UnitId>();
        if (cell is not { } target)
        {
            return result;
        }

        var mainByCell = new Dictionary<HexCoord, UnitState>();
        foreach (var order in orders)
        {
            if (order.Command != Command.AttackPrep || order.Target is not { } t || t != target)
            {
                continue;
            }

            var unit = FindUnit(after, order.Unit);
            if (unit is null)
            {
                continue;
            }

            if (!mainByCell.TryGetValue(unit.Position, out var current)
                || unit.Attack > current.Attack
                || (unit.Attack == current.Attack && unit.Id.Value < current.Id.Value))
            {
                mainByCell[unit.Position] = unit;
            }
        }

        foreach (var main in mainByCell.Values)
        {
            result.Add(main.Id);
        }

        return result;
    }

    private static UnitState? FindUnit(GameState s, UnitId id)
    {
        foreach (var unit in s.Units)
        {
            if (unit.Id == id)
            {
                return unit;
            }
        }

        return null;
    }

    /// <summary>把本回合全部可定位战斗投影为标识并一次性亮出（Req 8.5 扩展）。</summary>
    private void ShowAllCombatMarkers(IReadOnlyList<BattleView> battles)
    {
        if (_combatMarkers is null)
        {
            return;
        }

        var markers = new List<CombatMarkerView>(battles.Count);
        foreach (var battle in battles)
        {
            if (battle.Cell is { } cell && battle.Center is { } center)
            {
                markers.Add(new CombatMarkerView(cell, center, battle.Radius));
            }
        }

        _combatMarkers.ShowMarkers(markers);
    }

    /// <summary>
    /// 把镜头平滑移到给定像素点居中（Req 8.5 扩展）。已跳过、无相机、不在场景树或停顿 &lt;=0 时
    /// 直接就位，保证 <see cref="Skip"/> 后迅速短路。
    /// </summary>
    private async Task CenterCameraAsync(Vector2D center)
    {
        if (_camera is null)
        {
            return;
        }

        var target = ToGodot(center);
        SceneTree? tree = GetTree();
        if (_skipped || _stepSeconds <= 0.0 || tree is null)
        {
            _camera.Position = target;
            return;
        }

        // 镜头移动比每步停顿更短促，避免拖慢节奏。
        var duration = _stepSeconds < 0.35 ? _stepSeconds : 0.35;
        Tween tween = CreateTween();
        tween.TweenProperty(_camera, "position", target, duration);
        await ToSignal(tween, Tween.SignalName.Finished);
    }

    /// <summary>
    /// 由提交路径与实际落点构造重放轨迹：截断于实际落点（若落点在路径中），否则以落点收尾追加。
    /// 落点未知（单位已阵亡/不在回合末状态中）时按提交路径原样重放。
    /// </summary>
    private static IReadOnlyList<HexCoord> BuildTrajectory(
        IReadOnlyList<HexCoord> path,
        HexCoord? landing)
    {
        if (landing is not { } dest)
        {
            return path;
        }

        var result = new List<HexCoord>(path.Count + 1);
        foreach (var cell in path)
        {
            result.Add(cell);
            if (cell == dest)
            {
                // 实际落点早于计划终点：以实际落点截断（被 Core 截断/停下的情形）。
                return result;
            }
        }

        // 计划路径未抵达实际落点：以实际落点收尾。
        if (result.Count == 0 || result[^1] != dest)
        {
            result.Add(dest);
        }

        return result;
    }

    private static HexCoord? FindUnitPosition(GameState s, UnitId id)
    {
        foreach (var unit in s.Units)
        {
            if (unit.Id == id)
            {
                return unit.Position;
            }
        }

        return null;
    }

    // ── 视觉驱动（全部空值安全）─────────────────────────────────────────

    /// <summary>
    /// 收尾/跳过时把地图/单位/迷雾直达结算后最终态（Req 8.6/8.7）。敌方按可见度经
    /// <see cref="PresentationMapper.EnemyUnits"/> 过滤。可选渲染器缺席时安全跳过。
    /// </summary>
    private void PresentFinalState(GameState after)
    {
        // 清除移动动画的位置覆盖、主攻高亮与战斗标识，使显示回到结算后最终态（Req 8.4/8.6/8.7）。
        _unitRenderer?.ClearAnimationOverrides();
        _unitRenderer?.SetMainHighlight(System.Array.Empty<UnitId>());
        _combatMarkers?.Clear();

        _mapRenderer?.Render(_mapper.MapCells(after));

        if (_unitRenderer is not null || _fogView is not null)
        {
            var friendly = _mapper.FriendlyUnits(after, _viewer);
            var enemies = _mapper.EnemyUnits(after, _viewer);
            _unitRenderer?.Render(friendly, enemies);
            _fogView?.ApplyFog(enemies);
        }

        ClearPrompt();
    }

    /// <summary>高亮单个格（作为当前步骤的选中高亮）。<see cref="HighlightLayer"/> 或布局缺席时跳过。</summary>
    private void HighlightCell(HexCoord cell)
    {
        if (_highlightLayer is null)
        {
            return;
        }

        var corners = _layout.CornersOf(cell);
        _highlightLayer.Show(corners, EmptyCells, EmptyCells);
    }

    private void ClearHighlight() => _highlightLayer?.Clear();

    private void SetPrompt(string text)
    {
        if (_promptLabel is null)
        {
            return;
        }

        _promptLabel.Text = text;
        _promptLabel.Visible = true;
    }

    private void ClearPrompt()
    {
        if (_promptLabel is null)
        {
            return;
        }

        _promptLabel.Text = string.Empty;
    }

    /// <summary>
    /// 步骤间停顿（Req 8.5）：经 <c>SceneTreeTimer</c> 等待。已跳过、不在场景树中或停顿 <=0 时不等待，
    /// 使 <see cref="Skip"/> 后能迅速短路直达最终态（Req 8.7）。
    /// </summary>
    private async Task StepDelayAsync()
    {
        if (_skipped || _stepSeconds <= 0.0)
        {
            return;
        }

        SceneTree? tree = GetTree();
        if (tree is null)
        {
            return;
        }

        await ToSignal(tree.CreateTimer(_stepSeconds), SceneTreeTimer.SignalName.Timeout);
    }

    private static Vector2 ToGodot(Vector2D v) => new((float)v.X, (float)v.Y);

    /// <summary>单个移动单位的动画进度（像素轨迹 + 每 tick 步数 + 起步前挖出停顿 + 当前索引）。</summary>
    private sealed class MoverAnim
    {
        public MoverAnim(UnitId unit, IReadOnlyList<Vector2> centers, int pace, int startDelayTicks)
        {
            Unit = unit;
            Centers = centers;
            Pace = pace;
            StartDelayTicks = startDelayTicks;
        }

        public UnitId Unit { get; }

        /// <summary>像素轨迹：起点中心 + 逐格中心。</summary>
        public IReadOnlyList<Vector2> Centers { get; }

        /// <summary>每个 tick 前进的格数（&gt;= 1）。</summary>
        public int Pace { get; }

        /// <summary>起步前的挖出停顿 tick 数（据守/布防单位本回合移动时先停顿）。</summary>
        public int StartDelayTicks { get; }

        /// <summary>当前所在轨迹索引（0 起点）。</summary>
        public int Index { get; set; }
    }
}
