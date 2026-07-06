using System;
using System.Collections.Generic;
using Godot;
using Xjdl.Core.Hex;
using Xjdl.Core.State;
using Xjdl.Game.Presentation;
using Xjdl.Game.Presentation.ViewModels;

// Godot 亦定义了 Godot.Side（Left/Top/Right/Bottom），与规则引擎的阵营枚举同名。
// 本文件的“阵营”一律指 Xjdl.Core.State.Side，显式别名消歧（见 UnitRenderer 同款约定）。
using Side = Xjdl.Core.State.Side;

namespace Xjdl.Game.Input;

/// <summary>
/// 临机机动操作入口（节点层，Req 13.1–13.5、12.2）。
/// <para>
/// 对<b>未锁定</b>的己方单位，允许玩家花费指挥点（CP）为其<b>重规划移动路径</b>，产出携带触发时点
/// （<see cref="RepositionCommand.TriggerTick"/>）的 <see cref="RepositionCommand"/>，并在调用
/// <c>RulesEngine.NextState</c> <b>之前</b>纳入本回合待提交列表（<see cref="Pending"/>），由
/// <c>TurnController</c> 合并进 <c>TurnCommands.Repositions</c>、交 Core 在原子结算时按触发时点应用
/// （Req 13.1/13.2）。
/// </para>
/// <para>
/// <b>语义约束（Req 13.3）</b>：本控制器<b>绝不</b>在已开始播放的阶段动画中途实时改变棋子行进方向。
/// 玩家在动画阶段的任何“实时改动”在语义上<b>等价于</b>“提交一条将在下次 <c>NextState</c> 结算时生效的
/// <see cref="RepositionCommand"/>”——即向 <see cref="Pending"/> 追加一条命令，而非逐帧自由操控。
/// </para>
/// <para>
/// <b>只改路径、不改性质（Req 13.5）</b>：<see cref="TryReposition"/> 仅经
/// <see cref="PresentationMapper.BuildReposition"/> 组装“改路径”的命令；被调度单位的命令性质
/// （<see cref="Command"/>）不变，路径合法性与实际落点最终由 Core 校验并结算（Req 6.13）。
/// </para>
/// <para>
/// <b>CP 预检（Req 13.4）</b>：提交前读 <see cref="GameState.Cards"/> 中当前观察方的
/// <see cref="CardState.Cp"/> 做预检；不足以支付一次临机机动时<b>拒绝并提示</b>（<see cref="RepositionRejected"/>），
/// 且<b>不组装</b> <see cref="RepositionCommand"/>。<b>权威扣除与最终校验仍由 Core 结算</b>
/// （<c>TurnPipeline</c> 的 <c>repositionCost</c>，接入数据层前为固定 1 点）；这里的
/// <see cref="RepositionCpCost"/> 仅用于与 Core 一致的预检与视图投影，不做实际扣费。
/// </para>
/// <para>
/// <b>接线（供任务 15.2 组装 <c>Match.tscn</c> 时完成）</b>：通过 <see cref="Initialize"/> 注入
/// <see cref="PresentationMapper"/>、当前 <see cref="GameState"/>、观察方 <see cref="Side"/> 与触发时点来源；
/// 可选注入 <see cref="PathPlanner"/> 以复用“拖拽画箭头”重规划路径。<see cref="CommandPointsChanged"/>
/// 事件供 <c>CommandPointView</c>（任务 14.3）订阅以刷新 CP 显示（Req 12.2）——本控制器不硬依赖尚未
/// 存在的视图类型，仅暴露事件/回调，由 14.3/15.2 接线。
/// </para>
/// </summary>
public partial class RepositionController : Node2D
{
    /// <summary>
    /// 单次临机机动的指挥点预检代价，与 Core <c>TurnPipeline</c> 的 <c>RepositionCpCost</c>（默认 1）保持一致。
    /// <b>仅用于表现层预检与 CP 视图投影，Core 才是权威扣费方</b>（Req 13.4）。<c>repositionCost</c>
    /// 接入数据层后，此处应改为从配置读取以保持一致。
    /// </summary>
    public const int RepositionCpCost = 1;

    /// <summary>Core 回合日志中“单位接敌锁定”的条目类别键（与 <c>TurnPipeline</c> 的 <c>LockedKind</c> 一致）。</summary>
    private const string LockedKind = "Locked";

    private readonly List<RepositionCommand> _pending = new();

    private PresentationMapper? _mapper;
    private GameState? _state;
    private Side _viewer;
    private Func<int>? _triggerTickSource;
    private PathPlanner? _pathPlanner;

    /// <summary>
    /// 临机机动使 CP 待消耗量变化时触发，供 <c>CommandPointView</c>（任务 14.3）刷新（Req 12.2）。
    /// 由 14.3/15.2 订阅接线；本控制器不硬依赖该视图类型。
    /// </summary>
    public event Action? CommandPointsChanged;

    /// <summary>
    /// 一次临机机动被拒绝时触发，携带面向玩家的提示原因（Req 13.4）。由 UI 订阅显示提示。
    /// </summary>
    public event Action<string>? RepositionRejected;

    /// <summary>
    /// 本回合已收集、待提交的临机机动命令（Req 13.2）。<c>TurnController</c> 在调用
    /// <c>NextState</c> 前读取并合并进 <c>TurnCommands.Repositions</c>，提交后调用
    /// <see cref="ClearPending"/> 清空。每个单位至多保留一条（后提交者替换先前者）。
    /// </summary>
    public IReadOnlyList<RepositionCommand> Pending => _pending;

    /// <summary>
    /// 本回合待提交临机机动预计消耗的指挥点总量（= <see cref="Pending"/> 条数 × <see cref="RepositionCpCost"/>）。
    /// 供 <c>CommandPointView</c> 展示“消耗后可用数量”（Req 12.2）；权威扣费仍由 Core 结算。
    /// </summary>
    public int PendingCpCost => _pending.Count * RepositionCpCost;

    /// <summary>
    /// 注入依赖（供任务 15.2 组装场景时调用）。
    /// </summary>
    /// <param name="mapper">命令组装用的纯层映射器（<see cref="PresentationMapper.BuildReposition"/>）。</param>
    /// <param name="state">当前对局状态（读单位/阵营/锁定日志/CP，唯一事实来源）。</param>
    /// <param name="viewer">当前观察方阵营（临机机动仅对该方己方单位开放）。</param>
    /// <param name="triggerTickSource">触发时点来源；<c>null</c> 时默认使用时点 0。由 15.2 接入回合时序。</param>
    /// <param name="pathPlanner">可选：复用“拖拽画箭头”重规划路径的纯层规划器（Req 13.1）。</param>
    public void Initialize(
        PresentationMapper mapper,
        GameState state,
        Side viewer,
        Func<int>? triggerTickSource = null,
        PathPlanner? pathPlanner = null)
    {
        _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _viewer = viewer;
        _triggerTickSource = triggerTickSource;
        _pathPlanner = pathPlanner;
    }

    /// <summary>
    /// 更新到新的对局快照（新回合/结算后）。刷新状态并清空上一回合的待提交列表，
    /// 避免过期命令跨回合泄漏；随后通知 CP 视图刷新（Req 12.2）。
    /// </summary>
    public void UpdateState(GameState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _pathPlanner = null; // 旧规划器绑定的是过期快照，作废等待重新注入。
        ClearPending();
    }

    /// <summary>
    /// 供可选的“拖拽画箭头”重规划流程复用的纯层规划器（Req 13.1）。由 15.2 以当前快照构造后注入。
    /// </summary>
    public void SetPathPlanner(PathPlanner pathPlanner)
        => _pathPlanner = pathPlanner ?? throw new ArgumentNullException(nameof(pathPlanner));

    /// <summary>
    /// 尝试为一个未锁定的己方单位发起临机机动（Req 13.1–13.5）。
    /// <para>
    /// 依次校验：单位存在且属当前观察方（Req 13.1）→ 未被锁定（Req 13.1）→ 路径非空 →
    /// CP 预检足以支付一次临机机动（Req 13.4）。任一不满足则<b>拒绝、经
    /// <see cref="RepositionRejected"/> 提示、且不组装命令</b>并返回 <c>false</c>。
    /// </para>
    /// <para>
    /// 通过后经 <see cref="PresentationMapper.BuildReposition"/> 组装携带 <c>TriggerTick</c> 的命令
    /// （仅改路径、不改性质，Req 13.2/13.5），纳入 <see cref="Pending"/>（同一单位替换旧命令），
    /// 并触发 <see cref="CommandPointsChanged"/> 供 CP 视图刷新（Req 12.2），返回 <c>true</c>。
    /// </para>
    /// <para>
    /// 本方法<b>不</b>在动画中途实时改向：它只是把一条“将在下次结算生效”的命令入列（Req 13.3）。
    /// </para>
    /// </summary>
    /// <param name="unit">被调度的己方单位。</param>
    /// <param name="newPath">重规划后的移动路径（不得为空）。</param>
    /// <returns>成功入列返回 <c>true</c>；被拒绝返回 <c>false</c>。</returns>
    public bool TryReposition(UnitId unit, IReadOnlyList<HexCoord> newPath)
    {
        if (_mapper is null || _state is null)
        {
            // 未接线即调用属编程错误（不是玩家可触发的规则内失败）。
            throw new InvalidOperationException(
                $"{nameof(RepositionController)} 尚未 {nameof(Initialize)}，无法发起临机机动。");
        }

        var target = FindUnit(_state, unit);

        // Req 13.1：仅对己方单位开放。
        if (target is null || target.Owner != _viewer)
        {
            Reject($"单位 {unit.Value} 非当前方单位，无法临机机动。");
            return false;
        }

        // Req 13.1：仅对未锁定单位开放（Core 以 TurnLog 的 Locked 条目表达锁定，UnitState 无锁定字段）。
        if (IsLocked(_state, unit))
        {
            Reject($"单位 {unit.Value} 已接敌锁定，无法临机机动。");
            return false;
        }

        if (newPath is null || newPath.Count == 0)
        {
            Reject($"单位 {unit.Value} 的临机机动路径为空，操作无效。");
            return false;
        }

        // Req 13.4：CP 预检；不足则拒绝、提示、不组装命令（权威扣费由 Core 结算）。
        var availableCp = _mapper.CommandPoints(_state, _viewer);
        if (availableCp - PendingCpCostExcluding(unit) < RepositionCpCost)
        {
            Reject($"指挥点不足（需 {RepositionCpCost}，当前可用 {availableCp - PendingCpCostExcluding(unit)}），无法临机机动。");
            return false;
        }

        // Req 13.2/13.5：仅改路径、组装携带触发时点的命令（命令性质不变）。
        var command = _mapper.BuildReposition(unit, newPath, NextTriggerTick());

        // 同一单位至多保留一条待提交命令：后提交者替换先前者（Core 亦按单位去重）。
        _pending.RemoveAll(c => c.Unit == unit);
        _pending.Add(command);

        // Req 12.2：CP 待消耗量变化，通知 CommandPointView 刷新。
        CommandPointsChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// 撤销某单位本回合的待提交临机机动（若有）。撤销后通知 CP 视图刷新（Req 12.2）。
    /// </summary>
    /// <returns>确有命令被撤销返回 <c>true</c>。</returns>
    public bool CancelReposition(UnitId unit)
    {
        var removed = _pending.RemoveAll(c => c.Unit == unit) > 0;
        if (removed)
        {
            CommandPointsChanged?.Invoke();
        }

        return removed;
    }

    /// <summary>
    /// 清空待提交列表（<c>TurnController</c> 在提交本回合命令后调用）。清空后通知 CP 视图刷新。
    /// </summary>
    public void ClearPending()
    {
        if (_pending.Count == 0)
        {
            return;
        }

        _pending.Clear();
        CommandPointsChanged?.Invoke();
    }

    /// <summary>
    /// 可选的“拖拽画箭头”重规划入口（Req 13.1）：复用注入的 <see cref="PathPlanner"/> 从单位起点朝
    /// <paramref name="cursorPixel"/> 逐格生成合法路径，再走 <see cref="TryReposition"/> 的全部校验与组装。
    /// 未注入 <see cref="PathPlanner"/> 或规划结果仅含起点（未离开出发格）时拒绝。
    /// </summary>
    /// <param name="unit">被调度的己方单位。</param>
    /// <param name="cursorPixel">光标像素坐标（世界坐标系，已由调用方换算）。</param>
    /// <returns>成功入列返回 <c>true</c>；被拒绝返回 <c>false</c>。</returns>
    public bool TryRepositionTo(UnitId unit, Vector2D cursorPixel)
    {
        if (_pathPlanner is null)
        {
            Reject("未接入路径规划器，无法以拖拽方式发起临机机动。");
            return false;
        }

        var draft = _pathPlanner.Extend(_pathPlanner.Begin(unit), cursorPixel);

        // 仅含起点表示未离开出发格，等价于未规划出有效新路径。
        if (draft.Path.Count <= 1)
        {
            Reject($"单位 {unit.Value} 未规划出有效的新路径。");
            return false;
        }

        // 去掉起点，得到“离开出发格后的移动路径”，与 BuildMoveOrder/BuildReposition 的路径约定一致。
        var newPath = new List<HexCoord>(draft.Path.Count - 1);
        for (var i = 1; i < draft.Path.Count; i++)
        {
            newPath.Add(draft.Path[i]);
        }

        return TryReposition(unit, newPath);
    }

    private int NextTriggerTick() => _triggerTickSource?.Invoke() ?? 0;

    private int PendingCpCostExcluding(UnitId unit)
    {
        // 若该单位已有待提交命令，则本次为“替换”而非“新增”，预检时不应重复计其代价。
        var count = _pending.Count;
        foreach (var c in _pending)
        {
            if (c.Unit == unit)
            {
                count--;
                break;
            }
        }

        return count * RepositionCpCost;
    }

    private void Reject(string reason) => RepositionRejected?.Invoke(reason);

    /// <summary>
    /// 判定单位是否已锁定：Core 以 <see cref="GameState.TurnLog"/> 的 <c>Locked</c> 条目表达锁定
    /// （<see cref="UnitState"/> 无锁定字段）。存在该单位的 <c>Locked</c> 条目即视为锁定（Req 13.1）。
    /// </summary>
    private static bool IsLocked(GameState s, UnitId unit)
    {
        foreach (var entry in s.TurnLog)
        {
            if (entry.Kind == LockedKind && entry.Unit == unit)
            {
                return true;
            }
        }

        return false;
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
}
