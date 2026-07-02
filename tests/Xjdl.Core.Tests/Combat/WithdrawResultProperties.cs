using System;
using CsCheck;
using Xjdl.Core.Combat;
using Xjdl.Core.State;
using Xjdl.Core.Tests.Support;

namespace Xjdl.Core.Tests.Combat;

// Feature: core-rules-engine, Property 26: 「退」结果无战损且位置转移
//
// Req 7.4：WHERE 结果为「退」（仅表三）, THE CombatResolver SHALL 使劣势方撤出一格、
// 优势方推进占领该腾空格，且双方战损均为 0（位置转移而非战损）。
//
// ---- 建模选择（Modeling choice）----------------------------------------------------
// 完整流水线（阶段 6/7/8 把 ResultCode 应用到 GameState 并实施战损/撤退/推进位移）在任务
// 15.20 落地，此刻尚不存在。因此本属性在**当前可用的纯函数层**对「退」语义中可判定的不变量
// 分别建模断言（与任务 9.18「僵」测试同构）：
//
//   (A) 「仅表三」——结构不变量。以 CombatTables.ReadTable 覆盖三表全定义域穷举：
//       Withdraw 只可能由 Encounter（表三）产出，表一/表二在任何 (column, roll) 下都不产出。
//       这锁定了 Req 7.4 括注「（仅表三）」的前提。
//
//   (B) 「双方无战损」——语义不变量。把「退」对劣势方与优势方的**应掉战损次数均建模为 0**
//       （WithdrawCasualties() == 0，见下），再用现有纯函数 Casualty.ApplyCasualties(unit, 0)
//       验证：以 0 战损结算后单位的 攻/防/剩余韧性 全部**不变**（无战损）。双方同样为 0，
//       故对任意一方单位成立即证双方成立。
//
// ---- 关于「位置转移」（displacement）--------------------------------------------------
// 「退」与「僵」的关键差异在于**位置转移**：劣势方撤出一格、优势方推进占领。该位移属于
// 阶段 7（撤退 ChooseRetreatCell）与阶段 8（推进 Advance）在 GameState 上的整局应用，
// 由任务 15.20 串接落地；此处的纯函数层（Casualty.ApplyCasualties）刻意**不触碰** Position
// （其 `with` 仅涉及 Attack/Defense/ResilienceLeft），因此位移不是本层能断言的对象。
// 本文件断言「退」在结算层的**零战损不变量**，并显式记录位置转移的接线属于任务 15.20；
// 待 15.20 落地后可在其之上追加端到端断言（劣势方离开原格、优势方进入该腾空格、双方数值不变）。
// 本文件的零战损不变量将作为该端到端属性的下层引理保持有效。
public class WithdrawResultProperties
{
    // 骰轴定义域：正常 3..18，地形 DRM 可将读数推出该范围（两端钳制归段），取更宽的 [-5, 25]。
    private const int RollMin = -5;
    private const int RollMax = 25;

    /// <summary>
    /// 「退」对受影响方（劣势方撤离、优势方推进）的应掉战损次数建模：恒为 0（Req 7.4「双方战损均为 0」）。
    /// 这是本测试对 Withdraw 战损语义的显式模型；流水线落地后应与其战损映射一致。
    /// </summary>
    private static int WithdrawCasualties() => 0;

    /// <summary>某表某列（火力比档位）是否落在该表定义域内。表一 0..5；表二/三 1..5。</summary>
    private static bool InDomain(CombatTable table, int column) =>
        table == CombatTable.RegularAttack
            ? column >= 0 && column <= 5
            : column >= 1 && column <= 5;

    /// <summary>
    /// Property 26（结构不变量 · 全域穷举）：跨三张交战表的整个定义域，
    /// <see cref="ResultCode.Withdraw"/>（退）**只**可能出现在表三（<see cref="CombatTable.Encounter"/>），
    /// 表一/表二在任何合法 (column, adjustedRoll) 下都绝不产出「退」。锁定 Req 7.4「（仅表三）」前提。
    /// **Validates: Requirements 7.4**
    /// </summary>
    [Fact]
    public void Withdraw_AppearsOnlyInEncounterTable_OverEntireDomain()
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
                    var result = CombatTables.ReadTable(table, column, roll);

                    if (result == ResultCode.Withdraw)
                    {
                        Assert.Equal(CombatTable.Encounter, table);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Property 26（语义不变量 · ≥100 迭代）：对任意合法单位，把「退」建模为 0 次战损
    /// （<see cref="WithdrawCasualties"/>）后经 <see cref="Casualty.ApplyCasualties"/> 结算，
    /// 其 攻/防/剩余韧性 全部保持不变——即劣势方与优势方**双方均无战损**。
    /// 双方战损同为 0，故对任意一方单位成立即证双方成立。位置转移（劣势方撤出、优势方推进占领）
    /// 属阶段 7/8 的整局应用，由任务 15.20 接线，见文件头建模说明。
    /// **Validates: Requirements 7.4**
    /// </summary>
    [Fact]
    public void Withdraw_LeavesBothSidesUnhurt_ZeroCasualties()
    {
        Generators.UnitState.Sample(
            unit =>
            {
                var after = Casualty.ApplyCasualties(unit, WithdrawCasualties());

                // 无战损：攻/防/剩余韧性三值全部不变（劣势方与优势方对称同为 0 战损）。
                return
                    after.Attack == unit.Attack &&
                    after.Defense == unit.Defense &&
                    after.ResilienceLeft == unit.ResilienceLeft;
            },
            iter: 1000);
    }

    /// <summary>
    /// Property 26（示例断言）：随取一个具体单位，验证「退」（0 战损）既不减攻防也不减韧性，
    /// 作为随机属性之外的可读回归锚点。位置转移的整局接线属于任务 15.20。
    /// **Validates: Requirements 7.4**
    /// </summary>
    [Fact]
    public void Withdraw_ConcreteUnit_ZeroCasualtiesKeepsStats()
    {
        var pos = new Xjdl.Core.Hex.HexCoord(2, -1);
        var unit = new UnitState(
            new UnitId(9),
            Side.Red,
            "unit.infantry",
            UnitClass.LineHold,
            InitAttack: 6,
            InitDefense: 3,
            Resilience0: 3,
            Attack: 6,
            Defense: 3,
            ResilienceLeft: 3,
            Movement: 4,
            Vision: 2,
            SupportRange: 0,
            Position: pos,
            Command: Command.Hold,
            Flags: NightFlags.None);

        var after = Casualty.ApplyCasualties(unit, WithdrawCasualties());

        Assert.Equal(unit.Attack, after.Attack);
        Assert.Equal(unit.Defense, after.Defense);
        Assert.Equal(unit.ResilienceLeft, after.ResilienceLeft);
    }
}
