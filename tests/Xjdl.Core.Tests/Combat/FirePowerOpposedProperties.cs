using CsCheck;
using Xjdl.Core.Combat;
using Xjdl.Core.State;

namespace Xjdl.Core.Tests.Combat;

// Feature: core-rules-engine, Property 20: 对攻/遭遇战取高值为分子
public class FirePowerOpposedProperties
{
    /// <summary>接触一方的进攻战斗力：正整数（分母不可为零）。</summary>
    private static readonly Gen<int> GenAttack = Gen.Int[1, 10_000];

    /// <summary>
    /// Property 20: 对攻/遭遇战取高值为分子。
    /// 对任意表二（对攻）或表三（遭遇）交战，火力比取双方进攻战斗力，
    /// 高值为分子、低值为分母，故恒 &gt;= 1:1（Req 6.2、6.3）。
    /// 顺序无关：交换两个参数得到相同的火力比。
    /// **Validates: Requirements 6.2, 6.3**
    /// </summary>
    [Theory]
    [InlineData(CombatTable.MutualAttack)]
    [InlineData(CombatTable.Encounter)]
    public void Opposed_HigherValueIsNumerator_RatioAtLeastOneToOne(CombatTable table)
    {
        Gen.Select(GenAttack, GenAttack).Sample((a, b) =>
        {
            var ratio = FirePower.ComputeRatio(table, a, b);
            var expectedHigh = Math.Max(a, b);
            var expectedLow = Math.Min(a, b);

            // 高值为分子、低值为分母，恒 >= 1:1，且顺序无关。
            var swapped = FirePower.ComputeRatio(table, b, a);

            return ratio.Numerator == expectedHigh
                && ratio.Denominator == expectedLow
                && ratio.Numerator >= ratio.Denominator
                && ratio == swapped;
        }, iter: 1000);
    }
}
