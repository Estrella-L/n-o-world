using CsCheck;
using Xjdl.Core.Combat;

namespace Xjdl.Core.Tests.Combat;

// Feature: core-rules-engine, Property 22: 对攻平局由低点数方承受
public class MutualTieProperties
{
    /// <summary>3D6 读数的取值域：3..18（含端点）。</summary>
    private static readonly Gen<int> GenRoll = Gen.Int[3, 18];

    /// <summary>
    /// Property 22: 对攻平局由低点数方承受。
    /// 对任意表二 1:1 对攻，该回合两次 3D6 中点数较低的一方承受「劣-N」结果；
    /// 两次读数相等则无优劣归属、按对称「互-N」处理（返回 null）（Req 6.6）。
    /// **Validates: Requirements 6.6**
    /// </summary>
    [Fact]
    public void MutualTie_LowerRollTakesLoser_EqualIsSymmetric()
    {
        Gen.Select(GenRoll, GenRoll).Sample((rollA, rollB) =>
        {
            var loser = MutualTieResolver.LoserSide(rollA, rollB);

            if (rollA < rollB)
            {
                // A 点数较低 → A 承受「劣-N」。
                return loser == MutualTieResolver.SideA;
            }

            if (rollB < rollA)
            {
                // B 点数较低 → B 承受「劣-N」。
                return loser == MutualTieResolver.SideB;
            }

            // 平局 → 无优劣归属，按对称「互-N」处理。
            return loser is null;
        }, iter: 1000);
    }
}
