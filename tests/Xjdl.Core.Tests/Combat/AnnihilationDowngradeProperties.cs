using CsCheck;
using Xjdl.Core.Combat;
using Xjdl.Core.State;

namespace Xjdl.Core.Tests.Combat;

// Feature: core-rules-engine, Property 24: 歼灭降级判定
public class AnnihilationDowngradeProperties
{
    /// <summary>
    /// 任意结果代码均匀取样，覆盖「守歼」/「劣歼」两个歼灭码及其余非歼灭码。
    /// </summary>
    private static readonly Gen<ResultCode> GenResultCode =
        Gen.Int[0, Enum.GetValues<ResultCode>().Length - 1].Select(i => (ResultCode)i);

    /// <summary>非负的快照剩余韧性与应掉战损。</summary>
    private static readonly Gen<int> GenNonNeg = Gen.Int[0, 50];

    /// <summary>
    /// Property 24: 歼灭降级判定（Req 7.2）。
    /// 对任意结果代码与任意非负「快照剩余韧性 / 应掉战损」：
    ///  1) <see cref="ResultCode.DefenderAnnihilate"/>（守歼）在 快照剩余韧性 &gt; 应掉战损 时降级为
    ///     <see cref="ResultCode.DefenderNRetreat"/>（守-3退），否则保持守歼；
    ///  2) <see cref="ResultCode.LoserAnnihilate"/>（劣歼）在同一条件下降级为
    ///     <see cref="ResultCode.LoserNRetreat"/>（劣-3退），否则保持劣歼；
    ///  3) 其余所有结果代码原样返回，不受影响。
    /// **Validates: Requirements 7.2**
    /// </summary>
    [Fact]
    public void ResolveAnnihilation_DowngradesOnlyWhenTargetSurvives()
    {
        (from result in GenResultCode
         from resilienceLeft in GenNonNeg
         from casualtiesToTake in GenNonNeg
         select (result, resilienceLeft, casualtiesToTake))
            .Sample(t =>
            {
                var (result, resilienceLeft, casualtiesToTake) = t;
                var survives = resilienceLeft > casualtiesToTake;

                var expected = result switch
                {
                    ResultCode.DefenderAnnihilate when survives => ResultCode.DefenderNRetreat,
                    ResultCode.LoserAnnihilate when survives => ResultCode.LoserNRetreat,
                    _ => result,
                };

                return Casualty.ResolveAnnihilation(result, resilienceLeft, casualtiesToTake) == expected;
            }, iter: 1000);
    }
}
