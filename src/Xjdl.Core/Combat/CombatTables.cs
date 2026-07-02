using Xjdl.Core.State;

namespace Xjdl.Core.Combat;

/// <summary>
/// docs/01-战斗机制.md〈表一/表二/表三〉的 golden 参照编码。
/// 三张交战表以「火力比档位（列）× 3D6 读数区间（骰段）」映射到 <see cref="ResultCode"/>，
/// 作为 Property 23（model-based golden 测试，任务 9.8）的对照模型（Req 7.1、7.3、7.4、7.5）。
///
/// <para><b>列（火力比档位）索引约定</b></para>
/// 本类采用一条统一的「火力比档位阶梯」，由弱到强排列，索引见 <see cref="RatioColumn"/>：
/// <list type="table">
///   <item><term>0 Below1To1</term><description>&lt;1:1（劣势），仅表一存在</description></item>
///   <item><term>1 Even</term><description>1:1（均势/对攻/遭遇平局）</description></item>
///   <item><term>2 Ratio3To2</term><description>1.5:1</description></item>
///   <item><term>3 Ratio2To1</term><description>2:1</description></item>
///   <item><term>4 Ratio3To1</term><description>3:1</description></item>
///   <item><term>5 Ratio4To1Plus</term><description>4:1 及以上</description></item>
/// </list>
/// 表一使用列 0..5；表二/表三的火力比恒 ≥ 1:1（高值方为分子），故仅使用列 1..5，列 0 越出定义域。
///
/// <para><b>与任务 9.3 的对接（并行开发）</b></para>
/// <see cref="FirePowerRatio.ToColumn"/> 仍为占位实现（任务 9.3）。本类假定 <c>ToColumn()</c>
/// 最终返回上述同一套档位索引（0..5，向下取整偏向防守，Req 6.4）。若 9.3 采用不同基准，
/// 需以本处 <see cref="RatioColumn"/> 定义为准进行调和。
///
/// <para><b>骰轴</b></para>
/// <paramref name="adjustedRoll"/> 为叠加地形防御 DRM 后的 3D6 读数（正常 3..18；DRM 可能推出该范围）。
/// 归入五个骰段区间：3-6 / 7-8 / 9-12 / 13-14 / 15-18，两端开区间以钳制方式吸收越界读数。
///
/// <para><b>结果代码的粒度</b></para>
/// <see cref="ResultCode"/> 仅区分结果「类型」，不携带战损次数 N（如「攻-1」「攻-2」同为
/// <see cref="ResultCode.AttackerN"/>；「守-1退」「守-2退」同为 <see cref="ResultCode.DefenderNRetreat"/>）。
/// N 的具体数值在战损结算处按文档〈结果代码表〉解释，超出本表编码范围。
/// </summary>
public static class CombatTables
{
    /// <summary>火力比档位阶梯索引（列轴），详见 <see cref="CombatTables"/> 类注释。</summary>
    public enum RatioColumn
    {
        /// <summary>&lt;1:1（劣势）——仅表一存在。</summary>
        Below1To1 = 0,

        /// <summary>1:1（均势 / 对攻 / 遭遇平局）。</summary>
        Even = 1,

        /// <summary>1.5:1。</summary>
        Ratio3To2 = 2,

        /// <summary>2:1。</summary>
        Ratio2To1 = 3,

        /// <summary>3:1。</summary>
        Ratio3To1 = 4,

        /// <summary>4:1 及以上。</summary>
        Ratio4To1Plus = 5,
    }

    // 表一：常规进攻表（一攻一守）。行 = 火力比档位 0..5，列 = 骰段 0..4。
    // 结果代码来源 docs/01〈表一〉：攻-N→AttackerN，互-N→MutualN，
    // 守-N→DefenderN，守-N退→DefenderNRetreat，守歼→DefenderAnnihilate。
    private static readonly ResultCode[,] Table1 =
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

    // 表二：对攻表（双方均进攻准备）。行 0..4 对应火力比档位 1..5（恒 ≥ 1:1，无 <1:1 行）。
    // 结果代码来源 docs/01〈表二〉：互-N→MutualN，劣-N→LoserN，劣-N退→LoserNRetreat，劣歼→LoserAnnihilate。
    private static readonly ResultCode[,] Table2 =
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

    // 表三：遭遇战表（双方均未准备）。行 0..4 对应火力比档位 1..5（恒 ≥ 1:1）。
    // 结果代码来源 docs/01〈表三〉：僵→Stalemate，退→Withdraw，互-1→MutualN，劣-N退→LoserNRetreat。
    // 约束：1:1 行仅出现「僵/互-1」这两种双方对称结果（Req 7.5），且全表不含单方被歼（劣歼）。
    private static readonly ResultCode[,] Table3 =
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

    /// <summary>
    /// 读表：给定交战表、火力比档位（列，见 <see cref="RatioColumn"/>）与调整后 3D6 读数，
    /// 返回对应 <see cref="ResultCode"/>（golden 参照）。
    /// </summary>
    /// <param name="table">交战表类型（表一/表二/表三）。</param>
    /// <param name="column">
    /// 火力比档位索引（0..5）。表一支持 0..5；表二/表三仅支持 1..5（火力比恒 ≥ 1:1）。
    /// </param>
    /// <param name="adjustedRoll">叠加地形 DRM 后的 3D6 读数（正常 3..18，越界按端点钳制归段）。</param>
    /// <returns>该单元格的结果代码。</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="table"/> 非法，或 <paramref name="column"/> 越出该表定义域。
    /// </exception>
    public static ResultCode ReadTable(CombatTable table, int column, int adjustedRoll)
    {
        var band = DiceBandIndex(adjustedRoll);

        switch (table)
        {
            case CombatTable.RegularAttack:
                if (column < 0 || column > 5)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(column), column, "表一火力比档位须为 0..5（含 <1:1）。");
                }

                return Table1[column, band];

            case CombatTable.MutualAttack:
                return Table2[RequireRatioRow(table, column), band];

            case CombatTable.Encounter:
                return Table3[RequireRatioRow(table, column), band];

            default:
                throw new ArgumentOutOfRangeException(nameof(table), table, "未知的交战表类型。");
        }
    }

    /// <summary>
    /// 把调整后的 3D6 读数归入五个骰段列：3-6→0、7-8→1、9-12→2、13-14→3、15-18→4。
    /// 两端为开区间：≤6 归段 0、≥15 归段 4，以吸收地形 DRM 造成的越界读数。
    /// </summary>
    private static int DiceBandIndex(int adjustedRoll)
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

    // 表二/表三：把统一档位索引（1..5）转换为数组行索引（0..4）；档位 0（<1:1）越出定义域。
    private static int RequireRatioRow(CombatTable table, int column)
    {
        if (column < (int)RatioColumn.Even || column > (int)RatioColumn.Ratio4To1Plus)
        {
            throw new ArgumentOutOfRangeException(
                nameof(column),
                column,
                $"{table} 的火力比恒 ≥ 1:1，档位须为 1..5（不含 <1:1）。");
        }

        return column - (int)RatioColumn.Even;
    }
}
