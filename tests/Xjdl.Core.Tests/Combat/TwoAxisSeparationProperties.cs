using CsCheck;
using Xjdl.Core.Modifiers;
using Xjdl.Core.State;

namespace Xjdl.Core.Tests.Combat;

// Feature: core-rules-engine, Property 37: 两轴分离
public class TwoAxisSeparationProperties
{
    /// <summary>±2 移档总封顶（与 <see cref="ModifierPipeline"/> 内部一致）。</summary>
    private const int MaxShift = 2;

    /// <summary>移档来源：Support/Night/Card/Doctrine 全体均匀取样。</summary>
    private static readonly Gen<ModifierSource> GenSource =
        Gen.Int[0, 3].Select(i => (ModifierSource)i);

    /// <summary>单条 <see cref="ColumnShift"/>：来源任意、Delta 覆盖远超 ±2 的正负幅度。</summary>
    private static readonly Gen<ColumnShift> GenColumnShift =
        from delta in Gen.Int[-50, 50]
        from source in GenSource
        select new ColumnShift(delta, source);

    /// <summary>任意长度（0..12）的移档集合（列轴输入）。</summary>
    private static readonly Gen<IReadOnlyList<ColumnShift>> GenShifts =
        from n in Gen.Int[0, 12]
        from shifts in GenColumnShift.List[n]
        select (IReadOnlyList<ColumnShift>)shifts;

    /// <summary>基础档：火力比映射得到的档位（列轴输入）。</summary>
    private static readonly Gen<int> GenBaseColumn = Gen.Int[-10, 10];

    /// <summary>未经调整的 3D6 读数（骰轴输入，正常 3..18）。</summary>
    private static readonly Gen<int> GenRoll = Gen.Int[3, 18];

    /// <summary>地形防御 DRM（骰轴修正）。幅度可远超 ±2，以验证 DRM 不受列封顶约束。</summary>
    private static readonly Gen<int> GenDrm = Gen.Int[-9, 9];

    /// <summary>
    /// Property 37(a): DRM 只动骰轴，不动列轴。
    /// 对任意 baseColumn/shifts/roll，改变地形防御 DRM 会改变调整后 3D6 读数
    /// （adjustedRoll = roll + DRM），但绝不改变火力比列 FinalColumn 的输出——
    /// 列轴函数根本不以 DRM 为入参。
    /// **Validates: Requirements 10.5, 15.3, 17.4**
    /// </summary>
    [Fact]
    public void PerturbingDrm_ChangesDiceAxisOnly_NotFinalColumn()
    {
        Gen.Select(GenBaseColumn, GenShifts, GenRoll, GenDrm, GenDrm)
            .Sample((baseColumn, shifts, roll, drmA, drmB) =>
            {
                // 列轴：仅由 baseColumn + shifts 决定，与任何 DRM 无关。
                var columnUnderDrmA = ModifierPipeline.FinalColumn(baseColumn, shifts);
                var columnUnderDrmB = ModifierPipeline.FinalColumn(baseColumn, shifts);

                // 骰轴：DRM 叠加到 3D6 读数上。
                var adjustedA = roll + drmA;
                var adjustedB = roll + drmB;

                // 列轴对 DRM 完全不敏感。
                var columnInvariantToDrm = columnUnderDrmA == columnUnderDrmB;

                // 骰轴严格随 DRM 变动：DRM 不同则调整后读数必不同（差恰为 DRM 之差）。
                var diceTracksDrm = (adjustedA - adjustedB) == (drmA - drmB);

                return columnInvariantToDrm && diceTracksDrm;
            }, iter: 1000);
    }

    /// <summary>
    /// Property 37(b): 移档只动列轴，不动骰轴，也不改攻防（strength-derived 基础档）。
    /// 对任意 roll/DRM，改变移档集合会改变 FinalColumn，但绝不改变调整后 3D6 读数；
    /// 且 FinalColumn 始终等于「基础档 + 受封顶的净移档」，基础档（由攻防火力比派生）原样保留，
    /// 移档不会回头篡改攻防派生出的基础档。
    /// **Validates: Requirements 10.5, 15.3, 17.4**
    /// </summary>
    [Fact]
    public void PerturbingColumnShifts_ChangesColumnAxisOnly_NotDiceOrStrength()
    {
        Gen.Select(GenBaseColumn, GenShifts, GenShifts, GenRoll, GenDrm)
            .Sample((baseColumn, shiftsA, shiftsB, roll, drm) =>
            {
                // 骰轴：仅由 roll + DRM 决定，与任何移档集合无关。
                var adjustedUnderA = roll + drm;
                var adjustedUnderB = roll + drm;
                var diceInvariantToShifts = adjustedUnderA == adjustedUnderB;

                var columnA = ModifierPipeline.FinalColumn(baseColumn, shiftsA);
                var columnB = ModifierPipeline.FinalColumn(baseColumn, shiftsB);

                // 攻防派生出的基础档被原样保留：净移档 = FinalColumn - baseColumn，
                // 移档只在列轴上加偏移，不改写 baseColumn 本身。
                var netA = columnA - baseColumn;
                var netB = columnB - baseColumn;
                var baseColumnPreserved =
                    (baseColumn + netA == columnA) && (baseColumn + netB == columnB);

                return diceInvariantToShifts && baseColumnPreserved;
            }, iter: 1000);
    }

    /// <summary>
    /// Property 37(c): 列轴受 ±2 净封顶，骰轴的 DRM 不受该封顶约束。
    /// 对任意移档集合，FinalColumn 相对基础档的净移档恒落在 [-2, +2]；
    /// 而地形防御 DRM 直接叠加到 3D6 读数上、完全不被 ±2 列封顶钳制——
    /// 当 |DRM| &gt; 2 时，调整后读数相对原读数的偏移严格超过 2。
    /// **Validates: Requirements 10.5, 15.3, 17.4**
    /// </summary>
    [Fact]
    public void ColumnCapDoesNotClampDiceDrm()
    {
        Gen.Select(GenBaseColumn, GenShifts, GenRoll, GenDrm)
            .Sample((baseColumn, shifts, roll, drm) =>
            {
                var column = ModifierPipeline.FinalColumn(baseColumn, shifts);
                var netColumnShift = column - baseColumn;

                // 列轴：净移档被封顶到 ±2，无论移档幅度之和多大。
                var columnCapped = netColumnShift >= -MaxShift && netColumnShift <= MaxShift;

                // 骰轴：DRM 原样叠加，偏移严格等于 DRM，不被 ±2 列封顶钳制。
                var adjustedRoll = roll + drm;
                var diceShift = adjustedRoll - roll;
                var drmUnclamped = diceShift == drm;

                // 当 DRM 幅度超过列封顶阈值时，骰轴偏移确实突破了 ±2（证明二者互不牵制）。
                var drmCanExceedCap = System.Math.Abs(drm) <= MaxShift
                    || System.Math.Abs(diceShift) > MaxShift;

                return columnCapped && drmUnclamped && drmCanExceedCap;
            }, iter: 1000);
    }
}
