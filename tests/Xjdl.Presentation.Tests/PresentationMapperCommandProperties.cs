using System.Collections.Generic;
using System.Linq;
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
/// <see cref="PresentationMapper"/> 命令组装结构合法的属性测试（任务 3.5）。
/// 覆盖 <c>BuildMoveOrder</c>/<c>BuildAttackPrepOrder</c>/<c>BuildHoldOrder</c>/<c>BuildReposition</c>
/// 四个组装接缝，验证产出的 <see cref="UnitOrder"/>/<see cref="RepositionCommand"/> 结构合法。
/// </summary>
public sealed class PresentationMapperCommandProperties
{
    private const double HexSize = 32.0;

    private static readonly Gen<UnitId> GenUnitId =
        Gen.Int[0, 1000].Select(v => new UnitId(v));

    private static readonly Gen<HexCoord> GenHexCoord =
        Gen.Select(Gen.Int[-10, 10], Gen.Int[-10, 10], (q, r) => new HexCoord(q, r));

    // 非空路径：1..8 格随机坐标。
    private static readonly Gen<HexCoord[]> GenPath =
        GenHexCoord.Array[1, 8];

    private static readonly Gen<int> GenTriggerTick = Gen.Int[0, 100];

    /// <summary>
    /// 构造一个仅用于命令组装的最小 <see cref="PresentationMapper"/>。
    /// 命令组装不读取 <see cref="GameState"/>，故使用最小合法配置即可。
    /// </summary>
    private static PresentationMapper NewMapper()
    {
        var layout = new HexLayout(HexSize, new Vector2D(0.0, 0.0));
        var terrainProfile = new TerrainProfile(new Dictionary<TerrainType, TerrainSpec>());
        var data = new GameData(
            System.Array.Empty<UnitTemplate>(),
            terrainProfile,
            System.Array.Empty<LoadedDoctrine>(),
            new Dictionary<CardId, Card>(),
            new Dictionary<MapScale, MapScaleProfile>());
        var fogConfig = new FogConfig(BlipRingEnabled: true, NightVisionDivisor: 2);

        return new PresentationMapper(data, fogConfig, layout);
    }

    /// <summary>
    /// Feature: godot-presentation-layer, Property 4: 命令组装结构合法
    ///
    /// 对任意己方单位与合法路径，<see cref="PresentationMapper.BuildMoveOrder"/> 产出的
    /// <see cref="UnitOrder"/> 满足 <c>Command == Move</c>、<c>Path</c> 非空且与传入路径一致、
    /// <c>Target == null</c>、<c>Unit</c> 为传入单位。
    ///
    /// **Validates: Requirements 6.5, 6.8, 6.9, 13.2, 17.3**
    /// </summary>
    [Fact]
    public void BuildMoveOrder_ProducesMoveWithPathAndNoTarget()
    {
        var mapper = NewMapper();

        Gen.Select(GenUnitId, GenPath).Sample(
            tuple =>
            {
                var (unit, path) = tuple;
                var order = mapper.BuildMoveOrder(unit, path);

                Assert.Equal(unit, order.Unit);
                Assert.Equal(Command.Move, order.Command);
                Assert.Null(order.Target);
                Assert.NotNull(order.Path);
                Assert.NotEmpty(order.Path!);
                Assert.Equal(path, order.Path!);
            },
            iter: 200);
    }

    /// <summary>
    /// Feature: godot-presentation-layer, Property 4: 命令组装结构合法
    ///
    /// 对任意己方单位与目标格，<see cref="PresentationMapper.BuildAttackPrepOrder"/> 产出的
    /// <see cref="UnitOrder"/> 满足 <c>Command == AttackPrep</c>、<c>Target</c> 非空且与传入目标一致、
    /// <c>Path == null</c>、<c>Unit</c> 为传入单位。
    ///
    /// **Validates: Requirements 6.5, 6.8, 6.9, 13.2, 17.3**
    /// </summary>
    [Fact]
    public void BuildAttackPrepOrder_ProducesAttackPrepWithTargetAndNoPath()
    {
        var mapper = NewMapper();

        Gen.Select(GenUnitId, GenHexCoord).Sample(
            tuple =>
            {
                var (unit, target) = tuple;
                var order = mapper.BuildAttackPrepOrder(unit, target);

                Assert.Equal(unit, order.Unit);
                Assert.Equal(Command.AttackPrep, order.Command);
                Assert.Null(order.Path);
                Assert.NotNull(order.Target);
                Assert.Equal(target, order.Target);
            },
            iter: 200);
    }

    /// <summary>
    /// Feature: godot-presentation-layer, Property 4: 命令组装结构合法
    ///
    /// 对任意己方单位，<see cref="PresentationMapper.BuildHoldOrder"/> 产出的 <see cref="UnitOrder"/>
    /// 满足 <c>Command == Hold</c> 且 <c>Path</c>、<c>Target</c> 均为 <c>null</c>、<c>Unit</c> 为传入单位。
    ///
    /// **Validates: Requirements 6.5, 6.8, 6.9, 13.2, 17.3**
    /// </summary>
    [Fact]
    public void BuildHoldOrder_ProducesHoldWithNoPathOrTarget()
    {
        var mapper = NewMapper();

        GenUnitId.Sample(
            unit =>
            {
                var order = mapper.BuildHoldOrder(unit);

                Assert.Equal(unit, order.Unit);
                Assert.Equal(Command.Hold, order.Command);
                Assert.Null(order.Path);
                Assert.Null(order.Target);
            },
            iter: 200);
    }

    /// <summary>
    /// Feature: godot-presentation-layer, Property 4: 命令组装结构合法
    ///
    /// 对任意己方单位、合法新路径与触发时点，<see cref="PresentationMapper.BuildReposition"/> 产出的
    /// <see cref="RepositionCommand"/> 携带传入的 <c>NewPath</c> 与 <c>TriggerTick</c>、<c>Unit</c> 为传入单位。
    ///
    /// **Validates: Requirements 6.5, 6.8, 6.9, 13.2, 17.3**
    /// </summary>
    [Fact]
    public void BuildReposition_CarriesNewPathAndTriggerTick()
    {
        var mapper = NewMapper();

        Gen.Select(GenUnitId, GenPath, GenTriggerTick).Sample(
            tuple =>
            {
                var (unit, newPath, triggerTick) = tuple;
                var command = mapper.BuildReposition(unit, newPath, triggerTick);

                Assert.Equal(unit, command.Unit);
                Assert.Equal(triggerTick, command.TriggerTick);
                Assert.NotNull(command.NewPath);
                Assert.NotEmpty(command.NewPath);
                Assert.Equal(newPath, command.NewPath);
            },
            iter: 200);
    }
}
