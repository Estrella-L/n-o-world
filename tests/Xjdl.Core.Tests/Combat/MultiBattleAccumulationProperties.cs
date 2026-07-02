using System.Linq;
using CsCheck;
using Xjdl.Core.Combat;
using Xjdl.Core.State;
using Xjdl.Core.Tests.Support;

namespace Xjdl.Core.Tests.Combat;

// Feature: core-rules-engine, Property 28: 多战快照与累计歼灭
public class MultiBattleAccumulationProperties
{
    /// <summary>
    /// 满编快照单位 + 一组「各场应受战损」k_i 的生成器（模拟一个单位卷入多场战斗）。
    /// InitAttack/InitDefense 均整除 Resilience0（以 N×倍数构造保证零余数，Req 8.1），
    /// 快照处于满编（Attack==InitAttack、Defense==InitDefense、ResilienceLeft==Resilience0，Req 3.3）。
    /// 每场战损 k_i ∈ [0, 4]、场数 ∈ [0, 6]，故累计 Σk_i 可小于、等于或大于 N，
    /// 覆盖「存活线性衰减」「恰好打空阵亡」「超额打空钳制」三区间。
    /// </summary>
    private static readonly Gen<(UnitState Unit, int[] Casualties)> GenSnapshotUnitWithMultiBattleCasualties =
        from id in Gen.Int[0, 1000]
        from owner in Generators.Sides
        from typeKey in Generators.TypeKeys
        from cls in Generators.UnitClasses
        from n in Gen.Int[1, 6]
        from attackMul in Gen.Int[1, 6]
        from defenseMul in Gen.Int[1, 6]
        from movement in Gen.Int[1, 8]
        from vision in Gen.Int[1, 6]
        from supportRange in Gen.Int[0, 4]
        from pos in Generators.HexCoord
        from cmd in Generators.Commands
        from flags in Generators.NightFlags
        from ks in Gen.Int[0, 4].Array[0, 6]
        select (
            new UnitState(
                new UnitId(id),
                owner,
                typeKey,
                cls,
                n * attackMul, // InitAttack（整除 N）
                n * defenseMul, // InitDefense（整除 N）
                n, // Resilience0
                n * attackMul, // 满编当前攻 == InitAttack
                n * defenseMul, // 满编当前防 == InitDefense
                n, // 满编剩余韧性 == N
                movement,
                vision,
                supportRange,
                pos,
                cmd,
                flags),
            ks);

    /// <summary>
    /// Property 28: 多战快照与累计歼灭（Req 3.3、3.6、8.4）。
    /// 对任意卷入多场战斗的满编快照单位（快照剩余韧性 N）与各场应受战损 k_i：
    ///  1) 阶段 6 一次性扣除的战损等于各场之和 Σk_i —— 即 <see cref="Casualty.ApplyCasualties"/>
    ///     以累计总和结算，剩余韧性 == max(0, N − Σk_i)（Req 3.6）；
    ///  2) 歼灭当且仅当「累计应掉战损 Σk_i ≥ 快照剩余韧性 N」，判定以阶段 4 快照韧性 N 为准，
    ///     而非链式实时扣减（Req 8.4）；阵亡时攻/防/韧性同步归零，存活时攻/防按快照分母线性衰减；
    ///  3) 结果只依赖累计总和与快照，不依赖各场战损的先后顺序（快照式结算，Req 8.4）。
    /// **Validates: Requirements 3.3, 3.6, 8.4**
    /// </summary>
    [Fact]
    public void MultiBattleCasualties_AccumulateBySumAndJudgeBySnapshotResilience()
    {
        GenSnapshotUnitWithMultiBattleCasualties.Sample(tuple =>
        {
            var (unit, ks) = tuple;
            var n = unit.Resilience0;

            // 阶段 6 累计：把各场应受战损相加后一次性扣除（Req 3.6）。
            var total = ks.Sum();
            var applied = Casualty.ApplyCasualties(unit, total);

            var annihilated = total >= n; // 歼灭判据以快照剩余韧性 N 为准（Req 8.4）。

            bool coreInvariant;
            if (annihilated)
            {
                // 累计打空即阵亡：攻/防/剩余韧性同步归零（Req 8.2/8.4）。
                coreInvariant = applied.Attack == 0
                    && applied.Defense == 0
                    && applied.ResilienceLeft == 0;
            }
            else
            {
                // 存活：剩余韧性 == N − Σk_i，攻/防按快照分母线性衰减（Req 3.6/8.1）。
                coreInvariant = applied.ResilienceLeft == n - total
                    && applied.Attack == unit.InitAttack - (total * unit.AttackDecay)
                    && applied.Defense == unit.InitDefense - (total * unit.DefenseDecay);
            }

            // 剩余韧性恒为 max(0, N − Σk_i)（累计-钳制不变量，Req 3.6）。
            var resilienceInvariant = applied.ResilienceLeft == System.Math.Max(0, n - total);

            // 顺序无关：以相反顺序累加得到同一总和，且一次性结算得到字节级一致的单位（Req 8.4）。
            var reversedTotal = ks.Reverse().Sum();
            var appliedReversed = Casualty.ApplyCasualties(unit, reversedTotal);
            var orderIndependent = reversedTotal == total && appliedReversed == applied;

            return coreInvariant && resilienceInvariant && orderIndependent;
        }, iter: 1000);
    }
}
