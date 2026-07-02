using System.Collections.Generic;
using System.Linq;
using CsCheck;
using Xjdl.Core.State;
using Xjdl.Core.Tests.Support;
using Xjdl.Core.Turn;

namespace Xjdl.Core.Tests.Turn;

// Feature: core-rules-engine, Property 47: 参战强制显形
/// <summary>
/// Property 47（参战强制显形）的属性测试。见 design.md〈Property 47〉与 docs/01〈战争迷雾〉。
/// 覆盖 <see cref="TurnPipeline.ForceIdentifiedForCombatants(FogView, IReadOnlySet{UnitId})"/>（Req 14.7）：
/// 机动后重算的可见度快照中，凡属于本回合参战（接触）集合的敌方单位，一律被强制置为
/// <see cref="Visibility.Identified"/>（无论其原本是 <see cref="Visibility.Hidden"/>、<see cref="Visibility.Spotted"/>
/// 还是 <see cref="Visibility.Identified"/>）；不在参战集合中的条目则保持其原可见度不变，且键集合不变。
/// </summary>
public class CombatantForcedIdentifiedProperties
{
    /// <summary>
    /// 任意 <see cref="FogConfig"/>：V+1 环开/关任意，夜晚视野除数覆盖 0（退化 fail-safe）与正常正值。
    /// </summary>
    private static readonly Gen<FogConfig> GenFogConfig =
        from blipRing in Gen.Bool
        from divisor in Gen.Int[0, 4]
        select new FogConfig(blipRing, divisor);

    /// <summary>
    /// Property 47: 参战强制显形。
    /// <para>
    /// 由合法 <see cref="GameState"/> 与任意 <see cref="FogConfig"/> 经
    /// <see cref="TurnPipeline.ComputeFogSnapshot"/> 得到双方可见度快照（其中敌方单位自然分布于
    /// Hidden/Spotted/Identified 三档）。随机选取一部分单位 id 作为参战集合（并混入若干视图中不存在的
    /// 幽灵 id 以验证实现忽略未知 id）。调用
    /// <see cref="TurnPipeline.ForceIdentifiedForCombatants(FogView, IReadOnlySet{UnitId})"/> 后断言：
    /// </para>
    /// <list type="number">
    /// <item>两个方向视图的键集合均保持不变（不增删单位条目）。</item>
    /// <item>凡出现在视图中且属于参战集合的 id → <see cref="Visibility.Identified"/>（Req 14.7）。</item>
    /// <item>其余（非参战）条目 → 保持原可见度不变。</item>
    /// </list>
    /// **Validates: Requirements 14.7**
    /// </summary>
    [Fact]
    public void ForceIdentifiedForCombatants_ForcesCombatantsToIdentifiedAndPreservesOthers()
    {
        var gen =
            from state in Generators.GameState
            from cfg in GenFogConfig
            from mask in Gen.Bool.Array[state.Units.Count]
            from phantomN in Gen.Int[0, 3]
            from phantomIds in Gen.Int[10_000, 10_050].Array[phantomN]
            select (state, cfg, mask, phantomIds);

        gen.Sample(
            t =>
            {
                // 参战集合：按掩码选取部分真实单位 id，并混入若干视图中不存在的幽灵 id。
                var combatants = new HashSet<UnitId>();
                for (var i = 0; i < t.state.Units.Count; i++)
                {
                    if (t.mask[i])
                    {
                        combatants.Add(t.state.Units[i].Id);
                    }
                }

                foreach (var pid in t.phantomIds)
                {
                    combatants.Add(new UnitId(pid));
                }

                var snapshot = TurnPipeline.ComputeFogSnapshot(t.state, t.cfg);
                var result = TurnPipeline.ForceIdentifiedForCombatants(snapshot, combatants);

                return CheckView(snapshot.ByBlue, result.ByBlue, combatants)
                    && CheckView(snapshot.ByRed, result.ByRed, combatants);
            },
            iter: 200);
    }

    /// <summary>
    /// 对单个方向的可见度映射校验参战强制显形语义：键集合不变；参战 id → Identified；其余 → 原值不变。
    /// </summary>
    private static bool CheckView(
        IReadOnlyDictionary<UnitId, Visibility> original,
        IReadOnlyDictionary<UnitId, Visibility> result,
        IReadOnlySet<UnitId> combatants)
    {
        // 1) 键集合保持不变（不增删单位条目）。
        if (result.Count != original.Count || !original.Keys.All(result.ContainsKey))
        {
            return false;
        }

        foreach (var (id, originalVisibility) in original)
        {
            var expected = combatants.Contains(id) ? Visibility.Identified : originalVisibility;
            if (result[id] != expected)
            {
                return false;
            }
        }

        return true;
    }
}
