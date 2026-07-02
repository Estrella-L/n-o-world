using CsCheck;
using Xjdl.Core.Combat;
using Xjdl.Core.State;

namespace Xjdl.Core.Tests.Combat;

// Feature: core-rules-engine, Property 17: 选表映射
public class TableSelectionProperties
{
    /// <summary>阶段 0 命令（Move/AttackPrep/Hold）均匀取样。</summary>
    private static readonly Gen<Command> GenCommand =
        Gen.Int[0, 2].Select(i => (Command)i);

    /// <summary>
    /// 任意接触方姿态：命令随机、进攻准备是否指向对方随机。
    /// 覆盖「下达进攻准备但不指向本对方」（AttackPrep + false）等退化组合。
    /// </summary>
    private static readonly Gen<ContactSide> GenSide =
        from cmd in GenCommand
        from targetsOpponent in Gen.Bool
        select new ContactSide(cmd, targetsOpponent);

    /// <summary>任意一处接触（双方姿态独立随机）。</summary>
    private static readonly Gen<Contact> GenContact =
        from first in GenSide
        from second in GenSide
        select new Contact(first, second);

    /// <summary>
    /// Property 17: 选表映射。
    /// 对任意接触双方姿态，<see cref="TableSelection.SelectTable"/> 依据「各方是否对对方进攻准备」：
    ///  1) 双方均进攻准备指向对方 → <see cref="CombatTable.MutualAttack"/>（表二，Req 5.2）；
    ///  2) 恰有一方进攻准备指向对方（另一方据守/移动/进攻准备但不指向本对方）→ <see cref="CombatTable.RegularAttack"/>（表一，Req 5.1）；
    ///  3) 双方均无进攻准备而接触 → <see cref="CombatTable.Encounter"/>（表三，Req 5.3）。
    /// **Validates: Requirements 5.1, 5.2, 5.3**
    /// </summary>
    [Fact]
    public void SelectTable_MapsPosturesToCombatTable()
    {
        GenContact.Sample(contact =>
        {
            var firstAttacks = contact.First.IsAttackingOpponent;
            var secondAttacks = contact.Second.IsAttackingOpponent;

            var expected = (firstAttacks, secondAttacks) switch
            {
                (true, true) => CombatTable.MutualAttack,
                (true, false) => CombatTable.RegularAttack,
                (false, true) => CombatTable.RegularAttack,
                (false, false) => CombatTable.Encounter,
            };

            return TableSelection.SelectTable(contact) == expected;
        }, iter: 1000);
    }
}
