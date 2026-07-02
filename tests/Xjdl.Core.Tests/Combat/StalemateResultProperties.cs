using System;
using CsCheck;
using Xjdl.Core.Combat;
using Xjdl.Core.State;
using Xjdl.Core.Tests.Support;

namespace Xjdl.Core.Tests.Combat;

// Feature: core-rules-engine, Property 25: 「僵」结果无位移无战损
//
// Req 7.3：WHERE 结果为「僵」（仅表三）, THE CombatResolver SHALL 使双方均停在原地、
// 均不进入冲突格且均无战损。
//
// ---- 建模选择（Modeling choice）----------------------------------------------------
// 完整流水线（阶段 6/7/8 把 ResultCode 应用到 GameState 并实施战损/位移）在任务 15.20 落地，
// 此刻尚不存在。因此本属性在**当前可用的纯函数层**对「僵」语义的两个不变量分别建模断言：
//
//   (A) 「仅表三」——结构不变量。以 CombatTables.ReadTable 覆盖三表全定义域穷举：
//       Stalemate 只可能由 Encounter（表三）产出，表一/表二在任何 (column, roll) 下都不产出。
//       这锁定了 Req 7.3 括注「（仅表三）」的前提。
//
//   (B) 「无战损、无位移」——语义不变量。把「僵」对受影响方的**应掉战损次数建模为 0**
//       （StalemateCasualties() == 0，见下），再用现有纯函数 Casualty.ApplyCasualties(unit, 0)
//       验证：以 0 战损结算后单位的 攻/防/剩余韧性 全部**不变**（无战损），且 Position 不被触碰
//       （ApplyCasualties 的 `with` 只涉及 Attack/Defense/ResilienceLeft，从不改 Position，
//       故「停在原地、不进入冲突格」在构造上成立——无位移）。双方对称，故对任意单位成立即证双方成立。
//
// 待 15.20 的整局应用落地后，可在其之上追加端到端断言（GameState 前后单位位置与数值逐一相等）；
// 本文件的两条不变量将作为该端到端属性的下层引理保持有效。
public class StalemateResultProperties
{
    // 骰轴定义域：正常 3..18，地形 DRM 可将读数推出该范围（两端钳制归段），取更宽的 [-5, 25]。
    private const int RollMin = -5;
    private const int RollMax = 25;

    /// <summary>
    /// 「僵」对受影响方的应掉战损次数建模：恒为 0（Req 7.3「均无战损」）。
    /// 这是本测试对 Stalemate 战损语义的显式模型；流水线落地后应与其战损映射一致。
    /// </summary>
    private static int StalemateCasualties() => 0;

    /// <summary>某表某列（火力比档位）是否落在该表定义域内。表一 0..5；表二/三 1..5。</summary>
    private static bool InDomain(CombatTable table, int column) =>
        table == CombatTable.RegularAttack
            ? column >= 0 && column <= 5
            : column >= 1 && column <= 5;

    /// <summary>
    /// Property 25（结构不变量 · 全域穷举）：跨三张交战表的整个定义域，
    /// <see cref="ResultCode.Stalemate"/>（僵）**只**可能出现在表三（<see cref="CombatTable.Encounter"/>），
    /// 表一/表二在任何合法 (column, adjustedRoll) 下都绝不产出「僵」。锁定 Req 7.3「（仅表三）」前提。
    /// **Validates: Requirements 7.3**
    /// </summary>
    [Fact]
    public void Stalemate_AppearsOnlyInEncounterTable_OverEntireDomain()
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

                    if (result == ResultCode.Stalemate)
                    {
                        Assert.Equal(CombatTable.Encounter, table);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Property 25（语义不变量 · ≥100 迭代）：对任意合法单位，把「僵」建模为 0 次战损
    /// （<see cref="StalemateCasualties"/>）后经 <see cref="Casualty.ApplyCasualties"/> 结算，
    /// 其 攻/防/剩余韧性 与位置(<see cref="UnitState.Position"/>) 全部保持不变——即「无战损、无位移」。
    /// 双方对称，故对任意一方单位成立即证双方成立。
    /// **Validates: Requirements 7.3**
    /// </summary>
    [Fact]
    public void Stalemate_LeavesUnitUnchanged_NoCasualtyNoDisplacement()
    {
        Generators.UnitState.Sample(
            unit =>
            {
                var after = Casualty.ApplyCasualties(unit, StalemateCasualties());

                // 无战损：攻/防/剩余韧性三值全部不变。
                var noCasualty =
                    after.Attack == unit.Attack &&
                    after.Defense == unit.Defense &&
                    after.ResilienceLeft == unit.ResilienceLeft;

                // 无位移：位置保持原地（构造上 ApplyCasualties 从不改 Position）。
                var noDisplacement = after.Position == unit.Position;

                return noCasualty && noDisplacement;
            },
            iter: 1000);
    }

    /// <summary>
    /// Property 25（示例断言）：随取一个具体单位，验证「僵」（0 战损）既不减攻防韧性也不移动，
    /// 作为随机属性之外的可读回归锚点。
    /// **Validates: Requirements 7.3**
    /// </summary>
    [Fact]
    public void Stalemate_ConcreteUnit_UnchangedAndInPlace()
    {
        var pos = new Xjdl.Core.Hex.HexCoord(2, -1);
        var unit = new UnitState(
            new UnitId(7),
            Side.Blue,
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

        var after = Casualty.ApplyCasualties(unit, StalemateCasualties());

        Assert.Equal(unit.Attack, after.Attack);
        Assert.Equal(unit.Defense, after.Defense);
        Assert.Equal(unit.ResilienceLeft, after.ResilienceLeft);
        Assert.Equal(pos, after.Position);
    }
}
