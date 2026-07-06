using System.Linq;
using CsCheck;
using Xjdl.Core.Cards;
using Xjdl.Core.Fog;
using Xjdl.Core.Hex;
using Xjdl.Core.State;
using Xjdl.Data.Doctrines;
using Xjdl.Data.Loading;
using Xjdl.Game.Presentation;
using Xjdl.Game.Presentation.ViewModels;

namespace Xjdl.Presentation.Tests;

/// <summary>
/// <see cref="PresentationMapper.EnemyUnits"/> 敌方可见度过滤的属性测试。
/// 可见度过滤是安全边界：敏感字段在 DTO 源头即被裁剪，节点层无从泄露隐藏信息。
/// </summary>
public sealed class VisibilityFilterProperties
{
    private const double HexSize = 32.0;
    private const int GridMax = 6;

    private static readonly UnitClass[] AllClasses =
        (UnitClass[])System.Enum.GetValues(typeof(UnitClass));

    private static readonly DayNightPhase[] AllPhases =
        (DayNightPhase[])System.Enum.GetValues(typeof(DayNightPhase));

    /// <summary>
    /// 一个可供 <see cref="PresentationMapper"/> 过滤的完整场景：合法状态、配置、布局、观察方与迷雾配置。
    /// </summary>
    private sealed record Scenario(
        GameState State,
        GameData Data,
        HexLayout Layout,
        Side Viewer,
        FogConfig Fog);

    // 观察方（蓝）1–4 个观察单位与敌方（红）1–6 个单位随机撒布在 7x7 网格内；
    // 视野 1–3、V+1 环开关、夜晚视野除数与昼夜阶段随机，
    // 以在 100+ 次迭代中产出 Identified/Spotted/Hidden 混合可见度。
    private static readonly Gen<Scenario> GenScenario =
        from observerCount in Gen.Int[1, 4]
        from enemyCount in Gen.Int[1, 6]
        from obsQ in Gen.Int[0, GridMax].Array[observerCount]
        from obsR in Gen.Int[0, GridMax].Array[observerCount]
        from obsVision in Gen.Int[1, 3].Array[observerCount]
        from obsNightKeep in Gen.Bool.Array[observerCount]
        from enQ in Gen.Int[0, GridMax].Array[enemyCount]
        from enR in Gen.Int[0, GridMax].Array[enemyCount]
        from enClassIdx in Gen.Int[0, AllClasses.Length - 1].Array[enemyCount]
        from blipRing in Gen.Bool
        from nightVisionDivisor in Gen.Int[1, 3]
        from phaseIdx in Gen.Int[0, AllPhases.Length - 1]
        select BuildScenario(
            obsQ, obsR, obsVision, obsNightKeep,
            enQ, enR, enClassIdx,
            blipRing, nightVisionDivisor, AllPhases[phaseIdx]);

    /// <summary>
    /// Feature: godot-presentation-layer, Property 2: 可见度过滤不泄露隐藏信息
    ///
    /// 对任意场景与观察方，<see cref="PresentationMapper.EnemyUnits"/> 的产出与
    /// <see cref="FogSystem.Compute"/> 逐一吻合：
    /// <list type="number">
    /// <item>每个 <see cref="Visibility.Hidden"/> 的敌方单位不产出任何 <see cref="EnemyView"/> 条目。</item>
    /// <item>每个 <see cref="Visibility.Spotted"/> 的敌方单位恰产出一条，且兵种/攻/防/韧性/堆叠
    ///       字段一律为 <c>null</c>（仅暴露坐标与"有敌"）。</item>
    /// <item>每个 <see cref="Visibility.Identified"/> 的敌方单位恰产出一条，且上述字段均非 <c>null</c>。</item>
    /// <item>不产出任何观察方己方单位或隐匿敌方单位的条目——条目数恰等于非隐匿敌方单位数。</item>
    /// </list>
    ///
    /// **Validates: Requirements 10.2, 10.3, 10.4, 17.2**
    /// </summary>
    [Fact]
    public void EnemyUnits_HidesHiddenAndRedactsSpottedFields()
    {
        GenScenario.Sample(
            scenario =>
            {
                var mapper = new PresentationMapper(scenario.Data, scenario.Fog, scenario.Layout);
                var expected = FogSystem.Compute(scenario.State, scenario.Viewer, scenario.Fog);

                var views = mapper.EnemyUnits(scenario.State, scenario.Viewer);
                var byId = views.ToDictionary(v => v.Id);

                // 观察方己方单位永不出现在敌方视图中。
                var friendlyIds = scenario.State.Units
                    .Where(u => u.Owner == scenario.Viewer)
                    .Select(u => u.Id)
                    .ToHashSet();
                Assert.DoesNotContain(views, v => friendlyIds.Contains(v.Id));

                var expectedVisibleCount = 0;
                foreach (var enemy in scenario.State.Units.Where(u => u.Owner != scenario.Viewer))
                {
                    var vis = expected[enemy.Id];

                    if (vis == Visibility.Hidden)
                    {
                        // (1) 隐匿：不产出任何条目。
                        Assert.False(byId.ContainsKey(enemy.Id));
                        continue;
                    }

                    expectedVisibleCount++;

                    // 非隐匿：恰有一条，且可见度与 FogSystem 一致。
                    Assert.True(byId.TryGetValue(enemy.Id, out var view));
                    Assert.Equal(vis, view!.Visibility);

                    if (vis == Visibility.Spotted)
                    {
                        // (2) 侦得：敏感字段一律 null，仅暴露坐标。
                        Assert.Null(view.Class);
                        Assert.Null(view.Attack);
                        Assert.Null(view.Defense);
                        Assert.Null(view.ResilienceLeft);
                        Assert.Null(view.StackCount);
                        Assert.Equal(enemy.Position, view.Position);
                    }
                    else
                    {
                        // (3) 识别：全字段可见。
                        Assert.NotNull(view.Class);
                        Assert.NotNull(view.Attack);
                        Assert.NotNull(view.Defense);
                        Assert.NotNull(view.ResilienceLeft);
                        Assert.NotNull(view.StackCount);
                        Assert.Equal(enemy.Position, view.Position);
                    }
                }

                // (4) 条目数恰等于非隐匿敌方单位数（无多余泄露、无遗漏）。
                Assert.Equal(expectedVisibleCount, views.Count);
            },
            iter: 200);
    }

    private static Scenario BuildScenario(
        int[] obsQ,
        int[] obsR,
        int[] obsVision,
        bool[] obsNightKeep,
        int[] enQ,
        int[] enR,
        int[] enClassIdx,
        bool blipRing,
        int nightVisionDivisor,
        DayNightPhase phase)
    {
        var layout = new HexLayout(HexSize, new Vector2D(0.0, 0.0));

        // 7x7 平原网格，覆盖所有可能的单位坐标。
        var cells = new Dictionary<HexCoord, MapCell>();
        for (var q = 0; q <= GridMax; q++)
        {
            for (var r = 0; r <= GridMax; r++)
            {
                var coord = new HexCoord(q, r);
                cells[coord] = new MapCell(coord, TerrainType.Plain);
            }
        }

        var map = new GameMap(cells, MapScale.Small);

        var units = new List<UnitState>();
        var nextId = 0;

        // 观察方（蓝）单位。
        for (var i = 0; i < obsQ.Length; i++)
        {
            var flags = obsNightKeep[i] ? NightFlags.NightVisionKeep : NightFlags.None;
            units.Add(MakeUnit(
                nextId++, Side.Blue, UnitClass.LineHold,
                new HexCoord(obsQ[i], obsR[i]), obsVision[i], flags));
        }

        // 敌方（红）单位。
        for (var i = 0; i < enQ.Length; i++)
        {
            units.Add(MakeUnit(
                nextId++, Side.Red, AllClasses[enClassIdx[i]],
                new HexCoord(enQ[i], enR[i]), vision: 1, NightFlags.None));
        }

        var cards = (IReadOnlyDictionary<Side, CardState>)new Dictionary<Side, CardState>();
        var state = new GameState(
            SchemaVersion: 1,
            map,
            units,
            DayIndex: 0,
            phase,
            cards,
            RngState: 0UL,
            System.Array.Empty<TurnRecordEntry>());

        var data = new GameData(
            System.Array.Empty<UnitTemplate>(),
            new TerrainProfile(new Dictionary<TerrainType, TerrainSpec>()),
            System.Array.Empty<LoadedDoctrine>(),
            new Dictionary<CardId, Card>(),
            new Dictionary<MapScale, MapScaleProfile>());

        var fog = new FogConfig(blipRing, nightVisionDivisor);

        return new Scenario(state, data, layout, Side.Blue, fog);
    }

    private static UnitState MakeUnit(
        int id, Side owner, UnitClass unitClass, HexCoord position, int vision, NightFlags flags)
        => new(
            new UnitId(id),
            owner,
            "unit.test",
            unitClass,
            InitAttack: 3,
            InitDefense: 2,
            Resilience0: 4,
            Attack: 3,
            Defense: 2,
            ResilienceLeft: 4,
            Movement: 4,
            Vision: vision,
            SupportRange: 0,
            Position: position,
            Command.Hold,
            flags);
}
