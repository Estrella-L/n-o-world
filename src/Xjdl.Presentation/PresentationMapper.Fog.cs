using Xjdl.Core.Fog;
using Xjdl.Core.Hex;
using Xjdl.Core.State;
using Xjdl.Game.Presentation.ViewModels;

namespace Xjdl.Game.Presentation;

/// <summary>
/// <see cref="PresentationMapper"/> 的敌方可见度过滤映射（任务 3.2，Req 10.1–10.4、17.2、5.3/5.4）。
/// <para>
/// 可见度过滤是<b>安全边界</b>（Property 2）：先调 <see cref="FogSystem.Compute"/> 取每个敌方单位的
/// <see cref="Visibility"/>，再<b>在 DTO 源头</b>裁字段——
/// </para>
/// <list type="bullet">
/// <item><see cref="Visibility.Identified"/>：填充全字段（兵种、攻/防/韧性、同格堆叠数量）。</item>
/// <item><see cref="Visibility.Spotted"/>：仅坐标 + "有敌"标记，兵种与数值字段一律 <c>null</c>。</item>
/// <item><see cref="Visibility.Hidden"/>：不产出任何条目（<c>EnemyUnits</c>）/返回 <c>null</c>（<c>DescribeEnemy</c>）。</item>
/// </list>
/// 敏感字段在此处即为 <c>null</c>，即使节点层出错也无从泄露隐藏信息。
/// </summary>
public sealed partial class PresentationMapper
{
    /// <summary>
    /// 敌方单位 → 按 <see cref="Visibility"/> 过滤的 <see cref="EnemyView"/> 列表（Req 10.1–10.4、17.2）。
    /// 先经 <see cref="FogSystem.Compute"/> 取可见度；<see cref="Visibility.Hidden"/> 不产出条目，
    /// <see cref="Visibility.Spotted"/> 仅暴露坐标，<see cref="Visibility.Identified"/> 暴露全字段。
    /// 按 <see cref="UnitId"/> 稳定序产出，保持确定性（Req 2.6）。
    /// </summary>
    /// <param name="s">当前全场状态快照。</param>
    /// <param name="viewer">观察方阵营；其对手为敌方。</param>
    public IReadOnlyList<EnemyView> EnemyUnits(GameState s, Side viewer)
    {
        ArgumentNullException.ThrowIfNull(s);

        var visibility = FogSystem.Compute(s, viewer, _fogConfig);
        var stackCounts = EnemyStackCountsByCell(s, viewer);

        var views = new List<EnemyView>();
        foreach (var unit in s.Units)
        {
            if (unit.Owner == viewer)
            {
                continue;
            }

            // 无可见度记录（理论上不会发生）按隐匿处理，保守不泄露。
            if (!visibility.TryGetValue(unit.Id, out var vis) || vis == Visibility.Hidden)
            {
                continue;
            }

            views.Add(vis == Visibility.Identified
                ? new EnemyView(
                    unit.Id,
                    vis,
                    _layout.CenterOf(unit.Position),
                    unit.Position,
                    unit.Class,
                    unit.Attack,
                    unit.Defense,
                    unit.ResilienceLeft,
                    stackCounts[unit.Position])
                : new EnemyView(
                    unit.Id,
                    vis,
                    _layout.CenterOf(unit.Position),
                    unit.Position,
                    Class: null,
                    Attack: null,
                    Defense: null,
                    ResilienceLeft: null,
                    StackCount: null));
        }

        views.Sort((a, b) => a.Id.Value.CompareTo(b.Id.Value));
        return views;
    }

    /// <summary>
    /// 单个敌方单位详情（供 <c>InfoPanel</c>，按可见度裁字段，Req 5.3/5.4）。
    /// <see cref="Visibility.Identified"/> 暴露兵种与攻/防/韧性；<see cref="Visibility.Spotted"/>
    /// 仅暴露坐标；<see cref="Visibility.Hidden"/> 或该 id 非敌方单位时返回 <c>null</c>。
    /// </summary>
    /// <param name="s">当前全场状态快照。</param>
    /// <param name="viewer">观察方阵营。</param>
    /// <param name="id">目标单位 id。</param>
    public EnemyDetailView? DescribeEnemy(GameState s, Side viewer, UnitId id)
    {
        ArgumentNullException.ThrowIfNull(s);

        var unit = FindUnit(s, id);
        if (unit is null || unit.Owner == viewer)
        {
            return null;
        }

        var visibility = FogSystem.Compute(s, viewer, _fogConfig);
        if (!visibility.TryGetValue(id, out var vis) || vis == Visibility.Hidden)
        {
            return null;
        }

        return vis == Visibility.Identified
            ? new EnemyDetailView(
                vis,
                unit.Position,
                unit.Class,
                unit.Attack,
                unit.Defense,
                unit.ResilienceLeft)
            : new EnemyDetailView(
                vis,
                unit.Position,
                Class: null,
                Attack: null,
                Defense: null,
                ResilienceLeft: null);
    }

    /// <summary>按格统计敌方（非 <paramref name="viewer"/> 方）同格堆叠数量。</summary>
    private static Dictionary<HexCoord, int> EnemyStackCountsByCell(GameState s, Side viewer)
    {
        var counts = new Dictionary<HexCoord, int>();
        foreach (var unit in s.Units)
        {
            if (unit.Owner == viewer)
            {
                continue;
            }

            counts[unit.Position] = counts.TryGetValue(unit.Position, out var c) ? c + 1 : 1;
        }

        return counts;
    }
}
