using CsCheck;
using Xjdl.Core.Hex;
using Xjdl.Game.Presentation;
using Xjdl.Game.Presentation.ViewModels;

namespace Xjdl.Presentation.Tests;

/// <summary>
/// <see cref="HexLayout"/> 坐标换算的属性测试。
/// </summary>
public sealed class HexLayoutRoundTripProperties
{
    // 合法轴向坐标：q、r ∈ [-1000, 1000]，覆盖远离原点的大坐标以暴露
    // cube-round 的四舍五入分支与浮点累积误差。
    private static readonly Gen<HexCoord> GenCoord =
        Gen.Select(Gen.Int[-1000, 1000], Gen.Int[-1000, 1000], (q, r) => new HexCoord(q, r));

    // 六角尺寸（外接圆半径，像素）：正的有限数，覆盖不同缩放。
    private static readonly Gen<double> GenSize = Gen.Double[1.0, 256.0];

    // 原点像素偏移：任意有限偏移，验证平移不破坏往返一致。
    private static readonly Gen<Vector2D> GenOrigin =
        Gen.Select(Gen.Double[-500.0, 500.0], Gen.Double[-500.0, 500.0], (x, y) => new Vector2D(x, y));

    /// <summary>
    /// Feature: godot-presentation-layer, Property 1: 坐标换算往返一致
    ///
    /// 对任意合法 <see cref="HexCoord"/> c 与任意合法布局（尺寸/原点），
    /// 先经 <see cref="HexLayout.CenterOf"/> 换算为格中心像素、再经
    /// <see cref="HexLayout.CoordAt"/> 反解，恒返回原坐标 c。
    ///
    /// **Validates: Requirements 3.2, 3.3**
    /// </summary>
    [Fact]
    public void CoordAt_OfCenterOf_ReturnsOriginalCoord()
    {
        Gen.Select(GenCoord, GenSize, GenOrigin)
            .Sample(
                t =>
                {
                    var (coord, size, origin) = t;
                    var layout = new HexLayout(size, origin);

                    var roundTrip = layout.CoordAt(layout.CenterOf(coord));

                    Assert.Equal(coord, roundTrip);
                },
                iter: 1000);
    }
}
