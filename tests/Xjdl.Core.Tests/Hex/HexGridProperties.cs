using CsCheck;
using Xjdl.Core.Hex;

namespace Xjdl.Core.Tests.Hex;

/// <summary>
/// 六角几何的属性测试（CsCheck，一属性一测试，每个至少 100 次迭代）。
/// </summary>
public class HexGridProperties
{
    // Feature: core-rules-engine, Property 1: 立方坐标约束
    // For any integer (Q, R) constructed HexCoord, Q + R + S == 0 where S == -Q - R.
    [Fact]
    public void Property1_CubeConstraint_QPlusRPlusS_IsZero()
    {
        // 约束到安全范围，避免 S = -Q - R 的整数溢出造成的伪反例。
        Gen.Int[-1_000_000, 1_000_000]
            .Select(Gen.Int[-1_000_000, 1_000_000], (q, r) => new HexCoord(q, r))
            .Sample(
                hex => hex.S == -hex.Q - hex.R && hex.Q + hex.R + hex.S == 0,
                iter: 100);
    }
}
