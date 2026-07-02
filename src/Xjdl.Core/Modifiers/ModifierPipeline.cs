using Xjdl.Core.State;

namespace Xjdl.Core.Modifiers;

/// <summary>
/// 一次移档修正：带来源标记，用于统一累加与按来源精确抵消。
/// <see cref="Delta"/> 为列偏移（正右负左），<see cref="Source"/> 标明产生该移档的子系统
/// （Req 17.2：移档必须带来源）。
/// </summary>
public readonly record struct ColumnShift(int Delta, ModifierSource Source);

/// <summary>
/// 统一移档管线（Req 17.1–17.3、10.2）。
/// 将来自支援/夜战/卡牌/学说的移档以「基础档 → 各来源累加 → ±2 总封顶」的固定流程合并，
/// 任何来源均不得突破 ±2 封顶；夜战豁免通过 <see cref="Negate"/> 按来源精确抵消。
/// 全程纯整数、无副作用。
/// </summary>
public static class ModifierPipeline
{
    /// <summary>±2 总封顶：所有来源累加后，最终档相对基础档不超过 ±2（Req 10.2、17.1）。</summary>
    private const int MaxShift = 2;

    /// <summary>
    /// 计算最终档：将 <paramref name="shifts"/> 中所有 <see cref="ColumnShift.Delta"/> 累加到
    /// <paramref name="baseColumn"/>，再钳制到 <c>[baseColumn - 2, baseColumn + 2]</c>。
    /// 无论来源如何组合，净移档都不会超过 ±2（Req 17.1、10.2）。
    /// </summary>
    /// <param name="baseColumn">火力比映射得到的基础档。</param>
    /// <param name="shifts">本场战斗的全部移档修正（来源任意）。</param>
    /// <returns>钳制后的最终档。</returns>
    public static int FinalColumn(int baseColumn, IReadOnlyList<ColumnShift> shifts)
    {
        ArgumentNullException.ThrowIfNull(shifts);

        var sum = 0;
        for (var i = 0; i < shifts.Count; i++)
        {
            sum += shifts[i].Delta;
        }

        var clamped = Math.Clamp(sum, -MaxShift, MaxShift);
        return baseColumn + clamped;
    }

    /// <summary>
    /// 按来源精确抵消：返回移除了所有匹配 <paramref name="source"/> 的移档后的集合，
    /// 其余来源的移档保持原样与原相对顺序（Req 17.3，用于夜战豁免）。
    /// 不修改输入集合。
    /// </summary>
    /// <param name="shifts">原始移档集合。</param>
    /// <param name="source">需要被抵消的来源。</param>
    /// <returns>不含指定来源的新集合。</returns>
    public static IReadOnlyList<ColumnShift> Negate(
        IReadOnlyList<ColumnShift> shifts,
        ModifierSource source)
    {
        ArgumentNullException.ThrowIfNull(shifts);

        var result = new List<ColumnShift>(shifts.Count);
        for (var i = 0; i < shifts.Count; i++)
        {
            if (shifts[i].Source != source)
            {
                result.Add(shifts[i]);
            }
        }

        return result;
    }
}
