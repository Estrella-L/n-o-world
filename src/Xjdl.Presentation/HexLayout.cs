using Xjdl.Core.Hex;
using Xjdl.Game.Presentation.ViewModels;

namespace Xjdl.Game.Presentation;

/// <summary>
/// 六角轴向坐标（<see cref="HexCoord"/>）↔ 屏幕像素（<see cref="Vector2D"/>）的纯换算工具。
/// 采用 <b>pointy-top（尖顶）</b> 布局：其六个直接相邻格含正东、正西，与
/// <see cref="HexCoord.Directions"/>（<c>(1,0)</c> 自东开始）一致（Req 3.2/3.3）。
/// 换算全部使用浮点（表现层不属于 Core，允许浮点）；Core 仍保持整数坐标。
/// 不 <c>using Godot</c>，从而可脱离引擎编译并被属性测试覆盖。
/// </summary>
public sealed class HexLayout
{
    private static readonly double Sqrt3 = Math.Sqrt(3.0);

    private readonly double _size;
    private readonly Vector2D _origin;

    /// <summary>
    /// 构造六角布局。
    /// </summary>
    /// <param name="size">六角外接圆半径（像素），必须为正。</param>
    /// <param name="origin"><c>(0,0)</c> 格中心对应的像素坐标。</param>
    public HexLayout(double size, Vector2D origin)
    {
        if (size <= 0 || double.IsNaN(size) || double.IsInfinity(size))
        {
            throw new ArgumentOutOfRangeException(
                nameof(size), size, "六角尺寸必须为正的有限数。");
        }

        _size = size;
        _origin = origin;
    }

    /// <summary>
    /// 轴向坐标 → 格中心像素（pointy-top，Req 3.2）。
    /// <code>
    /// x = size * (√3 * q + √3/2 * r);  y = size * (3/2 * r)
    /// </code>
    /// </summary>
    public Vector2D CenterOf(HexCoord coord)
    {
        var x = _size * (Sqrt3 * coord.Q + Sqrt3 / 2.0 * coord.R);
        var y = _size * (3.0 / 2.0 * coord.R);
        return new Vector2D(_origin.X + x, _origin.Y + y);
    }

    /// <summary>
    /// 像素 → 轴向坐标：先算分数立方坐标，再 cube-round 到最近整格（Req 3.2）。
    /// 与 <see cref="CenterOf"/> 互逆，保证 <c>CoordAt(CenterOf(c)) == c</c>（Property 1）。
    /// </summary>
    public HexCoord CoordAt(Vector2D pixel)
    {
        var px = (pixel.X - _origin.X) / _size;
        var py = (pixel.Y - _origin.Y) / _size;

        // pointy-top 逆变换（CenterOf 的逆）：
        //   q = (√3/3 * px) - (1/3 * py);  r = (2/3 * py)
        var q = Sqrt3 / 3.0 * px - 1.0 / 3.0 * py;
        var r = 2.0 / 3.0 * py;

        return CubeRound(q, r);
    }

    /// <summary>
    /// 六角顶点像素序列（供 MapRenderer 画多边形）。pointy-top 布局，顶点从正上方开始，
    /// 每 60° 一个，共 6 个。
    /// </summary>
    public IReadOnlyList<Vector2D> CornersOf(HexCoord coord)
    {
        var center = CenterOf(coord);
        var corners = new Vector2D[6];
        for (var i = 0; i < 6; i++)
        {
            // pointy-top：起始角为 -90°（正上方），每格 60°。
            var angle = Math.PI / 180.0 * (60.0 * i - 90.0);
            corners[i] = new Vector2D(
                center.X + _size * Math.Cos(angle),
                center.Y + _size * Math.Sin(angle));
        }

        return corners;
    }

    /// <summary>
    /// 分数轴向坐标经立方坐标四舍五入到最近整格：分别舍入 q/r/s，
    /// 校正偏差最大的分量以恢复 <c>q + r + s == 0</c> 约束。
    /// </summary>
    private static HexCoord CubeRound(double q, double r)
    {
        var s = -q - r;

        var rq = Math.Round(q, MidpointRounding.AwayFromZero);
        var rr = Math.Round(r, MidpointRounding.AwayFromZero);
        var rs = Math.Round(s, MidpointRounding.AwayFromZero);

        var dq = Math.Abs(rq - q);
        var dr = Math.Abs(rr - r);
        var ds = Math.Abs(rs - s);

        if (dq > dr && dq > ds)
        {
            rq = -rr - rs;
        }
        else if (dr > ds)
        {
            rr = -rq - rs;
        }

        return new HexCoord((int)rq, (int)rr);
    }
}
