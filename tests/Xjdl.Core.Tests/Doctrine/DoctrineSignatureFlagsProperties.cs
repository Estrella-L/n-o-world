using CsCheck;
using Xjdl.Core.Doctrine;
using Xjdl.Core.State;
using Xjdl.Core.Tests.Support;
using CoreDoctrine = Xjdl.Core.Doctrine.Doctrine;

namespace Xjdl.Core.Tests.Doctrine;

// Feature: core-rules-engine, Property 55: 学说批量赋签名标志
public class DoctrineSignatureFlagsProperties
{
    /// <summary>
    /// 一个「合法单位 + 携带任意签名标志的学说」样本：单位由 <see cref="Generators.UnitState"/>
    /// 派生（携带随机 <see cref="UnitState.Flags"/>），学说携带随机 <see cref="CoreDoctrine.SignatureFlags"/>
    /// （<see cref="Generators.NightFlags"/> 的任意组合）。学说修正集合为空——本属性只关注签名标志的批量赋予（Req 16.4）。
    /// </summary>
    private static readonly Gen<(UnitState Unit, CoreDoctrine Doctrine)> GenUnitAndDoctrine =
        from unit in Generators.UnitState
        from signature in Generators.NightFlags
        let doctrine = new CoreDoctrine(
            "doctrine.test",
            System.Array.Empty<StatModifier>(),
            signature)
        select (unit, doctrine);

    /// <summary>
    /// Property 55: 学说批量赋签名标志。
    /// 对任意单位与任意学说：组队后 <see cref="DoctrineSystem.ApplySignatureFlags"/> 的结果
    ///  1) 并入了学说的全部签名标志（结果 HasFlag 每个签名位为真）；
    ///  2) 保留了单位原有的全部标志（原 Flags 的每个位在结果中仍存在）；
    ///  3) 结果 Flags 恰等于 (原 Flags | 签名 Flags)，无多余位；
    ///  4) 输入 <see cref="UnitState"/> 不被修改（不可变 record，施加后原实例仍等于施加前快照）。
    /// **Validates: Requirements 16.4**
    /// </summary>
    [Fact]
    public void ApplySignatureFlags_UnionsSignatureAndPreservesOriginal()
    {
        GenUnitAndDoctrine.Sample(sample =>
        {
            var (unit, doctrine) = sample;

            // 施加前对单位取快照（结构相等的独立副本），用于校验输入不被修改。
            var unitSnapshot = unit with { };
            var originalFlags = unit.Flags;
            var signature = doctrine.SignatureFlags;

            var result = DoctrineSystem.ApplySignatureFlags(unit, doctrine);

            // 1) 结果并入了全部签名标志。
            var containsSignature = (result.Flags & signature) == signature;

            // 2) 结果保留了原有全部标志。
            var retainsOriginal = (result.Flags & originalFlags) == originalFlags;

            // 3) 结果恰为并集，无多余位。
            var exactUnion = result.Flags == (originalFlags | signature);

            // 4) 输入单位不被修改。
            var inputUnchanged = unit == unitSnapshot;

            return containsSignature && retainsOriginal && exactUnion && inputUnchanged;
        }, iter: 1000);
    }
}
