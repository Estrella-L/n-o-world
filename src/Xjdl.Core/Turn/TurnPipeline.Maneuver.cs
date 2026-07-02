using Xjdl.Core.Hex;
using Xjdl.Core.Random;
using Xjdl.Core.State;

namespace Xjdl.Core.Turn;

/// <summary>
/// 阶段 2（机动）的纯函数变换（Req 4.2–4.8、15.2、21.6）。
/// 与 <see cref="TurnPipeline"/> 骨架合并为同一部分类，仅承载机动阶段逻辑，
/// 不原地修改输入状态（Req 2.1）。
/// <para>
/// 语义要点（事实来源 docs/01-战斗机制.md〈计划—动画双阶段与临机机动〉〈接敌即锁定〉〈控制区〉）：
/// </para>
/// <list type="bullet">
/// <item>逐格推进移动/进攻准备单位，按剩余机动点累加消耗；余量不足以进入下一格即止步（Req 15.2）。</item>
/// <item>接敌即锁定：进攻准备移动到与目标六角距离 1、移动单位进入敌方控制区（<see cref="ZoneOfControl.StopAtZoc"/>）
/// 被迫停下、或与敌同格触发接触时，单位锁定（位置/命令冻结，Req 4.2/4.3）。</item>
/// <item>临机机动（<see cref="TurnCommands.Repositions"/>）：仅对未锁定单位生效，只改移动路径、不改命令性质、
/// 不增机动力（Req 4.4/4.5）；花指挥点，指挥点不足则拒绝并保持原计划（Req 4.8）；每次被接受的临机机动
/// 向 <see cref="GameState.TurnLog"/> 追加含触发时点的条目（Req 4.7/21.6）；被改向后撞敌按遭遇战（Req 4.6）。</item>
/// </list>
/// <para>
/// <b>简化假设（待后续任务补全接线）：</b>
/// </para>
/// <list type="number">
/// <item><b>移动消耗模型</b>：<see cref="Terrain.TerrainSystem.MoveCost"/> 需要一份 <see cref="TerrainProfile"/>，
/// 而 <see cref="GameState"/> 当前不携带地形档；本阶段采用确定性的「每进入一格消耗 1 点」统一模型
/// （<see cref="DefaultMoveCostPerCell"/>），地形差异化消耗的接线留待后续任务（含全流水线接线的 15.20）。</item>
/// <item><b>锁定/遭遇的表示</b>：<see cref="UnitState"/> 尚无「锁定」字段，故本阶段以 <see cref="TurnRecordEntry"/>
/// 日志条目（<see cref="LockedKind"/>/<see cref="EncounterKind"/>）表达锁定与遭遇战意图，供接触选表阶段（15.20）读取；
/// 位置冻结由「单位停在接敌格」这一落点直接体现。</item>
/// <item><b>临机机动代价</b>：docs/01〈可配置字段〉的 <c>repositionCost</c> 尚未接入数据层，故采用固定
/// <see cref="RepositionCpCost"/>=1 的确定性默认值；剩余机动点以整回合机动力为预算校验（不按触发时点扣减已走里程，
/// 触发时点仍完整记入日志以保回放一致）。</item>
/// <item><b>回合号</b>：<see cref="GameState"/> 无独立回合计数，日志条目的 <see cref="TurnRecordEntry.Turn"/>
/// 采用 <see cref="GameState.DayIndex"/>。</item>
/// </list>
/// </summary>
internal static partial class TurnPipeline
{
    /// <summary>每进入一格的默认移动消耗（统一模型，地形档接线前的确定性占位）。</summary>
    private const int DefaultMoveCostPerCell = 1;

    /// <summary>单次临机机动的默认指挥点代价（<c>repositionCost</c> 接入数据层前的默认值）。</summary>
    private const int RepositionCpCost = 1;

    /// <summary>回合日志条目类别键：被接受的临机机动（含触发时点，Req 4.7/21.6）。</summary>
    private const string RepositionKind = "Reposition";

    /// <summary>回合日志条目类别键：单位接敌锁定（Req 4.2/4.3）。</summary>
    private const string LockedKind = "Locked";

    /// <summary>回合日志条目类别键：被临机改向后撞敌，按遭遇战（表三）处理（Req 4.6）。</summary>
    private const string EncounterKind = "Encounter";

    /// <summary>
    /// 阶段 2 机动的纯函数实现：返回落点、锁定/遭遇日志、指挥点消费后的新 <see cref="GameState"/>；
    /// 不修改输入（Req 2.1）。全程整数运算、按稳定 <see cref="UnitId"/> 序处理，确定性可重放（Req 2.5/2.6）。
    /// </summary>
    /// <param name="s">机动阶段开始时的状态。</param>
    /// <param name="cmds">本回合命令（阶段 0 命令 + 动画阶段临机机动）。</param>
    /// <param name="rng">注入的确定性随机源；机动阶段本身不消耗随机数。</param>
    /// <returns>推进落点、更新指挥点与回合日志后的新状态。</returns>
    internal static GameState ManeuverPhase(GameState s, TurnCommands cmds, ISeededRng rng)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(cmds);
        _ = rng; // 机动阶段确定性，不消耗随机数。

        var orders = BuildOrderMap(cmds);
        var repositions = BuildRepositionMap(cmds);

        // 机动阶段开始时的快照位置：控制区判定与接敌判定一律以此为准（Req 11.7）。
        var snapshot = new Dictionary<UnitId, HexCoord>();
        foreach (var unit in s.Units)
        {
            snapshot[unit.Id] = unit.Position;
        }

        // 稳定顺序处理，保证确定性（Req 2.6）。
        var ordered = s.Units.OrderBy(static u => u.Id.Value).ToArray();

        // 指挥点结余的可变副本（按方消费，Req 4.7/4.8）。
        var cp = new Dictionary<Side, int>();
        foreach (var kv in s.Cards)
        {
            cp[kv.Key] = kv.Value.Cp;
        }

        var log = new List<TurnRecordEntry>(s.TurnLog);

        // ── 第一步：敲定各单位「有效路径」，结算临机机动的接受/拒绝（Req 4.4/4.5/4.8）──
        var effectivePath = new Dictionary<UnitId, IReadOnlyList<HexCoord>>();
        var repositioned = new HashSet<UnitId>();

        foreach (var unit in ordered)
        {
            var order = orders.GetValueOrDefault(unit.Id);
            var plannedPath = order?.Path ?? Array.Empty<HexCoord>();
            var chosenPath = plannedPath;

            // 进攻准备开局即与目标相邻 → 已接敌锁定，不可临机改向（Req 4.2、11.2）。
            var lockedAtStart = order is { Command: Command.AttackPrep, Target: { } t0 }
                && HexCoord.Distance(snapshot[unit.Id], t0) == 1;

            if (!lockedAtStart && repositions.TryGetValue(unit.Id, out var rep))
            {
                var newPath = rep.NewPath ?? Array.Empty<HexCoord>();
                var cost = PathMoveCost(newPath);
                var balance = cp.GetValueOrDefault(unit.Owner);

                // 只在「移动消耗不超剩余机动」且「指挥点足够」时接受（Req 4.5/4.8、Property 13）。
                if (cost <= unit.Movement && balance >= RepositionCpCost)
                {
                    chosenPath = newPath;                       // 只改移动，不改命令性质（Req 4.4）
                    cp[unit.Owner] = balance - RepositionCpCost; // 扣指挥点（Req 4.7）
                    repositioned.Add(unit.Id);
                    log.Add(new TurnRecordEntry(
                        s.DayIndex, RepositionKind, unit.Id, rep.TriggerTick,
                        $"newPathLen={newPath.Count}")); // 含触发时点（Req 4.7/21.6）
                }

                // 指挥点不足或超出剩余机动 → 拒绝，保持原计划：不扣点、不记日志（Req 4.8）。
            }

            effectivePath[unit.Id] = chosenPath;
        }

        // 本回合实际实施移动的单位（有非空有效路径即视为推进）：供控制区产生判定（Req 11.3）。
        var movedUnits = new HashSet<UnitId>();
        foreach (var unit in ordered)
        {
            if (effectivePath[unit.Id].Count > 0)
            {
                movedUnits.Add(unit.Id);
            }
        }

        // 各方控制区格（基于快照位置，Req 11.1–11.3/11.7）；移动方受敌方控制区封锁。
        var blueZoc = new HashSet<HexCoord>(
            ZoneOfControl.ZocCells(s.Units, Side.Blue, snapshot, orders, movedUnits));
        var redZoc = new HashSet<HexCoord>(
            ZoneOfControl.ZocCells(s.Units, Side.Red, snapshot, orders, movedUnits));

        // ── 第二步：逐格推进、判定接敌锁定与遭遇（Req 4.2/4.3/4.6、15.2）──
        var newUnits = new List<UnitState>(ordered.Length);
        foreach (var unit in ordered)
        {
            var order = orders.GetValueOrDefault(unit.Id);
            var enemyZoc = unit.Owner == Side.Blue ? redZoc : blueZoc;
            var path = effectivePath[unit.Id];

            // 控制区「进入即停」截断（Req 11.4/11.5）。
            var afterZoc = ZoneOfControl.StopAtZoc(path, enemyZoc, unit.Flags);

            // 逐格推进，按剩余机动止步：余量不足以进入下一格即停在当前落点（Req 15.2）。
            var budget = unit.Movement;
            var pos = snapshot[unit.Id];
            foreach (var cell in afterZoc)
            {
                if (budget < DefaultMoveCostPerCell)
                {
                    break;
                }

                budget -= DefaultMoveCostPerCell;
                pos = cell;
            }

            var reachedEnd = afterZoc.Count > 0 && pos == afterZoc[afterZoc.Count - 1];

            // 锁定条件之一：被敌方控制区截停且实际停在该控制区格（Req 4.3/11.4）。
            var lockedByZoc = reachedEnd && enemyZoc.Contains(pos);

            // 锁定条件之二：进攻准备移动到与目标六角距离 1（Req 4.2）。
            var attackPrepContact = order is { Command: Command.AttackPrep, Target: { } tgt }
                && HexCoord.Distance(pos, tgt) == 1;

            // 锁定条件之三：与敌同格触发接触（机动冲突，Req 4.3/13.1）。
            var sameCellContact = EnemyWithin(unit.Owner, pos, 0, s.Units, snapshot);

            // 被临机改向后撞敌（落点与敌相邻）→ 按遭遇战（表三）处理（Req 4.6）。
            var repositionEncounter = repositioned.Contains(unit.Id)
                && EnemyWithin(unit.Owner, pos, 1, s.Units, snapshot);

            var locked = lockedByZoc || attackPrepContact || sameCellContact || repositionEncounter;

            if (locked)
            {
                log.Add(new TurnRecordEntry(s.DayIndex, LockedKind, unit.Id, null, null));
            }

            if (repositionEncounter)
            {
                log.Add(new TurnRecordEntry(
                    s.DayIndex, EncounterKind, unit.Id, null, $"at={pos}"));
            }

            // 落点写入新单位实例；命令性质与其余属性均不变（Req 2.1/4.4）。
            newUnits.Add(unit with { Position = pos });
        }

        // 指挥点消费写回（Req 4.7）。
        var newCards = new Dictionary<Side, CardState>();
        foreach (var kv in s.Cards)
        {
            newCards[kv.Key] = kv.Value with { Cp = cp[kv.Key] };
        }

        return s with
        {
            Units = newUnits,
            Cards = newCards,
            TurnLog = log,
        };
    }

    /// <summary>一条路径的移动消耗（统一模型：格数 × 每格消耗，Req 15.2）。</summary>
    private static int PathMoveCost(IReadOnlyList<HexCoord> path)
        => path.Count * DefaultMoveCostPerCell;

    /// <summary>
    /// 是否存在敌方单位处于 <paramref name="pos"/> 的 <paramref name="radius"/> 六角距离内（含）。
    /// 敌方位置以机动阶段开始的快照为准（Req 11.7）。
    /// </summary>
    private static bool EnemyWithin(
        Side owner,
        HexCoord pos,
        int radius,
        IReadOnlyList<UnitState> units,
        IReadOnlyDictionary<UnitId, HexCoord> snapshot)
    {
        foreach (var enemy in units)
        {
            if (enemy.Owner == owner)
            {
                continue;
            }

            var enemyPos = snapshot.TryGetValue(enemy.Id, out var p) ? p : enemy.Position;
            if (HexCoord.Distance(pos, enemyPos) <= radius)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 由 <see cref="TurnCommands.Orders"/> 构建「单位 → 命令」映射（阶段 0 已保证每单位唯一，Req 3.2）。
    /// </summary>
    private static IReadOnlyDictionary<UnitId, UnitOrder> BuildOrderMap(TurnCommands cmds)
    {
        var map = new Dictionary<UnitId, UnitOrder>();
        if (cmds.Orders is { } orders)
        {
            foreach (var order in orders)
            {
                map[order.Unit] = order;
            }
        }

        return map;
    }

    /// <summary>
    /// 由 <see cref="TurnCommands.Repositions"/> 构建「单位 → 临机机动」映射。
    /// 同一单位存在多条时取触发时点最早者，并以稳定序（先单位 id、再触发时点）遍历以保确定性（Req 2.6）。
    /// </summary>
    private static IReadOnlyDictionary<UnitId, RepositionCommand> BuildRepositionMap(TurnCommands cmds)
    {
        var map = new Dictionary<UnitId, RepositionCommand>();
        if (cmds.Repositions is { } reps)
        {
            foreach (var rep in reps
                .OrderBy(static r => r.Unit.Value)
                .ThenBy(static r => r.TriggerTick))
            {
                if (!map.ContainsKey(rep.Unit))
                {
                    map[rep.Unit] = rep;
                }
            }
        }

        return map;
    }
}
