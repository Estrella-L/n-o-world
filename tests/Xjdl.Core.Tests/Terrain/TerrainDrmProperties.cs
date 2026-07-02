using System;
using CsCheck;
using Xjdl.Core.State;
using Xjdl.Core.Terrain;

namespace Xjdl.Core.Tests.Terrain;

// Feature: core-rules-engine, Property 50: DRM 归属唯一来源
public class TerrainDrmProperties
{
    // 任意占位方所在格 DRM（含正负与零，覆盖绝对值比较分支）。
    private static readonly Gen<int> GenDrm = Gen.Int[-6, 6];

    // 任意防守方索引（0/1）。
    private static readonly Gen<int> GenSide = Gen.Int[0, 1];

    // 任意 BattleTerrain：随机 Side0Drm、Side1Drm、DefenderSide ∈ {0,1}。
    private static readonly Gen<BattleTerrain> GenTerrain =
        from d0 in GenDrm
        from d1 in GenDrm
        from def in GenSide
        select new BattleTerrain(d0, d1, def);

    // 任意交战表类型。
    private static readonly Gen<CombatTable> GenTable =
        Gen.Int[0, 2].Select(i => (CombatTable)i);

    // 劣势方索引：null（1:1 无固定劣势方）或 0/1。
    private static readonly Gen<int?> GenDisadvantaged =
        Gen.OneOf(
            Gen.Const((int?)null),
            GenSide.Select(s => (int?)s));

    /// <summary>
    /// Property 50: DRM 归属唯一来源。
    /// 对任意 <see cref="BattleTerrain"/>、交战表与劣势方索引：
    ///  1) 表一（RegularAttack）→ 守方所在格 DRM（DrmOf(DefenderSide)），忽略劣势方参数；
    ///  2) 表二/表三（MutualAttack/Encounter）且劣势方 == k → 劣势方所在格 DRM（DrmOf(k)），不取优势方；
    ///  3) 表二/表三且劣势方为 null（1:1 无固定劣势方）→ 双方 DRM 中绝对值较大者；绝对值相等时确定性取 side0。
    /// 无论何种分支，结果必等于某一单一占位方所在格的 DRM，绝不叠加。
    /// **Validates: Requirements 15.4, 15.5, 15.6, 15.8**
    /// </summary>
    [Fact]
    public void ResolveDrm_ComesFromSingleOccupantCell()
    {
        Gen.Select(GenTable, GenTerrain, GenDisadvantaged).Sample((table, terrain, disadvantaged) =>
        {
            var actual = TerrainSystem.ResolveDrm(table, terrain, disadvantaged);

            int expected;
            if (table == CombatTable.RegularAttack)
            {
                // Req 15.4：表一唯一取守方所在格 DRM。
                expected = terrain.DrmOf(terrain.DefenderSide);
            }
            else if (disadvantaged is int k)
            {
                // Req 15.5：表二/表三取劣势方所在格 DRM，不取优势方。
                expected = terrain.DrmOf(k);
            }
            else
            {
                // Req 15.6：1:1 无固定劣势方，取绝对值较大者；相等取 side0。
                expected = Math.Abs(terrain.Side0Drm) >= Math.Abs(terrain.Side1Drm)
                    ? terrain.Side0Drm
                    : terrain.Side1Drm;
            }

            // Req 15.8：结果必来自单一占位方所在格，绝不叠加（等于 side0 或 side1 之一）。
            var fromSingleCell = actual == terrain.Side0Drm || actual == terrain.Side1Drm;

            return actual == expected && fromSingleCell;
        }, iter: 1000);
    }
}
