using Xjdl.Core.Hex;

namespace Xjdl.Core.Tests.Hex;

public class HexTests
{
    [Fact]
    public void CubeConstraint_QPlusRPlusS_IsZero()
    {
        var h = new HexCoord(2, -5);
        Assert.Equal(0, h.Q + h.R + h.S);
    }

    [Fact]
    public void Distance_ToSelf_IsZero()
    {
        var h = new HexCoord(3, -1);
        Assert.Equal(0, HexCoord.Distance(h, h));
    }

    [Fact]
    public void Distance_IsSymmetric()
    {
        var a = new HexCoord(0, 0);
        var b = new HexCoord(3, -2);
        Assert.Equal(HexCoord.Distance(a, b), HexCoord.Distance(b, a));
    }

    [Theory]
    [InlineData(0, 0, 3, 0, 3)]
    [InlineData(0, 0, 0, 3, 3)]
    [InlineData(0, 0, 3, -3, 3)]
    [InlineData(0, 0, -1, -1, 2)]
    [InlineData(1, -2, -2, 2, 4)]
    public void Distance_KnownPairs(int q1, int r1, int q2, int r2, int expected)
    {
        Assert.Equal(expected, HexCoord.Distance(new HexCoord(q1, r1), new HexCoord(q2, r2)));
    }

    [Fact]
    public void Neighbors_AreSixDistinctCells_AllAtDistanceOne()
    {
        var center = new HexCoord(2, -1);
        var neighbors = center.Neighbors().ToList();

        Assert.Equal(6, neighbors.Count);
        Assert.Equal(6, neighbors.Distinct().Count());
        Assert.All(neighbors, n => Assert.Equal(1, center.DistanceTo(n)));
    }

    [Fact]
    public void Neighbors_OrderIsStable_GoldenSequence()
    {
        // 固定方向顺序是确定性遍历的基准，改动即视为破坏性变更。
        var expected = new[]
        {
            new HexCoord(1, 0),
            new HexCoord(1, -1),
            new HexCoord(0, -1),
            new HexCoord(-1, 0),
            new HexCoord(-1, 1),
            new HexCoord(0, 1),
        };

        Assert.Equal(expected, new HexCoord(0, 0).Neighbors().ToArray());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(6)]
    public void Neighbor_InvalidDirection_Throws(int direction)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HexCoord(0, 0).Neighbor(direction));
    }

    [Fact]
    public void AddAndSubtract_RoundTrip()
    {
        var a = new HexCoord(2, -3);
        var b = new HexCoord(-1, 4);
        Assert.Equal(a, a + b - b);
    }
}
