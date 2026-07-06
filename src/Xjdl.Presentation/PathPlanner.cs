using Xjdl.Core.Hex;
using Xjdl.Core.State;
using Xjdl.Core.Terrain;
using Xjdl.Data.Loading;
using Xjdl.Game.Presentation.ViewModels;

namespace Xjdl.Game.Presentation;

/// <summary>
/// 计划阶段「拖拽画箭头」路径规划的<b>纯逻辑</b>（Req 6.2/6.3/6.4/6.13）：
/// 将光标像素吸附到六角格、按六角相邻关系逐格扩展路径、用 Core 的
/// <see cref="TerrainSystem"/> 地形移动消耗规则累计路径总消耗。
/// <para>
/// <b>不重算规则</b>：进入消耗、兵种进入限制、进入即止均委托 <see cref="TerrainSystem"/>
/// 与注入的地形配置档（<see cref="GameData.Terrain"/>）判定；表现层只做吸附、逐格扩展与
/// 消耗累计的纯数据部分，提交的路径最终仍由 Core 在结算时校验（Req 6.13）。
/// </para>
/// <para>
/// 超机动点、遇不可进入或进入即止地形时，路径止步于最后一个合法格并置
/// <see cref="PathDraft.BlockedAhead"/>，绝不产出超限或含禁入格的 <see cref="PathDraft.Path"/>
/// （Req 6.4，Property 3 接缝）。
/// </para>
/// 不 <c>using Godot</c>，从而可脱离引擎编译并被属性测试覆盖。
/// </summary>
public sealed class PathPlanner
{
    private readonly GameState _state;
    private readonly TerrainProfile _terrain;
    private readonly HexLayout _layout;

    /// <summary>
    /// 构造路径规划器（design〈PathPlanner + PlanningController〉）。
    /// </summary>
    /// <param name="state">当前对局状态（用于查单位起点/兵种、按坐标查地形，唯一事实来源）。</param>
    /// <param name="data">配置聚合，提供地形消耗规则来源 <see cref="GameData.Terrain"/>。</param>
    /// <param name="layout">六角坐标 ↔ 像素换算工具，用于将光标吸附到格。</param>
    public PathPlanner(GameState state, GameData data, HexLayout layout)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(layout);

        _state = state;
        _terrain = data.Terrain;
        _layout = layout;
    }

    /// <summary>
    /// 开始为某己方单位规划：记录起点（单位当前位置）与该单位的机动点预算，
    /// 产出仅含起点、消耗为 0 的初始草案（Req 6.2）。
    /// </summary>
    /// <param name="unit">被规划单位的稳定标识。</param>
    /// <returns>仅含起点的初始 <see cref="PathDraft"/>。</returns>
    /// <exception cref="ArgumentException">状态中不存在该单位。</exception>
    public PathDraft Begin(UnitId unit)
    {
        var u = FindUnit(unit);
        return new PathDraft(
            unit,
            new[] { u.Position },
            UsedCost: 0,
            MovementBudget: u.Movement,
            BlockedAhead: false);
    }

    /// <summary>
    /// 将光标像素吸附到格，并从单位起点按六角相邻关系逐格朝该格扩展一条合法路径：
    /// 用 <see cref="TerrainSystem.MoveCost"/> 累计消耗，一旦下一格越出地图、对本兵种
    /// 不可进入（<see cref="TerrainSystem.CanEnter"/>）、进入会使累计消耗超过机动点，或
    /// 进入即止（<see cref="TerrainSpec.EnterAndStop"/>）无法继续，则止步于最后一个合法格
    /// 并置 <see cref="PathDraft.BlockedAhead"/>（Req 6.3/6.4/6.13，Property 3）。
    /// </summary>
    /// <param name="draft">当前草案（提供起点、被规划单位与机动点预算）。</param>
    /// <param name="cursorPixel">光标像素坐标。</param>
    /// <returns>朝吸附格扩展后的新草案（值语义快照）。</returns>
    public PathDraft Extend(PathDraft draft, Vector2D cursorPixel)
    {
        if (draft.Path.Count == 0)
        {
            return draft;
        }

        var unit = FindUnit(draft.Unit);
        var start = draft.Path[0];
        var target = _layout.CoordAt(cursorPixel);

        var path = new List<HexCoord> { start };
        var usedCost = 0;

        RouteSegment(unit, target, path, ref usedCost, draft.MovementBudget);

        // BlockedAhead 当且仅当未能一直延伸到目标格（含"抵达目标即止地形"仍算抵达）。
        return draft with
        {
            Path = path,
            UsedCost = usedCost,
            BlockedAhead = path[^1] != target,
        };
    }

    /// <summary>
    /// 经过一串<b>途径点</b>再到光标格的多段路径规划（Req 6.3/6.4/6.13，支持绕行/画弧）：
    /// 依次把起点 → <paramref name="waypoints"/> 中各点 → 光标吸附格拼成一条连续合法路径，
    /// 每段仍用 <see cref="RouteSegment"/> 逐格贪心推进并<b>跨段累计</b>移动消耗与预算。
    /// 任一段因越界/禁入/超限/进入即止无法抵达其目标时，路径止步于最后一个合法格并置
    /// <see cref="PathDraft.BlockedAhead"/>，不再续接后续途径点（Req 6.4）。
    /// <para>
    /// 供 <c>PlanningController</c> 在拖拽时按住修饰键「记忆途径点」以画出任意折线/弧线路径，
    /// 而非只能取最近路。途径点与光标格若与当前段起点重合则该段直接视为已达（零位移），
    /// 故重复/共线途径点无害。
    /// </para>
    /// </summary>
    /// <param name="draft">当前草案（提供起点、被规划单位与机动点预算）。</param>
    /// <param name="waypoints">按顺序经过的途径格（不含起点；可为空，此时等价于 <see cref="Extend"/>）。</param>
    /// <param name="cursorPixel">光标像素坐标（最终目标格）。</param>
    /// <returns>依次穿过各途径点后到光标格的新草案（值语义快照）。</returns>
    public PathDraft ExtendThrough(
        PathDraft draft, IReadOnlyList<HexCoord> waypoints, Vector2D cursorPixel)
    {
        ArgumentNullException.ThrowIfNull(waypoints);

        if (draft.Path.Count == 0)
        {
            return draft;
        }

        var unit = FindUnit(draft.Unit);
        var start = draft.Path[0];
        var target = _layout.CoordAt(cursorPixel);

        var path = new List<HexCoord> { start };
        var usedCost = 0;
        var blocked = false;

        foreach (var waypoint in waypoints)
        {
            var result = RouteSegment(unit, waypoint, path, ref usedCost, draft.MovementBudget);

            // 未能抵达该途径点（越界/禁入/超限）→ 截断于最后一个合法格。
            if (path[^1] != waypoint)
            {
                blocked = true;
                break;
            }

            // 恰好停在"进入即止"地形上的途径点：抵达但本回合无法继续，路径就此止步。
            if (result == SegmentResult.Stopped)
            {
                blocked = true;
                break;
            }
        }

        if (!blocked)
        {
            RouteSegment(unit, target, path, ref usedCost, draft.MovementBudget);
            blocked = path[^1] != target;
        }

        return draft with
        {
            Path = path,
            UsedCost = usedCost,
            BlockedAhead = blocked,
        };
    }

    /// <summary>一段路由的结果。<see cref="Reached"/> 表示抵达段目标；其余表示被截断（BlockedAhead）。</summary>
    private enum SegmentResult
    {
        /// <summary>抵达该段目标格。</summary>
        Reached,

        /// <summary>所有使距离减小的相邻格都不合法（越界/禁入/超限），止步于最后一个合法格。</summary>
        Blocked,

        /// <summary>踏入「进入即止」地形，本格合法但无法继续。</summary>
        Stopped,
    }

    /// <summary>
    /// 从 <paramref name="path"/> 末尾逐格朝 <paramref name="target"/> 贪心推进一段：在使六角距离
    /// 严格减小的相邻格中，按固定方向序（<see cref="HexCoord.Directions"/>）挑第一个「在图内 + 可进入
    /// + 预算充足」的格纳入路径并累计消耗，直到抵达 <paramref name="target"/>、被截断或踏入进入即止地形。
    /// 就地修改 <paramref name="path"/> 与 <paramref name="usedCost"/>，保证确定性。
    /// </summary>
    private SegmentResult RouteSegment(
        UnitState unit, HexCoord target, List<HexCoord> path, ref int usedCost, int budget)
    {
        var current = path[^1];

        // 六角网格上，非目标格必有相邻格使距离严格减小，故循环至多经过 current.DistanceTo(target) 步即终止。
        while (current != target)
        {
            var currentDistance = current.DistanceTo(target);
            var stepped = false;

            foreach (var direction in HexCoord.Directions)
            {
                var next = current + direction;
                if (next.DistanceTo(target) >= currentDistance)
                {
                    continue;
                }

                var cell = _state.Map.TryGet(next);
                if (cell is null)
                {
                    // 越出地图边界，非合法格。
                    continue;
                }

                if (!TerrainSystem.CanEnter(_terrain, cell.Terrain, unit.Class))
                {
                    // 本兵种不可进入该地形（Req 6.4）。
                    continue;
                }

                var cost = TerrainSystem.MoveCost(_terrain, cell.Terrain, unit.Class);
                if (usedCost + cost > budget)
                {
                    // 进入会使累计消耗超过机动点（Req 6.3/6.4）。
                    continue;
                }

                // 合法一步：纳入路径并累计消耗。
                path.Add(next);
                usedCost += cost;
                current = next;
                stepped = true;

                // 进入即止地形：本格合法但无法继续（Req 6.4）。
                if (IsEnterAndStop(cell.Terrain))
                {
                    return SegmentResult.Stopped;
                }

                break;
            }

            if (!stepped)
            {
                // 所有使距离减小的相邻格都不合法：止步于最后一个合法格。
                return SegmentResult.Blocked;
            }
        }

        return SegmentResult.Reached;
    }

    /// <summary>
    /// 清除当前草案，回到仅含起点、消耗为 0 的状态（Req 6.7）。
    /// 保留被规划单位与机动点预算。
    /// </summary>
    public PathDraft Clear(PathDraft draft)
    {
        if (draft.Path.Count == 0)
        {
            return draft with { UsedCost = 0, BlockedAhead = false };
        }

        return draft with
        {
            Path = new[] { draft.Path[0] },
            UsedCost = 0,
            BlockedAhead = false,
        };
    }

    private bool IsEnterAndStop(TerrainType terrain)
        => _terrain.Terrains.TryGetValue(terrain, out var spec) && spec.EnterAndStop;

    private UnitState FindUnit(UnitId id)
    {
        foreach (var unit in _state.Units)
        {
            if (unit.Id == id)
            {
                return unit;
            }
        }

        throw new ArgumentException($"状态中不存在单位 {id.Value}。", nameof(id));
    }
}

/// <summary>
/// 拖拽中的移动路径草案（值语义快照，Req 6.2/6.3/6.4）。
/// </summary>
/// <param name="Unit">被规划单位的稳定标识。</param>
/// <param name="Path">含起点的合法路径，逐格相邻相接。</param>
/// <param name="UsedCost">已累计的移动消耗（沿途各格 <see cref="TerrainSystem.MoveCost"/> 之和）。</param>
/// <param name="MovementBudget">该单位的机动点预算（<see cref="UnitState.Movement"/>）。</param>
/// <param name="BlockedAhead">是否因超限/禁入/进入即止而止步于最后一个合法格。</param>
public readonly record struct PathDraft(
    UnitId Unit,
    IReadOnlyList<HexCoord> Path,
    int UsedCost,
    int MovementBudget,
    bool BlockedAhead);
