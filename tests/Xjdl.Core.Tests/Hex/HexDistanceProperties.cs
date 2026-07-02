using CsCheck;
using Xjdl.Core.Hex;

namespace Xjdl.Core.Tests.Hex;

/// <summary>
/// 六角距离度量的属性测试（CsCheck，一属性一测试，每个至少 100 次迭代）。
/// </summary>
public class HexDistanceProperties
{
    // 合法坐标生成器：约束 q、r 到有界范围 [-1000, 1000]，避免整数溢出且覆盖正负与零。
    private static readonly Gen<HexCoord> GenHexCoord =
        Gen.Select(Gen.Int[-1000, 1000], Gen.Int[-1000, 1000], (q, r) => new HexCoord(q, r));

    // Feature: core-rules-engine, Property 3: 距离是对称且自反为零的度量
    // For any two HexCoord a, b: Distance(a, b) == Distance(b, a) and Distance(a, a) == 0.
    // Validates: Requirements 1.4
    [Fact]
    public void Distance_IsSymmetric_AndReflexiveZero()
    {
        Gen.Select(GenHexCoord, GenHexCoord)
            .Sample(
                pair =>
                {
                    var (a, b) = pair;

                    // 对称性：Distance(a, b) == Distance(b, a)。
                    Assert.Equal(HexCoord.Distance(a, b), HexCoord.Distance(b, a));

                    // 自反为零：任意格到自身距离为 0。
                    Assert.Equal(0, HexCoord.Distance(a, a));
                    Assert.Equal(0, HexCoord.Distance(b, b));
                },
                iter: 100);
    }
}
