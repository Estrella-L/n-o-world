namespace Xjdl.Core.State;

/// <summary>
/// 火力比：整数分子/分母对，从不转浮点（Req 6.5）。
/// 战斗结算全程以整数对表示攻防火力之比。
/// </summary>
public readonly record struct FirePowerRatio(int Numerator, int Denominator)
{
    /// <summary>
    /// 映射到交战表「火力比档位」的全局列索引（向下取整偏向防守，Req 6.4）。
    /// <para>
    /// 档位取自 docs/01-战斗机制.md〈表一/表二/表三〉出现过的全部火力比行，
    /// 归并为一条自低到高的有序列表（全局索引，供任务 9.7 的 CombatTables 统一编码）：
    /// </para>
    /// <list type="table">
    /// <listheader><term>列索引</term><description>火力比档位</description></listheader>
    /// <item><term>0</term><description>&lt;1:1（劣势，仅表一出现）</description></item>
    /// <item><term>1</term><description>1:1（均势）</description></item>
    /// <item><term>2</term><description>1.5:1</description></item>
    /// <item><term>3</term><description>2:1</description></item>
    /// <item><term>4</term><description>3:1</description></item>
    /// <item><term>5</term><description>4:1+（4:1 及以上）</description></item>
    /// </list>
    /// <para>
    /// 表一使用全部 6 档（列 0..5）；表二/表三火力比恒 &gt;= 1:1（见 <see cref="Combat.FirePower"/>），
    /// 故只会落在列 1..5，永不产生列 0。CombatTables 建议以同一全局索引编码，
    /// 表二/表三的列 0 单元格留空（永不被查询），避免任何偏移换算。
    /// </para>
    /// <para>
    /// 「向下取整偏向防守」（Req 6.4）：当真实比值落在两档之间时，取「阈值不高于真实比值的最高档」，
    /// 即偏向对进攻方更不利、对防守方更有利的较低档。全程整数交叉相乘比较，绝不转浮点（Req 6.5）。
    /// </para>
    /// </summary>
    /// <returns>火力比档位的全局列索引（0..5）。</returns>
    /// <exception cref="InvalidOperationException">当分母不为正整数时抛出（火力比无意义）。</exception>
    public int ToColumn()
    {
        if (Denominator <= 0)
        {
            throw new InvalidOperationException(
                $"火力比分母必须为正整数，当前为 {Denominator}。");
        }

        var n = Numerator;
        var d = Denominator;

        // 从高档向低档比较，命中第一个「阈值 <= 真实比值」的档位即为向下取整结果（偏向防守）。
        // 全部使用整数交叉相乘，避免浮点：n/d >= a/b  <=>  n*b >= a*d（d、b 均为正）。
        if (n >= 4 * d)
        {
            return 5; // >= 4:1
        }

        if (n >= 3 * d)
        {
            return 4; // >= 3:1
        }

        if (n >= 2 * d)
        {
            return 3; // >= 2:1
        }

        if (2 * n >= 3 * d)
        {
            return 2; // >= 1.5:1（n/d >= 3/2）
        }

        if (n >= d)
        {
            return 1; // >= 1:1
        }

        return 0; // < 1:1（劣势）
    }
}
