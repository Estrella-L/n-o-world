using CsCheck;
using Xjdl.Core.Hex;
using Xjdl.Core.State;

namespace Xjdl.Core.Tests.Support;

/// <summary>
/// 领域类型的可复用 CsCheck 自定义生成器（Req 2.2，见 design〈Testing Strategy · 生成器〉）。
/// <para>
/// 「合法」生成器默认满足领域不变量：坐标有界、<see cref="UnitTemplate"/> 攻/防整除韧性、
/// <see cref="GameState"/> 单位不越界且每格堆叠不超过 <see cref="StackLimit"/>。
/// </para>
/// <para>
/// 同时提供一组显式的 <c>EdgeCase*</c> 采样生成器覆盖 prework 中的 EDGE_CASE：
/// 空内容、非 ASCII key、越界方向、超堆叠、CP 不足、被包围无退路、不整除数据。
/// 这些非法/退化样本供针对性属性/示例测试使用（验证 fail-fast 与拒绝逻辑）。
/// </para>
/// <para>
/// TODO(任务 6.1)：待 <c>Xjdl.Core.Modifiers.ColumnShift</c> / <c>ModifierPipeline</c> 落地后，
/// 在此补充 <c>Gen&lt;ColumnShift&gt;</c> 与移档集合生成器（含 ±2 封顶与按 source 抵消的边界样本）。
/// 当前任务不引用尚未存在的类型，避免破坏并发构建。
/// </para>
/// </summary>
public static class Generators
{
    /// <summary>坐标生成下界（含）。</summary>
    public const int CoordMin = -8;

    /// <summary>坐标生成上界（含）。</summary>
    public const int CoordMax = 8;

    /// <summary>堆叠上限（docs/01〈堆叠规则〉默认 3）。</summary>
    public const int StackLimit = 3;

    // ---- 基础组合子 ----------------------------------------------------

    /// <summary>在枚举全部取值中均匀取样。</summary>
    private static Gen<T> GenEnum<T>()
        where T : struct, System.Enum
    {
        var values = (T[])System.Enum.GetValues(typeof(T));
        return Gen.Int[0, values.Length - 1].Select(i => values[i]);
    }

    /// <summary>在给定候选集合中均匀取样。</summary>
    private static Gen<T> Pick<T>(params T[] values) =>
        Gen.Int[0, values.Length - 1].Select(i => values[i]);

    // ---- 枚举生成器 ----------------------------------------------------

    /// <summary>阵营（蓝/红）。</summary>
    public static readonly Gen<Side> Sides = GenEnum<Side>();

    /// <summary>兵种类别。</summary>
    public static readonly Gen<UnitClass> UnitClasses = GenEnum<UnitClass>();

    /// <summary>阶段 0 命令。</summary>
    public static readonly Gen<Command> Commands = GenEnum<Command>();

    /// <summary>昼夜阶段。</summary>
    public static readonly Gen<DayNightPhase> DayNightPhases = GenEnum<DayNightPhase>();

    /// <summary>地图规模档位。</summary>
    public static readonly Gen<MapScale> MapScales = GenEnum<MapScale>();

    /// <summary>地形类型。</summary>
    public static readonly Gen<TerrainType> Terrains = GenEnum<TerrainType>();

    /// <summary>技能卡时机。</summary>
    public static readonly Gen<CardTiming> CardTimings = GenEnum<CardTiming>();

    // ---- 标识符 --------------------------------------------------------

    /// <summary>卡牌标识符。</summary>
    public static readonly Gen<CardId> CardIds = Gen.Int[0, 50].Select(v => new CardId(v));

    // ---- 坐标 ----------------------------------------------------------

    /// <summary>有界的合法 <see cref="Xjdl.Core.Hex.HexCoord"/>（q、r 落在 [CoordMin, CoordMax]）。</summary>
    public static readonly Gen<HexCoord> HexCoord =
        from q in Gen.Int[CoordMin, CoordMax]
        from r in Gen.Int[CoordMin, CoordMax]
        select new HexCoord(q, r);

    // ---- 兵种 key ------------------------------------------------------

    /// <summary>合法的 ASCII 兵种 key（无文案，形如 "unit.xxx"）。</summary>
    public static readonly Gen<string> TypeKeys =
        Pick("unit.infantry", "unit.armor", "unit.artillery", "unit.recon", "unit.engineer", "unit.fortress");

    // ---- 夜战标志位 ----------------------------------------------------

    /// <summary>
    /// <see cref="Xjdl.Core.State.NightFlags"/> 的任意子集（6 个定义位的任意组合，含 <c>None</c>）。
    /// </summary>
    public static readonly Gen<NightFlags> NightFlags =
        Gen.Int[0, 63].Select(mask => (NightFlags)mask);

    // ---- 兵种模板（满足整除约束） --------------------------------------

    /// <summary>
    /// 合法 <see cref="Xjdl.Core.State.UnitTemplate"/>：攻/防均整除韧性（Attack % Resilience == 0
    /// 且 Defense % Resilience == 0，Req 8.1/8.3），以攻 = 韧性×倍数的方式构造保证零余数。
    /// </summary>
    public static readonly Gen<UnitTemplate> UnitTemplate =
        from typeKey in TypeKeys
        from cls in UnitClasses
        from resilience in Gen.Int[1, 6]
        from attackMul in Gen.Int[1, 6]
        from defenseMul in Gen.Int[1, 6]
        from movement in Gen.Int[1, 8]
        from vision in Gen.Int[1, 6]
        from supportRange in Gen.Int[0, 4]
        from hasShift in Gen.Bool
        from shift in Gen.Int[1, 3]
        from flags in NightFlags
        select new UnitTemplate(
            typeKey,
            cls,
            resilience * attackMul,
            resilience * defenseMul,
            movement,
            resilience,
            vision,
            supportRange,
            hasShift ? shift : (int?)null,
            flags);

    // ---- 单位实例 ------------------------------------------------------

    /// <summary>
    /// 合法 <see cref="Xjdl.Core.State.UnitState"/>：由合法模板派生，当前攻/防按整除衰减量
    /// 与剩余韧性保持一致（ResilienceLeft ∈ [1, Resilience0]，Req 8.1/8.2）。
    /// </summary>
    public static readonly Gen<UnitState> UnitState =
        from id in Gen.Int[0, 1000]
        from owner in Sides
        from tmpl in UnitTemplate
        from pos in HexCoord
        from cmd in Commands
        from resLeft in Gen.Int[1, tmpl.Resilience]
        select new UnitState(
            new UnitId(id),
            owner,
            tmpl.TypeKey,
            tmpl.Class,
            tmpl.Attack,
            tmpl.Defense,
            tmpl.Resilience,
            (tmpl.Attack / tmpl.Resilience) * resLeft,
            (tmpl.Defense / tmpl.Resilience) * resLeft,
            resLeft,
            tmpl.Movement,
            tmpl.Vision,
            tmpl.SupportRange,
            pos,
            cmd,
            tmpl.DefaultFlags);

    // ---- 卡牌经济状态 --------------------------------------------------

    /// <summary>合法 <see cref="Xjdl.Core.State.CardState"/>：Cp ∈ [0, CpMax]（Req 19.2）。</summary>
    public static readonly Gen<CardState> CardState =
        from cpMax in Gen.Int[0, 10]
        from cp in Gen.Int[0, cpMax]
        from deckN in Gen.Int[0, 8]
        from deck in CardIds.List[deckN]
        from handN in Gen.Int[0, 5]
        from hand in CardIds.List[handN]
        select new CardState(cp, cpMax, deck, hand);

    private static readonly Gen<IReadOnlyDictionary<Side, CardState>> CardsDict =
        from blue in CardState
        from red in CardState
        select (IReadOnlyDictionary<Side, CardState>)new Dictionary<Side, CardState>
        {
            [Side.Blue] = blue,
            [Side.Red] = red,
        };

    // ---- 地图 ----------------------------------------------------------

    /// <summary>合法 <see cref="Xjdl.Core.State.GameMap"/>：矩形格阵，坐标在界内、地形随机。</summary>
    public static readonly Gen<GameMap> GameMap =
        from w in Gen.Int[1, 6]
        from h in Gen.Int[1, 6]
        from scale in MapScales
        from terrains in Terrains.Array[w * h]
        select BuildMap(w, h, terrains, scale);

    // ---- 回合日志 ------------------------------------------------------

    private static readonly Gen<TurnRecordEntry> TurnRecordEntry =
        from turn in Gen.Int[0, 20]
        from kind in Pick("plan", "reveal", "maneuver", "combat", "reposition")
        from hasUnit in Gen.Bool
        from uid in Gen.Int[0, 100]
        from hasTick in Gen.Bool
        from tick in Gen.Int[0, 50]
        from hasDetail in Gen.Bool
        select new TurnRecordEntry(
            turn,
            kind,
            hasUnit ? new UnitId(uid) : (UnitId?)null,
            hasTick ? tick : (int?)null,
            hasDetail ? "detail" : null);

    // ---- 状态根 --------------------------------------------------------

    /// <summary>
    /// 合法 <see cref="Xjdl.Core.State.GameState"/>：单位均落在地图格内、每格堆叠不超过
    /// <see cref="StackLimit"/>、单位 id 唯一（Req 2.6/2.7、9.1）。
    /// </summary>
    public static readonly Gen<GameState> GameState =
        from schema in Gen.Int[1, 3]
        from map in GameMap
        from units in UnitsOnMap(map)
        from dayIndex in Gen.Int[0, 20]
        from phase in DayNightPhases
        from cards in CardsDict
        from rngState in Gen.ULong
        from logN in Gen.Int[0, 6]
        from log in TurnRecordEntry.List[logN]
        select new GameState(schema, map, units, dayIndex, phase, cards, rngState, log);

    // ---- 命令集 --------------------------------------------------------

    /// <summary>单条单位命令（Move 带路径、AttackPrep 带目标、Hold 皆空）。</summary>
    public static readonly Gen<UnitOrder> UnitOrder =
        from uid in Gen.Int[0, 100]
        from cmd in Commands
        from pathN in Gen.Int[1, 4]
        from path in HexCoord.List[pathN]
        from target in HexCoord
        select new UnitOrder(
            new UnitId(uid),
            cmd,
            cmd == Command.Move ? path : null,
            cmd == Command.AttackPrep ? target : (HexCoord?)null);

    private static readonly Gen<RepositionCommand> Reposition =
        from uid in Gen.Int[0, 100]
        from n in Gen.Int[1, 4]
        from path in HexCoord.List[n]
        from tick in Gen.Int[0, 50]
        select new RepositionCommand(new UnitId(uid), path, tick);

    private static readonly Gen<CardPlay> CardPlay =
        from owner in Sides
        from card in CardIds
        from timing in CardTimings
        select new CardPlay(owner, card, timing);

    /// <summary>一组单位命令集合（阶段 0 下令）。</summary>
    public static readonly Gen<IReadOnlyList<UnitOrder>> CommandSet =
        from n in Gen.Int[0, 8]
        from orders in UnitOrder.List[n]
        select (IReadOnlyList<UnitOrder>)orders;

    /// <summary>单回合完整命令集：单位命令 + 临机机动 + 技能卡打出（Req 3.2/4.4/4.7）。</summary>
    public static readonly Gen<TurnCommands> TurnCommands =
        from ordersN in Gen.Int[0, 6]
        from orders in UnitOrder.List[ordersN]
        from repoN in Gen.Int[0, 3]
        from repos in Reposition.List[repoN]
        from playsN in Gen.Int[0, 3]
        from plays in CardPlay.List[playsN]
        select new TurnCommands(orders, repos, plays);

    // ================= EDGE_CASE 采样生成器 =============================

    /// <summary>EDGE_CASE · 空内容：全空的回合命令集。</summary>
    public static readonly Gen<TurnCommands> EdgeCaseEmptyTurnCommands =
        Gen.Const(new TurnCommands(
            System.Array.Empty<UnitOrder>(),
            System.Array.Empty<RepositionCommand>(),
            System.Array.Empty<CardPlay>()));

    /// <summary>EDGE_CASE · 空内容：无单位、单格空地图的最小 <see cref="Xjdl.Core.State.GameState"/>。</summary>
    public static readonly Gen<GameState> EdgeCaseEmptyGameState =
        from scale in MapScales
        select new GameState(
            1,
            BuildMap(1, 1, new[] { TerrainType.Plain }, scale),
            System.Array.Empty<UnitState>(),
            0,
            DayNightPhase.Morning,
            new Dictionary<Side, CardState>
            {
                [Side.Blue] = new CardState(0, 0, System.Array.Empty<CardId>(), System.Array.Empty<CardId>()),
                [Side.Red] = new CardState(0, 0, System.Array.Empty<CardId>(), System.Array.Empty<CardId>()),
            },
            0UL,
            System.Array.Empty<TurnRecordEntry>());

    /// <summary>EDGE_CASE · 非 ASCII / 空字符串兵种 key（i18n key 非法用例）。</summary>
    public static readonly Gen<UnitTemplate> EdgeCaseNonAsciiTypeKeyTemplate =
        from key in Pick("兵种.测试", "юнит", "ユニット", "🚩unit", "")
        from t in UnitTemplate
        select t with { TypeKey = key };

    /// <summary>EDGE_CASE · 越界方向索引（不在 0..5）。用于 <c>HexCoord.Neighbor</c> 的拒绝路径。</summary>
    public static readonly Gen<int> EdgeCaseOutOfRangeDirection =
        Gen.OneOf(Gen.Int[int.MinValue, -1], Gen.Int[6, int.MaxValue]);

    /// <summary>EDGE_CASE · 超堆叠：同一格上放置多于 <see cref="StackLimit"/> 个单位（id 唯一）。</summary>
    public static readonly Gen<IReadOnlyList<UnitState>> EdgeCaseOverStackedUnits =
        from pos in HexCoord
        from n in Gen.Int[StackLimit + 1, StackLimit + 4]
        from units in UnitState.List[n]
        select (IReadOnlyList<UnitState>)units
            .Select((u, i) => u with { Id = new UnitId(i), Position = pos })
            .ToList();

    /// <summary>EDGE_CASE · CP 不足：Cp == 0 但手牌非空（无法支付打出成本，Req 19.7）。</summary>
    public static readonly Gen<CardState> EdgeCaseInsufficientCpCardState =
        from cpMax in Gen.Int[5, 10]
        from handN in Gen.Int[1, 5]
        from hand in CardIds.List[handN]
        select new CardState(0, cpMax, System.Array.Empty<CardId>(), hand);

    /// <summary>
    /// EDGE_CASE · 被包围无退路：一个单位居中，六个相邻格各有一个敌方单位占据（Req 7.x 撤退阻断）。
    /// 返回单位集合（下标 0 为被包围方，其余为包围者），id 唯一。
    /// </summary>
    public static readonly Gen<IReadOnlyList<UnitState>> EdgeCaseSurroundedNoRetreatUnits =
        from center in HexCoord
        from template in UnitState
        select BuildSurrounded(center, template);

    /// <summary>
    /// EDGE_CASE · 不整除数据：攻/防对韧性有非零余数的 <see cref="Xjdl.Core.State.UnitTemplate"/>，
    /// 用于验证加载期整除校验的 fail-fast（Req 8.3/16.3）。
    /// </summary>
    public static readonly Gen<UnitTemplate> EdgeCaseNonDivisibleTemplate =
        from resilience in Gen.Int[2, 6]
        from k in Gen.Int[1, 5]
        from t in UnitTemplate
        select t with
        {
            Resilience = resilience,
            Attack = (resilience * k) + 1,
            Defense = (resilience * k) + 1,
        };

    // ---- 构造辅助 ------------------------------------------------------

    private static GameMap BuildMap(int width, int height, TerrainType[] terrains, MapScale scale)
    {
        var cells = new Dictionary<HexCoord, MapCell>();
        var idx = 0;
        for (var q = 0; q < width; q++)
        {
            for (var r = 0; r < height; r++)
            {
                var coord = new HexCoord(q, r);
                cells[coord] = new MapCell(coord, terrains[idx++]);
            }
        }

        return new GameMap(cells, scale);
    }

    /// <summary>在给定地图的格内放置单位，强制每格堆叠不超过 <see cref="StackLimit"/> 并重编唯一 id。</summary>
    private static Gen<IReadOnlyList<UnitState>> UnitsOnMap(GameMap map)
    {
        var coords = map.Cells.Keys
            .OrderBy(c => c.Q)
            .ThenBy(c => c.R)
            .ToArray();

        var placed =
            from idx in Gen.Int[0, coords.Length - 1]
            from unit in UnitState
            select unit with { Position = coords[idx] };

        return
            from n in Gen.Int[0, 12]
            from list in placed.List[n]
            select CapStackingAndReassignIds(list);
    }

    private static IReadOnlyList<UnitState> CapStackingAndReassignIds(IReadOnlyList<UnitState> units)
    {
        var result = new List<UnitState>();
        var perCell = new Dictionary<HexCoord, int>();
        var nextId = 0;
        foreach (var u in units)
        {
            perCell.TryGetValue(u.Position, out var count);
            if (count >= StackLimit)
            {
                continue;
            }

            perCell[u.Position] = count + 1;
            result.Add(u with { Id = new UnitId(nextId++) });
        }

        return result;
    }

    private static IReadOnlyList<UnitState> BuildSurrounded(HexCoord center, UnitState template)
    {
        var list = new List<UnitState>
        {
            template with { Id = new UnitId(0), Owner = Side.Blue, Position = center },
        };

        var id = 1;
        foreach (var neighbor in center.Neighbors())
        {
            list.Add(template with { Id = new UnitId(id++), Owner = Side.Red, Position = neighbor });
        }

        return list;
    }
}
