using System.Collections.Generic;
using System.Threading.Tasks;
using Xjdl.Core.State;

namespace Xjdl.Game.Turn;

/// <summary>
/// 阶段动画器接缝（Req 8.4/8.5/8.7）。
/// <para>
/// <see cref="TurnController"/> 通过本接口驱动阶段 0–9 的「重建/近似」呈现，从而与具体的
/// <c>PhaseAnimator</c>（任务 13.2，<c>Node2D</c>）解耦：13.1 先行落地结算触发，13.2 完成后让
/// <c>PhaseAnimator</c> 实现本接口即可接线；在其就绪前 <see cref="TurnController"/> 允许该依赖为
/// <c>null</c> 并直接同步到结算后状态（Req 8.6）。
/// </para>
/// <para>
/// 动画由「回合前 <see cref="GameState"/> + 回合末 <see cref="GameState"/> + <see cref="TurnRecordEntry"/>
/// 日志」三源重建，Core 不逐阶段回吐中间快照；移动轨迹依提交的命令 <c>Path</c>/<c>NewPath</c> 重放、
/// 以回合末实际落点收尾（Req 8.4）。
/// </para>
/// </summary>
public interface IPhaseAnimator
{
    /// <summary>
    /// 依「回合前 + 回合末 + 回合日志」按阶段顺序重建呈现本回合结算（Req 8.4/8.5）。
    /// </summary>
    /// <param name="before">提交前（结算前）的对局状态。</param>
    /// <param name="after">单次 <c>NextState</c> 结算得到的回合末状态。</param>
    /// <param name="log">回合日志（通常为 <paramref name="after"/> 的 <see cref="GameState.TurnLog"/>）。</param>
    /// <param name="orders">本回合双方单位命令（供移动轨迹重放）。</param>
    Task PlayAsync(
        GameState before,
        GameState after,
        IReadOnlyList<TurnRecordEntry> log,
        IReadOnlyList<UnitOrder> orders);

    /// <summary>跳过动画，直接呈现结算后的最终状态（Req 8.7）。</summary>
    void Skip();
}
