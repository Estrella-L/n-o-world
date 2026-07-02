using Xjdl.Core.Hex;
using Xjdl.Core.State;

namespace Xjdl.Core.Fog;

/// <summary>
/// 战争迷雾子系统：按敌方单位到最近己方观察单位的六角距离 <c>d</c> 分段可见度（Req 14.1–14.4）。
/// 纯函数、整数运算、确定性：遍历按稳定 <see cref="UnitId"/> 排序，结果与集合原始顺序无关（Req 2.6）。
/// <list type="bullet">
/// <item><description><c>d &lt;= V</c> → <see cref="Visibility.Identified"/>（暴露兵种与当前攻/防/韧性/堆叠，Req 14.2）。</description></item>
/// <item><description><c>BlipRingEnabled</c> 且 <c>d == V + 1</c> → <see cref="Visibility.Spotted"/>（仅暴露有敌，Req 14.3）。</description></item>
/// <item><description>否则 → <see cref="Visibility.Hidden"/>，不留残留标记（Req 14.4）。</description></item>
/// </list>
/// 夜晚回合中不持 <see cref="NightFlags.NightVisionKeep"/> 的观察单位，其有效视野为
/// <c>max(1, floor(V / nightVisionDivisor))</c>（Req 18.2）。
/// </summary>
public static class FogSystem
{
    /// <summary>
    /// 计算 <paramref name="viewer"/> 方对每个敌方单位的可见度（Req 14.1）。
    /// </summary>
    /// <param name="s">当前全场状态快照。</param>
    /// <param name="viewer">观察方阵营。</param>
    /// <param name="cfg">战争迷雾配置（是否启用 V+1 环、夜晚视野除数）。</param>
    /// <returns>以敌方 <see cref="UnitId"/> 为键、可见度为值的字典；按稳定 id 顺序构建以保证确定性。</returns>
    public static IReadOnlyDictionary<UnitId, Visibility> Compute(
        GameState s, Side viewer, FogConfig cfg)
    {
        ArgumentNullException.ThrowIfNull(s);
        ArgumentNullException.ThrowIfNull(cfg);

        var isNight = s.Phase == DayNightPhase.Night;

        // 己方观察单位：预计算各自「有效视野 V」（夜晚按 Req 18.2 折减）。
        var observers = s.Units
            .Where(u => u.Owner == viewer)
            .OrderBy(u => u.Id.Value)
            .Select(u => (u.Position, Vision: EffectiveVision(u, isNight, cfg)))
            .ToList();

        var result = new Dictionary<UnitId, Visibility>();

        // 敌方单位按稳定 id 排序遍历，保证插入顺序确定（Req 2.6）。
        foreach (var enemy in s.Units.Where(u => u.Owner != viewer).OrderBy(u => u.Id.Value))
        {
            result[enemy.Id] = Classify(enemy.Position, observers, cfg);
        }

        return result;
    }

    /// <summary>
    /// 观察单位的有效视野：夜晚且不持 <see cref="NightFlags.NightVisionKeep"/> 时
    /// 除以 <see cref="FogConfig.NightVisionDivisor"/> 向下取整、最低 1（Req 18.2）。
    /// </summary>
    private static int EffectiveVision(UnitState observer, bool isNight, FogConfig cfg)
    {
        if (!isNight || observer.Flags.HasFlag(NightFlags.NightVisionKeep))
        {
            return observer.Vision;
        }

        // 除数非正视为无效折减，退化为不折减，避免除零（fail-safe）。
        return cfg.NightVisionDivisor > 0
            ? Math.Max(1, observer.Vision / cfg.NightVisionDivisor)
            : observer.Vision;
    }

    /// <summary>
    /// 依据到最近观察单位的距离与各观察者有效视野，判定单个敌方单位的可见度。
    /// <see cref="Visibility.Identified"/> 优先于 <see cref="Visibility.Spotted"/>，
    /// 二者皆不满足则 <see cref="Visibility.Hidden"/>。
    /// </summary>
    private static Visibility Classify(
        HexCoord target,
        IReadOnlyList<(HexCoord Position, int Vision)> observers,
        FogConfig cfg)
    {
        var spotted = false;

        foreach (var (position, vision) in observers)
        {
            var d = HexCoord.Distance(position, target);

            if (d <= vision)
            {
                return Visibility.Identified;
            }

            if (cfg.BlipRingEnabled && d == vision + 1)
            {
                spotted = true;
            }
        }

        return spotted ? Visibility.Spotted : Visibility.Hidden;
    }
}
