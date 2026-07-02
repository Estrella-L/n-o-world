using CsCheck;
using Xjdl.Core.Doctrine;
using Xjdl.Core.State;
using CoreDoctrine = Xjdl.Core.Doctrine.Doctrine;

namespace Xjdl.Core.Tests.Doctrine;

// Feature: core-rules-engine, Property 53: 预算恰为 4 点
public class DoctrineBudgetProperties
{
    /// <summary>合法预算合计（学说 + 专精点数之和须恰为此值，Req 16.2、16.5）。</summary>
    private const int RequiredBudget = 4;

    /// <summary>属性维度：五个可修正维度均匀取样。</summary>
    private static readonly Gen<StatKind> GenStat =
        Gen.Int[0, 4].Select(i => (StatKind)i);

    /// <summary>加法增量：正负兼有，与预算点数无关。</summary>
    private static readonly Gen<int> GenValue = Gen.Int[-10, 10];

    /// <summary>单条修正，指定其预算点数（PointCost）。</summary>
    private static Gen<StatModifier> GenModifierWithCost(int cost) =>
        from stat in GenStat
        from value in GenValue
        select new StatModifier(stat, value, cost);

    /// <summary>单条修正，随机预算点数（-3..4）。</summary>
    private static readonly Gen<StatModifier> GenModifier =
        from cost in Gen.Int[-3, 4]
        from m in GenModifierWithCost(cost)
        select m;

    /// <summary>0..3 条修正的集合（点数合计由各条 PointCost 决定）。</summary>
    private static readonly Gen<IReadOnlyList<StatModifier>> GenModifierList =
        from n in Gen.Int[0, 3]
        from mods in GenModifier.List[n]
        select (IReadOnlyList<StatModifier>)mods;

    /// <summary>某集合的预算点数合计。</summary>
    private static int SumPoints(IReadOnlyList<StatModifier> mods)
    {
        var sum = 0;
        foreach (var m in mods)
        {
            sum += m.PointCost;
        }

        return sum;
    }

    /// <summary>
    /// 生成一对（学说, 专精）。专精的 <see cref="Specialization.Budget"/> 恒等于其修正点数合计，
    /// 故 <see cref="DoctrineSystem.Validate"/> 的专精一致性检查始终通过，仅由「合计是否为 4」决定结果。
    /// 通过 <c>forceExactFour</c> 分支既覆盖合计恰为 4（应通过），
    /// 也覆盖随机合计（多数不为 4，应拒绝），两类情形均可采样到。
    /// </summary>
    private static readonly Gen<(CoreDoctrine Doctrine, Specialization Spec)> GenPair =
        from specMods in GenModifierList
        let specPoints = SumPoints(specMods)
        from forceExactFour in Gen.Bool
        from doctrineMods in forceExactFour
            ? GenModifierWithCost(RequiredBudget - specPoints).Select(m =>
                (IReadOnlyList<StatModifier>)new[] { m })
            : GenModifierList
        select (
            new CoreDoctrine("doctrine.test", doctrineMods, NightFlags.None),
            new Specialization("spec.test", specMods, specPoints));

    /// <summary>
    /// Property 53: 预算恰为 4 点。
    /// 对任意学说与任意专精分支（A/B 同规则）：当且仅当「学说点数 + 专精点数 == 4」时
    /// <see cref="DoctrineSystem.Validate"/> 不抛异常；否则抛出 <see cref="InvalidDataException"/>。
    /// 生成器保证专精声明预算与其修正点数合计一致，故结果只取决于合计是否为 4。
    /// **Validates: Requirements 16.2, 16.5**
    /// </summary>
    [Fact]
    public void Validate_SucceedsIffBudgetSumIsExactlyFour()
    {
        GenPair.Sample(pair =>
        {
            var (doctrine, spec) = pair;
            var total = SumPoints(doctrine.Modifiers) + SumPoints(spec.Modifiers);
            var shouldPass = total == RequiredBudget;

            var threw = false;
            try
            {
                DoctrineSystem.Validate(doctrine, spec);
            }
            catch (InvalidDataException)
            {
                threw = true;
            }

            // 合计为 4 → 不抛；合计非 4 → 抛 InvalidDataException。
            return shouldPass ? !threw : threw;
        }, iter: 200);
    }
}
