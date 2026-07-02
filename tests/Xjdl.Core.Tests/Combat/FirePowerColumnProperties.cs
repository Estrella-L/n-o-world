using CsCheck;
using Xjdl.Core.State;

namespace Xjdl.Core.Tests.Combat;

// Feature: core-rules-engine, Property 21: 火力比向下取整偏向防守
public class FirePowerColumnProperties
{
    /// <summary>火力比分子：正整数（真实比值有意义，避免全零退化）。</summary>
    private static readonly Gen<int> GenNumerator = Gen.Int[1, 10_000];

    /// <summary>火力比分母：正整数（分母不可为零，否则 <see cref="FirePowerRatio.ToColumn"/> 抛出）。</summary>
    private static readonly Gen<int> GenDenominator = Gen.Int[1, 10_000];

    /// <summary>
    /// 各档位的下界阈值（分子, 分母），自低到高。col0 无下界（真实比值 &lt; 1:1）。
    /// 用整数交叉相乘比较：ratio(n/d) >= 阈值(tn/td)  <=>  n*td >= tn*d（d、td 均为正）。
    /// </summary>
    private static readonly (int Tn, int Td)[] ColumnLowerBounds =
    {
        (0, 1), // col0: 真实比值 < 1:1（下界视为 0，任何正比值均满足 >= 0）
        (1, 1), // col1: >= 1:1
        (3, 2), // col2: >= 1.5:1
        (2, 1), // col3: >= 2:1
        (3, 1), // col4: >= 3:1
        (4, 1), // col5: >= 4:1
    };

    /// <summary>
    /// 独立复算期望档位：以同一批边界阈值、整数交叉相乘（绝不转浮点）判定
    /// 「阈值不高于真实比值的最高档」，即向下取整偏向防守（Req 6.4、6.5）。
    /// </summary>
    private static int ExpectedColumn(int n, int d)
    {
        if (n >= 4 * d)
        {
            return 5;
        }

        if (n >= 3 * d)
        {
            return 4;
        }

        if (n >= 2 * d)
        {
            return 3;
        }

        if (2 * n >= 3 * d)
        {
            return 2;
        }

        if (n >= d)
        {
            return 1;
        }

        return 0;
    }

    /// <summary>
    /// Property 21: 火力比向下取整偏向防守。
    /// 对任意正整数火力比对，<see cref="FirePowerRatio.ToColumn"/> 映射到
    /// 「阈值不高于真实比值的最高合法档位」——落在两档之间时向下取整到较低档，
    /// 偏向防守方（Req 6.4），全程整数交叉相乘、绝不转浮点（Req 6.5）。
    /// **Validates: Requirements 6.4**
    /// </summary>
    [Fact]
    public void ToColumn_RoundsDown_FavoringDefender()
    {
        Gen.Select(GenNumerator, GenDenominator).Sample((n, d) =>
        {
            var actual = new FirePowerRatio(n, d).ToColumn();
            var expected = ExpectedColumn(n, d);

            if (actual != expected)
            {
                return false;
            }

            // 命中档位的下界阈值不高于真实比值：threshold(actual) <= n/d。
            // n/d >= tn/td  <=>  n*td >= tn*d（整数交叉相乘）。
            var (lowTn, lowTd) = ColumnLowerBounds[actual];
            var lowerHolds = (long)n * lowTd >= (long)lowTn * d;

            // 存在更高档时，真实比值严格低于该更高档的下界：n/d < threshold(actual + 1)。
            // n/d < tn/td  <=>  n*td < tn*d。
            var upperHolds = true;
            if (actual < ColumnLowerBounds.Length - 1)
            {
                var (nextTn, nextTd) = ColumnLowerBounds[actual + 1];
                upperHolds = (long)n * nextTd < (long)nextTn * d;
            }

            return lowerHolds && upperHolds;
        }, iter: 1000);
    }
}
