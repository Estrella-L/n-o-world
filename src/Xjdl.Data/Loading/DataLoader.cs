using System.Text.Json;
using System.Text.Json.Serialization;
using Xjdl.Core.Cards;
using Xjdl.Core.Doctrine;
using Xjdl.Core.State;
using Xjdl.Data.Cards;
using Xjdl.Data.Doctrines;
using Xjdl.Data.Scale;
using Xjdl.Data.Terrain;
using Xjdl.Data.Units;
using CoreDoctrine = Xjdl.Core.Doctrine.Doctrine;

namespace Xjdl.Data.Loading;

/// <summary>
/// 配置加载子系统（Xjdl.Data，Req 20）。以 <see cref="System.Text.Json"/> 反序列化兵种/地形/
/// 学说/卡牌/规模档位（枚举以字符串序列化，<see cref="JsonStringEnumConverter"/>，Req 20.5），
/// 加载即校验并在非法时快速失败（抛 <see cref="InvalidDataException"/>，Req 20.2）。
/// <para>
/// fail-fast 语义（Req 20.2、20.2b）：JSON 解析失败、缺失必需分节、内容非法，以及「校验过程本身
/// 因异常无法完成」——一律视为非法并抛 <see cref="InvalidDataException"/>，绝不静默接受。
/// 兵种初始攻/防不整除韧性 N 亦在此抛出（Req 8.3），学说预算 / 整除由
/// <see cref="DoctrineSystem.Validate"/> 复用校验（Req 16.2、16.5）。
/// </para>
/// </summary>
public static class DataLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    /// <summary>
    /// 加载并校验一份 JSON 配置，产出核心类型聚合 <see cref="GameData"/>（Req 20.1）。
    /// </summary>
    /// <param name="json">配置 JSON 文本。</param>
    /// <returns>校验通过的 <see cref="GameData"/>。</returns>
    /// <exception cref="InvalidDataException">
    /// 数据非法，或校验过程本身无法完成（含 JSON 解析失败、缺失分节、不整除韧性 N 等）。
    /// </exception>
    public static GameData Load(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidDataException("配置 JSON 为空。");
        }

        try
        {
            var dto = JsonSerializer.Deserialize<GameDataDto>(json, Options)
                ?? throw new InvalidDataException("配置 JSON 反序列化为 null。");

            var units = BuildUnits(dto.Units);
            var terrain = BuildTerrain(dto.Terrains);
            var doctrines = BuildDoctrines(dto.Doctrines);
            var cards = BuildCards(dto.Cards);
            var scales = BuildScales(dto.Scales);

            return new GameData(units, terrain, doctrines, cards, scales);
        }
        catch (InvalidDataException)
        {
            // 已是「非法数据」判定，原样上抛（fail-fast，Req 20.2）。
            throw;
        }
        catch (Exception ex)
        {
            // 校验过程本身失败（JSON 解析错误、映射异常等）——一律视为非法并抛出（Req 20.2b）。
            throw new InvalidDataException(
                "配置加载 / 校验过程失败，视为非法数据（fail-fast）。", ex);
        }
    }

    private static IReadOnlyList<UnitTemplate> BuildUnits(IReadOnlyList<UnitTemplateData>? units)
    {
        Require(units, "units");

        var result = new List<UnitTemplate>(units!.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var u in units)
        {
            RequireEntry(u, "units");
            if (string.IsNullOrWhiteSpace(u.TypeKey))
            {
                throw new InvalidDataException("兵种模板缺少 TypeKey。");
            }

            if (!seen.Add(u.TypeKey))
            {
                throw new InvalidDataException($"兵种模板 TypeKey 重复：'{u.TypeKey}'。");
            }

            if (u.Resilience <= 0)
            {
                throw new InvalidDataException(
                    $"兵种 '{u.TypeKey}' 的韧性 N 必须为正数，实际为 {u.Resilience}。");
            }

            if (u.Attack < 0 || u.Defense < 0 || u.Movement < 0 || u.Vision < 0 || u.SupportRange < 0)
            {
                throw new InvalidDataException($"兵种 '{u.TypeKey}' 含负数属性，非法。");
            }

            // 初始攻/防必须整除韧性 N，否则抛 InvalidDataException（Req 8.3），不静默取整。
            if (u.Attack % u.Resilience != 0)
            {
                throw new InvalidDataException(
                    $"兵种 '{u.TypeKey}' 的初始进攻 {u.Attack} 不能被韧性 N={u.Resilience} 整除。");
            }

            if (u.Defense % u.Resilience != 0)
            {
                throw new InvalidDataException(
                    $"兵种 '{u.TypeKey}' 的初始防御 {u.Defense} 不能被韧性 N={u.Resilience} 整除。");
            }

            result.Add(u.ToModel());
        }

        return result;
    }

    private static TerrainProfile BuildTerrain(IReadOnlyList<TerrainSpecData>? terrains)
    {
        Require(terrains, "terrains");

        var map = new Dictionary<TerrainType, TerrainSpec>();
        foreach (var t in terrains!)
        {
            RequireEntry(t, "terrains");
            if (t.MoveCost < 0)
            {
                throw new InvalidDataException($"地形 '{t.Terrain}' 的移动消耗为负，非法。");
            }

            if (!map.TryAdd(t.Terrain, t.ToModel()))
            {
                throw new InvalidDataException($"地形类型重复：'{t.Terrain}'。");
            }
        }

        return new TerrainProfile(map);
    }

    private static IReadOnlyList<LoadedDoctrine> BuildDoctrines(IReadOnlyList<DoctrineData>? doctrines)
    {
        Require(doctrines, "doctrines");

        var result = new List<LoadedDoctrine>(doctrines!.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var d in doctrines)
        {
            RequireEntry(d, "doctrines");
            if (string.IsNullOrWhiteSpace(d.Key))
            {
                throw new InvalidDataException("学说缺少 Key。");
            }

            if (!seen.Add(d.Key))
            {
                throw new InvalidDataException($"学说 Key 重复：'{d.Key}'。");
            }

            // 每种学说提供恰好两条专精分支供二选一（Req 16.5）。
            var specData = d.Specializations;
            if (specData is null || specData.Count != 2)
            {
                throw new InvalidDataException(
                    $"学说 '{d.Key}' 必须提供恰好 2 条专精分支（A/B），实际 {specData?.Count ?? 0} 条。");
            }

            CoreDoctrine doctrine = d.ToModel();
            var specs = new List<Specialization>(2);
            foreach (var s in specData)
            {
                RequireEntry(s, "specializations");
                if (string.IsNullOrWhiteSpace(s.Key))
                {
                    throw new InvalidDataException($"学说 '{d.Key}' 的专精分支缺少 Key。");
                }

                Specialization spec = s.ToModel();

                // 复用核心校验：预算合计恰为 4 且专精声明预算与点数一致（Req 16.2、16.5）；
                // 非法即抛 InvalidDataException（fail-fast）。
                DoctrineSystem.Validate(doctrine, spec);
                specs.Add(spec);
            }

            result.Add(new LoadedDoctrine(doctrine, specs));
        }

        return result;
    }

    private static IReadOnlyDictionary<CardId, Card> BuildCards(IReadOnlyList<CardData>? cards)
    {
        Require(cards, "cards");

        var map = new Dictionary<CardId, Card>();
        foreach (var c in cards!)
        {
            RequireEntry(c, "cards");
            if (c.CpCost < 0)
            {
                throw new InvalidDataException($"卡牌 {c.Id} 的 CP 消耗为负，非法。");
            }

            var card = c.ToModel();
            if (!map.TryAdd(card.Id, card))
            {
                throw new InvalidDataException($"卡牌 Id 重复：{c.Id}。");
            }
        }

        return map;
    }

    private static IReadOnlyDictionary<MapScale, MapScaleProfile> BuildScales(
        IReadOnlyList<MapScaleProfileData>? scales)
    {
        Require(scales, "scales");

        var map = new Dictionary<MapScale, MapScaleProfile>();
        foreach (var s in scales!)
        {
            RequireEntry(s, "scales");
            if (s.CpPerTurn < 0 || s.CpMax < 0 || s.DeckSize < 0 || s.HandLimit < 0)
            {
                throw new InvalidDataException($"规模档位 '{s.Scale}' 含负数参数，非法。");
            }

            if (!map.TryAdd(s.Scale, s.ToModel()))
            {
                throw new InvalidDataException($"规模档位重复：'{s.Scale}'。");
            }
        }

        return map;
    }

    private static void Require<T>(IReadOnlyList<T>? section, string name)
    {
        if (section is null)
        {
            throw new InvalidDataException($"配置缺少必需分节 '{name}'。");
        }
    }

    private static void RequireEntry(object? entry, string section)
    {
        if (entry is null)
        {
            throw new InvalidDataException($"分节 '{section}' 含 null 条目，非法。");
        }
    }

    /// <summary>根配置 DTO：五大分节各为一组条目（Req 20.1）。</summary>
    private sealed record GameDataDto(
        IReadOnlyList<UnitTemplateData>? Units,
        IReadOnlyList<TerrainSpecData>? Terrains,
        IReadOnlyList<DoctrineData>? Doctrines,
        IReadOnlyList<CardData>? Cards,
        IReadOnlyList<MapScaleProfileData>? Scales);
}
