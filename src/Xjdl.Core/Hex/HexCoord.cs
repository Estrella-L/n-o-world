namespace Xjdl.Core.Hex;

/// <summary>
/// 六角格轴向坐标（axial: q, r）。见 docs/01-战斗机制.md〈地图与格子〉。
/// 立方坐标第三轴 s = -q - r，恒满足 q + r + s = 0，用于统一计算相邻与距离。
/// 值类型、不可变，天然可序列化，契合确定性核心（docs/04 · 第 2、5 节）。
/// 约定：类型名不与命名空间末段同名（故用 HexCoord 而非 Hex），避免 CS0118。
/// </summary>
public readonly record struct HexCoord(int Q, int R)
{
    /// <summary>立方坐标第三轴，由 q、r 推导。</summary>
    public int S => -Q - R;

    /// <summary>
    /// 六个相邻方向（轴向增量）。顺序固定，作为确定性遍历的稳定基准
    /// （docs/04 · 第 2.4 条：结算次序无关，需要顺序时用固定顺序）。
    /// 自东开始，逆时针排列。
    /// </summary>
    public static readonly IReadOnlyList<HexCoord> Directions =
    [
        new HexCoord(1, 0),
        new HexCoord(1, -1),
        new HexCoord(0, -1),
        new HexCoord(-1, 0),
        new HexCoord(-1, 1),
        new HexCoord(0, 1),
    ];

    public static HexCoord operator +(HexCoord a, HexCoord b) => new(a.Q + b.Q, a.R + b.R);

    public static HexCoord operator -(HexCoord a, HexCoord b) => new(a.Q - b.Q, a.R - b.R);

    /// <summary>六角距离（相邻为 1）。</summary>
    public static int Distance(HexCoord a, HexCoord b)
    {
        var dq = a.Q - b.Q;
        var dr = a.R - b.R;
        return (Math.Abs(dq) + Math.Abs(dr) + Math.Abs(dq + dr)) / 2;
    }

    /// <summary>到另一格的六角距离。</summary>
    public int DistanceTo(HexCoord other) => Distance(this, other);

    /// <summary>指定方向（0..5，对应 <see cref="Directions"/>）的相邻格。</summary>
    public HexCoord Neighbor(int direction)
    {
        if (direction is < 0 or > 5)
        {
            throw new ArgumentOutOfRangeException(
                nameof(direction), direction, "方向必须在 0..5 之间。");
        }

        return this + Directions[direction];
    }

    /// <summary>按固定方向顺序返回全部 6 个相邻格。</summary>
    public IEnumerable<HexCoord> Neighbors()
    {
        foreach (var d in Directions)
        {
            yield return this + d;
        }
    }

    public override string ToString() => $"({Q}, {R})";
}
