using Xjdl.Core.Modifiers;
using Xjdl.Core.State;

namespace Xjdl.Core.Cards;

/// <summary>
/// 技能卡子系统（纯函数、整数运算、确定性，Req 19.x）。
/// 管理按地图规模的 CP 经济、时机标签与效果接入：
/// <list type="bullet">
/// <item><description><see cref="Init"/>：按 <see cref="MapScaleProfile"/> 套用 CP/`cpMax`/牌库/手牌（Req 19.1）。</description></item>
/// <item><description><see cref="GainCp"/>：每回合产出 CP，累积但不超 `cpMax`（Req 19.2）。</description></item>
/// <item><description><see cref="Play"/>：校验时机与 CP、显形要求，打出或拒绝（Req 19.3/19.6/19.7）。</description></item>
/// </list>
/// 涉及火力比的卡以 <see cref="ModifierSource.Card"/> 走 <see cref="ModifierPipeline"/> 并受 ±2 档总封顶（Req 19.4）；
/// 效果默认单回合/单场/一次性，不产生永久数值改变（Req 19.5）。
/// </summary>
public static class CardSystem
{
    /// <summary>单张卡自身火力比移档的档数上限（绝对值），与统一管线 ±2 总封顶一致（Req 19.4）。</summary>
    private const int MaxCardShift = 2;

    /// <summary>
    /// 按地图规模档位初始化单方卡牌经济状态（Req 19.1）。
    /// <para>CP 从 0 起累积（由 <see cref="GainCp"/> 于每回合产出）、<see cref="CardState.CpMax"/> 取自
    /// <paramref name="profile"/>。牌库与手牌初始为空——其实际卡牌内容由数据层加载并注入（任务 18.1），
    /// 本层不持有卡牌内容池，故 <paramref name="profile"/> 的牌库大小/手牌上限在填充与抽牌时生效。</para>
    /// </summary>
    /// <param name="profile">所选规模（小/中/大）对应的配置档。</param>
    /// <returns>初始卡牌经济状态。</returns>
    public static CardState Init(MapScaleProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return new CardState(
            Cp: 0,
            CpMax: profile.CpMax,
            Deck: Array.Empty<CardId>(),
            Hand: Array.Empty<CardId>());
    }

    /// <summary>
    /// 产出本回合 CP：在现有 CP 上累加 <paramref name="cpPerTurn"/>，但钳制到不超过 <paramref name="cpMax"/>（Req 19.2）。
    /// 累积单调不减（<paramref name="cpPerTurn"/> 视为非负产出，负值按 0 处理），且始终不超过 `cpMax`。
    /// </summary>
    /// <param name="s">当前卡牌经济状态。</param>
    /// <param name="cpPerTurn">本回合 CP 产出（取自规模档 <see cref="MapScaleProfile.CpPerTurn"/>）。</param>
    /// <param name="cpMax">CP 累积上限（取自规模档 <see cref="MapScaleProfile.CpMax"/>）。</param>
    /// <returns>更新 CP 后的新状态；其余字段不变。</returns>
    public static CardState GainCp(CardState s, int cpPerTurn, int cpMax)
    {
        ArgumentNullException.ThrowIfNull(s);

        var gain = Math.Max(0, cpPerTurn);
        var newCp = Math.Min(s.Cp + gain, cpMax);

        // 若当前 CP 已超上限（异常输入），保持不增，避免回退破坏单调性。
        newCp = Math.Max(newCp, Math.Min(s.Cp, cpMax));

        return s with { Cp = newCp };
    }

    /// <summary>
    /// 尝试打出一张卡：依次校验卡牌存在、在手、时机匹配、CP 充足与显形要求，
    /// 通过则扣 CP、将该卡移出手牌并产出（可能减效的）火力比移档；否则拒绝并原样返回状态（Req 19.3/19.6/19.7）。
    /// </summary>
    /// <param name="s">当前卡牌经济状态。</param>
    /// <param name="card">拟打出的卡牌标识符。</param>
    /// <param name="currentTiming">当前 WEGO 时机（Req 19.3）。</param>
    /// <param name="ctx">打出上下文：卡牌注册表与目标可见度。</param>
    /// <returns>打出结果（成功/拒绝、新状态、可能的火力比移档）。</returns>
    public static PlayResult Play(CardState s, CardId card, CardTiming currentTiming, PlayContext ctx)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(ctx);
        ArgumentNullException.ThrowIfNull(ctx.Cards);

        // 卡牌定义须存在于注册表（由数据层注入）。
        if (!ctx.Cards.TryGetValue(card, out var def))
        {
            return Reject(s, "未知卡牌：注册表中无此定义");
        }

        // 卡须在手牌中方可打出。
        if (!s.Hand.Contains(card))
        {
            return Reject(s, "手牌中无此卡");
        }

        // 时机校验：仅能在匹配时机打出（Req 19.3）。
        if (def.Timing != currentTiming)
        {
            return Reject(s, $"时机不符：需 {def.Timing}，当前 {currentTiming}");
        }

        // CP 校验：所需 CP 超过当前可用则拒绝（Req 19.7）。
        if (def.CpCost > s.Cp)
        {
            return Reject(s, $"指挥点不足：需 {def.CpCost}，当前 {s.Cp}");
        }

        // 显形要求校验（Req 19.6）：指向敌方且要求显形时，目标须为 Identified。
        var reduced = false;
        if (def.TargetsEnemy && def.RequiresRevealedTarget && ctx.TargetVisibility != Visibility.Identified)
        {
            switch (def.OnUnrevealed)
            {
                case UnrevealedEffect.Void:
                    return Reject(s, "目标未显形：该卡对 Spotted/Hidden 目标无效");
                case UnrevealedEffect.Reduce:
                    reduced = true;
                    break;
            }
        }

        // 通过全部校验：扣 CP、将该卡移出手牌（一次性消耗，Req 19.5）。
        var newHand = new List<CardId>(s.Hand);
        newHand.Remove(card); // 移除首个匹配实例
        var newState = s with
        {
            Cp = s.Cp - def.CpCost,
            Hand = newHand,
        };

        // 产出火力比移档：减效时按卡面折减（整数减半、向零取整），再钳制到 ±2（Req 19.4）。
        ColumnShift? shift = null;
        if (def.FirePowerShift != 0)
        {
            var delta = reduced ? def.FirePowerShift / 2 : def.FirePowerShift;
            delta = Math.Clamp(delta, -MaxCardShift, MaxCardShift);
            if (delta != 0)
            {
                shift = new ColumnShift(delta, ModifierSource.Card);
            }
        }

        return new PlayResult(
            Success: true,
            State: newState,
            RejectReason: null,
            FirePowerShift: shift,
            Reduced: reduced);
    }

    /// <summary>构造一个拒绝结果：状态原样返回、无移档、无减效（Req 19.7 拒绝为规则内结果，不抛异常）。</summary>
    private static PlayResult Reject(CardState s, string reason) =>
        new(Success: false, State: s, RejectReason: reason, FirePowerShift: null, Reduced: false);
}
