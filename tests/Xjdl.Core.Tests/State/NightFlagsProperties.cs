using CsCheck;
using Xjdl.Core.State;

namespace Xjdl.Core.Tests.State;

// Feature: core-rules-engine, Property 58: 夜战标志集合增删一致
public class NightFlagsProperties
{
    // 所有单一标志位（不含 None）。
    private static readonly NightFlags[] SingleFlags =
    {
        NightFlags.NightVisionKeep,
        NightFlags.NightRangeKeep,
        NightFlags.NightAttackKeep,
        NightFlags.NightMoveKeep,
        NightFlags.IgnoreZoc,
        NightFlags.NightZocKeep,
    };

    // 任意 NightFlags 组合：6 个位 -> 0..63。
    private static readonly Gen<NightFlags> GenSet =
        Gen.Int[0, 63].Select(i => (NightFlags)i);

    // 任意单一标志。
    private static readonly Gen<NightFlags> GenFlag =
        Gen.Int[0, SingleFlags.Length - 1].Select(i => SingleFlags[i]);

    /// <summary>
    /// Property 58: 夜战标志集合增删一致。
    /// 对任意集合与任意单一标志：
    ///  1) 添加后集合包含该标志（`| flag` 后 HasFlag 为真）；
    ///  2) 从添加结果中移除后不再包含该标志（`& ~flag`）；
    ///  3) （去掉该标志的原集合）添加再移除 == （去掉该标志的原集合），即增删可逆一致。
    /// **Validates: Requirements 18.8**
    /// </summary>
    [Fact]
    public void AddRemove_IsReversibleAndConsistent()
    {
        Gen.Select(GenSet, GenFlag).Sample((baseSet, flag) =>
        {
            var added = baseSet | flag;
            var removed = added & ~flag;
            var baseWithout = baseSet & ~flag;

            var containsAfterAdd = added.HasFlag(flag);
            var notContainsAfterRemove = !removed.HasFlag(flag);
            var reversible = ((baseWithout | flag) & ~flag) == baseWithout;

            return containsAfterAdd && notContainsAfterRemove && reversible;
        }, iter: 1000);
    }
}
