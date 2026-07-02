using CsCheck;
using Xjdl.Core.Hex;

namespace Xjdl.Core.Tests.Hex;

public class HexNeighborProperties
{
    // 合法坐标生成器：约束 q、r 到有界范围，避免整数溢出且覆盖正负与零。
    private static readonly Gen<HexCoord> GenHexCoord =
        Gen.Select(Gen.Int[-1000, 1000], Gen.Int[-1000, 1000], (q, r) => new HexCoord(q, r));

    // Feature: core-rules-engine, Property 2: 相邻格结构不变量
    // For any HexCoord center, Neighbors() returns exactly 6 distinct cells,
    // each at hex distance 1 from center, and the i-th equals center + Directions[i]
    // (fixed direction order, translation invariant).
    // Validates: Requirements 1.2, 1.3
    [Fact]
    public void Neighbors_StructureInvariant()
    {
        GenHexCoord.Sample(
            center =>
            {
                var neighbors = center.Neighbors().ToList();

                // 恰好 6 个相邻格。
                Assert.Equal(6, neighbors.Count);

                // 6 个互异。
                Assert.Equal(6, neighbors.Distinct().Count());

                // 每个与中心格的六角距离为 1。
                Assert.All(neighbors, n => Assert.Equal(1, center.DistanceTo(n)));

                // 第 i 个恒等于 center + Directions[i]（固定方向顺序，平移不变）。
                for (var i = 0; i < HexCoord.Directions.Count; i++)
                {
                    Assert.Equal(center + HexCoord.Directions[i], neighbors[i]);
                }
            },
            iter: 100);
    }
}
