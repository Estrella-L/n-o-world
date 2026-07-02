using Xjdl.Core.Combat;
using Xjdl.Core.Fog;
using Xjdl.Core.Hex;
using Xjdl.Core.Modifiers;
using Xjdl.Core.State;

namespace Xjdl.Core.Turn;

/// <summary>
/// 一次回合的双方可见度快照（Req 14.1–14.6）：以 <see cref="Side"/> 为维度分别持有
/// 「蓝方看红方各单位」与「红方看蓝方各单位」的可见度映射。
/// <para>
/// 由 <see cref="TurnPipeline.ComputeFogSnapshot"/> 产出，纯数据、不可变。回合初以上一回合末位置计算
/// 并在下令阶段内冻结（Req 14.5）；阶段 2 机动完成后由
/// <see cref="TurnPipeline.RecomputeFogAfterManeuver"/> 按新位置重算（Req 14.6）。
/// </para>
/// </summary>
/// <param name="ByBlue">蓝方对每个红方单位的可见度（键为红方 <see cref="UnitId"/>）。</param>
/// <param name="ByRed">红方对每个蓝方单位的可见度（键为蓝方 <see cref="UnitId"/>）。</param>
internal sealed record FogView(
    IReadOnlyDictionary<UnitId, Visibility> ByBlue,
    IReadOnlyDictionary<UnitId, Visibility> ByRed)
{
    /// <summary>取 <paramref name="viewer"/> 方看敌方各单位的可见度映射。</summary>
    public IReadOnlyDictionary<UnitId, Visibility> For(Side viewer)
        => viewer == Side.Blue ? ByBlue : ByRed;
}

/// <summary>
/// 战争迷雾刷新时点与火力支援归属的可复用纯函数助手（Req 14.5–14.7、4.9、10.1、10.3–10.5）。
/// 与 <see cref="TurnPipeline"/> 骨架合并为同一部分类，仅承载迷雾/支援归属逻辑，不原地修改输入（Req 2.1）。
/// <para>
/// <b>本任务（15.12）的定位：</b>提供可被战斗串接阶段（任务 15.20）与回合结束阶段（任务 15.24）消费的
/// 纯助手，而不改动既有阶段骨架。各助手均为确定性、整数运算、按稳定 <see cref="UnitId"/> 序遍历（Req 2.5/2.6）。
/// </para>
/// <para>
/// <b>迷雾刷新时点（Req 14.5/14.6/14.7）：</b>
/// </para>
/// <list type="number">
/// <item>回合开始：以「上一回合末位置」计算可见度快照。<see cref="TurnPipeline.Run"/> 收到的入参
/// <see cref="GameState"/> 其单位位置即上一回合末落点，故回合初调用
/// <see cref="ComputeFogSnapshot"/> 即得到「下令阶段所见画面」，并在下令阶段（阶段 0–1）内保持冻结——
/// 冻结由「调用方只在回合初计算一次、阶段 0–1 不再重算」这一时序保证（Req 14.5）。</item>
/// <item>阶段 2 机动完成后：以机动后新位置调用 <see cref="RecomputeFogAfterManeuver"/> 重算，
/// 使新进入视野的敌军翻为 <see cref="Visibility.Identified"/> 或 <see cref="Visibility.Spotted"/>（Req 14.6），
/// 同时对参战单位强制显形（Req 14.7）。</item>
/// </list>
/// <para>
/// <b>火力支援归属（Req 4.9/10.1/10.3/10.4/10.5）：</b><see cref="ResolveFireSupport"/> 以
/// <em>回合末位置</em> 判定某场战斗获得哪些火力支援（Req 4.9）：每个「在支援范围内、本回合未交战、
/// 且目标战斗格处于己方视野内」的己方火力支援单位贡献一次 <c>+1</c> 档、来源
/// <see cref="ModifierSource.Support"/> 的移档（Req 10.1）；自身处于交战中（Req 10.3）或目标格不在
/// 己方视野内（Req 10.4）时其贡献为 0。移档只作用于火力比所在列、绝不改单位攻防数值（Req 10.5），
/// 且总封顶由 <see cref="ModifierPipeline.FinalColumn"/> 施加 ±2（Req 10.2，见下方接线说明）。
/// </para>
/// <para>
/// <b>简化假设（待后续任务补全）：</b>
/// </para>
/// <list type="number">
/// <item><b>FogConfig 来源</b>：<see cref="GameState"/> 尚不携带 <see cref="FogConfig"/>，故
/// <see cref="ComputeFogSnapshot"/>/<see cref="RecomputeFogAfterManeuver"/> 由调用方注入
/// 迷雾配置（来自数据层规模档 / 场景配置）。</item>
/// <item><b>视野「格可见」判定</b>：<see cref="FogSystem"/> 输出的是敌方「单位」可见度，本任务需要判定
/// 「目标战斗格」是否在己方视野内（Req 10.4），故以 <see cref="CellInSideVision"/> 直接按己方各单位视野半径
/// 判定该格是否可见。夜晚视野折减（Req 18.2）已在 <see cref="FogSystem"/> 内实现，但
/// <see cref="CellInSideVision"/> 采用白天视野半径；夜晚下支援射程 -1（Req 18.3）与夜晚视野折减对格可见的影响
/// 统一留待任务 15.24 接入 <see cref="NightConfig"/> 时处理。</item>
/// <item><b>支援范围度量</b>：以支援单位回合末位置到「本场战斗任一格（目标格或各进攻格）」的最小六角距离
/// 不超过 <see cref="UnitState.SupportRange"/> 视为在范围内。</item>
/// <item><b>参战单位集合</b>：<see cref="UnitState"/> 无「交战中」字段，参战集合（<c>engagedUnits</c>）由战斗串接阶段
/// （15.20）在选表/接触后传入；本助手据此判定强制显形（Req 14.7）与支援单位是否自身交战（Req 10.3）。</item>
/// </list>
/// </summary>
internal static partial class TurnPipeline
{
    /// <summary>单个合法火力支援单位贡献的档数：单支援移 1 档（Req 10.1）。</summary>
    private const int SupportColumnShift = 1;

    /// <summary>
    /// 计算某一时点的双方可见度快照（Req 14.1）。回合初以上一回合末位置调用即得下令阶段所见（Req 14.5）。
    /// 纯函数：分别为蓝、红两方调用 <see cref="FogSystem.Compute"/>，不修改输入。
    /// </summary>
    /// <param name="s">状态快照（回合初时其单位位置即上一回合末落点）。</param>
    /// <param name="cfg">战争迷雾配置（V+1 环开关、夜晚视野除数）。</param>
    /// <returns>双方可见度快照 <see cref="FogView"/>。</returns>
    internal static FogView ComputeFogSnapshot(GameState s, FogConfig cfg)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(cfg);

        return new FogView(
            ByBlue: FogSystem.Compute(s, Side.Blue, cfg),
            ByRed: FogSystem.Compute(s, Side.Red, cfg));
    }

    /// <summary>
    /// 阶段 2 机动完成后的可见度重算（Req 14.6）：以机动后新位置重算双方可见度，
    /// 并对本回合参战（接触）单位强制显形为 <see cref="Visibility.Identified"/>（Req 14.7）。
    /// </summary>
    /// <param name="postManeuver">阶段 2 机动完成后的状态（单位为新落点）。</param>
    /// <param name="cfg">战争迷雾配置。</param>
    /// <param name="combatants">本回合参与战斗（接触）的单位集合，来自战斗串接阶段（15.20）。</param>
    /// <returns>重算并施加参战强制显形后的可见度快照。</returns>
    internal static FogView RecomputeFogAfterManeuver(
        GameState postManeuver,
        FogConfig cfg,
        IReadOnlySet<UnitId> combatants)
    {
        ArgumentNullException.ThrowIfNull(combatants);

        var recomputed = ComputeFogSnapshot(postManeuver, cfg);
        return ForceIdentifiedForCombatants(recomputed, combatants);
    }

    /// <summary>
    /// 参战强制显形（Req 14.7）：对双方视图中属于 <paramref name="combatants"/> 的敌方单位强制置为
    /// <see cref="Visibility.Identified"/>，其余可见度保持不变。返回新 <see cref="FogView"/>，不修改输入。
    /// </summary>
    /// <param name="view">重算得到的可见度快照。</param>
    /// <param name="combatants">参战（接触）单位集合。</param>
    /// <returns>参战单位被强制显形后的新快照。</returns>
    internal static FogView ForceIdentifiedForCombatants(FogView view, IReadOnlySet<UnitId> combatants)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(combatants);

        return new FogView(
            ByBlue: ForceIdentifiedForCombatants(view.ByBlue, combatants),
            ByRed: ForceIdentifiedForCombatants(view.ByRed, combatants));
    }

    /// <summary>
    /// 对单个方向的可见度映射施加参战强制显形（Req 14.7）：映射中键属于 <paramref name="combatants"/> 者
    /// 置为 <see cref="Visibility.Identified"/>，其余保持原值。按稳定 <see cref="UnitId"/> 序构建以保证确定性（Req 2.6）。
    /// </summary>
    private static IReadOnlyDictionary<UnitId, Visibility> ForceIdentifiedForCombatants(
        IReadOnlyDictionary<UnitId, Visibility> view,
        IReadOnlySet<UnitId> combatants)
    {
        var result = new Dictionary<UnitId, Visibility>(view.Count);
        foreach (var id in view.Keys.OrderBy(static k => k.Value))
        {
            result[id] = combatants.Contains(id) ? Visibility.Identified : view[id];
        }

        return result;
    }

    /// <summary>
    /// 火力支援归属：按 <em>回合末位置</em> 判定 <paramref name="battle"/> 获得的火力支援移档（Req 4.9、10.1、10.3–10.5）。
    /// <para>
    /// 对 <paramref name="supportingSide"/> 方的每个火力支援单位（<see cref="UnitClass.FireSupport"/>），
    /// 当且仅当同时满足：自身本回合未交战（不在 <paramref name="engagedUnits"/> 中，Req 10.3）、
    /// 在本场战斗支援范围内（Req 10.1）、且目标战斗格处于己方视野内（含支援单位自身，Req 10.4）时，
    /// 贡献一次 <c>+1</c> 档、来源 <see cref="ModifierSource.Support"/> 的移档（Req 10.1）。
    /// </para>
    /// <para>
    /// 返回的移档仅作用于火力比所在列、不改任何单位攻防（Req 10.5）；符号约定 <c>+1</c> 表示
    /// 「向被支援方（优势方）方向移 1 档」，与 <see cref="FirePowerRatio.ToColumn"/> 的列序一致
    /// （列号越大越利于进攻/优势方）。<b>总封顶 ±2 由调用方将本结果连同其他来源一并传入
    /// <see cref="ModifierPipeline.FinalColumn"/> 施加（Req 10.2），本助手不自行封顶。</b>
    /// </para>
    /// </summary>
    /// <param name="endOfTurnState">回合末（机动结算后）状态，用于支援单位回合末位置与己方视野判定（Req 4.9）。</param>
    /// <param name="battle">一场经接触合并的战斗（<see cref="MergedContact"/>）；<see cref="MergedContact.AdvanceOnly"/> 者无战斗、无支援。</param>
    /// <param name="supportingSide">被支援的一方（该场进攻方）。</param>
    /// <param name="engagedUnits">本回合处于交战（被接触）中的单位集合，用于排除自身交战的支援单位（Req 10.3）。</param>
    /// <returns>本场获得的火力支援移档列表（可能为空）；按稳定 <see cref="UnitId"/> 序生成。</returns>
    internal static IReadOnlyList<ColumnShift> ResolveFireSupport(
        GameState endOfTurnState,
        MergedContact battle,
        Side supportingSide,
        IReadOnlySet<UnitId> engagedUnits)
    {
        ArgumentNullException.ThrowIfNull(endOfTurnState);
        ArgumentNullException.ThrowIfNull(battle);
        ArgumentNullException.ThrowIfNull(engagedUnits);

        // 目标格已空、仅推进不触发战斗（Req 5.4）→ 无支援可归属。
        if (battle.AdvanceOnly)
        {
            return Array.Empty<ColumnShift>();
        }

        // Req 10.4：目标战斗格须处于己方（含支援单位自身）视野内，否则整场不提供支援。
        if (!CellInSideVision(endOfTurnState, supportingSide, battle.Target))
        {
            return Array.Empty<ColumnShift>();
        }

        var shifts = new List<ColumnShift>();

        // 按稳定 id 序遍历己方火力支援单位，保证确定性（Req 2.6）。
        foreach (var support in endOfTurnState.Units
            .Where(u => u.Owner == supportingSide && u.Class == UnitClass.FireSupport)
            .OrderBy(static u => u.Id.Value))
        {
            // Req 10.3：支援单位本回合自身处于交战中 → 不提供支援。
            if (engagedUnits.Contains(support.Id))
            {
                continue;
            }

            // Req 4.9/10.1：以回合末位置判定是否覆盖本场（在支援范围内）。
            if (!InSupportRange(support, battle))
            {
                continue;
            }

            // Req 10.1/10.5：单支援 +1 档，仅移列不改攻防；来源 Support。
            shifts.Add(new ColumnShift(SupportColumnShift, ModifierSource.Support));
        }

        return shifts;
    }

    /// <summary>
    /// 支援单位回合末位置是否覆盖本场战斗：到「目标格或任一进攻格」的最小六角距离不超过
    /// <see cref="UnitState.SupportRange"/>（Req 10.1）。
    /// </summary>
    private static bool InSupportRange(UnitState support, MergedContact battle)
    {
        if (HexCoord.Distance(support.Position, battle.Target) <= support.SupportRange)
        {
            return true;
        }

        foreach (var cell in battle.AttackingCells)
        {
            if (HexCoord.Distance(support.Position, cell) <= support.SupportRange)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 指定格是否处于 <paramref name="side"/> 方视野内：存在该方某单位到该格的六角距离不超过其视野半径
    /// （Req 10.4，视野含支援单位自身）。采用白天视野半径；夜晚折减留待 15.24（见类型文档〈简化假设〉）。
    /// </summary>
    private static bool CellInSideVision(GameState s, Side side, HexCoord cell)
    {
        foreach (var unit in s.Units)
        {
            if (unit.Owner == side && HexCoord.Distance(unit.Position, cell) <= unit.Vision)
            {
                return true;
            }
        }

        return false;
    }
}
