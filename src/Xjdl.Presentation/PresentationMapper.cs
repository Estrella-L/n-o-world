using Xjdl.Core.Hex;
using Xjdl.Core.State;
using Xjdl.Data.Loading;
using Xjdl.Game.Presentation.ViewModels;

namespace Xjdl.Game.Presentation;

/// <summary>
/// Core 类型 → 表现层视图 DTO 的纯映射层（Req 17.1–17.4）。不 <c>using Godot</c>，
/// 从而可脱离引擎编译并被属性测试覆盖。
/// <para>
/// 本类型以 <c>partial</c> 声明，按任务拆分到多个文件：
/// </para>
/// <list type="bullet">
/// <item>本文件（任务 3.1）：基础映射 <see cref="MapCells"/>、<see cref="FriendlyUnits"/>、
/// <see cref="DescribeFriendly"/>、<see cref="LogLines"/>、<see cref="PhaseView"/>、
/// <see cref="CommandPoints"/>。</item>
/// <item>任务 3.2：敌方可见度过滤映射（<c>EnemyUnits</c>/<c>DescribeEnemy</c>）。</item>
/// <item>任务 3.4：命令组装（<c>BuildMoveOrder</c> 等）。</item>
/// </list>
/// 视图 DTO 是不可变"投影"，权威状态始终是调用方持有的 <see cref="GameState"/>。
/// </summary>
public sealed partial class PresentationMapper
{
    private readonly GameData _data;
    private readonly FogConfig _fogConfig;
    private readonly HexLayout _layout;

    /// <summary>
    /// 构造映射器。
    /// </summary>
    /// <param name="data">已校验的配置聚合（Req 1.4）。</param>
    /// <param name="fogConfig">战争迷雾配置，供敌方可见度过滤使用（Req 10.x，任务 3.2）。</param>
    /// <param name="layout">六角布局，用于计算格中心与顶点（复用 <see cref="HexLayout"/>）。</param>
    public PresentationMapper(GameData data, FogConfig fogConfig, HexLayout layout)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(fogConfig);
        ArgumentNullException.ThrowIfNull(layout);

        _data = data;
        _fogConfig = fogConfig;
        _layout = layout;
    }

    // ── 地图 ──────────────────────────────────────────────────────────

    /// <summary>
    /// 地图 → 单元格视图，按 <see cref="GameMap.OrderedCells"/> 稳定序（Req 3.1）。
    /// 每格计算格中心与顶点（复用 <see cref="HexLayout"/>），并生成中文地形占位名（Req 3.4）。
    /// </summary>
    public IReadOnlyList<CellView> MapCells(GameState s)
    {
        ArgumentNullException.ThrowIfNull(s);

        var cells = new List<CellView>();
        foreach (var cell in s.Map.OrderedCells)
        {
            cells.Add(new CellView(
                cell.Coord,
                cell.Terrain,
                _layout.CenterOf(cell.Coord),
                _layout.CornersOf(cell.Coord),
                TerrainDisplayName(cell.Terrain)));
        }

        return cells;
    }

    // ── 己方单位 ──────────────────────────────────────────────────────

    /// <summary>
    /// 己方单位 → 完整 <see cref="UnitView"/>（Req 4.1/4.2/4.3）。己方总是全信息，
    /// 展示当前攻/防/韧性、机动/侦察、当前命令与同格（同阵营）堆叠数量。
    /// 按 <see cref="UnitId"/> 稳定序产出，保持确定性（Req 2.6）。
    /// </summary>
    public IReadOnlyList<UnitView> FriendlyUnits(GameState s, Side viewer)
    {
        ArgumentNullException.ThrowIfNull(s);

        var stackCounts = StackCountsByCell(s, viewer);

        var views = new List<UnitView>();
        foreach (var unit in s.Units)
        {
            if (unit.Owner != viewer)
            {
                continue;
            }

            views.Add(new UnitView(
                unit.Id,
                unit.Owner,
                unit.Class,
                _layout.CenterOf(unit.Position),
                unit.Position,
                unit.Attack,
                unit.Defense,
                unit.ResilienceLeft,
                unit.Movement,
                unit.Vision,
                unit.Command,
                stackCounts[unit.Position]));
        }

        views.Sort((a, b) => a.Id.Value.CompareTo(b.Id.Value));
        return views;
    }

    /// <summary>
    /// 单个己方单位详情（供 <c>InfoPanel</c>，Req 5.2）：完整属性与当前命令。
    /// 单位不存在时返回 <c>null</c>。
    /// </summary>
    public UnitDetailView? DescribeFriendly(GameState s, UnitId id)
    {
        ArgumentNullException.ThrowIfNull(s);

        var unit = FindUnit(s, id);
        if (unit is null)
        {
            return null;
        }

        return new UnitDetailView(
            unit.Id,
            unit.Owner,
            unit.Class,
            unit.Attack,
            unit.Defense,
            unit.ResilienceLeft,
            unit.Movement,
            unit.Vision,
            unit.Command);
    }

    // ── 回合日志 ──────────────────────────────────────────────────────

    /// <summary>
    /// 回合日志 → 可读中文行（Req 9.1/9.2/9.3）。按 <see cref="GameState.TurnLog"/> 原始顺序生成，
    /// 依 <see cref="TurnRecordEntry.Kind"/> 产出中文文案，并尽力解析关联格坐标供点击定位
    /// （<see cref="LogLineView.Locate"/>）。
    /// </summary>
    public IReadOnlyList<LogLineView> LogLines(GameState s)
    {
        ArgumentNullException.ThrowIfNull(s);

        var lines = new List<LogLineView>(s.TurnLog.Count);
        foreach (var entry in s.TurnLog)
        {
            lines.Add(new LogLineView(
                entry.Turn,
                entry.Kind,
                DescribeLogEntry(s, entry),
                LocateOf(s, entry)));
        }

        return lines;
    }

    // ── 昼夜 / CP ─────────────────────────────────────────────────────

    /// <summary>昼夜视图（Req 11.1）：当前昼夜阶段与其中文显示名。</summary>
    public ViewModels.DayNightView PhaseView(GameState s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new ViewModels.DayNightView(s.Phase, DayNightDisplayName(s.Phase));
    }

    /// <summary>
    /// 当前观察方可用指挥点（Req 12.1）。读 <see cref="GameState.Cards"/> 中该方的
    /// <see cref="CardState.Cp"/>；若该方无卡牌状态则返回 0。
    /// </summary>
    public int CommandPoints(GameState s, Side viewer)
    {
        ArgumentNullException.ThrowIfNull(s);
        return s.Cards.TryGetValue(viewer, out var cards) ? cards.Cp : 0;
    }

    // ── 内部辅助 ──────────────────────────────────────────────────────

    /// <summary>按格统计指定阵营的同格堆叠数量。</summary>
    private static Dictionary<HexCoord, int> StackCountsByCell(GameState s, Side side)
    {
        var counts = new Dictionary<HexCoord, int>();
        foreach (var unit in s.Units)
        {
            if (unit.Owner != side)
            {
                continue;
            }

            counts[unit.Position] = counts.TryGetValue(unit.Position, out var c) ? c + 1 : 1;
        }

        return counts;
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

    /// <summary>地形中文占位名（Req 3.4；Core 无文案，由表现层负责）。</summary>
    private static string TerrainDisplayName(TerrainType terrain) => terrain switch
    {
        TerrainType.Plain => "平原",
        TerrainType.Forest => "森林",
        TerrainType.Hill => "丘陵",
        TerrainType.City => "城市",
        TerrainType.River => "河流",
        TerrainType.Swamp => "沼泽",
        _ => terrain.ToString(),
    };

    /// <summary>昼夜阶段中文显示名（Req 11.1）。</summary>
    private static string DayNightDisplayName(DayNightPhase phase) => phase switch
    {
        DayNightPhase.Morning => "上午",
        DayNightPhase.Afternoon => "下午",
        DayNightPhase.Night => "晚上",
        _ => phase.ToString(),
    };
}
