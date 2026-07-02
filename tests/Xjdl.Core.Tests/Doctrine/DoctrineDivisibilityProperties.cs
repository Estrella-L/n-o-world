using CsCheck;
using Xjdl.Core.State;
using Xjdl.Core.Tests.Support;
using CoreDoctrine = Xjdl.Core.Doctrine;

namespace Xjdl.Core.Tests.Doctrine;

// Feature: core-rules-engine, Property 54: 学说修正保持整除
public class DoctrineDivisibilityProperties
{
    /// <summary>
    /// 「中立模板 + 学说 + 专精」场景生成器：
    /// 基准模板攻 = N×attackMul、防 = N×defenseMul（初始即整除韧性 N，Req 8.1）；
    /// 学说携带一条 Attack 修正、专精携带一条 Defense 修正，两者预算点数合计恒为 4
    /// （<see cref="CoreDoctrine.DoctrineSystem.RequiredBudget"/>），故必然通过预算校验（Req 16.2、16.5），
    /// 从而将测试聚焦于修正后攻/防的整除校验（Req 16.3）。
    /// <para>
    /// 攻/防修正量各以 50% 概率构造为 N 的整数倍（保持整除）或带非零余数（破坏整除），
    /// 以充分覆盖「保持整除→接受」与「破坏整除→拒绝」两条分支的全部组合。
    /// </para>
    /// </summary>
    private static readonly Gen<(UnitTemplate Baseline, CoreDoctrine.Doctrine Doctrine, CoreDoctrine.Specialization Spec, int AttackDelta, int DefenseDelta, int N)> GenScenario =
        from typeKey in Generators.TypeKeys
        from cls in Generators.UnitClasses
        from n in Gen.Int[2, 6]
        from attackMul in Gen.Int[1, 6]
        from defenseMul in Gen.Int[1, 6]
        from movement in Gen.Int[1, 8]
        from vision in Gen.Int[1, 6]
        from supportRange in Gen.Int[0, 4]
        from flags in Generators.NightFlags
        from pSpec in Gen.Int[0, 4]
        from aDiv in Gen.Bool
        from aK in Gen.Int[-4, 4]
        from aRem in Gen.Int[1, n - 1]
        from dDiv in Gen.Bool
        from dK in Gen.Int[-4, 4]
        from dRem in Gen.Int[1, n - 1]
        let attackDelta = aDiv ? n * aK : (n * aK) + aRem
        let defenseDelta = dDiv ? n * dK : (n * dK) + dRem
        select (
            new UnitTemplate(
                typeKey,
                cls,
                n * attackMul, // Attack 初始整除 N
                n * defenseMul, // Defense 初始整除 N
                movement,
                n, // Resilience N
                vision,
                supportRange,
                null,
                flags),
            new CoreDoctrine.Doctrine(
                "doctrine.test",
                new[] { new CoreDoctrine.StatModifier(CoreDoctrine.StatKind.Attack, attackDelta, CoreDoctrine.DoctrineSystem.RequiredBudget - pSpec) },
                NightFlags.None),
            new CoreDoctrine.Specialization(
                "doctrine.test.spec-a",
                new[] { new CoreDoctrine.StatModifier(CoreDoctrine.StatKind.Defense, defenseDelta, pSpec) },
                pSpec),
            attackDelta,
            defenseDelta,
            n);

    /// <summary>
    /// Property 54: 学说修正保持整除。
    /// 对任意中立模板叠加学说 + 专精修正（预算恒合法）：
    /// 当修正后攻与防仍均整除韧性 N 时，<see cref="CoreDoctrine.DoctrineSystem.Apply"/> 接受加载，
    /// 返回模板的攻/防等于「基础 + 修正」且仍整除 N；
    /// 当修正后攻或防任一破坏整除时，加载被拒绝并抛 <see cref="InvalidDataException"/>（Req 16.3）。
    /// **Validates: Requirements 16.3**
    /// </summary>
    [Fact]
    public void DoctrineModifier_PreservesDivisibility_OrRejectsLoad()
    {
        GenScenario.Sample(scenario =>
        {
            var (baseline, doctrine, spec, attackDelta, defenseDelta, n) = scenario;

            var modifiedAttack = baseline.Attack + attackDelta;
            var modifiedDefense = baseline.Defense + defenseDelta;
            var expectAccept = (modifiedAttack % n == 0) && (modifiedDefense % n == 0);

            if (expectAccept)
            {
                // 保持整除 → 接受加载，且结果攻/防 == 基础 + 修正、仍整除 N。
                var result = CoreDoctrine.DoctrineSystem.Apply(baseline, doctrine, spec);
                return result.Attack == modifiedAttack
                    && result.Defense == modifiedDefense
                    && result.Resilience == n
                    && result.Attack % n == 0
                    && result.Defense % n == 0;
            }

            // 破坏整除 → 拒绝加载（fail-fast）。
            try
            {
                CoreDoctrine.DoctrineSystem.Apply(baseline, doctrine, spec);
                return false; // 本应抛出却顺利返回，判定失败。
            }
            catch (InvalidDataException)
            {
                return true;
            }
        }, iter: 200);
    }
}
