using CsCheck;
using Xjdl.Core.State;
using Xjdl.Core.Terrain;

namespace Xjdl.Core.Tests.Terrain;

/// <summary>
/// 地形兵种进入限制的属性测试（CsCheck，一属性一测试，至少 100 次迭代）。
/// </summary>
public class TerrainRestrictionProperties
{
    private static readonly TerrainType[] AllTerrains =
        (TerrainType[])System.Enum.GetValues(typeof(TerrainType));

    private static readonly UnitClass[] AllClasses =
        (UnitClass[])System.Enum.GetValues(typeof(UnitClass));

    // 单种地形档：移动消耗 / 防御 DRM 随机，禁入兵种为 AllClasses 的任意子集，
    // 进入即停随机（如河流）。
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

    // 随机 TerrainProfile：为每种地形类型各随机生成一份 TerrainSpec，
    // 因此每种地形拥有各自独立的 ForbiddenClasses 集合。
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

    // Feature: core-rules-engine, Property 51: 地形单位限制
    // For any unit-class + terrain combination, TerrainSystem applies the
    // terrain-table restrictions: CanEnter is false exactly when the class is
    // listed in that terrain's TerrainSpec.ForbiddenClasses (e.g. heavy units
    // cannot enter swamp).
    // **Validates: Requirements 15.7**
    [Fact]
    public void Property51_TerrainForbiddenClassesGovernCanEnter()
    {
        GenProfile.Sample(
            profile =>
            {
                foreach (var terrain in AllTerrains)
                {
                    var forbidden = profile.Terrains[terrain].ForbiddenClasses;
                    foreach (var cls in AllClasses)
                    {
                        var expected = !forbidden.Contains(cls);
                        Assert.Equal(expected, TerrainSystem.CanEnter(profile, terrain, cls));
                    }
                }
            },
            iter: 100);
    }
}
