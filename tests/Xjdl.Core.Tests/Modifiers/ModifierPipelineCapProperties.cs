using CsCheck;
using Xjdl.Core.Modifiers;
using Xjdl.Core.State;

namespace Xjdl.Core.Tests.Modifiers;

// Feature: core-rules-engine, Property 35: ±2 移档总封顶
public class ModifierPipelineCapProperties
{
    /// <summary>±2 总封顶常量（与 <see cref="ModifierPipeline"/> 内部保持一致）。</summary>
    private const int MaxShift = 2;

    /// <summary>移档来源：Support/Night/Card/Doctrine 全体均匀取样。</summary>
    private static readonly Gen<ModifierSource> GenSource =
        Gen.Int[0, 3].Select(i => (ModifierSource)i);

    /// <summary>
    /// 单条 <see cref="ColumnShift"/>：来源任意、Delta 覆盖大幅正负值（远超 ±2），
    /// 以验证任何单一来源都无法突破封顶。范围有界以避免累加溢出。
    /// </summary>
    private static readonly Gen<ColumnShift> GenColumnShift =
        from delta in Gen.Int[-1000, 1000]
        from source in GenSource
        select new ColumnShift(delta, source);

    /// <summary>任意长度（0..24）的移档集合，来源与幅度随机混合。</summary>
    private static readonly Gen<IReadOnlyList<ColumnShift>> GenShifts =
        from n in Gen.Int[0, 24]
        from shifts in GenColumnShift.List[n]
        select (IReadOnlyList<ColumnShift>)shifts;

    /// <summary>基础档：覆盖常见档位区间的任意整数。</summary>
    private static readonly Gen<int> GenBaseColumn = Gen.Int[-20, 20];

    /// <summary>
    /// Property 35: ±2 移档总封顶。
    /// 对任意 baseColumn 与任意（Support/Night/Card/Doctrine 混合）移档集合：
    /// FinalColumn(baseColumn, shifts) == baseColumn + Clamp(Σdelta, -2, +2)。
    /// 无论来源如何组合、单条幅度多大，净移档都不会突破 ±2。
    /// **Validates: Requirements 10.2, 16.6, 17.1, 19.4**
    /// </summary>
    [Fact]
    public void FinalColumn_CapsNetShiftAtPlusMinusTwo()
    {
        Gen.Select(GenBaseColumn, GenShifts).Sample((baseColumn, shifts) =>
        {
            var sum = 0;
            foreach (var s in shifts)
            {
                sum += s.Delta;
            }

            var expected = baseColumn + Math.Clamp(sum, -MaxShift, MaxShift);
            var actual = ModifierPipeline.FinalColumn(baseColumn, shifts);

            return actual == expected;
        }, iter: 1000);
    }
}
