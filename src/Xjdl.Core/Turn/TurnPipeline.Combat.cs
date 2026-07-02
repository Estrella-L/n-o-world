using Xjdl.Core.Combat;
using Xjdl.Core.Hex;
using Xjdl.Core.Modifiers;
using Xjdl.Core.Random;
using Xjdl.Core.State;
using CombatAdvance = Xjdl.Core.Combat.Advance;
// 类名消歧：TurnPipeline 内已有私有阶段方法 Retreat/Advance/SelectTable/Snapshot，
// 与 Xjdl.Core.Combat 下的同名静态类冲突。以下别名保证阶段实现可无歧义地调用战斗助手类。
using CombatRetreat = Xjdl.Core.Combat.Retreat;

namespace Xjdl.Core.Turn;

/// <summary>
/// 阶段 3–8（战斗结算）的纯函数串接（Req 3.3、3.5、3.6、3.7、3.8、3.9、9.5）。
/// 与 <see cref="TurnPipeline"/> 骨架合并为同一部分类，仅承载战斗结算阶段逻辑，不原地修改输入（Req 2.1）。
/// <para>
/// 各阶段方法（<see cref="SelectTablePhase"/>/<see cref="SnapshotPhase"/>/<see cref="RollAndReadPhase"/>/
/// <see cref="ApplyCasualtiesPhase"/>/<see cref="RetreatPhase"/>/<see cref="AdvancePhase"/>）依次由
/// <c>TurnPipeline.cs</c> 中对应的阶段骨架委托调用，串接顺序即 3→4→5→6→7→8（Req 3.1）。
/// </para>
/// <para><b>阶段职责：</b></para>
/// <list type="number">
/// <item><b>阶段 3 选表</b>（<see cref="SelectTablePhase"/>）：以 <see cref="ContactBuilder.Build"/> 合并全场
/// 进攻准备为若干场战斗（Req 5.5），逐场用 <see cref="TableSelection.SelectTable"/> 定表（Req 5.1/5.2），
/// 将选表结果写入 <see cref="GameState.TurnLog"/> 作为审计记录，不改数值状态。</item>
/// <item><b>阶段 4 快照</b>（<see cref="SnapshotPhase"/>）：冻结全场攻/防/韧性（<see cref="BattleSnapshot"/>，Req 3.3）。
/// 由于阶段 3/4/5 均不改单位数值，进入阶段 6 时 <see cref="GameState.Units"/> 仍为该快照值，故本实现以
/// 「阶段 4 入口状态即快照」表达冻结：全部火力比与歼灭判定（阶段 5/6）均直接读取彼时未变的单位数值。</item>
/// <item><b>阶段 5 掷骰读表</b>（<see cref="RollAndReadPhase"/>）：逐场 基础火力比 → 累加支援等移档 →
/// <see cref="ModifierPipeline.FinalColumn"/>（±2 封顶）定最终列 → <c>rng.Fork(battleId)</c> 掷 3D6 →
/// 叠加地形防御 DRM → <see cref="CombatTables.ReadTable"/> 读表，<b>仅记录结果、不执行</b>（Req 3.4/3.5）。</item>
/// <item><b>阶段 6 战损同步结算</b>（<see cref="ApplyCasualtiesPhase"/>）：把每个单位在<em>所有</em>战斗中的应受
/// 战损累加后经 <see cref="Casualty.ApplyCasualties"/> 一次性扣除，以<em>快照</em>剩余韧性判歼灭（Req 3.6/8.4）；
/// 退/推进冲突以 <see cref="ConflictResolution.Resolve"/> 按退优先裁定（Req 3.7），并把撤退单位、冲突格与
/// 推进意图写入 <see cref="GameState.TurnLog"/> 交由阶段 7/8 消费。</item>
/// <item><b>阶段 7 撤退</b>（<see cref="RetreatPhase"/>）：在阶段 6 战损与歼灭<em>之后</em>执行（Req 3.8），
/// 以 <see cref="CombatRetreat.Resolve"/> 为每个撤退单位择合法撤退格并移动，无路可退则额外战损并就地歼灭（Req 12.3）。</item>
/// <item><b>阶段 8 推进</b>（<see cref="AdvancePhase"/>）：仅在冲突格被撤退/歼灭腾空后，由存活的优势方/进攻方经
/// <see cref="CombatAdvance.Decide"/> 可选推进占领（Req 3.9），以「格」为单位、先腾空后占领（Req 9.5）。</item>
/// </list>
/// <para><b>确定性：</b>全程整数运算、按稳定 <see cref="UnitId"/>/<c>(Q,R)</c> 序遍历（Req 2.5/2.6）；
/// 每场战斗的掷骰以 <c>rng.Fork((ulong)battleId)</c> 派生独立子流，与主随机源解耦，保证多场战斗互不干扰且可重放
/// （Req 2.3）。阶段 5 与阶段 6 以同一纯函数 <see cref="ResolveOutcomes"/> 从<em>同一未变快照</em>重算结果，
/// 且都只用 <c>Fork</c> 不推进主随机源，故两阶段结果字节级一致。</para>
/// <para><b>简化假设（documented；待后续任务补全接线）：</b></para>
/// <list type="number">
/// <item><b>地形防御 DRM = 0</b>：<see cref="Terrain.TerrainSystem.ResolveDrm"/> 需要 <see cref="TerrainProfile"/>，
/// 而 <see cref="GameState"/> 未携带地形档，故本串接采用中性均匀地形（DRM 恒 <see cref="NeutralTerrainDrm"/>＝0）。
/// 骰轴接入留待地形档随 <see cref="GameState"/> 落地后处理。</item>
/// <item><b>战损次数 N=1</b>：<see cref="ResultCode"/> 只区分结果类型、不携带 N；本实现对「-N」类结果统一取
/// <see cref="SimplifiedCasualtyN"/>＝1 次战损，对「歼」类经 <see cref="Casualty.ResolveAnnihilation"/> 以
/// <see cref="Casualty.DowngradeRetreatCasualties"/>（3）判降级或阵亡。真实 N 分级留待读表层细化。</item>
/// <item><b>表二/表三劣势方</b>：以「进攻战斗力较低方」为劣势方；1:1 平局按对称处理（双方各承受 1 次），
/// 不引入两次独立掷骰的 <see cref="MutualTieResolver"/> 细分（该细分留待后续）。</item>
/// <item><b>机动冲突遭遇战（表三）</b>：Req 13.1/13.2 的同格/互换遭遇由阶段 2（<see cref="ManeuverPhase"/>）以
/// 日志条目表达，本串接的战斗仅来自 <see cref="ContactBuilder"/>（进攻准备接触，表一/表二）；将机动遭遇重建为
/// 独立战斗需阶段 2 存储 <see cref="ManeuverIntent"/>，超出本任务改动范围（不改机动阶段），故留待后续。</item>
/// <item><b>阶段 6→7→8 的数据传递</b>：阶段间仅流转 <see cref="GameState"/>，故撤退单位、冲突格与推进意图以
/// <see cref="GameState.TurnLog"/> 条目承载（<see cref="RetreatOrderKind"/>/<see cref="ContestedCellKind"/>/
/// <see cref="AdvanceOrderKind"/>），并在被消费后从日志剔除，避免污染跨回合日志。</item>
/// <item><b>撤退后方参考/推进单位</b>：撤退择优的后方参考点取 <c>null</c>（仅按「远离敌方 + 固定方向序」择优）；
/// 推进以最小 id 的相邻存活单位入格（单位粒度的可选推进），互损同场出局（Req 12.6）在本串接判为否（无同格互-N 场景）。</item>
/// </list>
/// </summary>
internal static partial class TurnPipeline
{
    /// <summary>中性均匀地形的防御 DRM（骰轴），地形档接入前的确定性占位（Req 15.3）。</summary>
    private const int NeutralTerrainDrm = 0;

    /// <summary>「-N」类结果的简化战损次数（结果代码不携带 N 时的确定性占位，Req 8.1）。</summary>
    private const int SimplifiedCasualtyN = 1;

    /// <summary>日志类别键：阶段 3 选表结果（审计）。</summary>
    private const string TableSelectionKind = "TableSelection";

    /// <summary>日志类别键：阶段 4 战力快照标记（Req 3.3）。</summary>
    private const string SnapshotKind = "Snapshot";

    /// <summary>日志类别键：阶段 5 读表结果（仅记录、不执行，Req 3.4/3.5）。</summary>
    private const string CombatResultKind = "CombatResult";

    /// <summary>日志类别键（阶段 6 产出、阶段 7 消费）：冲突格坐标，用于撤退合法性裁定（Req 12.2）。</summary>
    private const string ContestedCellKind = "ContestedCell";

    /// <summary>日志类别键（阶段 6 产出、阶段 7 消费）：须撤退的单位（Req 3.7/3.8）。</summary>
    private const string RetreatOrderKind = "RetreatOrder";

    /// <summary>日志类别键（阶段 6 产出、阶段 8 消费）：待推进占领的冲突格与推进方（Req 3.9）。</summary>
    private const string AdvanceOrderKind = "AdvanceOrder";

    /// <summary>日志类别键：阶段 7 撤退落点（审计）。</summary>
    private const string RetreatedKind = "Retreated";

    /// <summary>日志类别键：阶段 7 无路可退就地歼灭（Req 12.3，审计）。</summary>
    private const string RetreatAnnihilatedKind = "RetreatAnnihilated";

    /// <summary>日志类别键：阶段 8 推进占领（审计）。</summary>
    private const string AdvancedKind = "Advanced";

    // ── 内部结算模型（不可变、确定性）────────────────────────────────────

    /// <summary>一场战斗对单个单位的战损/位移指令（由某场结果映射得到）。</summary>
    /// <param name="Unit">承受方单位 id。</param>
    /// <param name="Casualties">该场应受战损次数（可为 0，如「退/撤离」无战损）。</param>
    /// <param name="Retreat">该场是否要求该单位撤退（Req 3.8）。</param>
    /// <param name="Annihilated">该场结果是否为（未降级的）歼灭（Req 8.4）。</param>
    private readonly record struct CasualtyOrder(UnitId Unit, int Casualties, bool Retreat, bool Annihilated);

    /// <summary>一场战斗的完整结算产物（阶段 5 记录、阶段 6 施加共用，Req 3.3/3.6）。</summary>
    /// <param name="BattleId">稳定战斗序号（合并接触列表中的下标），也是 <c>rng.Fork</c> 的盐（Req 2.3）。</param>
    /// <param name="Table">本场交战表。</param>
    /// <param name="Target">目标格（防守方所在格）。</param>
    /// <param name="AttackingSide">进攻方阵营（推进资格归属方，Req 3.9）。</param>
    /// <param name="DefenderId">防守方主攻单位 id；<see cref="AdvanceOnly"/> 时为 <c>null</c>。</param>
    /// <param name="Result">读表结果代码（<see cref="AdvanceOnly"/> 时无意义）。</param>
    /// <param name="Roll">本场调整后 3D6 读数（审计）。</param>
    /// <param name="Column">本场最终火力比档位（审计）。</param>
    /// <param name="Orders">本场对各单位的战损/位移指令。</param>
    /// <param name="AdvanceOnly">目标已空、不触发战斗、进攻方可直接推进（Req 5.4）。</param>
    private sealed record BattleOutcome(
        int BattleId,
        CombatTable Table,
        HexCoord Target,
        Side AttackingSide,
        UnitId? DefenderId,
        ResultCode Result,
        int Roll,
        int Column,
        IReadOnlyList<CasualtyOrder> Orders,
        bool AdvanceOnly);

    // ── 阶段 3：接触选表 ─────────────────────────────────────────────────

    /// <summary>
    /// 阶段 3 选表（Req 5.1/5.2/5.5）：合并全场进攻准备为若干场战斗并逐场定表，将选表结果记入
    /// <see cref="GameState.TurnLog"/>（审计）。本阶段不改数值状态，原样返回单位（快照在阶段 4 冻结）。
    /// </summary>
    internal static GameState SelectTablePhase(GameState s, TurnCommands cmds, ISeededRng rng)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(cmds);
        _ = rng;

        var orders = cmds.Orders ?? Array.Empty<UnitOrder>();
        var merged = ContactBuilder.Build(s, orders);
        var ordersMap = BuildOrderMap(cmds);

        var log = new List<TurnRecordEntry>(s.TurnLog);
        for (var battleId = 0; battleId < merged.Count; battleId++)
        {
            var battle = merged[battleId];
            if (battle.AdvanceOnly)
            {
                log.Add(new TurnRecordEntry(
                    s.DayIndex, TableSelectionKind, null, null,
                    $"battle={battleId};advanceOnly;target={EncodeCell(battle.Target)}"));
                continue;
            }

            var table = SelectTableFor(s, battle, ordersMap);
            log.Add(new TurnRecordEntry(
                s.DayIndex, TableSelectionKind, null, null,
                $"battle={battleId};table={table};target={EncodeCell(battle.Target)}"));
        }

        return s with { TurnLog = log };
    }

    // ── 阶段 4：战力快照 ─────────────────────────────────────────────────

    /// <summary>
    /// 阶段 4 战力快照（Req 3.3）：冻结全场攻/防/韧性。阶段 3/4/5 不改单位数值，故进入阶段 6 时
    /// <see cref="GameState.Units"/> 仍等于此快照——本实现以「阶段 4 入口状态即快照」表达冻结，
    /// 阶段 5/6 一律读取彼时未变的单位数值作火力比与歼灭判定。仅记一条快照标记，原样返回状态。
    /// </summary>
    internal static GameState SnapshotPhase(GameState s, TurnCommands cmds, ISeededRng rng)
    {
        ArgumentNullException.ThrowIfNull(s);
        _ = cmds;
        _ = rng;

        // 构造快照对象以彰显语义（Req 3.3）；数值冻结由后续阶段读取未变单位保证。
        _ = new BattleSnapshot(s.Units, s.Phase);

        var log = new List<TurnRecordEntry>(s.TurnLog)
        {
            new(s.DayIndex, SnapshotKind, null, null, $"units={s.Units.Count}"),
        };
        return s with { TurnLog = log };
    }

    // ── 阶段 5：掷骰读表（仅记录）───────────────────────────────────────

    /// <summary>
    /// 阶段 5 掷骰读表（Req 3.4/3.5）：逐场结算火力比→移档→最终列→3D6→地形 DRM→读表，
    /// <b>仅把结果写入 <see cref="GameState.TurnLog"/>，不执行任何战损/位移</b>。单位数值保持不变。
    /// </summary>
    internal static GameState RollAndReadPhase(GameState s, TurnCommands cmds, ISeededRng rng)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(cmds);
        ArgumentNullException.ThrowIfNull(rng);

        var outcomes = ResolveOutcomes(s, cmds, rng);

        var log = new List<TurnRecordEntry>(s.TurnLog);
        foreach (var o in outcomes)
        {
            if (o.AdvanceOnly)
            {
                continue;
            }

            log.Add(new TurnRecordEntry(
                s.DayIndex, CombatResultKind, o.DefenderId, null,
                $"battle={o.BattleId};table={o.Table};col={o.Column};roll={o.Roll};result={o.Result}"));
        }

        // 仅记录、不执行：单位数值原样返回（Req 3.5）。
        return s with { TurnLog = log };
    }

    // ── 阶段 6：战损同步结算 ─────────────────────────────────────────────

    /// <summary>
    /// 阶段 6 战损同步结算（Req 3.5/3.6/8.4）：把每个单位在所有战斗中的应受战损累加后一次性扣除
    /// （<see cref="Casualty.ApplyCasualties"/>），以快照剩余韧性判歼灭；退优先裁定撤退单位（Req 3.7），
    /// 并把撤退单位、冲突格、推进意图写入 <see cref="GameState.TurnLog"/> 交阶段 7/8 消费。
    /// </summary>
    internal static GameState ApplyCasualtiesPhase(GameState s, TurnCommands cmds, ISeededRng rng)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(cmds);
        ArgumentNullException.ThrowIfNull(rng);

        var outcomes = ResolveOutcomes(s, cmds, rng);

        // 逐单位累加：应受战损、是否被要求撤退、是否收到（未降级）歼灭结果（Req 3.6）。
        var totalCasualties = new Dictionary<UnitId, int>();
        var mustRetreat = new Dictionary<UnitId, bool>();
        foreach (var o in outcomes)
        {
            foreach (var order in o.Orders)
            {
                totalCasualties[order.Unit] = totalCasualties.GetValueOrDefault(order.Unit) + order.Casualties;
                if (order.Retreat)
                {
                    mustRetreat[order.Unit] = true;
                }
            }
        }

        // 以快照值（阶段 4 入口、此刻仍未变的 s.Units）一次性扣除并判歼灭（Req 8.4）。
        var removed = new HashSet<UnitId>();
        var newUnits = new List<UnitState>(s.Units.Count);
        foreach (var unit in s.Units.OrderBy(static u => u.Id.Value))
        {
            if (!totalCasualties.TryGetValue(unit.Id, out var cas) || cas == 0)
            {
                newUnits.Add(unit);
                continue;
            }

            var after = Casualty.ApplyCasualties(unit, cas);
            if (after.ResilienceLeft <= 0)
            {
                removed.Add(unit.Id); // 阵亡：移除（Req 8.2）
            }
            else
            {
                newUnits.Add(after);
            }
        }

        var log = new List<TurnRecordEntry>(s.TurnLog);

        // 退优先冲突消解（Req 3.7）：只要被要求撤退即撤退；已阵亡单位不再撤退。
        foreach (var id in mustRetreat.Keys.OrderBy(static k => k.Value))
        {
            if (removed.Contains(id))
            {
                continue;
            }

            var outcome = ConflictResolution.Resolve(mustRetreat: true, mustStayOrAdvance: false);
            if (outcome == ConflictOutcome.Retreat)
            {
                log.Add(new TurnRecordEntry(s.DayIndex, RetreatOrderKind, id, null, null));
            }
        }

        // 冲突格（供阶段 7 撤退合法性，Req 12.2）与推进意图（供阶段 8，Req 3.9）。
        foreach (var o in outcomes)
        {
            if (o.AdvanceOnly)
            {
                // 目标已空：进攻方可直接推进占领（Req 5.4）。
                log.Add(new TurnRecordEntry(
                    s.DayIndex, AdvanceOrderKind, null, null, EncodeAdvance(o.Target, o.AttackingSide)));
                continue;
            }

            log.Add(new TurnRecordEntry(
                s.DayIndex, ContestedCellKind, null, null, EncodeCell(o.Target)));

            // 目标格因守方阵亡或撤退而将腾空 → 进攻方获得推进资格（Req 3.9）。
            var defenderVacates = o.DefenderId is { } did
                && (removed.Contains(did) || mustRetreat.ContainsKey(did));
            if (defenderVacates)
            {
                log.Add(new TurnRecordEntry(
                    s.DayIndex, AdvanceOrderKind, null, null, EncodeAdvance(o.Target, o.AttackingSide)));
            }
        }

        return s with { Units = newUnits, TurnLog = log };
    }

    // ── 阶段 7：撤退 ─────────────────────────────────────────────────────

    /// <summary>
    /// 阶段 7 撤退（Req 3.8/12.1/12.2/12.3）：在阶段 6 战损与歼灭之后，为每个 <see cref="RetreatOrderKind"/>
    /// 标记的存活单位以 <see cref="CombatRetreat.Resolve"/> 择合法撤退格并移动；无路可退则就地歼灭（移除）。
    /// 消费（剔除）<see cref="RetreatOrderKind"/> 日志条目，保留 <see cref="ContestedCellKind"/>/<see cref="AdvanceOrderKind"/> 供阶段 8。
    /// </summary>
    internal static GameState RetreatPhase(GameState s, TurnCommands cmds, ISeededRng rng)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(cmds);
        _ = rng;

        var retreatIds = s.TurnLog
            .Where(e => e.Kind == RetreatOrderKind && e.Unit is not null)
            .Select(e => e.Unit!.Value)
            .OrderBy(static id => id.Value)
            .ToList();

        var log = new List<TurnRecordEntry>(s.TurnLog);
        if (retreatIds.Count == 0)
        {
            return s; // 无撤退，保持不变（无 RetreatOrder 可剔除）。
        }

        var conflictCells = new HashSet<HexCoord>(
            s.TurnLog.Where(e => e.Kind == ContestedCellKind && e.Detail is not null)
                .Select(e => DecodeCell(e.Detail!)));

        // 敌方进攻准备指向的格（Req 12.2）。
        var attackPrepTargets = new HashSet<HexCoord>();
        foreach (var order in cmds.Orders ?? Array.Empty<UnitOrder>())
        {
            if (order.Command == Command.AttackPrep && order.Target is { } t)
            {
                attackPrepTargets.Add(t);
            }
        }

        var ordersMap = BuildOrderMap(cmds);
        var movedNone = (IReadOnlySet<UnitId>)new HashSet<UnitId>();

        // 可变的单位位置副本，随撤退逐步更新占位。
        var units = s.Units.ToList();

        foreach (var id in retreatIds)
        {
            var idx = units.FindIndex(u => u.Id == id);
            if (idx < 0)
            {
                continue; // 已被移除（阵亡）。
            }

            var unit = units[idx];
            var origin = unit.Position;
            var enemy = unit.Owner == Side.Blue ? Side.Red : Side.Blue;

            var occupied = (IReadOnlySet<HexCoord>)new HashSet<HexCoord>(
                units.Where(u => u.Id != id).Select(u => u.Position));

            // 敌方控制区（以当前位置重算，Req 11.6/11.7）；产生方静止，movedUnits 取空集。
            var snapshotPositions = units.ToDictionary(u => u.Id, u => u.Position);
            var enemyZoc = (IReadOnlySet<HexCoord>)new HashSet<HexCoord>(
                ZoneOfControl.ZocCells(units, enemy, snapshotPositions, ordersMap, movedNone));

            // 到最近敌方的六角距离；无敌方时取极大值（Req 12.1）。
            int DistanceToNearestEnemy(HexCoord cell)
            {
                var best = int.MaxValue;
                foreach (var e in units)
                {
                    if (e.Owner != enemy)
                    {
                        continue;
                    }

                    best = Math.Min(best, HexCoord.Distance(cell, e.Position));
                }

                return best;
            }

            var outcome = CombatRetreat.Resolve(
                origin,
                occupied,
                conflictCells,
                attackPrepTargets,
                enemyZoc,
                DistanceToNearestEnemy,
                rearReference: null);

            if (outcome.Destination is { } dest)
            {
                units[idx] = unit with { Position = dest };
                log.Add(new TurnRecordEntry(s.DayIndex, RetreatedKind, id, null, EncodeCell(dest)));
            }
            else
            {
                // 无合法撤退格：额外 1 次战损 → 仍无格 → 就地歼灭（Req 12.3）。
                units.RemoveAt(idx);
                log.Add(new TurnRecordEntry(s.DayIndex, RetreatAnnihilatedKind, id, null, null));
            }
        }

        // 消费撤退指令：剔除 RetreatOrder 条目（保留冲突格/推进意图供阶段 8）。
        var newLog = log.Where(e => e.Kind != RetreatOrderKind).ToList();
        return s with { Units = units, TurnLog = newLog };
    }

    // ── 阶段 8：推进 ─────────────────────────────────────────────────────

    /// <summary>
    /// 阶段 8 推进（Req 3.9/9.5/12.4）：仅在冲突格被撤退或歼灭腾空后，由存活的优势方/进攻方经
    /// <see cref="CombatAdvance.Decide"/> 可选推进占领（以格为单位、先腾空后占领）。消费（剔除）
    /// <see cref="AdvanceOrderKind"/>/<see cref="ContestedCellKind"/> 日志条目，保持跨回合日志整洁。
    /// </summary>
    internal static GameState AdvancePhase(GameState s, TurnCommands cmds, ISeededRng rng)
    {
        ArgumentNullException.ThrowIfNull(s);
        _ = cmds;
        _ = rng;

        var advanceOrders = s.TurnLog
            .Where(e => e.Kind == AdvanceOrderKind && e.Detail is not null)
            .Select(e => DecodeAdvance(e.Detail!))
            .OrderBy(static a => a.Cell.Q).ThenBy(static a => a.Cell.R).ThenBy(static a => (int)a.Side)
            .ToList();

        var log = new List<TurnRecordEntry>(s.TurnLog);
        if (advanceOrders.Count == 0)
        {
            // 无推进意图：仍需清理可能存在的冲突格条目。
            var cleaned = log.Where(e => e.Kind != ContestedCellKind && e.Kind != AdvanceOrderKind).ToList();
            return s with { TurnLog = cleaned };
        }

        var units = s.Units.ToList();

        foreach (var order in advanceOrders)
        {
            var cell = order.Cell;
            var side = order.Side;

            // 冲突格须先腾空方可占领（Req 9.5）。
            var cellVacated = !units.Any(u => u.Position == cell);

            // 有资格随格推进的存活单位：该方、与目标格相邻、快照剩余韧性 > 0（Req 12.4/12.5）。
            var candidates = units
                .Where(u => u.Owner == side && u.ResilienceLeft > 0
                    && HexCoord.Distance(u.Position, cell) == 1)
                .OrderBy(u => u.Id.Value)
                .ToList();

            var decision = CombatAdvance.Decide(
                cell, side, cellVacated, mutualElimination: false, candidates);

            if (!decision.CanAdvance || decision.EligibleUnits.Count == 0)
            {
                continue;
            }

            // 推进为可选，单位粒度：最小 id 的相邻存活单位入格（Req 9.5，堆叠上限此处目标格已空恒满足）。
            var mover = decision.EligibleUnits[0];
            var idx = units.FindIndex(u => u.Id == mover.Id);
            if (idx >= 0)
            {
                units[idx] = units[idx] with { Position = cell };
                log.Add(new TurnRecordEntry(s.DayIndex, AdvancedKind, mover.Id, null, EncodeCell(cell)));
            }
        }

        // 消费推进/冲突格条目，保持跨回合日志整洁。
        var newLog = log.Where(e => e.Kind != AdvanceOrderKind && e.Kind != ContestedCellKind).ToList();
        return s with { Units = units, TurnLog = newLog };
    }

    // ── 共享结算核心（阶段 5 记录 / 阶段 6 施加共用）──────────────────────

    /// <summary>
    /// 从当前（阶段 4 入口 = 未变快照）状态确定性地结算全部战斗（Req 3.3/3.6）。纯函数：
    /// 仅以 <c>rng.Fork((ulong)battleId)</c> 派生子流掷骰，<b>不推进主随机源</b>，故阶段 5 与阶段 6 两次调用
    /// 得到字节级一致的结果（Req 2.3）。按稳定 <see cref="UnitId"/>/battleId 序遍历（Req 2.6）。
    /// </summary>
    private static IReadOnlyList<BattleOutcome> ResolveOutcomes(
        GameState s, TurnCommands cmds, ISeededRng rng)
    {
        var orders = cmds.Orders ?? Array.Empty<UnitOrder>();
        var merged = ContactBuilder.Build(s, orders);
        var ordersMap = BuildOrderMap(cmds);

        // 参战单位集合（用于火力支援排除自身交战者，Req 10.3）。
        var engaged = new HashSet<UnitId>();
        foreach (var battle in merged)
        {
            if (battle.AdvanceOnly)
            {
                continue;
            }

            foreach (var atk in battle.MainAttackers)
            {
                engaged.Add(atk.Id);
            }

            var def = MainDefender(s, battle.Target);
            if (def is not null)
            {
                engaged.Add(def.Id);
            }
        }

        var engagedSet = (IReadOnlySet<UnitId>)engaged;
        var outcomes = new List<BattleOutcome>(merged.Count);

        for (var battleId = 0; battleId < merged.Count; battleId++)
        {
            var battle = merged[battleId];
            var attackingSide = AttackingSideOf(battle, s, ordersMap);

            if (battle.AdvanceOnly)
            {
                outcomes.Add(new BattleOutcome(
                    battleId, CombatTable.RegularAttack, battle.Target, attackingSide,
                    DefenderId: null, ResultCode.Stalemate, Roll: 0, Column: 0,
                    Orders: Array.Empty<CasualtyOrder>(), AdvanceOnly: true));
                continue;
            }

            var defender = MainDefender(s, battle.Target);
            if (defender is null)
            {
                continue; // 理论不达（非 AdvanceOnly 即目标有单位）。
            }

            var table = SelectTableFor(s, battle, ordersMap);

            // 基础火力比（整数对，Req 6.1/6.2）；防御/攻击力非正时跳过（无有效战斗）。
            FirePowerRatio ratio;
            if (table == CombatTable.RegularAttack)
            {
                if (defender.Defense <= 0)
                {
                    continue;
                }

                ratio = FirePower.ComputeRatio(table, battle.AttackNumerator, defender.Defense);
            }
            else
            {
                if (battle.AttackNumerator <= 0 || defender.Attack <= 0)
                {
                    continue;
                }

                ratio = FirePower.ComputeRatio(table, battle.AttackNumerator, defender.Attack);
            }

            var baseColumn = ratio.ToColumn();

            // 火力支援移档（Req 10.1/10.3/10.4）+ ±2 总封顶（Req 10.2/17.1）。
            var shifts = ResolveFireSupport(s, battle, attackingSide, engagedSet);
            var finalColumn = ModifierPipeline.FinalColumn(baseColumn, shifts);

            // 档位钳制到该表定义域：表一 0..5，表二/表三 1..5。
            var minCol = table == CombatTable.RegularAttack ? 0 : 1;
            var column = Math.Clamp(finalColumn, minCol, 5);

            // 每场以 battleId 派生独立子流掷 3D6（Req 2.3），叠加中性地形 DRM（简化，Req 15.3）。
            var roll = rng.Fork((ulong)battleId).Roll3D6();
            var adjustedRoll = roll + NeutralTerrainDrm;

            var result = CombatTables.ReadTable(table, column, adjustedRoll);
            var casualtyOrders = MapResult(result, table, battle, defender);

            outcomes.Add(new BattleOutcome(
                battleId, table, battle.Target, attackingSide,
                defender.Id, result, adjustedRoll, column, casualtyOrders, AdvanceOnly: false));
        }

        return outcomes;
    }

    /// <summary>
    /// 把读表结果映射为对各单位的战损/位移指令（Req 7.2/8.1/8.4，简化 N=1）。
    /// 「歼」类经 <see cref="Casualty.ResolveAnnihilation"/> 以 <see cref="Casualty.DowngradeRetreatCasualties"/> 判降级/阵亡。
    /// </summary>
    private static IReadOnlyList<CasualtyOrder> MapResult(
        ResultCode result, CombatTable table, MergedContact battle, UnitState defender)
    {
        var attackers = battle.MainAttackers;

        // 单个单位的战损指令构造（区分「-N」与「歼」）。
        static CasualtyOrder ForUnit(UnitState u, ResultCode res, bool retreat)
        {
            if (res is ResultCode.DefenderAnnihilate or ResultCode.LoserAnnihilate)
            {
                var resolved = Casualty.ResolveAnnihilation(
                    res, u.ResilienceLeft, Casualty.DowngradeRetreatCasualties);
                if (resolved is ResultCode.DefenderNRetreat or ResultCode.LoserNRetreat)
                {
                    // 快照韧性足够 → 降级为「N退」（Req 7.2）。
                    return new CasualtyOrder(u.Id, Casualty.DowngradeRetreatCasualties, true, false);
                }

                // 保持歼灭：应受战损取到快照剩余韧性，累计判歼灭（Req 8.4）。
                return new CasualtyOrder(u.Id, u.ResilienceLeft, false, true);
            }

            return new CasualtyOrder(u.Id, SimplifiedCasualtyN, retreat, false);
        }

        var list = new List<CasualtyOrder>();

        switch (result)
        {
            case ResultCode.AttackerN:
                foreach (var a in attackers)
                {
                    list.Add(ForUnit(a, ResultCode.AttackerN, false));
                }

                break;

            case ResultCode.DefenderN:
                list.Add(ForUnit(defender, ResultCode.DefenderN, false));
                break;

            case ResultCode.DefenderNRetreat:
                list.Add(ForUnit(defender, ResultCode.DefenderNRetreat, true));
                break;

            case ResultCode.DefenderAnnihilate:
                list.Add(ForUnit(defender, ResultCode.DefenderAnnihilate, false));
                break;

            case ResultCode.MutualN:
                list.Add(ForUnit(defender, ResultCode.MutualN, false));
                foreach (var a in attackers)
                {
                    list.Add(ForUnit(a, ResultCode.MutualN, false));
                }

                break;

            case ResultCode.LoserN:
            case ResultCode.LoserNRetreat:
            case ResultCode.LoserAnnihilate:
                {
                    var retreat = result == ResultCode.LoserNRetreat;
                    var loserIsDefender = LoserIsDefender(battle, defender, out var tie);
                    if (tie)
                    {
                        // 1:1 平局对称处理（简化）：双方各承受（Req 6.6 的细分留待后续）。
                        list.Add(ForUnit(defender, result, retreat));
                        foreach (var a in attackers)
                        {
                            list.Add(ForUnit(a, result, retreat));
                        }
                    }
                    else if (loserIsDefender)
                    {
                        list.Add(ForUnit(defender, result, retreat));
                    }
                    else
                    {
                        foreach (var a in attackers)
                        {
                            list.Add(ForUnit(a, result, retreat));
                        }
                    }

                    break;
                }

            case ResultCode.Withdraw:
                // 表三「退」：劣势方撤出一格、无战损（Req 7.4）。以 0 战损 + 撤退表达。
                {
                    var loserIsDefender = LoserIsDefender(battle, defender, out var tie);
                    if (!tie && loserIsDefender)
                    {
                        list.Add(new CasualtyOrder(defender.Id, 0, true, false));
                    }
                    else if (!tie)
                    {
                        foreach (var a in attackers)
                        {
                            list.Add(new CasualtyOrder(a.Id, 0, true, false));
                        }
                    }

                    break;
                }

            case ResultCode.Stalemate:
            default:
                // 僵：双方停在原地、无战损（Req 7.3）。无指令。
                break;
        }

        return list;
    }

    /// <summary>表二/表三劣势方判定（简化）：进攻战斗力较低者为劣势方；相等则平局（<paramref name="tie"/> 为真）。</summary>
    private static bool LoserIsDefender(MergedContact battle, UnitState defender, out bool tie)
    {
        var attackerPower = battle.AttackNumerator;
        var defenderPower = defender.Attack;
        tie = attackerPower == defenderPower;
        return defenderPower < attackerPower; // 防守方进攻力较低 → 防守方为劣势方
    }

    /// <summary>取目标格主攻防守单位（Req 9.2）；目标格为空返回 <c>null</c>。</summary>
    private static UnitState? MainDefender(GameState s, HexCoord target)
        => Stacking.SelectMainUnit(s.Units.Where(u => u.Position == target));

    /// <summary>
    /// 逐场定表（Req 5.1/5.2）：防守方主攻单位若下达进攻准备且指向某进攻格 → 表二（对攻）；否则表一（进攻）。
    /// </summary>
    private static CombatTable SelectTableFor(
        GameState s, MergedContact battle, IReadOnlyDictionary<UnitId, UnitOrder> ordersMap)
    {
        var defender = MainDefender(s, battle.Target);
        var defenderAttacksBack = false;
        if (defender is not null
            && ordersMap.TryGetValue(defender.Id, out var defOrder)
            && defOrder.Command == Command.AttackPrep
            && defOrder.Target is { } dt)
        {
            defenderAttacksBack = battle.AttackingCells.Contains(dt);
        }

        var first = new ContactSide(Command.AttackPrep, AttackPrepTargetsOpponent: true);
        var second = new ContactSide(
            defenderAttacksBack ? Command.AttackPrep : (defender?.Command ?? Command.Hold),
            AttackPrepTargetsOpponent: defenderAttacksBack);
        return TableSelection.SelectTable(new Contact(first, second));
    }

    /// <summary>进攻方阵营：非空场取主攻单位阵营；空场（直接推进）由指向该目标的进攻准备命令推断。</summary>
    private static Side AttackingSideOf(
        MergedContact battle, GameState s, IReadOnlyDictionary<UnitId, UnitOrder> ordersMap)
    {
        if (battle.MainAttackers.Count > 0)
        {
            return battle.MainAttackers[0].Owner;
        }

        // AdvanceOnly：找一条指向该目标格的进攻准备命令，取其单位阵营。
        foreach (var unit in s.Units.OrderBy(static u => u.Id.Value))
        {
            if (ordersMap.TryGetValue(unit.Id, out var order)
                && order.Command == Command.AttackPrep
                && order.Target == battle.Target)
            {
                return unit.Owner;
            }
        }

        return Side.Blue; // 兜底（不影响：空场无守方，推进裁定仅按存活相邻单位进行）。
    }

    // ── 日志 Detail 编解码（阶段间经 TurnLog 传递结构化数据的简化载体）─────

    private static string EncodeCell(HexCoord c) => $"{c.Q}:{c.R}";

    private static HexCoord DecodeCell(string detail)
    {
        var parts = detail.Split(':');
        return new HexCoord(int.Parse(parts[0]), int.Parse(parts[1]));
    }

    private static string EncodeAdvance(HexCoord c, Side side) => $"{c.Q}:{c.R}:{(int)side}";

    private static (HexCoord Cell, Side Side) DecodeAdvance(string detail)
    {
        var parts = detail.Split(':');
        var cell = new HexCoord(int.Parse(parts[0]), int.Parse(parts[1]));
        var side = (Side)int.Parse(parts[2]);
        return (cell, side);
    }
}
