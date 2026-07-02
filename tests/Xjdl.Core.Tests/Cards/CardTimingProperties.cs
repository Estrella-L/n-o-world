using CsCheck;
using Xjdl.Core.Cards;
using Xjdl.Core.State;

namespace Xjdl.Core.Tests.Cards;

// Feature: core-rules-engine, Property 61: 卡牌时机约束
public class CardTimingProperties
{
    /// <summary>WEGO 时机：计划/结算前/反应（Req 19.3），三档均匀取样。</summary>
    private static readonly Gen<CardTiming> GenTiming =
        Gen.Int[0, 2].Select(i => (CardTiming)i);

    /// <summary>唯一的卡牌标识符（固定，便于注册与置入手牌）。</summary>
    private static readonly CardId TheCard = new(1);

    /// <summary>
    /// 生成一个「除时机外全部通过」的打出场景：卡在手、CP 充足、非指向敌方（免除显形要求），
    /// 附一个随机的当前 WEGO 时机。这样 <see cref="CardSystem.Play"/> 的成功与否只取决于时机是否匹配。
    /// <para>字段：卡牌定义时机、当前时机、CP 上限（≥ 卡费，保证可负担）。</para>
    /// </summary>
    private static readonly Gen<(Card Card, CardTiming Current, CardState State, PlayContext Ctx)> GenScenario =
        from cardTiming in GenTiming
        from currentTiming in GenTiming
        from cpCost in Gen.Int[0, 5]
        from spareCp in Gen.Int[0, 5]
        select BuildScenario(cardTiming, currentTiming, cpCost, cpCost + spareCp);

    private static (Card, CardTiming, CardState, PlayContext) BuildScenario(
        CardTiming cardTiming, CardTiming currentTiming, int cpCost, int cp)
    {
        // 非指向敌方：TargetsEnemy=false，从而显形要求（Req 19.6）不触发，隔离出时机变量。
        var card = new Card(
            Id: TheCard,
            Timing: cardTiming,
            CpCost: cpCost,
            TargetsEnemy: false,
            RequiresRevealedTarget: false,
            FirePowerShift: 0,
            OnUnrevealed: UnrevealedEffect.Void);

        var state = new CardState(
            Cp: cp,
            CpMax: cp,
            Deck: Array.Empty<CardId>(),
            Hand: new[] { TheCard });

        var ctx = new PlayContext(
            Cards: new Dictionary<CardId, Card> { [TheCard] = card },
            TargetVisibility: null);

        return (card, currentTiming, state, ctx);
    }

    /// <summary>
    /// Property 61: 卡牌时机约束。
    /// 对任意卡牌与当前 WEGO 时机（其余校验均已通过）：当且仅当卡牌时机标签与当前时机相同，
    /// <see cref="CardSystem.Play"/> 方成功；否则拒绝（<see cref="PlayResult.Success"/> 为假）且拒绝原因指向时机不符。
    /// **Validates: Requirements 19.3**
    /// </summary>
    [Fact]
    public void Play_SucceedsIffTimingMatches()
    {
        GenScenario.Sample(scenario =>
        {
            var (card, current, state, ctx) = scenario;
            var result = CardSystem.Play(state, card.Id, current, ctx);

            var timingMatches = card.Timing == current;

            if (timingMatches)
            {
                // 时机匹配且其余校验通过 → 成功打出。
                return result.Success;
            }

            // 时机不符 → 拒绝，且原因指向时机。
            return !result.Success
                && result.RejectReason is not null
                && result.RejectReason.Contains("时机");
        }, iter: 200);
    }
}
