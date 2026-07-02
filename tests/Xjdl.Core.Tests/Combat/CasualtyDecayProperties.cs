using CsCheck;
using Xjdl.Core.Combat;
using Xjdl.Core.State;
using Xjdl.Core.Tests.Support;

namespace Xjdl.Core.Tests.Combat;

// Feature: core-rules-engine, Property 27: 战损线性衰减至归零阵亡
public class CasualtyDecayProperties
{
    /// <summary>
    /// 满编快照单位 + 战损次数 k 的生成器：
    /// InitAttack/InitDefense 均整除 Resilience0（以 N×倍数构造保证零余数，Req 8.1），
    /// 快照处于满编（Attack==InitAttack、Defense==InitDefense、ResilienceLeft==Resilience0），
    /// k ∈ [0, N] 覆盖「零战损」到「打满阵亡」全区间。
    /// </summary>
    private static readonly Gen<(UnitState Unit, int Casualties)> GenFullStrengthUnitWithCasualties =
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
        from k in Gen.Int[0, n]
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
            k);

    /// <summary>
    /// Property 27: 战损线性衰减至归零阵亡。
    /// 对任意满编单位受 k（0 ≤ k ≤ N）次战损：
    /// 当 k &lt; N 时 攻 == InitAttack − k×(InitAttack/N)、防 == InitDefense − k×(InitDefense/N)、
    /// 剩余韧性 == N − k（线性衰减，Req 8.1）；
    /// 当 k == N 时 攻/防/剩余韧性同步归零、单位阵亡（Req 8.2）。
    /// **Validates: Requirements 8.1, 8.2**
    /// </summary>
    [Fact]
    public void Casualties_LinearlyDecayAttackDefenseResilience_ToZeroOnAnnihilation()
    {
        GenFullStrengthUnitWithCasualties.Sample(tuple =>
        {
            var (unit, k) = tuple;
            var n = unit.Resilience0;

            var result = Casualty.ApplyCasualties(unit, k);

            if (k == n)
            {
                // 打满即阵亡：攻/防/剩余韧性同步归零（Req 8.2）。
                return result.Attack == 0
                    && result.Defense == 0
                    && result.ResilienceLeft == 0;
            }

            // 未打满：三项各按整除衰减量线性递减（Req 8.1）。
            return result.Attack == unit.InitAttack - (k * unit.AttackDecay)
                && result.Defense == unit.InitDefense - (k * unit.DefenseDecay)
                && result.ResilienceLeft == n - k;
        }, iter: 1000);
    }
}
