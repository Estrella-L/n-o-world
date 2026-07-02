using Xjdl.Core.State;

namespace Xjdl.Core.Combat;

/// <summary>
/// 阶段 5 基础火力比计算（Req 6.1、6.2、6.3）。全程整数对，绝不转浮点（Req 6.5）。
/// <para>
/// 三张交战表的火力比构造规则（见 docs/01-战斗机制.md〈表一/表二/表三〉）：
/// </para>
/// <list type="bullet">
/// <item>表一 <see cref="CombatTable.RegularAttack"/>：分子 = 进攻方主攻单位进攻战斗力，分母 = 防守方主攻单位防御战斗力（Req 6.1）。可低于 1:1。</item>
/// <item>表二 <see cref="CombatTable.MutualAttack"/>：分子/分母取双方进攻战斗力，高值为分子、低值为分母，故恒 &gt;= 1:1（Req 6.2）。</item>
/// <item>表三 <see cref="CombatTable.Encounter"/>：同表二，双方进攻战斗力高值为分子、低值为分母，恒 &gt;= 1:1（Req 6.3）。</item>
/// </list>
/// <para>
/// 本类型只负责构造「基础火力比整数对」；档位向下取整偏向防守由
/// <see cref="FirePowerRatio.ToColumn"/> 完成（Req 6.4）。多格打一的攻击力求和（Req 5.5）、
/// 主攻单位选取（Req 9.2）等由上层战斗结算在调用本方法前完成。
/// </para>
/// </summary>
public static class FirePower
{
    /// <summary>
    /// 按交战表类型计算基础火力比整数对。
    /// </summary>
    /// <param name="table">交战表类型，决定分子/分母的构造规则。</param>
    /// <param name="primaryValue">
    /// 表一：进攻方主攻单位的进攻战斗力（多格打一时为各进攻格主攻单位攻击力之和，Req 5.5）。
    /// 表二/表三：接触一方的进攻战斗力（与 <paramref name="opposingValue"/> 顺序无关）。
    /// </param>
    /// <param name="opposingValue">
    /// 表一：防守方主攻单位的防御战斗力。
    /// 表二/表三：接触另一方的进攻战斗力。
    /// </param>
    /// <returns>基础火力比整数对（<see cref="FirePowerRatio"/>）。</returns>
    /// <exception cref="ArgumentOutOfRangeException">当交战表类型未知时抛出。</exception>
    public static FirePowerRatio ComputeRatio(CombatTable table, int primaryValue, int opposingValue)
    {
        return table switch
        {
            CombatTable.RegularAttack => RegularAttack(primaryValue, opposingValue),
            CombatTable.MutualAttack or CombatTable.Encounter => Opposed(primaryValue, opposingValue),
            _ => throw new ArgumentOutOfRangeException(nameof(table), table, "未知的交战表类型。"),
        };
    }

    /// <summary>
    /// 表一（常规进攻）火力比：进攻方主攻攻击力 ÷ 防守方主攻防御力（Req 6.1）。
    /// 结果可低于 1:1（进攻不利），交由 <see cref="FirePowerRatio.ToColumn"/> 落到「&lt;1:1」档。
    /// </summary>
    /// <param name="attackerAttack">进攻方主攻单位的进攻战斗力（&gt;= 0；多格打一时为攻击力之和）。</param>
    /// <param name="defenderDefense">防守方主攻单位的防御战斗力（必须 &gt; 0）。</param>
    /// <returns>火力比整数对 <c>(attackerAttack : defenderDefense)</c>。</returns>
    /// <exception cref="ArgumentOutOfRangeException">进攻力为负、或防御力不为正时抛出（火力比无意义）。</exception>
    public static FirePowerRatio RegularAttack(int attackerAttack, int defenderDefense)
    {
        if (attackerAttack < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(attackerAttack), attackerAttack, "进攻战斗力不能为负。");
        }

        if (defenderDefense <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(defenderDefense), defenderDefense, "防守方防御战斗力必须为正整数。");
        }

        return new FirePowerRatio(attackerAttack, defenderDefense);
    }

    /// <summary>
    /// 表二/表三（对攻/遭遇）火力比：双方进攻战斗力高值为分子、低值为分母，恒 &gt;= 1:1（Req 6.2、6.3）。
    /// 顺序无关：交换两个参数得到相同的火力比。
    /// </summary>
    /// <param name="firstAttack">接触一方的进攻战斗力（必须 &gt; 0）。</param>
    /// <param name="secondAttack">接触另一方的进攻战斗力（必须 &gt; 0）。</param>
    /// <returns>火力比整数对 <c>(max : min)</c>，恒 &gt;= 1:1。</returns>
    /// <exception cref="ArgumentOutOfRangeException">任一方进攻力不为正时抛出（分母不可为零）。</exception>
    public static FirePowerRatio Opposed(int firstAttack, int secondAttack)
    {
        if (firstAttack <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(firstAttack), firstAttack, "对攻/遭遇战进攻战斗力必须为正整数。");
        }

        if (secondAttack <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(secondAttack), secondAttack, "对攻/遭遇战进攻战斗力必须为正整数。");
        }

        var high = Math.Max(firstAttack, secondAttack);
        var low = Math.Min(firstAttack, secondAttack);
        return new FirePowerRatio(high, low);
    }
}
