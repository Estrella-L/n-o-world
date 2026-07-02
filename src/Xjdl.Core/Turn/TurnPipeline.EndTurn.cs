using Xjdl.Core.Modifiers;
using Xjdl.Core.State;

namespace Xjdl.Core.Turn;

/// <summary>
/// 阶段 9（回合结束）与昼夜机制（Req 3.10、18.1–18.7）。
/// <para>
/// 本分部区分两类职责：
/// </para>
/// <list type="bullet">
/// <item>
/// <b><see cref="EndTurnPhase"/></b>——阶段 9 的纯函数状态变换：清除全部「进攻准备」状态、
/// 按固定顺序推进昼夜属性到下一回合（Req 3.10/18.1）。该变换<em>不需要</em> <see cref="NightConfig"/>，
/// 因为固定序推进与清进攻准备均与夜晚修正值无关。
/// </item>
/// <item>
/// <b>夜晚修正助手</b>（<see cref="EffectiveVision"/>/<see cref="EffectiveSupportRange"/>/
/// <see cref="EffectiveMovement"/>/<see cref="NightAttackColumnShift"/>/<see cref="ProducesZocConsideringNight"/>）
/// ——供 <c>FogSystem</c>（视野，Req 18.2）、<c>CombatResolver</c>（支援射程，Req 18.3；进攻移档，Req 18.4）、
/// 机动阶段（机动，Req 18.5）、<c>ZoneOfControl</c>（控制区，Req 18.6）等消费方在结算时<em>瞬态</em>取用：
/// 它们计算「有效值」而<b>不</b>永久改写单位基础属性，因此可逆、确定、可重放（Req 2.1）。
/// 修正值经参数化 <see cref="NightConfig"/> 注入（Req 18.7），调用方负责传入配置——
/// 由于 <see cref="GameState"/> 当前不携带 <see cref="NightConfig"/>，助手一律以静态纯函数形式暴露、由调用方注入配置。
/// </item>
/// </list>
/// </summary>
internal static partial class TurnPipeline
{
    /// <summary>
    /// 夜晚机动惩罚（Req 18.5）：机动减 1、最低为 1。
    /// Req 18.7 将「夜晚视野除数 / 支援射程修正 / 进攻移档」列为 <see cref="NightConfig"/> 可配置项，
    /// 但未将机动惩罚纳入配置，故此处以固定规则常量表达，不硬编码进任何配置字段。
    /// </summary>
    private const int NightMovementPenalty = -1;

    /// <summary>
    /// 阶段 9（回合结束，Req 3.10/18.1）的纯函数变换：
    /// <list type="number">
    /// <item>清除全部「进攻准备」状态——将任何 <see cref="Command.AttackPrep"/> 单位的命令归位为中性的
    /// <see cref="Command.Hold"/>，使进攻准备不会残留进入下一回合（Req 3.10）。</item>
    /// <item>按固定顺序 上午 → 下午 → 晚上（→ 上午）推进本局昼夜属性到下一回合（Req 18.1）；
    /// 当由 <see cref="DayNightPhase.Night"/> 折回 <see cref="DayNightPhase.Morning"/> 时递增
    /// <see cref="GameState.DayIndex"/>（一天含上午/下午/晚上三段，跨夜进入新的一天）。</item>
    /// </list>
    /// 返回新 <see cref="GameState"/>，从不原地修改输入（Req 2.1）。命令、rng 未使用：
    /// 回合结束的清理与昼夜推进是确定性且与随机源无关的。
    /// </summary>
    /// <param name="s">阶段 8 之后的状态。</param>
    /// <returns>清进攻准备并推进昼夜后的新状态。</returns>
    internal static GameState EndTurnPhase(GameState s)
    {
        ArgumentNullException.ThrowIfNull(s);

        // (1) 清除全部进攻准备：AttackPrep → Hold（中性据守），其余命令保持不变（Req 3.10）。
        // 仅在存在需改写单位时生成新列表，保持「无变更即原样返回」的最小分配语义。
        var units = s.Units;
        List<UnitState>? cleared = null;
        for (var i = 0; i < units.Count; i++)
        {
            var unit = units[i];
            if (unit.Command != Command.AttackPrep)
            {
                continue;
            }

            cleared ??= new List<UnitState>(units);
            cleared[i] = unit with { Command = Command.Hold };
        }

        var nextUnits = cleared is null ? units : (IReadOnlyList<UnitState>)cleared;

        // (2) 按固定序推进昼夜，跨夜进入新的一天（Req 18.1）。
        var (nextPhase, dayDelta) = AdvanceDayNight(s.Phase);

        return s with
        {
            Units = nextUnits,
            Phase = nextPhase,
            DayIndex = s.DayIndex + dayDelta,
        };
    }

    /// <summary>
    /// 昼夜固定序推进（Req 18.1）：上午 → 下午 → 晚上 → 上午 循环。
    /// 返回下一昼夜属性与「天数增量」——仅在由晚上折回上午（进入新的一天）时为 1，否则为 0。
    /// </summary>
    /// <param name="phase">当前回合昼夜属性。</param>
    /// <returns>下一回合昼夜属性与 <see cref="GameState.DayIndex"/> 的增量。</returns>
    internal static (DayNightPhase Phase, int DayDelta) AdvanceDayNight(DayNightPhase phase) => phase switch
    {
        DayNightPhase.Morning => (DayNightPhase.Afternoon, 0),
        DayNightPhase.Afternoon => (DayNightPhase.Night, 0),
        DayNightPhase.Night => (DayNightPhase.Morning, 1),
        _ => (DayNightPhase.Morning, 0),
    };

    /// <summary>是否为夜晚回合（Req 18.2–18.6 的共同前置）。</summary>
    /// <param name="phase">当前回合昼夜属性。</param>
    /// <returns><paramref name="phase"/> 为 <see cref="DayNightPhase.Night"/> 时为 <c>true</c>。</returns>
    internal static bool IsNight(DayNightPhase phase) => phase == DayNightPhase.Night;

    /// <summary>
    /// 夜晚有效视野（Req 18.2）：夜晚且不持 <see cref="NightFlags.NightVisionKeep"/> 时，
    /// 视野除以 <see cref="NightConfig.NightVisionDivisor"/> 向下取整、最低为 1；否则原样返回。
    /// 纯计算，不改写 <see cref="UnitState.Vision"/>。
    /// </summary>
    /// <param name="baseVision">单位基础视野半径。</param>
    /// <param name="flags">单位夜战/特性标志。</param>
    /// <param name="phase">当前回合昼夜属性。</param>
    /// <param name="config">夜战配置（提供除数，Req 18.7）。</param>
    /// <returns>本回合应采用的有效视野。</returns>
    internal static int EffectiveVision(int baseVision, NightFlags flags, DayNightPhase phase, NightConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!IsNight(phase) || flags.HasFlag(NightFlags.NightVisionKeep))
        {
            return baseVision;
        }

        // 除数至少为 1，避免除零/放大；向下取整由整除天然保证（Req 18.2）。
        var divisor = config.NightVisionDivisor < 1 ? 1 : config.NightVisionDivisor;
        return Math.Max(1, baseVision / divisor);
    }

    /// <summary>
    /// 夜晚有效支援射程（Req 18.3）：夜晚且火力支援单位不持 <see cref="NightFlags.NightRangeKeep"/> 时，
    /// 支援射程加 <see cref="NightConfig.NightRangeMod"/>（默认 -1）、最低为 1；否则原样返回。
    /// 纯计算，不改写 <see cref="UnitState.SupportRange"/>。
    /// </summary>
    /// <param name="baseRange">单位基础支援射程。</param>
    /// <param name="flags">单位夜战/特性标志。</param>
    /// <param name="phase">当前回合昼夜属性。</param>
    /// <param name="config">夜战配置（提供射程修正，Req 18.7）。</param>
    /// <returns>本回合应采用的有效支援射程。</returns>
    internal static int EffectiveSupportRange(int baseRange, NightFlags flags, DayNightPhase phase, NightConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!IsNight(phase) || flags.HasFlag(NightFlags.NightRangeKeep))
        {
            return baseRange;
        }

        return Math.Max(1, baseRange + config.NightRangeMod);
    }

    /// <summary>
    /// 夜晚有效机动（Req 18.5）：夜晚且不持 <see cref="NightFlags.NightMoveKeep"/> 时，
    /// 机动加 <see cref="NightMovementPenalty"/>（-1）、最低为 1；否则原样返回。
    /// 纯计算，不改写 <see cref="UnitState.Movement"/>。
    /// </summary>
    /// <param name="baseMovement">单位基础机动。</param>
    /// <param name="flags">单位夜战/特性标志。</param>
    /// <param name="phase">当前回合昼夜属性。</param>
    /// <returns>本回合应采用的有效机动。</returns>
    internal static int EffectiveMovement(int baseMovement, NightFlags flags, DayNightPhase phase)
    {
        if (!IsNight(phase) || flags.HasFlag(NightFlags.NightMoveKeep))
        {
            return baseMovement;
        }

        return Math.Max(1, baseMovement + NightMovementPenalty);
    }

    /// <summary>
    /// 夜晚进攻移档（Req 18.4）：夜晚且主动进攻方不持 <see cref="NightFlags.NightAttackKeep"/> 时，
    /// 对表一进攻方 / 表二施加一档进攻火力比修正（来源 <see cref="ModifierSource.Night"/>，
    /// 档数取自 <see cref="NightConfig.NightAttackShift"/>，默认 -1）；否则、或档数为 0 时返回 <c>null</c>。
    /// 调用方（<c>CombatResolver</c>）负责判定接触表类型并将本移档并入 <see cref="ModifierPipeline.FinalColumn"/>；
    /// 净移档仍受 ±2 总封顶约束（Req 17.1）。
    /// </summary>
    /// <param name="attackerFlags">主动进攻方的夜战/特性标志。</param>
    /// <param name="phase">当前回合昼夜属性。</param>
    /// <param name="config">夜战配置（提供进攻移档档数，Req 18.7）。</param>
    /// <returns>应施加的夜晚进攻移档；无修正时为 <c>null</c>。</returns>
    internal static ColumnShift? NightAttackColumnShift(NightFlags attackerFlags, DayNightPhase phase, NightConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (!IsNight(phase) || attackerFlags.HasFlag(NightFlags.NightAttackKeep) || config.NightAttackShift == 0)
        {
            return null;
        }

        return new ColumnShift(config.NightAttackShift, ModifierSource.Night);
    }

    /// <summary>
    /// 夜晚控制区差异（Req 18.6）：夜晚仅保留据守（<see cref="Command.Hold"/>）单位的控制区，
    /// 进攻准备控制区一律失效；持 <see cref="NightFlags.NightZocKeep"/> 的据守单位不受夜晚影响
    /// （据守控制区本就在夜晚保留，该标志明确保证其不因夜晚被禁用）。白天原样返回白天判定结果。
    /// </summary>
    /// <param name="daytimeProducesZoc">白天规则下该单位是否产生控制区（见 <see cref="ZoneOfControl.ProducesZoc"/>）。</param>
    /// <param name="command">该单位本回合命令。</param>
    /// <param name="flags">该单位夜战/特性标志。</param>
    /// <param name="phase">当前回合昼夜属性。</param>
    /// <returns>考虑昼夜后该单位是否产生控制区。</returns>
    internal static bool ProducesZocConsideringNight(
        bool daytimeProducesZoc,
        Command command,
        NightFlags flags,
        DayNightPhase phase)
    {
        if (!IsNight(phase))
        {
            return daytimeProducesZoc;
        }

        // 夜晚：据守控制区保留（NightZocKeep 据守单位同样不受影响）；进攻准备控制区失效（Req 18.6）。
        return command == Command.Hold && daytimeProducesZoc;
    }
}
