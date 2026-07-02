using Xjdl.Core.Hex;
using Xjdl.Core.State;

namespace Xjdl.Core.Turn;

/// <summary>
/// 控制区（ZOC）产生与路径拦截的纯函数计算（Req 11.1–11.5、11.7）。
/// 见 docs/01-战斗机制.md〈控制区〉与 design.md〈阶段 2 机动 / Property 39〉。
/// <para>
/// 产生规则：据守单位在其 6 个相邻格产生全向控制区（Req 11.1）；进攻准备单位当且仅当
/// 其目标「机动阶段开始即相邻」且「本回合实际未移动」时同样产生全向控制区（Req 11.2）；
/// 下达移动命令的单位、以及已实施接敌移动的进攻准备单位均不产生控制区（Req 11.3）。
/// </para>
/// <para>
/// 判定一律以机动阶段开始时的快照位置为准（Req 11.7）：产生方本身静止，故其快照位置
/// 即当前位置，控制区即该位置的 6 个相邻格。
/// </para>
/// <para>
/// 拦截规则：移动路径进入敌方控制区格的单位立即停在首个进入的控制区格，本回合不再前进
/// （Req 11.4）；持 <see cref="NightFlags.IgnoreZoc"/> 标志者无视控制区正常穿越（Req 11.5）。
/// </para>
/// <remarks>
/// 本类为纯函数、确定性实现（整数运算、稳定排序，Req 2.5/2.6），仅接受计算所需的最小输入。
/// 与回合流水线的完整接线（快照采集、移动集合、逐格推进锁定）在任务 15.20 落地，届时由
/// <c>TurnPipeline</c> 提供快照位置与实际移动集合并调用本类。
/// </remarks>
/// </summary>
public static class ZoneOfControl
{
    /// <summary>
    /// 依 <c>(Q, R)</c> 字典序对六角格排序，保证控制区格集合输出的确定性（Req 2.6）。
    /// </summary>
    private static readonly IComparer<HexCoord> HexOrder =
        Comparer<HexCoord>.Create(static (a, b) =>
        {
            var byQ = a.Q.CompareTo(b.Q);
            return byQ != 0 ? byQ : a.R.CompareTo(b.R);
        });

    /// <summary>
    /// 判定单个单位在本回合是否产生控制区（Req 11.1–11.3）。
    /// </summary>
    /// <param name="command">该单位在阶段 0 领取的命令。</param>
    /// <param name="snapshotPosition">机动阶段开始时的快照位置（Req 11.7）。</param>
    /// <param name="attackPrepTarget">
    /// 进攻准备的目标格（取自 <see cref="UnitOrder.Target"/>）；非进攻准备命令可为空。
    /// </param>
    /// <param name="movedThisTurn">该单位本回合是否实际发生过移动（逐格推进过至少一步）。</param>
    /// <returns>产生全向控制区返回 <c>true</c>，否则 <c>false</c>。</returns>
    public static bool ProducesZoc(
        Command command,
        HexCoord snapshotPosition,
        HexCoord? attackPrepTarget,
        bool movedThisTurn)
    {
        switch (command)
        {
            case Command.Hold:
                // 据守单位始终产生控制区（Req 11.1）。
                return true;

            case Command.AttackPrep:
                // 已实施接敌移动的进攻准备不产生控制区（Req 11.3）。
                if (movedThisTurn)
                {
                    return false;
                }

                // 目标开局即相邻（快照距离为 1）且未移动，产生控制区（Req 11.2）。
                return attackPrepTarget is { } target &&
                       HexCoord.Distance(snapshotPosition, target) == 1;

            case Command.Move:
            default:
                // 下达移动命令的单位不产生控制区（Req 11.3）。
                return false;
        }
    }

    /// <summary>
    /// 计算指定阵营在本回合产生的全部控制区格（Req 11.1–11.3、11.7）。
    /// </summary>
    /// <param name="units">全场单位现状，用于取阵营、命令与标志。</param>
    /// <param name="side">要计算控制区的阵营（其产生的控制区将封锁敌方移动）。</param>
    /// <param name="snapshotPositions">
    /// 机动阶段开始时各单位的快照位置（Req 11.7）。缺失某单位时回退到其当前
    /// <see cref="UnitState.Position"/>（产生方静止，二者一致）。
    /// </param>
    /// <param name="orders">
    /// 本回合单位命令，提供进攻准备的目标格（<see cref="UnitOrder.Target"/>）。
    /// 键为单位 id；缺失或非进攻准备时目标视为空。
    /// </param>
    /// <param name="movedUnits">本回合实际发生过移动的单位 id 集合。</param>
    /// <returns>
    /// 该阵营控制区格的去重集合，按 <c>(Q, R)</c> 字典序稳定排序（Req 2.6）。
    /// 集合覆盖所有产生方快照位置的 6 个相邻格（全向，Req 11.1）。
    /// </returns>
    public static IReadOnlyList<HexCoord> ZocCells(
        IReadOnlyList<UnitState> units,
        Side side,
        IReadOnlyDictionary<UnitId, HexCoord> snapshotPositions,
        IReadOnlyDictionary<UnitId, UnitOrder> orders,
        IReadOnlySet<UnitId> movedUnits)
    {
        ArgumentNullException.ThrowIfNull(units);
        ArgumentNullException.ThrowIfNull(snapshotPositions);
        ArgumentNullException.ThrowIfNull(orders);
        ArgumentNullException.ThrowIfNull(movedUnits);

        var cells = new SortedSet<HexCoord>(HexOrder);

        foreach (var unit in units)
        {
            if (unit.Owner != side)
            {
                continue;
            }

            // 判定位置一律用机动阶段开始的快照（Req 11.7）；缺失则回退当前位置。
            var origin = snapshotPositions.TryGetValue(unit.Id, out var snap)
                ? snap
                : unit.Position;

            var target = orders.TryGetValue(unit.Id, out var order) ? order.Target : null;
            var moved = movedUnits.Contains(unit.Id);

            if (!ProducesZoc(unit.Command, origin, target, moved))
            {
                continue;
            }

            // 全向控制区：快照位置的 6 个相邻格（Req 11.1）。
            foreach (var neighbor in origin.Neighbors())
            {
                cells.Add(neighbor);
            }
        }

        return cells.ToArray();
    }

    /// <summary>
    /// 对一条移动路径施加控制区拦截（Req 11.4、11.5）。
    /// </summary>
    /// <param name="path">
    /// 单位将按序进入的格列表（<c>path[0]</c> 为离开出发格后进入的首格，依此类推）。
    /// 通常取自 <see cref="UnitOrder.Path"/>。
    /// </param>
    /// <param name="enemyZocCells">敌方控制区格集合（见 <see cref="ZocCells"/>）。</param>
    /// <param name="flags">移动单位的夜战/特性标志；含 <see cref="NightFlags.IgnoreZoc"/> 时无视控制区。</param>
    /// <returns>
    /// 截断后的路径：若不持 <see cref="NightFlags.IgnoreZoc"/> 且路径进入敌方控制区，则返回
    /// 从起点到「首个进入的控制区格」（含该格）为止的前缀，单位停在该格且本回合不再前进（Req 11.4）；
    /// 若持 <see cref="NightFlags.IgnoreZoc"/> 或路径全程不入控制区，则原样返回整条路径（Req 11.5）。
    /// </returns>
    public static IReadOnlyList<HexCoord> StopAtZoc(
        IReadOnlyList<HexCoord> path,
        IReadOnlySet<HexCoord> enemyZocCells,
        NightFlags flags)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(enemyZocCells);

        // 持 IgnoreZoc 标志者正常穿越，不受控制区影响（Req 11.5）。
        if (flags.HasFlag(NightFlags.IgnoreZoc))
        {
            return path;
        }

        for (var i = 0; i < path.Count; i++)
        {
            if (enemyZocCells.Contains(path[i]))
            {
                // 进入首个控制区格即停下，返回含该格的前缀（Req 11.4）。
                var truncated = new HexCoord[i + 1];
                for (var j = 0; j <= i; j++)
                {
                    truncated[j] = path[j];
                }

                return truncated;
            }
        }

        // 全程未入控制区，路径不变。
        return path;
    }
}
