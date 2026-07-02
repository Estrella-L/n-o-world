using CsCheck;
using Xjdl.Core.Combat;
using Xjdl.Core.State;

namespace Xjdl.Core.Tests.Combat;

// Feature: core-rules-engine, Property 19: 表一火力比构造
public class FirePowerRegularProperties
{
    /// <summary>进攻方主攻单位进攻战斗力：非负整数（Req 6.1，多格打一时为攻击力之和，可为 0）。</summary>
    private static readonly Gen<int> GenAttackerAttack = Gen.Int[0, 10_000];

    /// <summary>防守方主攻单位防御战斗力：正整数（分母不可为零）。</summary>
    private static readonly Gen<int> GenDefenderDefense = Gen.Int[1, 10_000];

    /// <summary>
    /// Property 19: 表一火力比构造。
    /// 对任意表一（常规进攻）交战，火力比整数对恒等于
    /// (进攻方主攻单位进攻战斗力, 防守方主攻单位防御战斗力)：
    /// <see cref="FirePower.ComputeRatio"/>(RegularAttack, a, d) 的分子/分母
    /// 分别等于 a、d，绝不化简、绝不转浮点（Req 6.1、6.5）。
    /// **Validates: Requirements 6.1**
    /// </summary>
    [Fact]
    public void RegularAttack_RatioEquals_AttackOverDefense()
    {
        Gen.Select(GenAttackerAttack, GenDefenderDefense).Sample((a, d) =>
        {
            var ratio = FirePower.ComputeRatio(CombatTable.RegularAttack, a, d);

            return ratio == new FirePowerRatio(a, d)
                && ratio.Numerator == a
                && ratio.Denominator == d;
        }, iter: 1000);
    }
}
