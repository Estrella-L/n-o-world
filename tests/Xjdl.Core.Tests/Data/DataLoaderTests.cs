using System.IO;
using Xjdl.Data.Loading;

namespace Xjdl.Core.Tests.Data;

/// <summary>
/// DataLoader 快速失败（fail-fast）示例单元测试（任务 18.2）。
/// 覆盖：合法配置成功加载；兵种攻/防不整除韧性 N（Req 8.3）；非法/缺失/重复/负值配置
/// （Req 20.2）；空白与非法 JSON——校验过程失败一律视为非法（Req 20.2b）。
/// <para>
/// 关于 Req 1.6「越界方向」：DataLoader 的数据 schema 中不含方向索引字段（方向仅存在于
/// <see cref="Xjdl.Core.Hex.HexCoord"/> 邻接语义中），故越界方向由 Hex 相关测试覆盖，
/// 本文件聚焦加载子系统自身的 fail-fast 行为。
/// </para>
/// </summary>
public class DataLoaderTests
{
    /// <summary>
    /// 一份完全合法的配置：两个兵种、两种地形、一条学说（含 A/B 两条专精，预算合计恰为 4）、
    /// 两张卡牌、两个规模档位。枚举以字符串序列化（Req 20.5）。
    /// </summary>
    private const string ValidJson = """
    {
      "units": [
        { "typeKey": "inf", "class": "LineHold", "attack": 6, "defense": 4, "movement": 4, "resilience": 2, "vision": 2, "supportRange": 0, "supportShift": null, "defaultFlags": "None" },
        { "typeKey": "art", "class": "FireSupport", "attack": 9, "defense": 3, "movement": 3, "resilience": 3, "vision": 2, "supportRange": 3, "supportShift": 1 }
      ],
      "terrains": [
        { "terrain": "Plain", "moveCost": 1, "defensiveDrm": 0, "forbiddenClasses": null, "enterAndStop": false },
        { "terrain": "Swamp", "moveCost": 3, "defensiveDrm": 1, "forbiddenClasses": [ "Elite" ], "enterAndStop": true }
      ],
      "doctrines": [
        {
          "key": "doc.assault",
          "modifiers": null,
          "signatureFlags": "None",
          "specializations": [
            { "key": "a", "modifiers": [ { "stat": "Attack", "value": 2, "pointCost": 4 } ], "budget": 4 },
            { "key": "b", "modifiers": [ { "stat": "Defense", "value": 2, "pointCost": 4 } ], "budget": 4 }
          ]
        }
      ],
      "cards": [
        { "id": 1, "timing": "Plan", "cpCost": 2, "targetsEnemy": false, "requiresRevealedTarget": false, "firePowerShift": 0, "onUnrevealed": "Void" },
        { "id": 2, "timing": "Reaction", "cpCost": 1, "targetsEnemy": true, "requiresRevealedTarget": true, "firePowerShift": 1, "onUnrevealed": "Reduce" }
      ],
      "scales": [
        { "scale": "Small", "cpPerTurn": 3, "cpMax": 9, "deckSize": 20, "handLimit": 5 },
        { "scale": "Large", "cpPerTurn": 6, "cpMax": 18, "deckSize": 40, "handLimit": 8 }
      ]
    }
    """;

    // ---- 合法数据成功加载（Req 20.1） ----

    [Fact]
    public void Load_ValidConfig_ReturnsGameDataWithExpectedCounts()
    {
        var data = DataLoader.Load(ValidJson);

        Assert.NotNull(data);
        Assert.Equal(2, data.UnitTemplates.Count);
        Assert.Equal(2, data.Terrain.Terrains.Count);
        Assert.Single(data.Doctrines);
        Assert.Equal(2, data.Doctrines[0].Specializations.Count);
        Assert.Equal(2, data.Cards.Count);
        Assert.Equal(2, data.ScaleProfiles.Count);
    }

    // ---- 兵种初始攻/防必须整除韧性 N（Req 8.3） ----

    [Fact]
    public void Load_UnitAttackNotDivisibleByResilience_Throws()
    {
        // attack 7 不能被 resilience 2 整除。
        var json = """
        {
          "units": [ { "typeKey": "inf", "class": "LineHold", "attack": 7, "defense": 4, "movement": 4, "resilience": 2, "vision": 2, "supportRange": 0 } ],
          "terrains": [], "doctrines": [], "cards": [], "scales": []
        }
        """;

        Assert.Throws<InvalidDataException>(() => DataLoader.Load(json));
    }

    [Fact]
    public void Load_UnitDefenseNotDivisibleByResilience_Throws()
    {
        // defense 5 不能被 resilience 3 整除。
        var json = """
        {
          "units": [ { "typeKey": "art", "class": "FireSupport", "attack": 9, "defense": 5, "movement": 3, "resilience": 3, "vision": 2, "supportRange": 3 } ],
          "terrains": [], "doctrines": [], "cards": [], "scales": []
        }
        """;

        Assert.Throws<InvalidDataException>(() => DataLoader.Load(json));
    }

    [Fact]
    public void Load_UnitNonPositiveResilience_Throws()
    {
        var json = """
        {
          "units": [ { "typeKey": "inf", "class": "LineHold", "attack": 6, "defense": 4, "movement": 4, "resilience": 0, "vision": 2, "supportRange": 0 } ],
          "terrains": [], "doctrines": [], "cards": [], "scales": []
        }
        """;

        Assert.Throws<InvalidDataException>(() => DataLoader.Load(json));
    }

    // ---- 非法 / 缺失 / 重复 / 负值配置（Req 20.2） ----

    [Fact]
    public void Load_MissingRequiredSection_Throws()
    {
        // 缺失 "scales" 分节。
        var json = """
        {
          "units": [], "terrains": [], "doctrines": [], "cards": []
        }
        """;

        Assert.Throws<InvalidDataException>(() => DataLoader.Load(json));
    }

    [Fact]
    public void Load_NullEntryInSection_Throws()
    {
        var json = """
        {
          "units": [ null ], "terrains": [], "doctrines": [], "cards": [], "scales": []
        }
        """;

        Assert.Throws<InvalidDataException>(() => DataLoader.Load(json));
    }

    [Fact]
    public void Load_DuplicateUnitTypeKey_Throws()
    {
        var json = """
        {
          "units": [
            { "typeKey": "inf", "class": "LineHold", "attack": 6, "defense": 4, "movement": 4, "resilience": 2, "vision": 2, "supportRange": 0 },
            { "typeKey": "inf", "class": "Elite", "attack": 8, "defense": 6, "movement": 4, "resilience": 2, "vision": 2, "supportRange": 0 }
          ],
          "terrains": [], "doctrines": [], "cards": [], "scales": []
        }
        """;

        Assert.Throws<InvalidDataException>(() => DataLoader.Load(json));
    }

    [Fact]
    public void Load_NegativeCardCpCost_Throws()
    {
        var json = """
        {
          "units": [], "terrains": [], "doctrines": [],
          "cards": [ { "id": 1, "timing": "Plan", "cpCost": -1, "targetsEnemy": false, "requiresRevealedTarget": false, "firePowerShift": 0, "onUnrevealed": "Void" } ],
          "scales": []
        }
        """;

        Assert.Throws<InvalidDataException>(() => DataLoader.Load(json));
    }

    [Fact]
    public void Load_DoctrineBudgetNotFour_Throws()
    {
        // 专精点数合计 3 + 学说 0 = 3 ≠ 4（Req 16.2、16.5，经 DoctrineSystem.Validate 复用校验）。
        var json = """
        {
          "units": [], "terrains": [],
          "doctrines": [
            {
              "key": "doc.bad",
              "modifiers": null,
              "signatureFlags": "None",
              "specializations": [
                { "key": "a", "modifiers": [ { "stat": "Attack", "value": 1, "pointCost": 3 } ], "budget": 3 },
                { "key": "b", "modifiers": [ { "stat": "Defense", "value": 1, "pointCost": 3 } ], "budget": 3 }
              ]
            }
          ],
          "cards": [], "scales": []
        }
        """;

        Assert.Throws<InvalidDataException>(() => DataLoader.Load(json));
    }

    [Fact]
    public void Load_DoctrineWithWrongSpecializationCount_Throws()
    {
        // 只有 1 条专精分支，须恰为 2（Req 16.5）。
        var json = """
        {
          "units": [], "terrains": [],
          "doctrines": [
            {
              "key": "doc.single",
              "modifiers": null,
              "signatureFlags": "None",
              "specializations": [
                { "key": "a", "modifiers": [ { "stat": "Attack", "value": 1, "pointCost": 4 } ], "budget": 4 }
              ]
            }
          ],
          "cards": [], "scales": []
        }
        """;

        Assert.Throws<InvalidDataException>(() => DataLoader.Load(json));
    }

    [Fact]
    public void Load_NegativeScaleParameter_Throws()
    {
        var json = """
        {
          "units": [], "terrains": [], "doctrines": [], "cards": [],
          "scales": [ { "scale": "Small", "cpPerTurn": -3, "cpMax": 9, "deckSize": 20, "handLimit": 5 } ]
        }
        """;

        Assert.Throws<InvalidDataException>(() => DataLoader.Load(json));
    }

    [Fact]
    public void Load_DuplicateTerrainType_Throws()
    {
        var json = """
        {
          "units": [],
          "terrains": [
            { "terrain": "Plain", "moveCost": 1, "defensiveDrm": 0, "forbiddenClasses": null, "enterAndStop": false },
            { "terrain": "Plain", "moveCost": 2, "defensiveDrm": 1, "forbiddenClasses": null, "enterAndStop": false }
          ],
          "doctrines": [], "cards": [], "scales": []
        }
        """;

        Assert.Throws<InvalidDataException>(() => DataLoader.Load(json));
    }

    // ---- 空白 / 非法 JSON：校验过程失败一律视为非法（Req 20.2b） ----

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Load_EmptyOrWhitespace_Throws(string json)
    {
        Assert.Throws<InvalidDataException>(() => DataLoader.Load(json));
    }

    [Fact]
    public void Load_MalformedJson_ThrowsInvalidData()
    {
        // 语法非法的 JSON：解析失败被归一化为 InvalidDataException（不外泄 JsonException）。
        var json = "{ this is not valid json ";

        Assert.Throws<InvalidDataException>(() => DataLoader.Load(json));
    }

    [Fact]
    public void Load_JsonNullLiteral_Throws()
    {
        Assert.Throws<InvalidDataException>(() => DataLoader.Load("null"));
    }
}
