using System.Text.Json;
using System.Text.Json.Serialization;
using Xjdl.Core.Hex;
using Xjdl.Core.State;

namespace Xjdl.Core.Save;

/// <summary>
/// 存档与回放的序列化设施（Req 21.x）。
/// <para>
/// <see cref="Serialize"/>/<see cref="Deserialize"/> 以 <c>System.Text.Json</c> 将
/// <see cref="GameState"/> 表达为纯数据字符串（无引擎类型、无循环引用，Req 21.1）。
/// <see cref="GameState.SchemaVersion"/> 作为普通字段随存档写入，承载版本号以支持迁移（Req 21.2/21.3）。
/// 全部枚举经 <see cref="JsonStringEnumConverter"/> 序列化为字符串名而非魔法整数（Req 20.5）。
/// </para>
/// <para>
/// 共享的 <see cref="Options"/> 对 <c>internal</c> 可见，供后续任务（17.3 迁移、17.5 回放）复用，
/// 保证读写一致的 JSON 形状。
/// </para>
/// </summary>
public static class SaveSystem
{
    /// <summary>
    /// 当前存档结构版本号。新写出的存档以 <see cref="GameState.SchemaVersion"/> 承载版本；
    /// 迁移任务（17.3）以此常量为目标版本升级旧档（Req 21.2/21.3）。
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>
    /// 共享序列化选项：枚举转字符串（含 <c>[Flags]</c> 逗号分隔形式，Req 20.5）、
    /// 自定义 <see cref="GameMap"/> 转换器处理 <see cref="HexCoord"/> 键字典（见 <see cref="GameMapJsonConverter"/>）。
    /// 属性名保持 PascalCase 以匹配 record 主构造函数参数名，保证参数化构造反序列化（.NET 8）。
    /// </summary>
    internal static readonly JsonSerializerOptions Options = CreateOptions();

    /// <summary>将 <see cref="GameState"/> 序列化为 JSON 纯数据（Req 21.1）。</summary>
    public static string Serialize(GameState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        return JsonSerializer.Serialize(state, Options);
    }

    /// <summary>从 JSON 反序列化回 <see cref="GameState"/>（Req 21.1）。</summary>
    /// <exception cref="JsonException">当 JSON 非法或缺少必需字段时抛出。</exception>
    public static GameState Deserialize(string json)
    {
        ArgumentException.ThrowIfNullOrEmpty(json);
        return JsonSerializer.Deserialize<GameState>(json, Options)
            ?? throw new JsonException("反序列化结果为 null：JSON 不表示合法的 GameState。");
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            // 可读性优先：存档文件便于人工检视与版本比对。
            WriteIndented = true,
            // 属性名大小写保持一致（PascalCase），匹配 record 参数名。
            PropertyNameCaseInsensitive = false,
        };

        // 枚举以字符串名序列化（Req 20.5）；[Flags] 枚举输出逗号分隔的名称集合。
        options.Converters.Add(new JsonStringEnumConverter());
        // HexCoord 键字典无法被 System.Text.Json 直接序列化：改以 MapCell 数组表达并在读时重建（Req 21.1）。
        options.Converters.Add(new GameMapJsonConverter());

        return options;
    }
}

/// <summary>
/// <see cref="GameMap"/> 的自定义转换器：将 <see cref="GameMap.Cells"/>
/// （<c>IReadOnlyDictionary&lt;HexCoord, MapCell&gt;</c>）序列化为 <see cref="MapCell"/> 数组，
/// 读回时依据每个 <see cref="MapCell.Coord"/> 重建字典。
/// <para>
/// 采用数组形状的原因：<see cref="HexCoord"/> 是复合值类型键，System.Text.Json 无法将其作为
/// 字典键直接序列化；而 <see cref="MapCell"/> 已自带坐标，数组表达既能无损往返又保持 JSON 简洁（Req 21.1）。
/// 写出时按 <see cref="GameMap.OrderedCells"/> 的稳定序输出，保证字节级确定性（Req 2.6）。
/// </para>
/// </summary>
internal sealed class GameMapJsonConverter : JsonConverter<GameMap>
{
    public override GameMap Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("GameMap 期望以对象起始。");
        }

        List<MapCell>? cells = null;
        MapScale? scale = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                break;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("GameMap 内部期望属性名。");
            }

            var propertyName = reader.GetString();
            reader.Read();

            switch (propertyName)
            {
                case nameof(GameMap.Cells):
                    cells = JsonSerializer.Deserialize<List<MapCell>>(ref reader, options);
                    break;
                case nameof(GameMap.Scale):
                    scale = JsonSerializer.Deserialize<MapScale>(ref reader, options);
                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        if (cells is null)
        {
            throw new JsonException($"GameMap 缺少必需字段 {nameof(GameMap.Cells)}。");
        }

        if (scale is null)
        {
            throw new JsonException($"GameMap 缺少必需字段 {nameof(GameMap.Scale)}。");
        }

        var byCoord = new Dictionary<HexCoord, MapCell>(cells.Count);
        foreach (var cell in cells)
        {
            byCoord[cell.Coord] = cell;
        }

        return new GameMap(byCoord, scale.Value);
    }

    public override void Write(Utf8JsonWriter writer, GameMap value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(value);

        writer.WriteStartObject();

        writer.WritePropertyName(nameof(GameMap.Cells));
        // 以稳定序写出，保证同一地图产出确定性字节序列（Req 2.6）。
        JsonSerializer.Serialize(writer, value.OrderedCells, options);

        writer.WritePropertyName(nameof(GameMap.Scale));
        JsonSerializer.Serialize(writer, value.Scale, options);

        writer.WriteEndObject();
    }
}
