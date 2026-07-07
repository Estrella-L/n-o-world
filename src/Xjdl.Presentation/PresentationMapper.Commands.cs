using Xjdl.Core.Hex;
using Xjdl.Core.State;

namespace Xjdl.Game.Presentation;

/// <summary>
/// <see cref="PresentationMapper"/> 的命令组装（任务 3.4，Req 6.5/6.8/6.9、13.2、17.3）。
/// 把表现层的下令意图组装为结构合法的 <see cref="UnitOrder"/>/<see cref="RepositionCommand"/>
/// （<b>Property 4</b> 接缝）。表现层仅做结构组装，路径/目标的规则合法性最终由 Core 在
/// <c>NextState</c> 校验（Req 6.13）。
/// </summary>
public sealed partial class PresentationMapper
{
    /// <summary>
    /// 组装移动命令（Req 6.5）：<see cref="UnitOrder.Command"/> 为 <see cref="Command.Move"/>，
    /// 携带传入的 <paramref name="path"/>（拷贝为不可变列表），<see cref="UnitOrder.Target"/> 为 <c>null</c>。
    /// </summary>
    /// <param name="unit">下令单位。</param>
    /// <param name="path">移动路径（<c>path[0]</c> 为离开出发格后进入的首格，依此类推）。不得为空且至少含一格。</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> 为 <c>null</c>。</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> 为空。</exception>
    public UnitOrder BuildMoveOrder(UnitId unit, IReadOnlyList<HexCoord> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Count == 0)
        {
            throw new ArgumentException("移动命令的路径不得为空。", nameof(path));
        }

        return new UnitOrder(unit, Command.Move, path.ToArray(), Target: null);
    }

    /// <summary>
    /// 组装移动布防命令（Req 3.2「移动到落点再据守」）：<see cref="UnitOrder.Command"/> 为
    /// <see cref="Command.MoveHold"/>，携带传入的 <paramref name="path"/>（拷贝为不可变列表），
    /// <see cref="UnitOrder.Target"/> 为 <c>null</c>。单位沿路径机动到落点后就地据守。
    /// </summary>
    /// <param name="unit">下令单位。</param>
    /// <param name="path">移动路径（<c>path[0]</c> 为离开出发格后进入的首格）。不得为空且至少含一格。</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> 为 <c>null</c>。</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> 为空。</exception>
    public UnitOrder BuildMoveHoldOrder(UnitId unit, IReadOnlyList<HexCoord> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (path.Count == 0)
        {
            throw new ArgumentException("移动布防命令的路径不得为空。", nameof(path));
        }

        return new UnitOrder(unit, Command.MoveHold, path.ToArray(), Target: null);
    }

    /// <summary>
    /// 组装进攻准备命令（Req 6.8）：<see cref="UnitOrder.Command"/> 为 <see cref="Command.AttackPrep"/>，
    /// 携带传入的 <paramref name="target"/>，<see cref="UnitOrder.Path"/> 为 <c>null</c>。
    /// </summary>
    /// <param name="unit">下令单位。</param>
    /// <param name="target">进攻准备目标格。</param>
    /// <param name="path">
    /// 可选的接敌路径（<c>path[0]</c> 为离开出发格后进入的首格，末格应为与目标相邻的落点）。
    /// 采用"移动接敌"模型：单位先沿此路径机动到目标相邻格，再发起进攻。已相邻（无需移动）时可为 <c>null</c>/空。
    /// </param>
    public UnitOrder BuildAttackPrepOrder(
        UnitId unit, HexCoord target, IReadOnlyList<HexCoord>? path = null) =>
        new(unit, Command.AttackPrep, path is { Count: > 0 } ? path.ToArray() : null, target);

    /// <summary>
    /// 组装据守命令（Req 6.9）：<see cref="UnitOrder.Command"/> 为 <see cref="Command.Hold"/>，
    /// <see cref="UnitOrder.Path"/> 与 <see cref="UnitOrder.Target"/> 均为 <c>null</c>。
    /// </summary>
    /// <param name="unit">下令单位。</param>
    public UnitOrder BuildHoldOrder(UnitId unit) =>
        new(unit, Command.Hold, Path: null, Target: null);

    /// <summary>
    /// 组装临机机动命令（Req 13.2）：产出携带传入 <paramref name="newPath"/> 与
    /// <paramref name="triggerTick"/> 的 <see cref="RepositionCommand"/>（仅改路径，不改命令性质，Req 13.5）。
    /// </summary>
    /// <param name="unit">下令单位。</param>
    /// <param name="newPath">重规划后的移动路径（拷贝为不可变列表）。不得为空且至少含一格。</param>
    /// <param name="triggerTick">触发时点，用于确定性回放（Req 13.3）。</param>
    /// <exception cref="ArgumentNullException"><paramref name="newPath"/> 为 <c>null</c>。</exception>
    /// <exception cref="ArgumentException"><paramref name="newPath"/> 为空。</exception>
    public RepositionCommand BuildReposition(UnitId unit, IReadOnlyList<HexCoord> newPath, int triggerTick)
    {
        ArgumentNullException.ThrowIfNull(newPath);
        if (newPath.Count == 0)
        {
            throw new ArgumentException("临机机动的路径不得为空。", nameof(newPath));
        }

        return new RepositionCommand(unit, newPath.ToArray(), triggerTick);
    }
}
