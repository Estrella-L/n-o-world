using System.Globalization;
using Xjdl.Core.Hex;
using Xjdl.Core.State;

namespace Xjdl.Game.Presentation;

/// <summary>
/// <see cref="PresentationMapper"/> 的回合日志文案生成（任务 3.1，Req 9.1/9.2/9.3）。
/// 依据 Core 写入且回合末保留的 <see cref="TurnRecordEntry.Kind"/> 生成中文文案，
/// 并从 <see cref="TurnRecordEntry.Detail"/>/关联单位解析可点击定位坐标。
/// <para>
/// Core 回合末保留的类别键：<c>Reposition</c>、<c>Locked</c>、<c>Encounter</c>、
/// <c>TableSelection</c>、<c>Snapshot</c>、<c>CombatResult</c>、<c>Retreated</c>、
/// <c>RetreatAnnihilated</c>、<c>Advanced</c>（<c>RetreatOrder</c>/<c>ContestedCell</c>/
/// <c>AdvanceOrder</c> 为阶段间中转，回合末已被消费剔除）。未知类别键回退为原样展示。
/// </para>
/// </summary>
public sealed partial class PresentationMapper
{
    /// <summary>依条目类别键生成中文文案（Req 9.2）。</summary>
    private static string DescribeLogEntry(GameState s, TurnRecordEntry entry)
    {
        var kv = ParseKeyValues(entry.Detail);
        var unitLabel = entry.Unit is { } u ? u.Value.ToString(CultureInfo.InvariantCulture) : "?";

        switch (entry.Kind)
        {
            case "Reposition":
                var tick = entry.TriggerTick?.ToString(CultureInfo.InvariantCulture) ?? "-";
                return $"临机机动：单位 {unitLabel} 改路径（触发时点 {tick}）";

            case "Locked":
                return $"单位 {unitLabel} 接敌锁定";

            case "Encounter":
                var at = ParseParenCell(kv.GetValueOrDefault("at"));
                return at is { } ec ? $"遭遇战于 {FormatCell(ec)}" : "遭遇战";

            case "TableSelection":
                var target = ParseColonCell(kv.GetValueOrDefault("target"));
                var targetText = target is { } tc ? FormatCell(tc) : "?";
                if (kv.ContainsKey("advanceOnly"))
                {
                    return $"选表：直接推进 @ {targetText}";
                }

                var tableName = TableDisplayName(kv.GetValueOrDefault("table"));
                return $"选表：{tableName} @ {targetText}";

            case "Snapshot":
                var n = kv.GetValueOrDefault("units") ?? "?";
                return $"战力快照冻结（{n} 单位）";

            case "CombatResult":
                var battle = kv.GetValueOrDefault("battle") ?? "?";
                var table = TableDisplayName(kv.GetValueOrDefault("table"));
                var col = kv.GetValueOrDefault("col") ?? "?";
                var roll = kv.GetValueOrDefault("roll") ?? "?";
                var result = ResultDisplayName(kv.GetValueOrDefault("result"));
                return $"战斗 {battle}：{table} 第 {col} 档，掷 {roll} → {result}";

            case "Retreated":
                var dest = ParseColonCell(entry.Detail);
                return dest is { } rc
                    ? $"单位 {unitLabel} 撤退至 {FormatCell(rc)}"
                    : $"单位 {unitLabel} 撤退";

            case "RetreatAnnihilated":
                return $"单位 {unitLabel} 无路可退，就地歼灭";

            case "Advanced":
                var cell = ParseColonCell(entry.Detail);
                return cell is { } ac
                    ? $"单位 {unitLabel} 推进占领 {FormatCell(ac)}"
                    : $"单位 {unitLabel} 推进占领";

            default:
                // 未知/中转类别键：回退为可读的原样展示，避免丢失信息。
                return string.IsNullOrEmpty(entry.Detail)
                    ? entry.Kind
                    : $"{entry.Kind}：{entry.Detail}";
        }
    }

    /// <summary>解析条目关联的地图定位坐标（Req 9.3）；无关联则返回 <c>null</c>。</summary>
    private static HexCoord? LocateOf(GameState s, TurnRecordEntry entry)
    {
        var kv = ParseKeyValues(entry.Detail);

        switch (entry.Kind)
        {
            case "Encounter":
                return ParseParenCell(kv.GetValueOrDefault("at"));

            case "TableSelection":
                return ParseColonCell(kv.GetValueOrDefault("target"));

            case "Retreated":
            case "Advanced":
                return ParseColonCell(entry.Detail);

            case "Reposition":
            case "Locked":
            case "CombatResult":
            case "RetreatAnnihilated":
                // 关联单位：以其在当前状态中的位置定位（阵亡单位可能已不存在）。
                if (entry.Unit is { } id)
                {
                    var unit = FindUnit(s, id);
                    return unit?.Position;
                }

                return null;

            default:
                return null;
        }
    }

    // ── Detail 解析 ───────────────────────────────────────────────────

    /// <summary>解析 <c>key=value;key=value</c> 形式的 Detail；无值的键（如 <c>advanceOnly</c>）值为空串。</summary>
    private static Dictionary<string, string> ParseKeyValues(string? detail)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(detail))
        {
            return map;
        }

        foreach (var part in detail.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = part.IndexOf('=');
            if (eq < 0)
            {
                map[part] = string.Empty;
            }
            else
            {
                map[part[..eq]] = part[(eq + 1)..];
            }
        }

        return map;
    }

    /// <summary>解析 <c>"Q:R"</c> 形式的坐标（<c>EncodeCell</c> 编码）；失败返回 <c>null</c>。</summary>
    private static HexCoord? ParseColonCell(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var parts = value.Split(':');
        if (parts.Length < 2
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var q)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var r))
        {
            return null;
        }

        return new HexCoord(q, r);
    }

    /// <summary>解析 <c>"(Q, R)"</c> 形式的坐标（<see cref="HexCoord.ToString"/> 编码）；失败返回 <c>null</c>。</summary>
    private static HexCoord? ParseParenCell(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        var trimmed = value.Trim().Trim('(', ')');
        var parts = trimmed.Split(',');
        if (parts.Length < 2
            || !int.TryParse(parts[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var q)
            || !int.TryParse(parts[1].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var r))
        {
            return null;
        }

        return new HexCoord(q, r);
    }

    private static string FormatCell(HexCoord c) =>
        $"({c.Q.ToString(CultureInfo.InvariantCulture)}, {c.R.ToString(CultureInfo.InvariantCulture)})";

    /// <summary>交战表名中文化（Req 9.2）；未知回退原值。</summary>
    private static string TableDisplayName(string? table) => table switch
    {
        "RegularAttack" => "表一（进攻）",
        "MutualAttack" => "表二（对攻）",
        "Encounter" => "表三（遭遇）",
        null or "" => "?",
        _ => table,
    };

    /// <summary>战斗结果代码中文化（Req 9.2）；未知回退原值。</summary>
    private static string ResultDisplayName(string? result) => result switch
    {
        "MutualN" => "双方各损",
        "AttackerN" => "攻方战损",
        "DefenderN" => "守方战损",
        "DefenderNRetreat" => "守方战损并撤退",
        "DefenderAnnihilate" => "守方歼灭",
        "LoserN" => "败方战损",
        "LoserNRetreat" => "败方战损并撤退",
        "LoserAnnihilate" => "败方歼灭",
        "Stalemate" => "僵持",
        "Withdraw" => "脱离",
        null or "" => "?",
        _ => result,
    };
}
