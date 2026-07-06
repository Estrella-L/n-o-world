using CsCheck;
using Xjdl.Core.Cards;
using Xjdl.Core.Hex;
using Xjdl.Core.State;
using Xjdl.Core.Terrain;
using Xjdl.Data.Doctrines;
using Xjdl.Data.Loading;
using Xjdl.Game.Presentation;
using Xjdl.Game.Presentation.ViewModels;

namespace Xjdl.Presentation.Tests;

/// <summary>
/// <see cref="PathPlanner"/> 路径消耗累计与机动点截断的属性测试。
/// </summary>
public sealed class PathPlannerCostTruncationProperties
{
    private const double HexSize = 32.0;

    private static readonly TerrainType[] AllTerrains =
        (TerrainType[])System.Enum.GetValues(typeof(TerrainType));

    private static readonly UnitClass[] AllClasses =
        (UnitClass[])System.Enum.GetValues(typeof(UnitClass));

    /// <summary>
    /// 一个可供 <see cref="PathPlanner"/> 规划的完整场景：合法状态、配置、布局、被规划单位与光标序列。
    /// </summary>
    private sealed record Scenario(
        GameState State,
        GameData Data,
        HexLayout Layout,
        UnitId Unit,
        IReadOnlyList<Vector2D> Cursors);

    // 光标：目标格坐标覆盖地图内外（[-2, 6] 超出最大 5x5 网格），
    // 以经 CenterOf 得到确定吸附的像素，用于驱动逐格扩展与越界截断。
    private static readonly Gen<HexCoord> GenCursorCoord =
        Gen.Select(Gen.Int[-2, 6], Gen.Int[-2, 6], (q, r) => new HexCoord(q, r));

    // 每种地形一份规则：移动消耗为正（保证预算截断有意义）、禁入兵种位掩码、进入即止；
    // 每格随机地形（索引进 AllTerrains），单位随机兵种/起点/机动点，光标覆盖图内外。
    private static readonly Gen<Scenario> GenScenario =
        from w in Gen.Int[1, 5]
        from h in Gen.Int[1, 5]
        from moveCosts in Gen.Int[1, 3].Array[AllTerrains.Length]
        from forbiddenMasks in Gen.Int[0, (1 << 4) - 1].Array[AllTerrains.Length]
        from enterStops in Gen.Bool.Array[AllTerrains.Length]
        from cellTerrainIdx in Gen.Int[0, AllTerrains.Length - 1].Array[w * h]
        from unitClassIdx in Gen.Int[0, AllClasses.Length - 1]
        from startIdx in Gen.Int[0, (w * h) - 1]
        from budget in Gen.Int[0, 15]
        from cursorCount in Gen.Int[1, 5]
        from cursors in GenCursorCoord.List[cursorCount]
        select BuildScenario(
            w, h, moveCosts, forbiddenMasks, enterStops,
            cellTerrainIdx, unitClassIdx, startIdx, budget, cursors);

    /// <summary>
    /// Feature: godot-presentation-layer, Property 3: 路径消耗累计与机动点截断
    ///
    /// 对任意起点、光标序列与单位剩余机动点，<see cref="PathPlanner"/> 产出的
    /// <see cref="PathDraft.Path"/> 是一条从起点出发、相邻格逐格相接的合法路径，
    /// 其 <see cref="PathDraft.UsedCost"/> 等于沿途各格 <see cref="TerrainSystem.MoveCost"/> 之和，
    /// 且不超过机动点；一旦下一格越界、对本兵种不可进入、超预算或进入即止，路径止步于最后一个
    /// 合法格（<see cref="PathDraft.BlockedAhead"/> 为真），绝不产出超限或含禁入格的路径。
    ///
    /// **Validates: Requirements 6.3, 6.4, 6.13**
    /// </summary>
    [Fact]
    public void Extend_AccumulatesCostAndTruncatesAtBudgetOrForbidden()
    {
        GenScenario.Sample(
            scenario =>
            {
                var planner = new PathPlanner(scenario.State, scenario.Data, scenario.Layout);
                var unit = FindUnit(scenario.State, scenario.Unit);
                var terrain = scenario.Data.Terrain;
                var map = scenario.State.Map;

                // 施加整段光标序列（每次 Extend 从起点重建，最终草案对应最后一个光标）。
                var draft = planner.Begin(scenario.Unit);
                foreach (var cursor in scenario.Cursors)
                {
                    draft = planner.Extend(draft, cursor);
                }

                var path = draft.Path;

                // (1) 路径非空且首格锚定在单位起点。
                Assert.NotEmpty(path);
                Assert.Equal(unit.Position, path[0]);

                // (2) 逐格相邻、(3) 全程在图内、(4)(6) 消耗累计一致且各格可进入。
                var expectedCost = 0;
                for (var i = 1; i < path.Count; i++)
                {
                    Assert.Equal(1, path[i - 1].DistanceTo(path[i]));

                    var cell = map.TryGet(path[i]);
                    Assert.NotNull(cell);

                    Assert.True(TerrainSystem.CanEnter(terrain, cell!.Terrain, unit.Class));

                    expectedCost += TerrainSystem.MoveCost(terrain, cell.Terrain, unit.Class);

                    // (7) 进入即止地形只能作为路径的最后一格出现。
                    if (i < path.Count - 1)
                    {
                        Assert.False(IsEnterAndStop(terrain, cell.Terrain));
                    }
                }

                // 起点亦须在图内。
                Assert.True(map.Contains(path[0]));

                // (4) UsedCost 等于沿途各格消耗之和。
                Assert.Equal(expectedCost, draft.UsedCost);

                // (5) 绝不超过机动点，且预算与单位机动点一致。
                Assert.Equal(unit.Movement, draft.MovementBudget);
                Assert.True(draft.UsedCost <= draft.MovementBudget);

                // BlockedAhead 当且仅当未能延伸到最后一个光标的吸附目标格。
                var finalTarget = scenario.Layout.CoordAt(scenario.Cursors[^1]);
                Assert.Equal(path[^1] != finalTarget, draft.BlockedAhead);
            },
            iter: 200);
    }

    private static Scenario BuildScenario(
        int width,
        int height,
        int[] moveCosts,
        int[] forbiddenMasks,
        bool[] enterStops,
        int[] cellTerrainIdx,
        int unitClassIdx,
        int startIdx,
        int budget,
        IReadOnlyList<HexCoord> cursorCoords)
    {
        var layout = new HexLayout(HexSize, new Vector2D(0.0, 0.0));

        // 每种地形一份规则档。
        var terrainSpecs = new Dictionary<TerrainType, TerrainSpec>();
        for (var i = 0; i < AllTerrains.Length; i++)
        {
            var forbidden = new List<UnitClass>();
            for (var c = 0; c < AllClasses.Length; c++)
            {
                if ((forbiddenMasks[i] & (1 << c)) != 0)
                {
                    forbidden.Add(AllClasses[c]);
                }
            }

            terrainSpecs[AllTerrains[i]] = new TerrainSpec(
                moveCosts[i], DefensiveDrm: 0, forbidden, enterStops[i]);
        }

        var terrainProfile = new TerrainProfile(terrainSpecs);

        // 矩形网格：q 外层、r 内层，与 cellTerrainIdx 顺序对齐。
        var coords = new List<HexCoord>();
        var cells = new Dictionary<HexCoord, MapCell>();
        var idx = 0;
        for (var q = 0; q < width; q++)
        {
            for (var r = 0; r < height; r++)
            {
                var coord = new HexCoord(q, r);
                var terrain = AllTerrains[cellTerrainIdx[idx++]];
                coords.Add(coord);
                cells[coord] = new MapCell(coord, terrain);
            }
        }

        var map = new GameMap(cells, MapScale.Small);
        var start = coords[startIdx];
        var unitClass = AllClasses[unitClassIdx];

        var unit = new UnitState(
            new UnitId(0),
            Side.Blue,
            "unit.test",
            unitClass,
            InitAttack: 1,
            InitDefense: 1,
            Resilience0: 1,
            Attack: 1,
            Defense: 1,
            ResilienceLeft: 1,
            Movement: budget,
            Vision: 1,
            SupportRange: 0,
            Position: start,
            Command.Hold,
            NightFlags.None);

        var cards = (IReadOnlyDictionary<Side, CardState>)new Dictionary<Side, CardState>();
        var state = new GameState(
            SchemaVersion: 1,
            map,
            new[] { unit },
            DayIndex: 0,
            DayNightPhase.Morning,
            cards,
            RngState: 0UL,
            System.Array.Empty<TurnRecordEntry>());

        var data = new GameData(
            System.Array.Empty<UnitTemplate>(),
            terrainProfile,
            System.Array.Empty<LoadedDoctrine>(),
            new Dictionary<CardId, Card>(),
            new Dictionary<MapScale, MapScaleProfile>());

        var cursors = cursorCoords.Select(layout.CenterOf).ToList();

        return new Scenario(state, data, layout, unit.Id, cursors);
    }

    private static UnitState FindUnit(GameState state, UnitId id)
    {
        foreach (var unit in state.Units)
        {
            if (unit.Id == id)
            {
                return unit;
            }
        }

        throw new System.InvalidOperationException($"场景中不存在单位 {id.Value}。");
    }

    private static bool IsEnterAndStop(TerrainProfile profile, TerrainType terrain)
        => profile.Terrains.TryGetValue(terrain, out var spec) && spec.EnterAndStop;
}
