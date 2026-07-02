using System.Collections.Generic;
using System.Linq;
using CsCheck;
using Xjdl.Core.Fog;
using Xjdl.Core.Hex;
using Xjdl.Core.State;
using Xjdl.Core.Tests.Support;
using Xjdl.Core.Turn;

namespace Xjdl.Core.Tests.Turn;

// Feature: core-rules-engine, Property 46: 可见度刷新时点
/// <summary>
/// Property 46（可见度刷新时点，Req 14.5/14.6）验证两个刷新时点的语义：
/// <list type="number">
/// <item><b>回合初冻结（Req 14.5）：</b>下令阶段所见可见度等于「以上一回合末位置计算」的回合初快照。
/// <see cref="TurnPipeline.ComputeFogSnapshot"/> 收到的 <see cref="GameState"/> 其单位位置即上一回合末落点，
/// 故快照必须逐 <see cref="Side"/> 等于对同一位置直接调用 <see cref="FogSystem.Compute"/> 的结果
/// （纯函数、确定性），并在位置不变时重复调用保持不变（冻结）。</item>
/// <item><b>阶段 2 机动后重算（Req 14.6）：</b>机动改变位置后，
/// <see cref="TurnPipeline.RecomputeFogAfterManeuver"/> 按<em>新</em>位置重算可见度：无参战强制时它逐方等于
/// 对机动后状态直接 <see cref="FogSystem.Compute"/>，且当某敌方单位由视野外进入视野内时，重算结果
/// 由 <see cref="Visibility.Hidden"/> 翻为 <see cref="Visibility.Identified"/>，而回合初快照仍反映旧（视野外）位置。</item>
/// </list>
/// </summary>
public class FogRefreshTimingProperties
{
    /// <summary>
    /// 任意 <see cref="FogConfig"/>：V+1 环开/关任意，夜晚视野除数覆盖 0（退化 fail-safe）与正常正值。
    /// </summary>
    private static readonly Gen<FogConfig> GenFogConfig =
        from blipRing in Gen.Bool
        from divisor in Gen.Int[0, 4]
        select new FogConfig(blipRing, divisor);

    /// <summary>
    /// Property 46（前半，Req 14.5）：回合初快照 == 以给定（上一回合末）位置计算的纯函数结果，且冻结（重复调用不变）。
    /// 对任意合法 <see cref="GameState"/> 与 <see cref="FogConfig"/>：
    /// <see cref="TurnPipeline.ComputeFogSnapshot"/> 的 <c>ByBlue</c>/<c>ByRed</c> 分别逐键等于
    /// <see cref="FogSystem.Compute"/>(Blue/Red)；再次调用得到完全相同的映射（下令阶段冻结语义）。
    /// **Validates: Requirements 14.5**
    /// </summary>
    [Fact]
    public void RoundStartSnapshot_EqualsPureFunctionOfPositions_AndIsFrozen()
    {
        Gen.Select(Generators.GameState, GenFogConfig)
            .Sample((state, cfg) =>
            {
                var snapshot = TurnPipeline.ComputeFogSnapshot(state, cfg);

                // 纯函数：快照逐方等于对同一位置直接调用 FogSystem.Compute。
                var byBlue = FogSystem.Compute(state, Side.Blue, cfg);
                var byRed = FogSystem.Compute(state, Side.Red, cfg);

                if (!SameView(snapshot.For(Side.Blue), byBlue) ||
                    !SameView(snapshot.For(Side.Red), byRed))
                {
                    return false;
                }

                // 冻结/确定性：位置不变时重复计算得到完全相同的可见度。
                var again = TurnPipeline.ComputeFogSnapshot(state, cfg);
                return SameView(snapshot.For(Side.Blue), again.For(Side.Blue)) &&
                       SameView(snapshot.For(Side.Red), again.For(Side.Red));
            }, iter: 200);
    }

    /// <summary>
    /// Property 46（后半，Req 14.6）：阶段 2 机动后按新位置重算。
    /// 构造场景：蓝方观察单位视野 V 固定于原点，红方单位回合初处于 <c>距离 &gt; V+1</c>（回合初快照 == Hidden，
    /// 反映上一回合末位置，Req 14.5）；机动后红方单位移动到 <c>距离 &lt;= V</c>（重算 == Identified，反映新位置，Req 14.6）。
    /// 同时验证无参战强制时重算逐方等于对机动后状态直接 <see cref="FogSystem.Compute"/>（重算是新位置的纯函数）。
    /// **Validates: Requirements 14.5, 14.6**
    /// </summary>
    [Fact]
    public void RecomputeAfterManeuver_ReflectsNewPositions_WhileRoundStartReflectedOld()
    {
        var scenario =
            from vision in Gen.Int[1, 4]
            from farExtra in Gen.Int[0, 3]   // 回合初距离 = V + 2 + farExtra > V+1 → Hidden
            from nearDist in Gen.Int[0, 4]   // 机动后距离 = min(nearDist, V) <= V → Identified
            from enemyVision in Gen.Int[1, 4]
            from cfg in GenFogConfig
            select (vision, farExtra, nearDist, enemyVision, cfg);

        var empty = (IReadOnlySet<UnitId>)new HashSet<UnitId>();

        scenario.Sample(sc =>
        {
            var (vision, farExtra, nearDist, enemyVision, cfg) = sc;

            var observerPos = new HexCoord(0, 0);
            var farPos = new HexCoord(vision + 2 + farExtra, 0);         // 距离 > V+1
            var nearPos = new HexCoord(System.Math.Min(nearDist, vision), 0); // 距离 <= V

            var observer = Unit(id: 0, Side.Blue, observerPos, vision);
            var enemyId = new UnitId(1);

            // 回合初状态：敌方处于视野外（上一回合末位置）。
            var preState = StateWith(
                observer,
                Unit(enemyId.Value, Side.Red, farPos, enemyVision));

            // 机动后状态：仅敌方位置改变到视野内（新位置）。
            var postState = StateWith(
                observer,
                Unit(enemyId.Value, Side.Red, nearPos, enemyVision));

            // 白天避免夜晚视野折减干扰（本属性聚焦刷新时点，非夜晚折减）。
            preState = preState with { Phase = DayNightPhase.Morning };
            postState = postState with { Phase = DayNightPhase.Morning };

            var roundStart = TurnPipeline.ComputeFogSnapshot(preState, cfg);
            var recomputed = TurnPipeline.RecomputeFogAfterManeuver(postState, cfg, empty);

            // Req 14.5：回合初快照反映旧（视野外）位置 → Hidden。
            if (roundStart.For(Side.Blue)[enemyId] != Visibility.Hidden)
            {
                return false;
            }

            // Req 14.6：机动后重算反映新（视野内）位置 → Identified。
            if (recomputed.For(Side.Blue)[enemyId] != Visibility.Identified)
            {
                return false;
            }

            // 重算随位置刷新而与回合初快照相异（该单位由 Hidden 翻为 Identified）。
            if (roundStart.For(Side.Blue)[enemyId] == recomputed.For(Side.Blue)[enemyId])
            {
                return false;
            }

            // 无参战强制时，重算逐方等于对机动后状态直接计算（新位置的纯函数）。
            var pureBlue = FogSystem.Compute(postState, Side.Blue, cfg);
            var pureRed = FogSystem.Compute(postState, Side.Red, cfg);
            return SameView(recomputed.For(Side.Blue), pureBlue) &&
                   SameView(recomputed.For(Side.Red), pureRed);
        }, iter: 200);
    }

    /// <summary>确定性示例：观察者视野 2，敌方由距离 4（Hidden）机动到距离 1（Identified）。</summary>
    [Fact]
    public void Example_EnemyEntersVisionOnlyAfterManeuver()
    {
        var cfg = new FogConfig(BlipRingEnabled: true, NightVisionDivisor: 2);
        var empty = (IReadOnlySet<UnitId>)new HashSet<UnitId>();
        var enemyId = new UnitId(1);
        var observer = Unit(0, Side.Blue, new HexCoord(0, 0), vision: 2);

        var pre = StateWith(observer, Unit(1, Side.Red, new HexCoord(4, 0), 2)) with { Phase = DayNightPhase.Morning };
        var post = StateWith(observer, Unit(1, Side.Red, new HexCoord(1, 0), 2)) with { Phase = DayNightPhase.Morning };

        var roundStart = TurnPipeline.ComputeFogSnapshot(pre, cfg);
        var recomputed = TurnPipeline.RecomputeFogAfterManeuver(post, cfg, empty);

        Assert.Equal(Visibility.Hidden, roundStart.For(Side.Blue)[enemyId]);
        Assert.Equal(Visibility.Identified, recomputed.For(Side.Blue)[enemyId]);
    }

    // ---- 辅助 ----------------------------------------------------------

    /// <summary>逐键比较两份可见度映射是否完全一致（键集合与每个值均相等）。</summary>
    private static bool SameView(
        IReadOnlyDictionary<UnitId, Visibility> a,
        IReadOnlyDictionary<UnitId, Visibility> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        foreach (var kv in a)
        {
            if (!b.TryGetValue(kv.Key, out var v) || v != kv.Value)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>构造一个仅承载可见度所需字段的合法 <see cref="UnitState"/>（攻/防整除韧性）。</summary>
    private static UnitState Unit(int id, Side owner, HexCoord pos, int vision) =>
        new(
            new UnitId(id),
            owner,
            "unit.test",
            UnitClass.LineHold,
            InitAttack: 1,
            InitDefense: 1,
            Resilience0: 1,
            Attack: 1,
            Defense: 1,
            ResilienceLeft: 1,
            Movement: 1,
            Vision: vision,
            SupportRange: 0,
            Position: pos,
            Command: Command.Hold,
            Flags: NightFlags.None);

    /// <summary>以给定单位构造最小合法 <see cref="GameState"/>（可见度计算不依赖地图内容）。</summary>
    private static GameState StateWith(params UnitState[] units)
    {
        var map = new GameMap(
            new Dictionary<HexCoord, MapCell>
            {
                [new HexCoord(0, 0)] = new MapCell(new HexCoord(0, 0), TerrainType.Plain),
            },
            MapScale.Small);

        var cards = new Dictionary<Side, CardState>
        {
            [Side.Blue] = new CardState(0, 0, System.Array.Empty<CardId>(), System.Array.Empty<CardId>()),
            [Side.Red] = new CardState(0, 0, System.Array.Empty<CardId>(), System.Array.Empty<CardId>()),
        };

        return new GameState(
            SchemaVersion: 1,
            Map: map,
            Units: units,
            DayIndex: 0,
            Phase: DayNightPhase.Morning,
            Cards: cards,
            RngState: 0UL,
            TurnLog: System.Array.Empty<TurnRecordEntry>());
    }
}
