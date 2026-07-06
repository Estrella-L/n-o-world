using Xjdl.Core.State;

namespace Xjdl.Game.Presentation;

/// <summary>
/// 占位对手：为完成一整回合结算提供敌方命令来源，<b>不含任何智能决策</b>（Req 7.3）。
/// <para>
/// 默认<b>脚本化</b>：为指定阵营的每个单位产出结构合法的 <see cref="Command.Hold"/> 命令
/// （据守，<see cref="UnitOrder.Path"/>/<see cref="UnitOrder.Target"/> 均为空，Req 7.1）。
/// </para>
/// <para>
/// 可选<b>本地热座</b>：直接采用玩家为红方逐单位下达的命令（Req 7.2）。
/// </para>
/// 不 <c>using Godot</c>，保持纯层可脱离引擎编译。
/// </summary>
public sealed class PlaceholderOpponent
{
    /// <summary>
    /// 默认脚本化命令：为 <paramref name="opponent"/> 阵营的每个单位产出一条
    /// <see cref="Command.Hold"/> 命令，无智能决策（Req 7.1/7.3）。
    /// 按 <see cref="GameState.Units"/> 的稳定顺序产出，保持确定性。
    /// </summary>
    /// <param name="s">当前对局状态（唯一事实来源）。</param>
    /// <param name="opponent">占位对手所属阵营。</param>
    /// <returns>该阵营每个单位一条 <c>Hold</c> 的命令集合；若无单位则为空集合。</returns>
    public IReadOnlyList<UnitOrder> ScriptedOrders(GameState s, Side opponent)
    {
        ArgumentNullException.ThrowIfNull(s);

        var orders = new List<UnitOrder>();
        foreach (var unit in s.Units)
        {
            if (unit.Owner == opponent)
            {
                orders.Add(new UnitOrder(unit.Id, Command.Hold, Path: null, Target: null));
            }
        }

        return orders;
    }

    /// <summary>
    /// 本地热座命令：直接采用玩家为红方逐单位下达的命令（Req 7.2）。
    /// 表现层不做智能决策，仅原样透传玩家已组装的结构合法命令。
    /// </summary>
    /// <param name="humanRedOrders">玩家为红方逐单位组装的命令集合。</param>
    /// <returns>与输入等价的命令集合（不可变快照）。</returns>
    public IReadOnlyList<UnitOrder> HotSeatOrders(IReadOnlyList<UnitOrder> humanRedOrders)
    {
        ArgumentNullException.ThrowIfNull(humanRedOrders);

        return humanRedOrders.ToArray();
    }
}
