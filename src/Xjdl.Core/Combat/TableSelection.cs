using Xjdl.Core.State;

namespace Xjdl.Core.Combat;

/// <summary>
/// 一处接触中单方的姿态描述（选表所需的最小输入）。
/// 只关心两件事：该方本回合的命令，以及其「进攻准备」是否指向本次接触的对方。
/// 见 docs/01-战斗机制.md〈选表〉与 design.md〈CombatResolver · SelectTable〉。
/// </summary>
/// <param name="Command">该方在阶段 0 领取的命令（<see cref="Command.Move"/>/<see cref="Command.AttackPrep"/>/<see cref="Command.Hold"/>）。</param>
/// <param name="AttackPrepTargetsOpponent">
/// 当且仅当 <see cref="Command"/> 为 <see cref="Command.AttackPrep"/> 且其目标就是本次接触的对方时为真。
/// 若下达进攻准备但指向别处（非本对方），则为假，视为对本次接触无进攻准备。
/// </param>
public readonly record struct ContactSide(Command Command, bool AttackPrepTargetsOpponent)
{
    /// <summary>该方是否对本次接触的对方发起有准备的进攻。</summary>
    public bool IsAttackingOpponent =>
        Command == Command.AttackPrep && AttackPrepTargetsOpponent;
}

/// <summary>
/// 一处接触的双方姿态（选表的最小输入）。两侧顺序无关：
/// 选表结果只取决于「各方是否对对方进攻准备」，不依赖 <see cref="First"/>/<see cref="Second"/> 的排列。
/// </summary>
/// <param name="First">接触的一方。</param>
/// <param name="Second">接触的另一方。</param>
public readonly record struct Contact(ContactSide First, ContactSide Second);

/// <summary>
/// 阶段 3 选表：按一处接触双方姿态确定使用哪张交战表（Req 5.1、5.2、5.3）。
/// </summary>
public static class TableSelection
{
    /// <summary>
    /// 依据接触双方姿态选择交战表：
    /// <list type="bullet">
    /// <item>一方进攻准备指向对方、另一方据守/移动（无进攻准备）→ <see cref="CombatTable.RegularAttack"/>（表一），进攻方即持进攻准备者（Req 5.1）。</item>
    /// <item>双方互指进攻准备 → <see cref="CombatTable.MutualAttack"/>（表二）（Req 5.2）。</item>
    /// <item>双方均无进攻准备却接触（移入同格/相撞）→ <see cref="CombatTable.Encounter"/>（表三）（Req 5.3）。</item>
    /// </list>
    /// </summary>
    /// <param name="contact">接触双方的姿态。</param>
    /// <returns>本次接触应使用的交战表。</returns>
    public static CombatTable SelectTable(Contact contact)
    {
        var firstAttacks = contact.First.IsAttackingOpponent;
        var secondAttacks = contact.Second.IsAttackingOpponent;

        if (firstAttacks && secondAttacks)
        {
            // 双方互指进攻准备 → 表二对攻（Req 5.2）。
            return CombatTable.MutualAttack;
        }

        if (firstAttacks || secondAttacks)
        {
            // 恰有一方进攻准备指向对方，另一方据守/移动 → 表一进攻（Req 5.1）。
            return CombatTable.RegularAttack;
        }

        // 双方均无进攻准备而接触 → 表三遭遇（Req 5.3）。
        return CombatTable.Encounter;
    }
}
