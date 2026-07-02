using CsCheck;
using Xjdl.Core.Hex;

namespace Xjdl.Core.Tests.Hex;

/// <summary>
/// HexGrid.Range 的属性测试（CsCheck，一属性一测试，每个至少 100 次迭代）。
/// </summary>
public class HexRangeProperties
{
    // 合法中心格生成器：约束到有界范围，避免坐标叠加半径后整数溢出。
    private static readonly Gen<HexCoord> GenCenter =
        Gen.Select(Gen.Int[-1000, 1000], Gen.Int[-1000, 1000], (q, r) => new HexCoord(q, r));

    // 半径生成器：0..30 的合理有界区间（含 V == 0 边界）。
    private static readonly Gen<int> GenRadius = Gen.Int[0, 30];

    // Feature: core-rules-engine, Property 4: 半径范围完备且精确
    // For any center and radius V >= 0, every cell in Range(center, V) has distance <= V,
    // and the element count is exactly 3V^2 + 3V + 1 (hex disk count), i.e. it contains
    // exactly all cells at distance <= V.
    // Validates: Requirements 1.5
    [Fact]
    public void Property4_Range_IsCompleteAndExact()
    {
        GenCenter.Select(GenRadius)
            .Sample(
                t =>
                {
                    var (center, radius) = t;
                    var cells = HexGrid.Range(center, radius);

                    // 所有格到中心的距离 <= V。
                    Assert.All(cells, c => Assert.True(center.DistanceTo(c) <= radius));

                    // 元素数恰为六角盘计数 3V² + 3V + 1。
                    var expectedCount = (3 * radius * radius) + (3 * radius) + 1;
                    Assert.Equal(expectedCount, cells.Count);

                    // 所有格互异（无重复）。
                    Assert.Equal(cells.Count, cells.Distinct().Count());
                },
                iter: 100);
    }
}
