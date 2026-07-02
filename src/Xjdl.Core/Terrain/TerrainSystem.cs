using Xjdl.Core.State;

namespace Xjdl.Core.Terrain;

/// <summary>
/// 一场战斗中「占位方所在格」防御 DRM 的最小快照（Req 15.4–15.8）。
/// 两个参战占位方以索引 <c>0</c>/<c>1</c> 标识，各自持有其所在格的防御 DRM
/// （值由 <see cref="TerrainSystem.DefensiveDrm"/> 预取，骰轴、非档轴）。
/// <see cref="DefenderSide"/> 指出表一中的防守方索引，仅在
/// <see cref="CombatTable.RegularAttack"/> 下有意义；表二/表三由外部传入的
/// 劣势方索引决定取哪一侧。<see cref="TerrainSystem.ResolveDrm"/> 只做「选取单一占位方」，
/// 绝不叠加多块地形（Req 15.8）。
/// </summary>
/// <param name="Side0Drm">索引 0 一方所在格的防御 DRM。</param>
/// <param name="Side1Drm">索引 1 一方所在格的防御 DRM。</param>
/// <param name="DefenderSide">表一中防守方的索引（0 或 1）；其他表忽略此字段。</param>
public readonly record struct BattleTerrain(int Side0Drm, int Side1Drm, int DefenderSide)
{
    /// <summary>取指定索引（0/1）一方所在格的防御 DRM。</summary>
    public int DrmOf(int side) => side switch
    {
        0 => Side0Drm,
        1 => Side1Drm,
        _ => throw new ArgumentOutOfRangeException(
            nameof(side), side, "占位方索引必须为 0 或 1。"),
    };
}

/// <summary>
/// 地形子系统（Req 15）：提供移动消耗（档轴外的移动点消耗）、防御 DRM（骰轴）、
/// 兵种进入限制，以及一场战斗中「取哪一个占位方所在格 DRM」的裁定。
/// 全程整数运算、数据驱动（值取自注入的 <see cref="TerrainProfile"/>），
/// 引擎无关、确定性，契合 docs/04 纯核心约定。
/// </summary>
public static class TerrainSystem
{
    /// <summary>
    /// 某地形对指定兵种的移动消耗（Req 15.1/15.2）。值取自 <paramref name="profile"/>，
    /// 不硬编码。<paramref name="cls"/> 作为兵种维度保留，便于未来按兵种细分消耗，
    /// 并与 <see cref="CanEnter"/> 保持一致的签名形态。
    /// </summary>
    public static int MoveCost(TerrainProfile profile, TerrainType t, UnitClass cls)
    {
        _ = cls;
        return Spec(profile, t).MoveCost;
    }

    /// <summary>
    /// 某地形的防御 DRM（骰轴，Req 15.3）。作用于 3D6 读数，
    /// 不计入 ±2 移档封顶。值取自 <paramref name="profile"/>。
    /// </summary>
    public static int DefensiveDrm(TerrainProfile profile, TerrainType t)
        => Spec(profile, t).DefensiveDrm;

    /// <summary>
    /// 指定兵种是否可进入某地形（Req 15.7）。地形档中列入
    /// <see cref="TerrainSpec.ForbiddenClasses"/> 的兵种（如沼泽禁重型）不可进入。
    /// </summary>
    public static bool CanEnter(TerrainProfile profile, TerrainType t, UnitClass cls)
        => !Spec(profile, t).ForbiddenClasses.Contains(cls);

    /// <summary>
    /// 裁定一场战斗取哪一个占位方所在格的防御 DRM（Req 15.4/15.5/15.6/15.8）：
    /// <list type="bullet">
    /// <item>表一（<see cref="CombatTable.RegularAttack"/>）：取守方所在格 DRM。</item>
    /// <item>表二/表三（对攻/遭遇）：取劣势方所在格 DRM，不取优势方。</item>
    /// <item>火力比 1:1 无固定劣势方（<paramref name="disadvantagedSide"/> 为 <c>null</c>）：
    /// 取双方所在格 DRM 中绝对值较大者。</item>
    /// </list>
    /// 始终只返回单一占位方的 DRM，绝不叠加多块地形。
    /// </summary>
    /// <param name="table">交战表类型。</param>
    /// <param name="terrain">两个占位方所在格 DRM 的快照。</param>
    /// <param name="disadvantagedSide">
    /// 表二/表三的劣势方索引（0/1）；1:1 无固定劣势方时为 <c>null</c>。表一忽略此参数。
    /// </param>
    public static int ResolveDrm(CombatTable table, BattleTerrain terrain, int? disadvantagedSide)
    {
        switch (table)
        {
            case CombatTable.RegularAttack:
                // 表一：唯一取守方所在格 DRM（Req 15.4）。
                return terrain.DrmOf(terrain.DefenderSide);

            case CombatTable.MutualAttack:
            case CombatTable.Encounter:
                if (disadvantagedSide is int side)
                {
                    // 表二/表三：取劣势方所在格 DRM，不取优势方（Req 15.5）。
                    return terrain.DrmOf(side);
                }

                // 1:1 无固定劣势方：取绝对值较大者（Req 15.6）；绝对值相等时确定性取索引 0。
                return Math.Abs(terrain.Side0Drm) >= Math.Abs(terrain.Side1Drm)
                    ? terrain.Side0Drm
                    : terrain.Side1Drm;

            default:
                throw new ArgumentOutOfRangeException(
                    nameof(table), table, "未知的交战表类型。");
        }
    }

    /// <summary>按地形类型取其配置档；缺失即快速失败（fail-fast，Req 20.2）。</summary>
    private static TerrainSpec Spec(TerrainProfile profile, TerrainType t)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!profile.Terrains.TryGetValue(t, out var spec))
        {
            throw new ArgumentException($"地形档缺少地形类型 {t} 的配置。", nameof(t));
        }

        return spec;
    }
}
