using Xjdl.Core.State;

namespace Xjdl.Core.Combat;

/// <summary>
/// 阶段 6 战损同步结算：韧性战损衰减与歼灭降级（Req 8.1、8.2、8.4、7.2）。
/// 一次战损使 攻 -（初始攻÷N）、防 -（初始防÷N）、韧性 -1（整除保证整数，Req 8.1）；
/// 韧性归零即阵亡，攻防同步归零并移除（Req 8.2）。
/// 歼灭判定以「快照剩余韧性」对比「累计应掉战损」，而非链式实时扣减（Req 8.4）。
/// 「守歼」/「劣歼」在快照剩余韧性大于应掉战损时降级为「守-3退」/「劣-3退」（Req 7.2）。
/// 见 docs/01-战斗机制.md〈战损与歼灭〉与 design.md〈CombatResolver · ApplyCasualties〉。
/// </summary>
public static class Casualty
{
    /// <summary>降级为「守-N退」/「劣-N退」时固定的战损次数（守-3退/劣-3退，Req 7.2）。</summary>
    public const int DowngradeRetreatCasualties = 3;

    /// <summary>
    /// 把一个单位本回合所有战斗的应受战损累加后一次性扣除（阶段 6，Req 8.1、8.2）。
    /// 全程以快照值线性计算而非链式扣减：攻/防各减 <c>应掉战损 × 每次衰减量</c>，韧性减去应掉战损。
    /// 若扣减后剩余韧性 ≤ 0 判定阵亡，攻/防/韧性同步归零（Req 8.2）；否则攻/防夹取到不小于 0。
    /// 不原地修改输入，返回新的不可变 <see cref="UnitState"/>（Req 2.1、2.7）。
    /// </summary>
    /// <param name="snapshotUnit">阶段 4 冻结的快照单位（衰减分母与当前值来源）。</param>
    /// <param name="totalCasualties">本回合累计应受战损次数（&gt;= 0）。</param>
    /// <returns>扣除战损后的新单位实例；阵亡时攻/防/韧性均为 0。</returns>
    public static UnitState ApplyCasualties(UnitState snapshotUnit, int totalCasualties)
    {
        ArgumentNullException.ThrowIfNull(snapshotUnit);
        if (totalCasualties < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(totalCasualties),
                totalCasualties,
                "战损次数不能为负。");
        }

        // 以快照值一次性计算，避免链式实时扣减带来的顺序依赖（Req 8.4）。
        var resilienceLeft = snapshotUnit.ResilienceLeft - totalCasualties;

        if (resilienceLeft <= 0)
        {
            // 韧性归零即阵亡，攻防同步降至 0（Req 8.2）。
            return snapshotUnit with
            {
                Attack = 0,
                Defense = 0,
                ResilienceLeft = 0,
            };
        }

        var attack = snapshotUnit.Attack - (totalCasualties * snapshotUnit.AttackDecay);
        var defense = snapshotUnit.Defense - (totalCasualties * snapshotUnit.DefenseDecay);

        return snapshotUnit with
        {
            Attack = Math.Max(0, attack),
            Defense = Math.Max(0, defense),
            ResilienceLeft = resilienceLeft,
        };
    }

    /// <summary>
    /// 歼灭降级判定（Req 7.2）：对「守歼」（<see cref="ResultCode.DefenderAnnihilate"/>）或
    /// 「劣歼」（<see cref="ResultCode.LoserAnnihilate"/>）结果，
    /// 若被歼方「快照剩余韧性」严格大于「应掉战损」，则改按「守-3退」（<see cref="ResultCode.DefenderNRetreat"/>）
    /// 或「劣-3退」（<see cref="ResultCode.LoserNRetreat"/>）处理；否则保持阵亡结果。
    /// 其余结果代码原样返回，不受影响。
    /// </summary>
    /// <param name="result">交战表读出的原始结果代码。</param>
    /// <param name="snapshotResilienceLeft">被歼方在阶段 4 的快照剩余韧性。</param>
    /// <param name="casualtiesToTake">该结果本应造成的战损次数。</param>
    /// <returns>降级后的结果代码，或原结果代码。</returns>
    public static ResultCode ResolveAnnihilation(
        ResultCode result,
        int snapshotResilienceLeft,
        int casualtiesToTake)
    {
        // 快照剩余韧性大于应掉战损 → 未被打空 → 降级为「N退」（Req 7.2）。
        var survives = snapshotResilienceLeft > casualtiesToTake;

        return result switch
        {
            ResultCode.DefenderAnnihilate when survives => ResultCode.DefenderNRetreat,
            ResultCode.LoserAnnihilate when survives => ResultCode.LoserNRetreat,
            _ => result,
        };
    }
}
