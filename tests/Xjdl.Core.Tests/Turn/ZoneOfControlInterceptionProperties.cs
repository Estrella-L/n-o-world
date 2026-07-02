using CsCheck;
using Xjdl.Core.Hex;
using Xjdl.Core.State;
using Xjdl.Core.Turn;

namespace Xjdl.Core.Tests.Turn;

/// <summary>
/// 控制区拦截与豁免的属性测试（CsCheck，一属性一测试，至少 100 次迭代）。
/// 见 <see cref="ZoneOfControl.StopAtZoc"/>（Req 11.4/11.5）。
/// </summary>
// Feature: core-rules-engine, Property 39: 控制区拦截与豁免
public class ZoneOfControlInterceptionProperties
{
    // 有界坐标：q、r ∈ [-4, 4]，令路径与控制区格有足够概率重叠，充分覆盖拦截分支。
    private static readonly Gen<HexCoord> GenHexCoord =
        Gen.Select(Gen.Int[-4, 4], Gen.Int[-4, 4], (q, r) => new HexCoord(q, r));

    // 随机路径：长度 0..8 的 HexCoord 列表。
    private static readonly Gen<IReadOnlyList<HexCoord>> GenPath =
        Gen.Int[0, 8].SelectMany(n => GenHexCoord.List[n].Select(l => (IReadOnlyList<HexCoord>)l));

    // 随机敌方控制区格集合：0..12 个 HexCoord 去重成 set。
    private static readonly Gen<IReadOnlySet<HexCoord>> GenZocCells =
        Gen.Int[0, 12].SelectMany(n =>
            GenHexCoord.List[n].Select(l => (IReadOnlySet<HexCoord>)new HashSet<HexCoord>(l)));

    // 随机夜战标志：6 个定义位的任意组合（含/不含 IgnoreZoc）。
    private static readonly Gen<NightFlags> GenFlags =
        Gen.Int[0, 63].Select(mask => (NightFlags)mask);

    /// <summary>
    /// Property 39: 控制区拦截与豁免。
    /// 对任意移动路径、敌方控制区格集合与标志：
    ///  - 持 <see cref="NightFlags.IgnoreZoc"/>：原样返回整条路径（Req 11.5）；
    ///  - 不持 IgnoreZoc 且路径进入控制区：返回到首个控制区格（含）为止的前缀，
    ///    末格恰为该首个控制区格，其后不再有任何格（Req 11.4）；
    ///  - 不持 IgnoreZoc 且路径全程不入控制区：原样返回整条路径。
    /// **Validates: Requirements 11.4, 11.5**
    /// </summary>
    [Fact]
    public void StopAtZoc_InterceptsWithoutIgnore_PassesWithIgnore()
    {
        Gen.Select(GenPath, GenZocCells, GenFlags)
            .Sample(
                t =>
                {
                    var (path, zoc, flags) = t;
                    var result = ZoneOfControl.StopAtZoc(path, zoc, flags);

                    if (flags.HasFlag(NightFlags.IgnoreZoc))
                    {
                        // 豁免：整条路径原样返回（Req 11.5）。
                        Assert.Equal(path, result);
                        return;
                    }

                    // 首个进入的控制区格下标（-1 表示全程不入）。
                    var firstZoc = -1;
                    for (var i = 0; i < path.Count; i++)
                    {
                        if (zoc.Contains(path[i]))
                        {
                            firstZoc = i;
                            break;
                        }
                    }

                    if (firstZoc < 0)
                    {
                        // 全程未入控制区：路径不变。
                        Assert.Equal(path, result);
                    }
                    else
                    {
                        // 拦截：返回到首个控制区格（含）为止的前缀（Req 11.4）。
                        Assert.Equal(firstZoc + 1, result.Count);
                        Assert.Equal(path.Take(firstZoc + 1), result);

                        // 末格恰为首个控制区格，其后不再有任何格。
                        Assert.Equal(path[firstZoc], result[^1]);
                    }
                },
                iter: 100);
    }
}
