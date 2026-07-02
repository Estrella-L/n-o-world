using Xjdl.Core.Combat;
using Xjdl.Core.Hex;
using Xjdl.Core.State;

namespace Xjdl.Core.Turn;

/// <summary>
/// 阶段 2（机动）的<b>机动冲突裁定</b>纯函数助手（Req 13.1–13.4）。
/// 与 <see cref="TurnPipeline"/> 骨架合并为同一部分类，仅承载「移动落点相撞」的确定性裁定，
/// 不原地修改输入（Req 2.1）。见 docs/01-战斗机制.md〈机动冲突〉与 design.md Property 43/44。
/// <para>
/// 语义要点（事实来源 Req 13.1–13.4）：
/// </para>
/// <list type="bullet">
/// <item>双方各有单位移入<em>同一空格</em>，或两敌对单位<em>互换格子</em>（A→B、B→A）：二者均<b>不占据</b>该格，
/// 在该处触发遭遇战（表三，Req 13.1/13.2）。</item>
/// <item>移入敌方<em>正在离开</em>的格：敌成功离开则我方占据该格；敌因他处战斗<em>未能离开</em>（仍据守）
/// 则触发<b>接触</b>而非占据（Req 13.3）。</item>
/// <item>友方移入同一格：按堆叠规则处理（<see cref="Stacking.AdmitIntoCell"/>），
/// 超出堆叠上限的单位<b>止步于前一格</b>（即其出发格，Req 13.4）。</item>
/// </list>
/// <para>
/// <b>纯函数 / 确定性：</b>本裁定不消耗随机数、不读写外部状态；所有集合遍历与配对均以稳定
/// <see cref="UnitId"/> 升序、六角格 <c>(Q, R)</c> 字典序进行，保证结算次序无关、可重放（Req 2.6）。
/// </para>
/// <para>
/// <b>15.20 全流水线接线消费方式：</b>机动/接触阶段先逐格推进得到每个单位的「出发格（快照）」与
/// 「意图落点」及「是否成功离开出发格」（离开与否取决于该单位是否因自身战斗/控制区而被迫据守），
/// 打包为 <see cref="ManeuverIntent"/> 列表调用 <see cref="ResolveManeuverConflicts"/>。返回的
/// <see cref="ManeuverConflictResolution.FinalPositions"/> 用于写回各单位位置；
/// <see cref="ManeuverConflictResolution.Encounters"/> 交由接触选表阶段以表三（遭遇战）建接触；
/// <see cref="ManeuverConflictResolution.Contacts"/> 作为普通接触并入 <see cref="ContactBuilder"/> 的输入；
/// <see cref="ManeuverConflictResolution.StoppedByStacking"/> 用于向 <see cref="GameState.TurnLog"/> 记录止步事件。
/// 本任务只提供可复用的纯助手，<b>不改动</b>任何阶段骨架方法体（接线属 15.20）。
/// </para>
/// </summary>
internal static partial class TurnPipeline
{
    /// <summary>机动冲突的种类（供 <see cref="ManeuverEncounter"/> 标注遭遇战成因）。</summary>
    internal enum ManeuverConflictKind
    {
        /// <summary>双方单位移入同一空格（Req 13.1）。</summary>
        SameCell,

        /// <summary>两敌对单位互换格子、途中相遇（Req 13.2）。</summary>
        Swap,
    }

    /// <summary>
    /// 单个单位在机动阶段的移动意图，作为 <see cref="ResolveManeuverConflicts"/> 的输入原子（Req 13.1–13.4）。
    /// </summary>
    /// <param name="Unit">单位现状（提供 <see cref="UnitState.Id"/>/<see cref="UnitState.Owner"/>/攻击力等，供堆叠主攻判定）。</param>
    /// <param name="Origin">机动阶段开始时的出发格（快照位置，Req 11.7）；也是止步/不占据时的落点。</param>
    /// <param name="Destination">逐格推进后的意图落点；若与 <paramref name="Origin"/> 相同表示原地未动。</param>
    /// <param name="LeavesOrigin">
    /// 该单位是否<em>成功离开</em>出发格：为真表示确实腾空 <paramref name="Origin"/> 前往 <paramref name="Destination"/>；
    /// 为假表示因自身战斗/据守等原因未能离开（Req 13.3 的「敌未能离开」即以此为假表达）。
    /// 当 <paramref name="Destination"/> 等于 <paramref name="Origin"/> 时应为假。
    /// </param>
    internal readonly record struct ManeuverIntent(
        UnitState Unit,
        HexCoord Origin,
        HexCoord Destination,
        bool LeavesOrigin);

    /// <summary>
    /// 一处机动遭遇战（表三）：双方移入同一空格或两敌对单位互换（Req 13.1/13.2）。
    /// 参与单位均<b>不占据</b>争夺格，仍停在各自出发格。
    /// </summary>
    /// <param name="Location">遭遇战发生格：同格冲突为共同意图落点；互换冲突取两出发格中 <c>(Q, R)</c> 较小者作代表。</param>
    /// <param name="Kind">冲突成因（同格 / 互换）。</param>
    /// <param name="Participants">参与遭遇战的单位，按 <see cref="UnitId"/> 升序（确定性，Req 2.6）。</param>
    internal readonly record struct ManeuverEncounter(
        HexCoord Location,
        ManeuverConflictKind Kind,
        IReadOnlyList<UnitId> Participants);

    /// <summary>
    /// 一处机动接触：我方单位移入敌方<em>未能离开</em>（仍据守）的格（Req 13.3）。移入方不占据，停在出发格。
    /// </summary>
    /// <param name="Location">被移入的目标格（敌方据守格）。</param>
    /// <param name="Mover">尝试移入的单位。</param>
    /// <param name="Defender">据守目标格的敌方单位（该格按 <see cref="UnitId"/> 升序取最小者为代表）。</param>
    internal readonly record struct ManeuverContact(
        HexCoord Location,
        UnitId Mover,
        UnitId Defender);

    /// <summary>
    /// 一次因堆叠上限而止步的移入（Req 13.4）：友方移入使目标格超限，超出者止步于前一格（出发格）。
    /// </summary>
    /// <param name="Unit">被拒的移入单位。</param>
    /// <param name="IntendedCell">其原本意图进入的格。</param>
    /// <param name="StoppedAt">实际止步落点（其出发格 / 前一格）。</param>
    internal readonly record struct StoppedUnit(
        UnitId Unit,
        HexCoord IntendedCell,
        HexCoord StoppedAt);

    /// <summary>
    /// 机动冲突裁定的完整结果（不可变、确定性）。见 <see cref="ResolveManeuverConflicts"/>。
    /// </summary>
    /// <param name="FinalPositions">每个参与单位裁定后的落点（覆盖全部输入单位，按 <see cref="UnitId"/> 可查）。</param>
    /// <param name="Occupancy">占据映射：格 → 占据该格的单位（按 <see cref="UnitId"/> 升序）。仅含最终有单位的格。</param>
    /// <param name="Encounters">触发遭遇战（表三）的冲突（Req 13.1/13.2），按位置 <c>(Q, R)</c> 字典序。</param>
    /// <param name="Contacts">触发接触的移入（Req 13.3），按 <see cref="ManeuverContact.Mover"/> 升序。</param>
    /// <param name="StoppedByStacking">因堆叠上限止步于前一格的移入（Req 13.4），按 <see cref="StoppedUnit.Unit"/> 升序。</param>
    internal sealed record ManeuverConflictResolution(
        IReadOnlyDictionary<UnitId, HexCoord> FinalPositions,
        IReadOnlyDictionary<HexCoord, IReadOnlyList<UnitId>> Occupancy,
        IReadOnlyList<ManeuverEncounter> Encounters,
        IReadOnlyList<ManeuverContact> Contacts,
        IReadOnlyList<StoppedUnit> StoppedByStacking);

    /// <summary>依六角格 <c>(Q, R)</c> 字典序排序，保证遍历与输出确定（Req 2.6，与 <see cref="ContactBuilder"/> 一致）。</summary>
    private static readonly IComparer<HexCoord> ConflictHexOrder =
        Comparer<HexCoord>.Create(static (a, b) =>
        {
            var byQ = a.Q.CompareTo(b.Q);
            return byQ != 0 ? byQ : a.R.CompareTo(b.R);
        });

    /// <summary>
    /// 对一组机动意图做确定性冲突裁定（Req 13.1–13.4）。纯函数：不修改输入、不消耗随机数、可重放（Req 2.1/2.6）。
    /// </summary>
    /// <param name="intents">全部参与机动裁定的单位意图（含原地据守单位，其 <see cref="ManeuverIntent.LeavesOrigin"/> 为假）。</param>
    /// <param name="stackLimit">堆叠上限，默认 <see cref="Stacking.DefaultStackLimit"/>。须为正数。</param>
    /// <returns>落点、占据、遭遇战、接触与止步的完整裁定结果。</returns>
    /// <exception cref="ArgumentOutOfRangeException">当 <paramref name="stackLimit"/> 小于 1。</exception>
    internal static ManeuverConflictResolution ResolveManeuverConflicts(
        IReadOnlyList<ManeuverIntent> intents,
        int stackLimit = Stacking.DefaultStackLimit)
    {
        ArgumentNullException.ThrowIfNull(intents);
        if (stackLimit < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(stackLimit), stackLimit, "堆叠上限必须为正数。");
        }

        // 稳定顺序：一律按 UnitId 升序处理，杜绝依赖集合迭代顺序（Req 2.6）。
        var ordered = intents.OrderBy(static i => i.Unit.Id.Value).ToArray();

        // 落点初值：全部单位先停在各自出发格；成功占据的移入单位随后覆盖为意图落点。
        var finalPositions = new Dictionary<UnitId, HexCoord>(ordered.Length);
        foreach (var it in ordered)
        {
            finalPositions[it.Unit.Id] = it.Origin;
        }

        var encounters = new List<ManeuverEncounter>();
        var contacts = new List<ManeuverContact>();
        var stopped = new List<StoppedUnit>();

        // 「移入者」= 成功离开出发格且落点不同于出发格的单位（Req 13.x 的冲突主体）。
        static bool IsMover(ManeuverIntent i) => i.LeavesOrigin && i.Destination != i.Origin;

        // ── 第一步：敌对互换（A→B、B→A）判定为途中相遇 → 遭遇战（Req 13.2）──
        var consumed = new HashSet<UnitId>();
        for (var a = 0; a < ordered.Length; a++)
        {
            var ua = ordered[a];
            if (consumed.Contains(ua.Unit.Id) || !IsMover(ua))
            {
                continue;
            }

            for (var b = a + 1; b < ordered.Length; b++)
            {
                var ub = ordered[b];
                if (consumed.Contains(ub.Unit.Id) || !IsMover(ub))
                {
                    continue;
                }

                var enemies = ua.Unit.Owner != ub.Unit.Owner;
                var swap = ua.Origin == ub.Destination && ua.Destination == ub.Origin;
                if (enemies && swap)
                {
                    // 二者均不占据、停在各自出发格（落点初值已满足）；记一处互换遭遇战。
                    var location = ConflictHexOrder.Compare(ua.Origin, ub.Origin) <= 0
                        ? ua.Origin
                        : ub.Origin;
                    var pair = new[] { ua.Unit.Id, ub.Unit.Id }
                        .OrderBy(static id => id.Value).ToArray();
                    encounters.Add(new ManeuverEncounter(
                        location, ManeuverConflictKind.Swap, pair));

                    consumed.Add(ua.Unit.Id);
                    consumed.Add(ub.Unit.Id);
                    break; // ua 已配对，转下一个 a
                }
            }
        }

        // ── 第二步：按意图落点分组，逐格裁定同格冲突 / 移入据守格 / 友方堆叠（Req 13.1/13.3/13.4）──
        // 出发格 → 该格出发的意图（用于判定目标格是否有「据守（未离开）」的原住单位）。
        var residentsByCell = new Dictionary<HexCoord, List<ManeuverIntent>>();
        foreach (var it in ordered)
        {
            if (!residentsByCell.TryGetValue(it.Origin, out var list))
            {
                list = new List<ManeuverIntent>();
                residentsByCell[it.Origin] = list;
            }

            list.Add(it);
        }

        // 目标格 → 移入该格的意图（排除已被互换消解者）。以 SortedDictionary 保证遍历确定（Req 2.6）。
        var moversByTarget = new SortedDictionary<HexCoord, List<ManeuverIntent>>(ConflictHexOrder);
        foreach (var it in ordered)
        {
            if (consumed.Contains(it.Unit.Id) || !IsMover(it))
            {
                continue;
            }

            if (!moversByTarget.TryGetValue(it.Destination, out var list))
            {
                list = new List<ManeuverIntent>();
                moversByTarget[it.Destination] = list;
            }

            list.Add(it);
        }

        foreach (var (cell, moversRaw) in moversByTarget)
        {
            var movers = moversRaw.OrderBy(static m => m.Unit.Id.Value).ToList();

            // 目标格「据守（未离开）」的原住单位：Origin == cell 且非移入者。它们最终仍在该格。
            var stayers = residentsByCell.TryGetValue(cell, out var res)
                ? res.Where(r => !IsMover(r)).ToList()
                : new List<ManeuverIntent>();

            if (stayers.Count > 0)
            {
                // 目标格仍有据守单位（同阵营，初始不可能敌我共格）：区分友方堆叠 vs 敌方接触。
                var residentOwner = stayers[0].Unit.Owner;

                var friendlyMovers = movers
                    .Where(m => m.Unit.Owner == residentOwner).ToList();
                var enemyMovers = movers
                    .Where(m => m.Unit.Owner != residentOwner).ToList();

                // 敌方移入据守格：敌未能离开 → 触发接触，移入方不占据（停在出发格，Req 13.3）。
                var defender = stayers
                    .OrderBy(static s => s.Unit.Id.Value).First().Unit.Id;
                foreach (var em in enemyMovers)
                {
                    contacts.Add(new ManeuverContact(cell, em.Unit.Id, defender));
                }

                // 友方移入据守格：按堆叠上限裁定，超出者止步于前一格（Req 13.4）。
                if (friendlyMovers.Count > 0)
                {
                    var admission = Stacking.AdmitIntoCell(
                        stayers.Select(static s => s.Unit),
                        friendlyMovers.Select(static m => m.Unit),
                        stackLimit);

                    var admittedIds = admission.Admitted.Select(static u => u.Id).ToHashSet();
                    foreach (var fm in friendlyMovers)
                    {
                        if (admittedIds.Contains(fm.Unit.Id))
                        {
                            finalPositions[fm.Unit.Id] = cell; // 占据目标格
                        }
                        else
                        {
                            stopped.Add(new StoppedUnit(fm.Unit.Id, cell, fm.Origin));
                            // 落点保持出发格（初值），即止步于前一格。
                        }
                    }
                }

                continue;
            }

            // 目标格已空（无据守单位；敌方原住者若有则已成功离开，Req 13.3 的「成功离开」路径）。
            var hasBlue = movers.Any(static m => m.Unit.Owner == Side.Blue);
            var hasRed = movers.Any(static m => m.Unit.Owner == Side.Red);

            if (hasBlue && hasRed)
            {
                // 双方移入同一空格 → 遭遇战；全部移入者均不占据，停在各自出发格（Req 13.1）。
                var participants = movers
                    .Select(static m => m.Unit.Id)
                    .OrderBy(static id => id.Value).ToArray();
                encounters.Add(new ManeuverEncounter(
                    cell, ManeuverConflictKind.SameCell, participants));
                continue;
            }

            // 仅单方移入空格：按堆叠规则占据（含「移入敌方成功离开的格」占据，Req 13.3/13.4）。
            var admissionEmpty = Stacking.AdmitIntoCell(
                Array.Empty<UnitState>(),
                movers.Select(static m => m.Unit),
                stackLimit);
            var admittedEmptyIds = admissionEmpty.Admitted.Select(static u => u.Id).ToHashSet();
            foreach (var m in movers)
            {
                if (admittedEmptyIds.Contains(m.Unit.Id))
                {
                    finalPositions[m.Unit.Id] = cell;
                }
                else
                {
                    stopped.Add(new StoppedUnit(m.Unit.Id, cell, m.Origin));
                }
            }
        }

        // ── 汇总占据映射（格 → 单位，按 id 升序）────────────────────────────
        var occupancy = new SortedDictionary<HexCoord, List<UnitId>>(ConflictHexOrder);
        foreach (var it in ordered)
        {
            var pos = finalPositions[it.Unit.Id];
            if (!occupancy.TryGetValue(pos, out var list))
            {
                list = new List<UnitId>();
                occupancy[pos] = list;
            }

            list.Add(it.Unit.Id);
        }

        var occupancyOut = new Dictionary<HexCoord, IReadOnlyList<UnitId>>(occupancy.Count);
        foreach (var (pos, ids) in occupancy)
        {
            ids.Sort(static (x, y) => x.Value.CompareTo(y.Value));
            occupancyOut[pos] = ids;
        }

        return new ManeuverConflictResolution(
            FinalPositions: finalPositions,
            Occupancy: occupancyOut,
            Encounters: encounters
                .OrderBy(e => e.Location, ConflictHexOrder).ToArray(),
            Contacts: contacts
                .OrderBy(static c => c.Mover.Value).ToArray(),
            StoppedByStacking: stopped
                .OrderBy(static s => s.Unit.Value).ToArray());
    }
}
