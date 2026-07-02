using CsCheck;
using Xjdl.Core.Cards;
using Xjdl.Core.State;

namespace Xjdl.Core.Tests.Cards;

// Feature: core-rules-engine, Property 62: 显形要求约束
public class CardRevealRequirementProperties
{
    /// <summary>唯一的卡牌标识符（固定，便于注册与置入手牌）。</summary>
    private static readonly CardId TheCard = new(1);

    /// <summary>未显形处置：无效/减效（Req 19.6），均匀取样。</summary>
    private static readonly Gen<UnrevealedEffect> GenUnrevealed =
        Gen.Int[0, 1].Select(i => (UnrevealedEffect)i);

    /// <summary>目标可见度：隐匿/侦得/识别（Req 14.x），均匀取样。</summary>
    private static readonly Gen<Visibility> GenVisibility =
        Gen.Int[0, 2].Select(i => (Visibility)i);

    /// <summary>
    /// 火力比移档档数：非零且落在 ±2 档内（Req 19.4）。
    /// 限定绝对值 ≤ 2 使总封顶不参与折减，从而「向零减半」始终是一次真实的幅度削减，
    /// 便于对减效与全效两种产出做确定性断言（±1 减半为 0，±2 减半为 ±1）。
    /// </summary>
    private static readonly Gen<int> GenFirePowerShift =
        Gen.Int[-2, 2].Where(v => v != 0);

    /// <summary>
    /// 生成一个「除显形要求外全部通过」的打出场景：卡指向敌方且要求显形、在手、时机匹配当前、CP 充足；
    /// 随机的未显形处置（无效/减效）、非零火力比移档，以及随机的目标可见度（识别/侦得/隐匿）。
    /// 这样 <see cref="CardSystem.Play"/> 的成败与减效与否只取决于显形要求（Req 19.6）。
    /// </summary>
    private static readonly Gen<(Card Card, Visibility Visibility, CardState State, PlayContext Ctx)> GenScenario =
        from timing in Gen.Int[0, 2].Select(i => (CardTiming)i)
        from onUnrevealed in GenUnrevealed
        from fps in GenFirePowerShift
        from visibility in GenVisibility
        from cpCost in Gen.Int[0, 5]
        from spareCp in Gen.Int[0, 5]
        select BuildScenario(timing, onUnrevealed, fps, visibility, cpCost, cpCost + spareCp);

    private static (Card, Visibility, CardState, PlayContext) BuildScenario(
        CardTiming timing, UnrevealedEffect onUnrevealed, int fps, Visibility visibility, int cpCost, int cp)
    {
        var card = new Card(
            Id: TheCard,
            Timing: timing,
            CpCost: cpCost,
            TargetsEnemy: true,
            RequiresRevealedTarget: true,
            FirePowerShift: fps,
            OnUnrevealed: onUnrevealed);

        var state = new CardState(
            Cp: cp,
            CpMax: cp,
            Deck: Array.Empty<CardId>(),
            Hand: new[] { TheCard });

        // 时机固定与当前一致、CP 充足，隔离出显形要求为唯一变量。
        var ctx = new PlayContext(
            Cards: new Dictionary<CardId, Card> { [TheCard] = card },
            TargetVisibility: visibility);

        return (card, visibility, state, ctx);
    }

    /// <summary>
    /// Property 62: 显形要求约束。
    /// 对任意「指向敌方且要求目标显形」的卡（其余校验均已通过）：
    /// <list type="bullet">
    /// <item><description>目标为 <see cref="Visibility.Identified"/> → 以全效打出（成功、<see cref="PlayResult.Reduced"/> 为假，产出全额火力比移档）。</description></item>
    /// <item><description>目标为 Spotted/Hidden 且处置为 <see cref="UnrevealedEffect.Void"/> → 拒绝打出（<see cref="PlayResult.Success"/> 为假）。</description></item>
    /// <item><description>目标为 Spotted/Hidden 且处置为 <see cref="UnrevealedEffect.Reduce"/> → 成功但减效（<see cref="PlayResult.Reduced"/> 为真，火力比移档向零减半、幅度严格小于全额）。</description></item>
    /// </list>
    /// **Validates: Requirements 19.6**
    /// </summary>
    [Fact]
    public void Play_EnforcesRevealRequirementPerCardFace()
    {
        GenScenario.Sample(scenario =>
        {
            var (card, visibility, state, ctx) = scenario;
            var result = CardSystem.Play(state, card.Id, card.Timing, ctx);

            // 全效档数（识别时的产出）：卡面移档钳制到 ±2。
            var fullDelta = Math.Clamp(card.FirePowerShift, -2, 2);

            if (visibility == Visibility.Identified)
            {
                // 目标已显形 → 全效打出，不减效，产出全额移档。
                return result.Success
                    && !result.Reduced
                    && result.FirePowerShift is { } fs
                    && fs.Delta == fullDelta
                    && fs.Source == ModifierSource.Card;
            }

            // 目标未显形（Spotted/Hidden）：按卡面处置。
            switch (card.OnUnrevealed)
            {
                case UnrevealedEffect.Void:
                    // 无效 → 拒绝，且原因指向未显形。
                    return !result.Success
                        && result.RejectReason is not null
                        && result.RejectReason.Contains("显形")
                        && result.FirePowerShift is null
                        && !result.Reduced;

                case UnrevealedEffect.Reduce:
                    // 减效 → 成功、Reduced 为真、移档向零减半且幅度严格小于全额。
                    var reducedDelta = Math.Clamp(card.FirePowerShift / 2, -2, 2);
                    var shiftMatches = reducedDelta == 0
                        ? result.FirePowerShift is null
                        : result.FirePowerShift is { } rs
                            && rs.Delta == reducedDelta
                            && rs.Source == ModifierSource.Card;
                    return result.Success
                        && result.Reduced
                        && Math.Abs(reducedDelta) < Math.Abs(fullDelta)
                        && shiftMatches;

                default:
                    return false;
            }
        }, iter: 200);
    }
}
