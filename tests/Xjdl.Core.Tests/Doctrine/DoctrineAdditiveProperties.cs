using CsCheck;
using Xjdl.Core.Doctrine;
using Xjdl.Core.State;
using Xjdl.Core.Tests.Support;
using DoctrineDef = Xjdl.Core.Doctrine.Doctrine;

namespace Xjdl.Core.Tests.Doctrine;

// Feature: core-rules-engine, Property 52: 模板加法叠加且不改基线
public class DoctrineAdditiveProperties
{
    /// <summary>
    /// 一组「合法可施加」的样本：中立模板 <c>Baseline</c>（攻/防整除韧性 N）+ 学说 <c>Doctrine</c>
    /// + 专精 <c>Spec</c>，构造使 <see cref="DoctrineSystem.Apply"/> 的
    /// <see cref="DoctrineSystem.Validate"/> 通过（点数合计恰为 4，专精声明预算与其点数一致），
    /// 且修正后攻/防仍整除 N（攻/防修正一律取 N 的整数倍）。
    /// <para>
    /// 学说占用点数 d ∈ [0,4]、专精占用 4-d，两者合计恒为 4（Req 16.2/16.5）；
    /// 攻/防修正取韧性 N 的整数倍以保证修正后仍整除 N（Req 16.3）；
    /// 机动/视野/支援射程修正为任意小整数增量（不影响预算校验与整除约束）。
    /// </para>
    /// </summary>
    private static readonly Gen<(UnitTemplate Baseline, DoctrineDef Doctrine, Specialization Spec)> GenApplicable =
        from baseline in Generators.UnitTemplate
        from d in Gen.Int[0, 4]
        from atkMulDoc in Gen.Int[-3, 3]
        from defMulDoc in Gen.Int[-3, 3]
        from atkMulSpec in Gen.Int[-3, 3]
        from defMulSpec in Gen.Int[-3, 3]
        from movDoc in Gen.Int[-3, 3]
        from visDoc in Gen.Int[-3, 3]
        from supDoc in Gen.Int[-3, 3]
        from movSpec in Gen.Int[-3, 3]
        from visSpec in Gen.Int[-3, 3]
        from supSpec in Gen.Int[-3, 3]
        let n = baseline.Resilience
        let doctrine = new DoctrineDef(
            "doctrine.test",
            new[]
            {
                new StatModifier(StatKind.Attack, atkMulDoc * n, d),
                new StatModifier(StatKind.Defense, defMulDoc * n, 0),
                new StatModifier(StatKind.Movement, movDoc, 0),
                new StatModifier(StatKind.Vision, visDoc, 0),
                new StatModifier(StatKind.SupportRange, supDoc, 0),
            },
            baseline.DefaultFlags)
        let spec = new Specialization(
            "doctrine.test.spec-a",
            new[]
            {
                new StatModifier(StatKind.Attack, atkMulSpec * n, 4 - d),
                new StatModifier(StatKind.Defense, defMulSpec * n, 0),
                new StatModifier(StatKind.Movement, movSpec, 0),
                new StatModifier(StatKind.Vision, visSpec, 0),
                new StatModifier(StatKind.SupportRange, supSpec, 0),
            },
            4 - d)
        select (baseline, doctrine, spec);

    /// <summary>
    /// Property 52: 模板加法叠加且不改基线。
    /// 对任意「学说 + 专精」应用到中立模板：
    ///  1) 输出的每个属性维度恰等于「基线值 + 该维度全部修正 Value 之和」（纯加法叠加，Req 16.1）；
    ///  2) 未被修正的字段（TypeKey/Class/Resilience/SupportShift/DefaultFlags）保持不变；
    ///  3) 基线模板对象不被修改（record 不可变，施加后原实例仍与施加前的快照值相等）。
    /// **Validates: Requirements 16.1**
    /// </summary>
    [Fact]
    public void Apply_IsAdditiveAndLeavesBaselineUnchanged()
    {
        GenApplicable.Sample(sample =>
        {
            var (baseline, doctrine, spec) = sample;

            // 施加前对基线取值快照（结构相等的独立副本），用于校验基线不被修改。
            var baselineSnapshot = baseline with { };

            var result = DoctrineSystem.Apply(baseline, doctrine, spec);

            // 独立预言：按维度累加所有修正的 Value 得到期望模板。
            var expected = baseline with
            {
                Attack = baseline.Attack + SumFor(StatKind.Attack, doctrine, spec),
                Defense = baseline.Defense + SumFor(StatKind.Defense, doctrine, spec),
                Movement = baseline.Movement + SumFor(StatKind.Movement, doctrine, spec),
                Vision = baseline.Vision + SumFor(StatKind.Vision, doctrine, spec),
                SupportRange = baseline.SupportRange + SumFor(StatKind.SupportRange, doctrine, spec),
            };

            var additive = result == expected;
            var baselineUnchanged = baseline == baselineSnapshot;

            return additive && baselineUnchanged;
        }, iter: 1000);
    }

    /// <summary>累加学说与专精中作用于指定属性维度的全部修正 Value。</summary>
    private static int SumFor(StatKind stat, DoctrineDef doctrine, Specialization spec)
    {
        var sum = 0;
        foreach (var m in doctrine.Modifiers)
        {
            if (m.Stat == stat)
            {
                sum += m.Value;
            }
        }

        foreach (var m in spec.Modifiers)
        {
            if (m.Stat == stat)
            {
                sum += m.Value;
            }
        }

        return sum;
    }
}
