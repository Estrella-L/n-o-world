using Xjdl.Core.Hex;
using Xjdl.Core.State;
using Xjdl.Game.Presentation.ViewModels;

namespace Xjdl.Game.Presentation;

/// <summary>
/// <see cref="PresentationMapper"/> 的战斗分组与标识映射（Req 8.5 扩展；逐场结算 + 战斗位置标识）。
/// <para>
/// Core 以单次原子结算回吐扁平、按阶段排序的回合日志；本部分把其中的战斗相关条目
/// （选表 <c>TableSelection</c>、读表结果 <c>CombatResult</c>、遭遇 <c>Encounter</c>、撤退
/// <c>Retreated</c>/<c>RetreatAnnihilated</c>、推进 <c>Advanced</c>）按 <c>battle</c> id
/// <b>重新分组为一场一场的战斗</b>（<see cref="BattleView"/>），并产出战斗位置标识
/// （<see cref="CombatMarkerView"/>）。纯投影、不改权威状态、不重算规则。
/// </para>
/// <para>
/// 关联依据：<c>TableSelection</c> 同时带 <c>battle</c> 与 <c>target</c>（战斗格），
/// <c>CombatResult</c> 带 <c>battle</c> 与关联守方单位——由此可把结果落到正确的格；
/// 撤退按守方单位归属、推进按目标格归属。无法归属者并入统一"余波"组。
/// </para>
/// </summary>
public sealed partial class PresentationMapper
{
    private const string EncounterKind = "Encounter";
    private const string TableSelectionKind = "TableSelection";
    private const string CombatResultKind = "CombatResult";
    private const string RetreatedKind = "Retreated";
    private const string RetreatAnnihilatedKind = "RetreatAnnihilated";
    private const string AdvancedKind = "Advanced";
    private const string AftermathBattleId = "aftermath";

    /// <summary>
    /// 判断某日志类别键是否属于"战斗"范畴（由 <see cref="Battles"/> 消费、逐场呈现）。
    /// 其余（临机机动/锁定/快照等）为非战斗前置事件，由动画层单独通用呈现。
    /// </summary>
    public static bool IsCombatKind(string kind) => kind switch
    {
        EncounterKind or TableSelectionKind or CombatResultKind
            or RetreatedKind or RetreatAnnihilatedKind or AdvancedKind => true,
        _ => false,
    };

    /// <summary>
    /// 把回合日志的战斗条目按 <c>battle</c> id 聚合为一场一场的 <see cref="BattleView"/>，
    /// 按各场在日志中<b>首次出现</b>的顺序返回（确定性）。每场含定位格、描述双方交战的
    /// 引导文本与按序分步结果文案。
    /// </summary>
    public IReadOnlyList<BattleView> Battles(GameState s)
    {
        ArgumentNullException.ThrowIfNull(s);
        var log = s.TurnLog;

        // 第一遍：建立 battle id ↔ 战斗格、守方单位 → battle id 的映射（供第二遍归属）。
        var battleCell = new Dictionary<string, HexCoord>(StringComparer.Ordinal);
        var cellToBattle = new Dictionary<HexCoord, string>();
        var unitToBattle = new Dictionary<int, string>();

        foreach (var e in log)
        {
            var kv = ParseKeyValues(e.Detail);
            if (e.Kind == TableSelectionKind)
            {
                var id = kv.GetValueOrDefault("battle");
                var cell = ParseColonCell(kv.GetValueOrDefault("target"));
                if (!string.IsNullOrEmpty(id) && cell is { } c)
                {
                    battleCell[id] = c;
                    cellToBattle[c] = id;
                }
            }
            else if (e.Kind == CombatResultKind)
            {
                var id = kv.GetValueOrDefault("battle");
                if (!string.IsNullOrEmpty(id) && e.Unit is { } u)
                {
                    unitToBattle[u.Value] = id;
                }
            }
        }

        // 第二遍：按首次出现顺序建组并归属每条战斗条目。
        var order = new List<string>();
        var groups = new Dictionary<string, BattleGroup>(StringComparer.Ordinal);

        BattleGroup GetGroup(string id)
        {
            if (!groups.TryGetValue(id, out var g))
            {
                g = new BattleGroup(id);
                groups[id] = g;
                order.Add(id);
            }

            return g;
        }

        foreach (var e in log)
        {
            var kv = ParseKeyValues(e.Detail);
            string? id;

            switch (e.Kind)
            {
                case TableSelectionKind:
                case CombatResultKind:
                    id = kv.GetValueOrDefault("battle");
                    break;

                case EncounterKind:
                    var at = ParseParenCell(kv.GetValueOrDefault("at"));
                    if (at is { } ecell)
                    {
                        id = cellToBattle.TryGetValue(ecell, out var bid)
                            ? bid
                            : $"enc:{ecell.Q}:{ecell.R}";
                        if (!battleCell.ContainsKey(id))
                        {
                            battleCell[id] = ecell;
                        }
                    }
                    else
                    {
                        id = null;
                    }

                    break;

                case RetreatedKind:
                case RetreatAnnihilatedKind:
                    id = e.Unit is { } ru && unitToBattle.TryGetValue(ru.Value, out var rbid)
                        ? rbid
                        : null;
                    break;

                case AdvancedKind:
                    var dest = ParseColonCell(e.Detail);
                    id = dest is { } dc && cellToBattle.TryGetValue(dc, out var abid) ? abid : null;
                    break;

                default:
                    // 非战斗类别键：不纳入战斗分组（由动画层单独呈现）。
                    continue;
            }

            id ??= AftermathBattleId;

            var group = GetGroup(id);
            group.Entries.Add(e);

            if (e.Kind == CombatResultKind)
            {
                group.Table = kv.GetValueOrDefault("table");
                group.Result = kv.GetValueOrDefault("result");
                group.Roll = kv.GetValueOrDefault("roll");
            }
            else if (e.Kind == TableSelectionKind && group.Table is null)
            {
                group.Table = kv.GetValueOrDefault("table");
            }
        }

        // 生成视图。
        var result = new List<BattleView>(order.Count);
        foreach (var id in order)
        {
            var group = groups[id];
            HexCoord? cell = battleCell.TryGetValue(id, out var c) ? c : null;
            Vector2D? center = cell is { } cc ? _layout.CenterOf(cc) : null;
            var radius = cell is { } cr ? MarkerRadius(cr) : 0.0;

            var steps = new List<string>(group.Entries.Count);
            foreach (var entry in group.Entries)
            {
                steps.Add(DescribeLogEntry(s, entry));
            }

            result.Add(new BattleView(id, cell, center, radius, BattleNarrative(group, cell), steps));
        }

        return result;
    }

    /// <summary>
    /// 本回合全部战斗位置标识（<see cref="CombatMarkerView"/>），供 <c>CombatMarkerLayer</c>
    /// 在接触阶段一次性亮出。由 <see cref="Battles"/> 中可定位的战斗投影而来。
    /// </summary>
    public IReadOnlyList<CombatMarkerView> CombatMarkers(GameState s)
    {
        var markers = new List<CombatMarkerView>();
        foreach (var battle in Battles(s))
        {
            if (battle.Cell is { } cell && battle.Center is { } center)
            {
                markers.Add(new CombatMarkerView(cell, center, battle.Radius));
            }
        }

        return markers;
    }

    /// <summary>标识半径：取格中心到首个顶点的距离（占位视觉，随布局缩放）。</summary>
    private double MarkerRadius(HexCoord cell)
    {
        var corners = _layout.CornersOf(cell);
        if (corners.Count == 0)
        {
            return 0.0;
        }

        var center = _layout.CenterOf(cell);
        var dx = corners[0].X - center.X;
        var dy = corners[0].Y - center.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    /// <summary>
    /// 生成"描述双方交战"的一段引导文本（本地化中文，Req 8.5 扩展）。
    /// 依该场的交战表、掷骰与结果拼装；无结果（敌已撤离，仅推进）时给出对应说明。
    /// 独立成方法便于后续丰富措辞或替换风格。
    /// </summary>
    private static string BattleNarrative(BattleGroup group, HexCoord? cell)
    {
        var where = cell is { } c ? FormatCell(c) : "战场";

        if (group.Result is null)
        {
            return $"{where}：敌军已撤离，我方径直推进占领。";
        }

        var table = TableDisplayName(group.Table);
        var result = ResultDisplayName(group.Result);
        var roll = group.Roll ?? "?";
        return $"{where} 爆发{table}：双方接火交战，掷 3D6 得 {roll} → {result}。";
    }

    /// <summary>一场战斗聚合的可变累加器（仅 <see cref="Battles"/> 内部使用）。</summary>
    private sealed class BattleGroup
    {
        public BattleGroup(string id) => Id = id;

        public string Id { get; }

        public List<TurnRecordEntry> Entries { get; } = new();

        public string? Table { get; set; }

        public string? Result { get; set; }

        public string? Roll { get; set; }
    }
}
