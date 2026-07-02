using CsCheck;
using Xjdl.Core.Modifiers;
using Xjdl.Core.State;

namespace Xjdl.Core.Tests.Modifiers;

// Feature: core-rules-engine, Property 36: 按来源精确抵消
public class ModifierNegateProperties
{
    // 所有移档来源取值。
    private static readonly ModifierSource[] Sources =
    {
        ModifierSource.Support,
        ModifierSource.Night,
        ModifierSource.Card,
        ModifierSource.Doctrine,
    };

    /// <summary>任意移档来源（均匀取样）。</summary>
    private static readonly Gen<ModifierSource> GenSource =
        Gen.Int[0, Sources.Length - 1].Select(i => Sources[i]);

    /// <summary>
    /// 单条移档：Delta 覆盖正/负/零（含超出 ±2 的极端值），来源任意。
    /// </summary>
    private static readonly Gen<ColumnShift> GenShift =
        from delta in Gen.Int[-4, 4]
        from source in GenSource
        select new ColumnShift(delta, source);

    /// <summary>混合来源的移档集合（0..12 条，来源随机混合）。</summary>
    private static readonly Gen<IReadOnlyList<ColumnShift>> GenMixedShifts =
        from n in Gen.Int[0, 12]
        from shifts in GenShift.List[n]
        select (IReadOnlyList<ColumnShift>)shifts;

    /// <summary>
    /// Property 36: 按来源精确抵消。
    /// 对任意混合来源的移档集合与任意目标来源，<see cref="ModifierPipeline.Negate"/>：
    ///  1) 结果恰好等于原集合中 Source != target 的移档（保持原相对顺序）；
    ///  2) 结果中不再包含任何 Source == target 的移档；
    ///  3) 其余来源的移档保持原样，数量与内容均不变。
    /// **Validates: Requirements 17.3**
    /// </summary>
    [Fact]
    public void Negate_RemovesOnlyMatchingSource_PreservingOthersInOrder()
    {
        Gen.Select(GenMixedShifts, GenSource).Sample((shifts, target) =>
        {
            var result = ModifierPipeline.Negate(shifts, target);

            // 期望：原集合中所有非目标来源的移档，保持原相对顺序。
            var expected = shifts.Where(s => s.Source != target).ToList();

            // 1) 结果与期望逐项相等（顺序保留）。
            var matchesExpected =
                result.Count == expected.Count &&
                result.SequenceEqual(expected);

            // 2) 结果中不含目标来源。
            var noneMatchTarget = result.All(s => s.Source != target);

            // 3) 目标来源被全部移除（数量差 == 原集合中目标来源数量）。
            var removedCount = shifts.Count(s => s.Source == target);
            var countConsistent = result.Count == shifts.Count - removedCount;

            return matchesExpected && noneMatchTarget && countConsistent;
        }, iter: 1000);
    }
}
