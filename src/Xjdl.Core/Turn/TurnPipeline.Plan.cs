using Xjdl.Core.State;

namespace Xjdl.Core.Turn;

/// <summary>
/// 阶段 0（计划下令）的校验逻辑（Req 3.2、4.1）。
/// 与 <see cref="TurnPipeline"/> 骨架合并为同一部分类，仅承载 Plan 阶段的纯校验，
/// 不修改输入状态（Req 2.1）。
/// </summary>
internal static partial class TurnPipeline
{
    /// <summary>
    /// 校验本回合的阶段 0 命令：每个在场单位必须恰好持有一条 <see cref="UnitOrder"/>（Req 3.2）。
    /// <para>缺失（某在场单位无命令）或重复（同一单位多条命令）均视为非法计划，抛出
    /// <see cref="InvalidOperationException"/>。此外，指向不存在单位的命令同样视为非法。</para>
    /// <para>本方法为纯校验，不修改任何状态；计划阶段不消耗指挥点（Req 4.1）由
    /// <see cref="Plan"/> 原样返回状态保证。</para>
    /// </summary>
    /// <param name="s">回合状态，其 <see cref="GameState.Units"/> 为全部在场单位。</param>
    /// <param name="cmds">本回合输入命令集合，其 <see cref="TurnCommands.Orders"/> 为阶段 0 命令。</param>
    /// <exception cref="ArgumentNullException"><paramref name="s"/> 或 <paramref name="cmds"/> 为空。</exception>
    /// <exception cref="InvalidOperationException">存在缺失、重复或指向不存在单位的命令。</exception>
    internal static void ValidateOrders(GameState s, TurnCommands cmds)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(cmds);

        var orders = cmds.Orders ?? throw new InvalidOperationException(
            "计划阶段命令集合 TurnCommands.Orders 不可为空（Req 3.2）。");

        // 在场单位 id 集合（按稳定 id 判定，Req 2.6）。
        var inPlay = new HashSet<UnitId>();
        foreach (var unit in s.Units)
        {
            inPlay.Add(unit.Id);
        }

        // 逐条校验命令：不得重复、不得指向不存在单位。
        var ordered = new HashSet<UnitId>();
        foreach (var order in orders)
        {
            var id = order.Unit;

            if (!inPlay.Contains(id))
            {
                throw new InvalidOperationException(
                    $"命令指向不存在的单位 {id}：每条命令都必须对应一个在场单位（Req 3.2）。");
            }

            if (!ordered.Add(id))
            {
                throw new InvalidOperationException(
                    $"单位 {id} 收到多条命令：每个在场单位恰好持有一条命令（Req 3.2）。");
            }
        }

        // 覆盖性校验：每个在场单位都必须被下令。
        if (ordered.Count != inPlay.Count)
        {
            foreach (var unit in s.Units)
            {
                if (!ordered.Contains(unit.Id))
                {
                    throw new InvalidOperationException(
                        $"在场单位 {unit.Id} 缺少命令：每个在场单位恰好持有一条命令（Req 3.2）。");
                }
            }
        }
    }
}
