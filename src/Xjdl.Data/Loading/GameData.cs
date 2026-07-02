using Xjdl.Core.Cards;
using Xjdl.Core.State;
using Xjdl.Data.Doctrines;

namespace Xjdl.Data.Loading;

/// <summary>
/// 一次加载 + 校验通过后的全部配置聚合（Req 20.1）。持有的均为 <c>Xjdl.Core</c> 侧不可变类型
/// （或其分组），即 DataLoader「产出核心类型」而非数据层 DTO。所有集合非空、内容已校验（fail-fast）。
/// </summary>
/// <param name="UnitTemplates">兵种模板集合（Req 16.1、8.3）。</param>
/// <param name="Terrain">地形配置档（按 <see cref="TerrainType"/> 索引，Req 15.x）。</param>
/// <param name="Doctrines">学说集合（含各自 A/B 专精分支，Req 16）。</param>
/// <param name="Cards">卡牌定义注册表（按 <see cref="CardId"/> 索引，Req 19.x）。</param>
/// <param name="ScaleProfiles">规模档位（按 <see cref="MapScale"/> 索引，Req 19.1）。</param>
public sealed record GameData(
    IReadOnlyList<UnitTemplate> UnitTemplates,
    TerrainProfile Terrain,
    IReadOnlyList<LoadedDoctrine> Doctrines,
    IReadOnlyDictionary<CardId, Card> Cards,
    IReadOnlyDictionary<MapScale, MapScaleProfile> ScaleProfiles);
