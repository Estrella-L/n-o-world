using System.Linq;
using CsCheck;
using Xjdl.Core.Fog;
using Xjdl.Core.Hex;
using Xjdl.Core.State;
using Xjdl.Core.Tests.Support;

namespace Xjdl.Core.Tests.Fog;

// Feature: core-rules-engine, Property 45: 可见度分段函数
public class FogVisibilityProperties
{
    /// <summary>
    /// 任意 <see cref="FogConfig"/>：V+1 环开/关任意，夜晚视野除数覆盖 0（退化 fail-safe）与正常正值。
    /// </summary>
    private static readonly Gen<FogConfig> GenFogConfig =
        from blipRing in Gen.Bool
        from divisor in Gen.Int[0, 4]
        select new FogConfig(blipRing, divisor);

    /// <summary>
    /// Property 45: 可见度分段函数。
    /// 对任意合法 <see cref="GameState"/>、观察方 <paramref name="viewer"/> 与 <see cref="FogConfig"/>：
    /// 每个敌方单位恰好获得一个可见度值，由其到最近己方观察者「有效视野」的六角距离 d 决定：
    ///  1) 存在观察者使 d &lt;= V → <see cref="Visibility.Identified"/>（Req 14.2）；
    ///  2) 否则 BlipRingEnabled 且存在观察者使 d == V+1 → <see cref="Visibility.Spotted"/>（Req 14.3）；
    ///  3) 否则 → <see cref="Visibility.Hidden"/>，不留残留（Req 14.4）。
    /// 有效视野在 Phase==Night 且观察者不持 NightVisionKeep 时按 NightVisionDivisor 折减
    /// （max(1, floor(V/divisor))，divisor&lt;=0 退化为不折减）。
    /// 并验证每个敌方单位在结果中恰好出现一次（Req 14.1）。
    /// **Validates: Requirements 14.1, 14.2, 14.3, 14.4**
    /// </summary>
    [Fact]
    public void Compute_ClassifiesEachEnemyByNearestObserverDistance()
    {
        Gen.Select(Generators.GameState, Generators.Sides, GenFogConfig)
            .Sample((state, viewer, cfg) =>
            {
                var actual = FogSystem.Compute(state, viewer, cfg);

                var isNight = state.Phase == DayNightPhase.Night;

                // 独立重算：己方观察者的有效视野（复刻 Req 18.2 折减语义）。
                var observers = state.Units
                    .Where(u => u.Owner == viewer)
                    .Select(u => (u.Position, Vision: EffectiveVision(u, isNight, cfg)))
                    .ToList();

                var enemies = state.Units.Where(u => u.Owner != viewer).ToList();

                // 每个敌方单位恰好出现一次：键集合等于敌方 id 集合，且计数一致。
                var enemyIds = enemies.Select(e => e.Id).ToHashSet();
                var exactlyOnce =
                    actual.Count == enemyIds.Count &&
                    actual.Keys.All(enemyIds.Contains);

                if (!exactlyOnce)
                {
                    return false;
                }

                // 逐个敌方单位独立分类并与实现比对。
                foreach (var enemy in enemies)
                {
                    var identified = observers.Any(o =>
                        HexCoord.Distance(o.Position, enemy.Position) <= o.Vision);

                    var spotted = cfg.BlipRingEnabled && observers.Any(o =>
                        HexCoord.Distance(o.Position, enemy.Position) == o.Vision + 1);

                    var expected = identified
                        ? Visibility.Identified
                        : spotted
                            ? Visibility.Spotted
                            : Visibility.Hidden;

                    if (actual[enemy.Id] != expected)
                    {
                        return false;
                    }
                }

                return true;
            }, iter: 1000);
    }

    /// <summary>
    /// 独立复刻的有效视野计算：夜晚且不持 <see cref="NightFlags.NightVisionKeep"/> 时
    /// 除以 <see cref="FogConfig.NightVisionDivisor"/> 向下取整、最低 1；除数非正退化为不折减。
    /// </summary>
    private static int EffectiveVision(UnitState observer, bool isNight, FogConfig cfg)
    {
        if (!isNight || observer.Flags.HasFlag(NightFlags.NightVisionKeep))
        {
            return observer.Vision;
        }

        return cfg.NightVisionDivisor > 0
            ? System.Math.Max(1, observer.Vision / cfg.NightVisionDivisor)
            : observer.Vision;
    }
}
