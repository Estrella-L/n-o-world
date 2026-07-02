using CsCheck;
using Xjdl.Core.Combat;
using Xjdl.Core.Hex;
using Xjdl.Core.State;
using Xjdl.Core.Tests.Support;

namespace Xjdl.Core.Tests.Combat;

// Feature: core-rules-engine, Property 31: 仅主攻计入火力比
public class StackingMainUnitProperties
{
    /// <summary>本组场景统一落位的目标格（<see cref="Stacking.SelectMainUnit"/> 只读攻/防与 id，坐标仅用于表达「同格」语义）。</summary>
    private static readonly HexCoord Cell = new(0, 0);

    /// <summary>
    /// 生成「同格战斗」场景：一支非空基线堆叠 + 一组不越过当前主攻的随从。
    /// <para>
    /// 基线单位重编唯一 id（0..b-1）并同格落位；随从 id 从 b 起始（严格大于全部基线 id），
    /// 其进攻战斗力经 <c>Attack % (mainAttack+1)</c> 收敛到 [0, 主攻攻击力]。
    /// 由此保证随从「不越过主攻」：攻击力低者天然不胜；攻击力并列者因 id 更大而由稳定 id 决胜落败
    /// （对齐 <see cref="Stacking.SelectMainUnit"/> 的选取规则，Req 9.2、2.6）。
    /// </para>
    /// </summary>
    private static readonly Gen<(IReadOnlyList<UnitState> Baseline, IReadOnlyList<UnitState> WithFollowers)> GenScenario =
        from baseCount in Gen.Int[1, 5]
        from baseUnits in Generators.UnitState.List[baseCount]
        from followerCount in Gen.Int[0, 5]
        from followerUnits in Generators.UnitState.List[followerCount]
        select BuildScenario(baseUnits, followerUnits);

    /// <summary>
    /// Property 31: 仅主攻计入火力比。
    /// 对任意同格战斗，火力比只取决于该格主攻单位的攻/防；向该格增删「不越过主攻」的随从，
    /// 主攻单位不变，故其计入火力比的进攻战斗力（<see cref="UnitState.Attack"/>）恒不变（Req 9.2）。
    /// **Validates: Requirements 9.2**
    /// </summary>
    [Fact]
    public void AddingFollowers_DoesNotChangeMainUnitFirePower()
    {
        GenScenario.Sample(scenario =>
        {
            var (baseline, withFollowers) = scenario;

            var mainBaseline = Stacking.SelectMainUnit(baseline);
            var mainWithFollowers = Stacking.SelectMainUnit(withFollowers);

            // 基线非空 → 两侧主攻均存在；主攻身份与其计入火力比的攻击力均不因随从而改变。
            return mainBaseline is not null
                && mainWithFollowers is not null
                && mainWithFollowers.Id == mainBaseline.Id
                && mainWithFollowers.Attack == mainBaseline.Attack
                && mainWithFollowers.Defense == mainBaseline.Defense;
        }, iter: 1000);
    }

    private static (IReadOnlyList<UnitState> Baseline, IReadOnlyList<UnitState> WithFollowers) BuildScenario(
        IReadOnlyList<UnitState> baseUnits,
        IReadOnlyList<UnitState> followerUnits)
    {
        // 基线：重编唯一 id（0..b-1）并同格落位。
        var baseline = baseUnits
            .Select((u, i) => u with { Id = new UnitId(i), Position = Cell })
            .ToList();

        var main = Stacking.SelectMainUnit(baseline)!;

        // 随从：id 从 baseline.Count 起（严格大于全部基线 id），攻击力收敛到 [0, 主攻攻击力]。
        var withFollowers = new List<UnitState>(baseline);
        var nextId = baseline.Count;
        foreach (var f in followerUnits)
        {
            var cappedAttack = f.Attack % (main.Attack + 1); // ∈ [0, main.Attack]
            withFollowers.Add(f with
            {
                Id = new UnitId(nextId++),
                Position = Cell,
                Attack = cappedAttack,
            });
        }

        return (baseline, withFollowers);
    }
}
