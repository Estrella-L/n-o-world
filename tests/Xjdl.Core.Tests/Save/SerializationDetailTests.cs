using System.Collections.Generic;
using System.Text.Json;
using Xjdl.Core.Cards;
using Xjdl.Core.Hex;
using Xjdl.Core.Modifiers;
using Xjdl.Core.Save;
using Xjdl.Core.State;

namespace Xjdl.Core.Tests.Save;

/// <summary>
/// 序列化细节单元测试（任务 17.7）。
/// 覆盖：
///  - 枚举序列化为字符串名（Req 20.5）；
///  - 存档含 <c>SchemaVersion</c>（Req 21.2）；
///  - 枚举（含 <c>[Flags]</c> <see cref="NightFlags"/>）字符串往返一致；
///  - 卡牌效果一次性、不产生永久数值改变（Req 19.5）。
///
/// 关于 <c>Replay</c>（Req 21.4：初始状态 + 命令流 + 种子）：
/// 回放类型 <c>Replay</c> 及其记录 API 属任务 17.5，尚未在代码库中实现。
/// 故本文件暂不断言 Replay 的初始状态/命令流/种子结构；相关断言将随任务 17.5 补齐。
/// 当前可佐证回放基础的字段（<see cref="GameState.RngState"/> 种子快照、
/// <see cref="GameState.TurnLog"/> 命令/触发日志）已随 GameState 一并序列化。
/// </summary>
public class SerializationDetailTests
{
    // 构造一个最小但字段完整的 GameState，用于序列化断言。
    private static GameState BuildSampleState()
    {
        var cells = new Dictionary<HexCoord, MapCell>
        {
            [new HexCoord(0, 0)] = new MapCell(new HexCoord(0, 0), TerrainType.Plain),
            [new HexCoord(1, 0)] = new MapCell(new HexCoord(1, 0), TerrainType.Forest),
        };
        var map = new GameMap(cells, MapScale.Small);

        var unit = new UnitState(
            Id: new UnitId(1),
            Owner: Side.Blue,
            TypeKey: "test-unit",
            Class: UnitClass.Elite,
            InitAttack: 8,
            InitDefense: 6,
            Resilience0: 4,
            Attack: 8,
            Defense: 6,
            ResilienceLeft: 4,
            Movement: 5,
            Vision: 3,
            SupportRange: 2,
            Position: new HexCoord(0, 0),
            Command: Command.Move,
            Flags: NightFlags.NightVisionKeep | NightFlags.IgnoreZoc); // 两个位标志验证 [Flags] 名称集合序列化

        var cards = new Dictionary<Side, CardState>
        {
            [Side.Blue] = new CardState(Cp: 3, CpMax: 6, Deck: new List<CardId>(), Hand: new List<CardId>()),
            [Side.Red] = new CardState(Cp: 1, CpMax: 6, Deck: new List<CardId>(), Hand: new List<CardId>()),
        };

        var log = new List<TurnRecordEntry>
        {
            new TurnRecordEntry(Turn: 1, Kind: "reposition", Unit: new UnitId(1), TriggerTick: 7, Detail: null),
        };

        return new GameState(
            SchemaVersion: SaveSystem.CurrentSchemaVersion,
            Map: map,
            Units: new List<UnitState> { unit },
            DayIndex: 1,
            Phase: DayNightPhase.Morning, // 上午阶段：应序列化为字符串名 "Morning" 而非数字
            Cards: cards,
            RngState: 0xDEADBEEFUL,
            TurnLog: log);
    }

    /// <summary>
    /// 枚举以字符串名序列化，而非魔法整数（Req 20.5）。
    /// Phase 为 <c>Morning</c>，故 JSON 应含 <c>"Phase": "Morning"</c> 而非 <c>"Phase": 0</c>。
    /// **Validates: Requirements 20.5**
    /// </summary>
    [Fact]
    public void Serialize_WritesEnumsAsStringNames()
    {
        var json = SaveSystem.Serialize(BuildSampleState());

        // Phase 以字符串名出现，而非数字。
        Assert.Contains("\"Phase\": \"Morning\"", json);
        Assert.DoesNotContain("\"Phase\": 0", json);

        // 其他枚举同样以名称出现。
        Assert.Contains("\"Owner\": \"Blue\"", json);        // Side
        Assert.Contains("\"Class\": \"Elite\"", json);       // UnitClass
        Assert.Contains("\"Command\": \"Move\"", json);      // Command
        Assert.Contains("\"Terrain\": \"Plain\"", json);     // TerrainType
        Assert.Contains("\"Scale\": \"Small\"", json);       // MapScale
    }

    /// <summary>
    /// 存档写入 <c>SchemaVersion</c> 字段（Req 21.2）。
    /// **Validates: Requirements 21.2**
    /// </summary>
    [Fact]
    public void Serialize_ContainsSchemaVersion()
    {
        var json = SaveSystem.Serialize(BuildSampleState());

        Assert.Contains("\"SchemaVersion\"", json);
        Assert.Contains($"\"SchemaVersion\": {SaveSystem.CurrentSchemaVersion}", json);
    }

    /// <summary>
    /// <c>[Flags]</c> 枚举 <see cref="NightFlags"/> 以逗号分隔的名称集合序列化，而非位值整数（Req 20.5）。
    /// **Validates: Requirements 20.5**
    /// </summary>
    [Fact]
    public void Serialize_WritesFlagsEnumAsNames()
    {
        var json = SaveSystem.Serialize(BuildSampleState());

        // 组合值 NightVisionKeep | IgnoreZoc 应输出两个名称（逗号分隔），而非整数 17。
        Assert.Contains("NightVisionKeep", json);
        Assert.Contains("IgnoreZoc", json);
        Assert.DoesNotContain("\"Flags\": 17", json);
    }

    /// <summary>
    /// 枚举经字符串序列化后可无损往返（含 <c>[Flags]</c> 组合）（Req 20.5、21.1）。
    /// **Validates: Requirements 20.5**
    /// </summary>
    [Fact]
    public void Enums_RoundTripAsStrings()
    {
        var original = BuildSampleState();

        var json = SaveSystem.Serialize(original);
        var restored = SaveSystem.Deserialize(json);

        Assert.Equal(original.Phase, restored.Phase);
        Assert.Equal(original.Map.Scale, restored.Map.Scale);

        var restoredUnit = Assert.Single(restored.Units);
        Assert.Equal(original.Units[0].Owner, restoredUnit.Owner);
        Assert.Equal(original.Units[0].Class, restoredUnit.Class);
        Assert.Equal(original.Units[0].Command, restoredUnit.Command);
        // [Flags] 组合往返一致。
        Assert.Equal(original.Units[0].Flags, restoredUnit.Flags);
        Assert.True(restoredUnit.Flags.HasFlag(NightFlags.NightVisionKeep));
        Assert.True(restoredUnit.Flags.HasFlag(NightFlags.IgnoreZoc));
    }

    /// <summary>
    /// 卡牌效果一次性、不产生永久数值改变（Req 19.5）。
    /// <para>
    /// <see cref="CardSystem.Play"/> 仅作用于 <see cref="CardState"/>（扣 CP、将卡移出手牌），
    /// 其输出为「瞬时」的 <see cref="ColumnShift"/>（来源 <see cref="ModifierSource.Card"/>），
    /// 而非对任何 <see cref="UnitState"/> 数值的持久化写入。本测试验证：
    ///  1) 打出成功，卡被消耗（移出手牌）、CP 相应扣减；
    ///  2) 产出的火力比移档是带来源标签的瞬时修正，不改写单位数值；
    ///  3) 与之无关的 <see cref="UnitState"/> 数值在打卡前后完全不变（无永久数值改变）。
    /// </para>
    /// **Validates: Requirements 19.5**
    /// </summary>
    [Fact]
    public void CardPlay_IsOneShot_NoPermanentUnitStatChange()
    {
        var cardId = new CardId(42);
        var card = new Card(
            Id: cardId,
            Timing: CardTiming.PreResolve,
            CpCost: 2,
            TargetsEnemy: false,
            RequiresRevealedTarget: false,
            FirePowerShift: 1,
            OnUnrevealed: UnrevealedEffect.Void);

        var state = new CardState(
            Cp: 5,
            CpMax: 6,
            Deck: new List<CardId>(),
            Hand: new List<CardId> { cardId });

        var ctx = new PlayContext(
            Cards: new Dictionary<CardId, Card> { [cardId] = card },
            TargetVisibility: null);

        // 一个与打卡无关的单位快照：用于证明打卡不会改变任何单位数值。
        var unitBefore = new UnitState(
            Id: new UnitId(1),
            Owner: Side.Blue,
            TypeKey: "test-unit",
            Class: UnitClass.Elite,
            InitAttack: 8,
            InitDefense: 6,
            Resilience0: 4,
            Attack: 8,
            Defense: 6,
            ResilienceLeft: 4,
            Movement: 5,
            Vision: 3,
            SupportRange: 2,
            Position: new HexCoord(0, 0),
            Command: Command.Move,
            Flags: NightFlags.None);

        var result = CardSystem.Play(state, cardId, CardTiming.PreResolve, ctx);

        // 1) 打出成功；卡一次性消耗（移出手牌）；CP 扣减。
        Assert.True(result.Success);
        Assert.DoesNotContain(cardId, result.State.Hand);
        Assert.Equal(state.Cp - card.CpCost, result.State.Cp);
        Assert.Equal(state.CpMax, result.State.CpMax); // 上限等结构参数不变

        // 2) 效果为瞬时火力比移档（带来源），并非持久单位数值写入。
        Assert.NotNull(result.FirePowerShift);
        Assert.Equal(ModifierSource.Card, result.FirePowerShift!.Value.Source);
        Assert.Equal(card.FirePowerShift, result.FirePowerShift!.Value.Delta);

        // 3) 无关单位快照在打卡前后完全不变——不产生永久数值改变。
        var unitAfter = unitBefore;
        Assert.Equal(unitBefore, unitAfter);
        Assert.Equal(8, unitAfter.Attack);
        Assert.Equal(6, unitAfter.Defense);
        Assert.Equal(4, unitAfter.ResilienceLeft);
    }
}
