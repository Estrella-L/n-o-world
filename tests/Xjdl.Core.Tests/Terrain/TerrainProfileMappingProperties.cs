using CsCheck;
using Xjdl.Core.State;
using Xjdl.Core.Terrain;

namespace Xjdl.Core.Tests.Terrain;

/// <summary>
/// 地形属性映射的属性测试（CsCheck，一属性一测试，至少 100 次迭代）。
/// </summary>
public class TerrainProfileMappingProperties
{
    private static readonly TerrainType[] AllTerrains =
        (TerrainType[])System.Enum.GetValues(typeof(TerrainType));

    private static readonly UnitClass[] AllClasses =
        (UnitClass[])System.Enum.GetValues(typeof(UnitClass));

    // 单种地形档：随机移动消耗 / 防御 DRM / 禁入兵种子集 / 进入即停。
    private static readonly Gen<TerrainSpec> GenSpec =
        from moveCost in Gen.Int[0, 10]
        from defensiveDrm in Gen.Int[-4, 4]
        from forbiddenMask in Gen.Int[0, (1 << 4) - 1]
        from enterAndStop in Gen.Bool
        select new TerrainSpec(
            moveCost,
            defensiveDrm,
            (IReadOnlyList<UnitClass>)AllClasses
                .Where((_, i) => (forbiddenMask & (1 << i)) != 0)
                .ToList(),
            enterAndStop);

    // 随机 TerrainProfile：为每种地形类型各随机生成一份 TerrainSpec。
    private static readonly Gen<TerrainProfile> GenProfile =
        GenSpec.Array[AllTerrains.Length].Select(specs =>
        {
            var dict = new Dictionary<TerrainType, TerrainSpec>();
            for (var i = 0; i < AllTerrains.Length; i++)
            {
                dict[AllTerrains[i]] = specs[i];
            }

            return new TerrainProfile(dict);
        });

    private static readonly Gen<UnitClass> GenClass =
        Gen.Int[0, AllClasses.Length - 1].Select(i => AllClasses[i]);

    // Feature: core-rules-engine, Property 48: 地形属性映射一致
    // For any terrain type, TerrainSystem's returned move cost and defensive DRM
    // equal the values in the current terrainProfile config.
    // **Validates: Requirements 15.1**
    [Fact]
    public void Property48_TerrainAttributesMatchProfile()
    {
        GenProfile.Select(GenClass)
            .Sample(
                t =>
                {
                    var (profile, cls) = t;

                    foreach (var terrain in AllTerrains)
                    {
                        var spec = profile.Terrains[terrain];

                        Assert.Equal(spec.MoveCost, TerrainSystem.MoveCost(profile, terrain, cls));
                        Assert.Equal(spec.DefensiveDrm, TerrainSystem.DefensiveDrm(profile, terrain));
                    }
                },
                iter: 100);
    }
}
