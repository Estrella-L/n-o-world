namespace Xjdl.Core.Hex;

/// <summary>
/// 六角几何的静态范围查询服务，扩展既有 <see cref="HexCoord"/>。
/// 见 docs/01-战斗机制.md〈地图与格子〉与 design.md〈HexGrid〉。
/// 返回顺序按 <c>(Q, R)</c> 字典序固定，保证确定性遍历（docs/04 · 第 2.4 条）。
/// </summary>
public static class HexGrid
{
    /// <summary>
    /// 半径 <paramref name="radius"/> 内（含）的所有格，即到 <paramref name="center"/>
    /// 六角距离 <c>&lt;= radius</c> 的全部格，按 <c>(Q, R)</c> 字典序返回（Req 1.5）。
    /// </summary>
    /// <param name="center">中心格。</param>
    /// <param name="radius">范围半径，须 <c>&gt;= 0</c>；为 0 时仅返回 <paramref name="center"/>。</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="radius"/> 小于 0。</exception>
    public static IReadOnlyList<HexCoord> Range(HexCoord center, int radius)
    {
        if (radius < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(radius), radius, "半径必须为非负数。");
        }

        // 轴向遍历：q 由小到大，r 在合法区间内由小到大，
        // 天然产出 (Q, R) 字典序，无需额外排序。
        var result = new List<HexCoord>();
        for (var dq = -radius; dq <= radius; dq++)
        {
            var rMin = Math.Max(-radius, -dq - radius);
            var rMax = Math.Min(radius, -dq + radius);
            for (var dr = rMin; dr <= rMax; dr++)
            {
                result.Add(new HexCoord(center.Q + dq, center.R + dr));
            }
        }

        return result;
    }

    /// <summary>
    /// 恰好距离 <c>== radius</c> 的环（V+1 圈用于迷雾未明圈，Req 14.3），
    /// 按 <c>(Q, R)</c> 字典序返回。<paramref name="radius"/> 为 0 时仅返回 <paramref name="center"/>。
    /// </summary>
    /// <param name="center">中心格。</param>
    /// <param name="radius">环半径，须 <c>&gt;= 0</c>。</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="radius"/> 小于 0。</exception>
    public static IReadOnlyList<HexCoord> Ring(HexCoord center, int radius)
    {
        if (radius < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(radius), radius, "半径必须为非负数。");
        }

        if (radius == 0)
        {
            return [center];
        }

        // 复用范围遍历并筛出边界格，保持 (Q, R) 字典序。
        var result = new List<HexCoord>();
        for (var dq = -radius; dq <= radius; dq++)
        {
            var rMin = Math.Max(-radius, -dq - radius);
            var rMax = Math.Min(radius, -dq + radius);
            for (var dr = rMin; dr <= rMax; dr++)
            {
                // 立方距离 = (|dq| + |dr| + |dq + dr|) / 2；边界即等于 radius。
                var dist = (Math.Abs(dq) + Math.Abs(dr) + Math.Abs(dq + dr)) / 2;
                if (dist == radius)
                {
                    result.Add(new HexCoord(center.Q + dq, center.R + dr));
                }
            }
        }

        return result;
    }
}
