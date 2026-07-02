using System;
using System.Collections.Generic;
using CsCheck;
using Xjdl.Core.Combat;
using Xjdl.Core.State;

namespace Xjdl.Core.Tests.Combat;

// Feature: core-rules-engine, Property 23: 交战表读表匹配文档模型
//
// 本测试以 docs/01-战斗机制.md〈表一/表二/表三〉为 golden 参照模型：三张表在此**独立重新誊写**
// （与被测实现 CombatTables 各自独立地从同一文档转录），并对整个定义域逐格（cell-by-cell）断言
// ReadTable 的输出等于文档单元格的 ResultCode——这是 model-based 属性测试中最强的形式（全域覆盖）。
// 除全域穷举断言外，另含一段 CsCheck 随机采样（≥100 迭代）与结构不变量断言（Req 7.1、7.5）。
public class CombatTablesReadProperties
{
    // ================= 独立 golden 参照模型（从 docs/01 誊写）=========================
    //
    // 结果代码粒度同 ResultCode：仅区分类型、不携带 N（攻-1/攻-2 同为 AttackerN，
    // 守-1退/守-2退 同为 DefenderNRetreat，劣-2/劣-3 同为 LoserN 等）。
    // 行索引 = 火力比档位；列索引 = 3D6 骰段（0..4，见 GoldenBandIndex）。

    // 表一：常规进攻（docs/01〈表一〉）。行 0..5 对应火力比档位 <1:1 / 1:1 / 1.5:1 / 2:1 / 3:1 / 4:1+。
    private static readonly ResultCode[,] GoldenTable1 =
    {
        // <1:1：攻-2  攻-2  攻-1  攻-1  互-1
        { ResultCode.AttackerN, ResultCode.AttackerN, ResultCode.AttackerN, ResultCode.AttackerN, ResultCode.MutualN },
        // 1:1：攻-1  互-1  互-1  守-1  守-1退
        { ResultCode.AttackerN, ResultCode.MutualN, ResultCode.MutualN, ResultCode.DefenderN, ResultCode.DefenderNRetreat },
        // 1.5:1：互-1  守-1  守-1  守-1退  守-2退
        { ResultCode.MutualN, ResultCode.DefenderN, ResultCode.DefenderN, ResultCode.DefenderNRetreat, ResultCode.DefenderNRetreat },
        // 2:1：守-1  守-1  守-1退  守-2退  守歼
        { ResultCode.DefenderN, ResultCode.DefenderN, ResultCode.DefenderNRetreat, ResultCode.DefenderNRetreat, ResultCode.DefenderAnnihilate },
        // 3:1：守-1退  守-1退  守-2退  守歼  守歼
        { ResultCode.DefenderNRetreat, ResultCode.DefenderNRetreat, ResultCode.DefenderNRetreat, ResultCode.DefenderAnnihilate, ResultCode.DefenderAnnihilate },
        // 4:1+：守-1退  守-2退  守歼  守歼  守歼
        { ResultCode.DefenderNRetreat, ResultCode.DefenderNRetreat, ResultCode.DefenderAnnihilate, ResultCode.DefenderAnnihilate, ResultCode.DefenderAnnihilate },
    };

    // 表二：对攻（docs/01〈表二〉）。行 0..4 对应火力比档位 1:1 / 1.5:1 / 2:1 / 3:1 / 4:1+（恒 ≥ 1:1）。
    private static readonly ResultCode[,] GoldenTable2 =
    {
        // 1:1：互-2  互-2  互-1  劣-2  劣-2退
        { ResultCode.MutualN, ResultCode.MutualN, ResultCode.MutualN, ResultCode.LoserN, ResultCode.LoserNRetreat },
        // 1.5:1：互-2  互-1  劣-2  劣-2退  劣-3退
        { ResultCode.MutualN, ResultCode.MutualN, ResultCode.LoserN, ResultCode.LoserNRetreat, ResultCode.LoserNRetreat },
        // 2:1：互-1  劣-2  劣-2退  劣-3退  劣歼
        { ResultCode.MutualN, ResultCode.LoserN, ResultCode.LoserNRetreat, ResultCode.LoserNRetreat, ResultCode.LoserAnnihilate },
        // 3:1：劣-2  劣-2退  劣-3退  劣歼  劣歼
        { ResultCode.LoserN, ResultCode.LoserNRetreat, ResultCode.LoserNRetreat, ResultCode.LoserAnnihilate, ResultCode.LoserAnnihilate },
        // 4:1+：劣-2退  劣-3退  劣歼  劣歼  劣歼
        { ResultCode.LoserNRetreat, ResultCode.LoserNRetreat, ResultCode.LoserAnnihilate, ResultCode.LoserAnnihilate, ResultCode.LoserAnnihilate },
    };

    // 表三：遭遇战（docs/01〈表三〉）。行 0..4 对应火力比档位 1:1 / 1.5:1 / 2:1 / 3:1 / 4:1+（恒 ≥ 1:1）。
    // 约束：1:1 行仅「僵/互-1」两种对称结果（Req 7.5）；全表不含单方被歼（无 劣歼）。
    private static readonly ResultCode[,] GoldenTable3 =
    {
        // 1:1：僵  僵  僵  僵  互-1
        { ResultCode.Stalemate, ResultCode.Stalemate, ResultCode.Stalemate, ResultCode.Stalemate, ResultCode.MutualN },
        // 1.5:1：僵  僵  僵  退  劣-1退
        { ResultCode.Stalemate, ResultCode.Stalemate, ResultCode.Stalemate, ResultCode.Withdraw, ResultCode.LoserNRetreat },
        // 2:1：僵  僵  退  劣-1退  劣-1退
        { ResultCode.Stalemate, ResultCode.Stalemate, ResultCode.Withdraw, ResultCode.LoserNRetreat, ResultCode.LoserNRetreat },
        // 3:1：僵  退  退  劣-1退  劣-2退
        { ResultCode.Stalemate, ResultCode.Withdraw, ResultCode.Withdraw, ResultCode.LoserNRetreat, ResultCode.LoserNRetreat },
        // 4:1+：退  退  劣-1退  劣-1退  劣-2退
        { ResultCode.Withdraw, ResultCode.Withdraw, ResultCode.LoserNRetreat, ResultCode.LoserNRetreat, ResultCode.LoserNRetreat },
    };

    // 表一/二/三专属的「单方结果」代码：表一独有（守/攻方向），断言表二/三绝不产出。
    private static readonly HashSet<ResultCode> Table1OnlyCodes = new()
    {
        ResultCode.AttackerN,
        ResultCode.DefenderN,
        ResultCode.DefenderNRetreat,
        ResultCode.DefenderAnnihilate,
    };

    // 骰轴定义域：正常 3..18；地形 DRM 可推出该范围（两端钳制归段），故取更宽的 [-5, 25]。
    private const int RollMin = -5;
    private const int RollMax = 25;

    /// <summary>
    /// 独立誊写自 docs/01 的骰段归并：3-6→0、7-8→1、9-12→2、13-14→3、15-18→4；
    /// 两端开区间（≤6 归 0、≥15 归 4）吸收越界读数。此为测试侧的独立参照，不复用实现。
    /// </summary>
    private static int GoldenBandIndex(int adjustedRoll)
    {
        if (adjustedRoll <= 6)
        {
            return 0;
        }

        if (adjustedRoll <= 8)
        {
            return 1;
        }

        if (adjustedRoll <= 12)
        {
            return 2;
        }

        if (adjustedRoll <= 14)
        {
            return 3;
        }

        return 4;
    }

    /// <summary>某表某列（火力比档位）是否落在该表定义域内。表一 0..5；表二/三 1..5。</summary>
    private static bool InDomain(CombatTable table, int column) =>
        table == CombatTable.RegularAttack
            ? column >= 0 && column <= 5
            : column >= 1 && column <= 5;

    /// <summary>golden 参照单元格：按表类型与档位/骰段索引查独立誊写表。</summary>
    private static ResultCode GoldenCell(CombatTable table, int column, int band) => table switch
    {
        // 表一：列直接是行索引 0..5。
        CombatTable.RegularAttack => GoldenTable1[column, band],
        // 表二/三：档位 1..5 映射到行 0..4。
        CombatTable.MutualAttack => GoldenTable2[column - 1, band],
        CombatTable.Encounter => GoldenTable3[column - 1, band],
        _ => throw new ArgumentOutOfRangeException(nameof(table)),
    };

    /// <summary>
    /// Property 23: 交战表读表匹配文档模型（全域穷举断言 · golden model 逐格对照）。
    /// 对定义域内每个 (table, column, adjustedRoll)，<see cref="CombatTables.ReadTable"/> 返回的
    /// ResultCode 必等于 docs/01〈表一/表二/表三〉对应单元格的值（以独立誊写的三表为 golden 参照）。
    /// 骰轴覆盖 [-5, 25]（含地形 DRM 越界钳制），列轴覆盖各表全部合法档位。
    /// **Validates: Requirements 7.1, 7.5**
    /// </summary>
    [Fact]
    public void ReadTable_MatchesGoldenModel_OverEntireDomain()
    {
        foreach (CombatTable table in Enum.GetValues(typeof(CombatTable)))
        {
            for (var column = 0; column <= 5; column++)
            {
                if (!InDomain(table, column))
                {
                    continue;
                }

                for (var roll = RollMin; roll <= RollMax; roll++)
                {
                    var expected = GoldenCell(table, column, GoldenBandIndex(roll));
                    var actual = CombatTables.ReadTable(table, column, roll);

                    Assert.Equal(expected, actual);
                }
            }
        }
    }

    /// <summary>
    /// Property 23（随机采样 · ≥100 迭代）：在定义域内随机取 (table, column, roll)，
    /// ReadTable 输出恒等于 golden 参照单元格。补充全域穷举，作为持续随机回归。
    /// **Validates: Requirements 7.1, 7.5**
    /// </summary>
    [Fact]
    public void ReadTable_MatchesGoldenModel_Randomized()
    {
        // 定义域内随机采样：表类型任意；列按表定义域约束（表一 0..5，表二/三 1..5）；骰值覆盖越界带。
        var gen =
            from table in Gen.Int[0, 2].Select(i => (CombatTable)i)
            from column in Gen.Int[table == CombatTable.RegularAttack ? 0 : 1, 5]
            from roll in Gen.Int[RollMin, RollMax]
            select (table, column, roll);

        gen.Sample(
            t =>
            {
                var expected = GoldenCell(t.table, t.column, GoldenBandIndex(t.roll));
                return CombatTables.ReadTable(t.table, t.column, t.roll) == expected;
            },
            iter: 1000);
    }

    /// <summary>
    /// Property 23（结构不变量 · Req 7.5）：表三 1:1 档位（column == 1）在所有骰段上
    /// 只产出对称结果——<see cref="ResultCode.Stalemate"/>（僵）或 <see cref="ResultCode.MutualN"/>（互-1），
    /// 绝不产生优劣归属（无 Withdraw/LoserNRetreat 等单方结果）。
    /// **Validates: Requirements 7.5**
    /// </summary>
    [Fact]
    public void ReadTable_Encounter1To1_YieldsOnlySymmetricResults()
    {
        for (var roll = RollMin; roll <= RollMax; roll++)
        {
            var result = CombatTables.ReadTable(CombatTable.Encounter, column: 1, adjustedRoll: roll);

            Assert.True(
                result is ResultCode.Stalemate or ResultCode.MutualN,
                $"遭遇战 1:1（roll={roll}）应仅为 僵/互-1，实际为 {result}。");
        }
    }

    /// <summary>
    /// Property 23（结构不变量）：表二/表三在其整个定义域内绝不产出表一独有的单方结果代码
    /// （攻-N/守-N/守-N退/守歼）——对攻与遭遇战无攻守方之分。
    /// **Validates: Requirements 7.1**
    /// </summary>
    [Fact]
    public void ReadTable_MutualAndEncounter_NeverYieldTable1OnlyCodes()
    {
        foreach (var table in new[] { CombatTable.MutualAttack, CombatTable.Encounter })
        {
            for (var column = 1; column <= 5; column++)
            {
                for (var roll = RollMin; roll <= RollMax; roll++)
                {
                    var result = CombatTables.ReadTable(table, column, roll);

                    Assert.False(
                        Table1OnlyCodes.Contains(result),
                        $"{table}（column={column}, roll={roll}）不应产出表一独有代码 {result}。");
                }
            }
        }
    }

    /// <summary>
    /// Property 23（定义域边界）：越出该表火力比档位定义域的列抛
    /// <see cref="ArgumentOutOfRangeException"/>——表一域为 0..5，表二/三域为 1..5（列 0 越界）。
    /// **Validates: Requirements 7.1**
    /// </summary>
    [Fact]
    public void ReadTable_OutOfDomainColumn_Throws()
    {
        var gen =
            from table in Gen.Int[0, 2].Select(i => (CombatTable)i)
            from column in Gen.Int[-3, 8]
            from roll in Gen.Int[RollMin, RollMax]
            select (table, column, roll);

        gen.Sample(
            t =>
            {
                if (InDomain(t.table, t.column))
                {
                    // 域内：不得抛出，且与 golden 一致。
                    var expected = GoldenCell(t.table, t.column, GoldenBandIndex(t.roll));
                    return CombatTables.ReadTable(t.table, t.column, t.roll) == expected;
                }

                // 域外列：必抛 ArgumentOutOfRangeException。
                try
                {
                    CombatTables.ReadTable(t.table, t.column, t.roll);
                    return false;
                }
                catch (ArgumentOutOfRangeException)
                {
                    return true;
                }
            },
            iter: 1000);
    }
}
