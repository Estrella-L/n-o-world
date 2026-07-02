using CsCheck;
using Xjdl.Core.Hex;
using Xjdl.Core.State;

namespace Xjdl.Core.Tests.Support;

/// <summary>
/// 冒烟测试：确认每个自定义生成器都能成功取样并满足其声明的领域不变量（Req 2.2）。
/// 这些不是〈Correctness Properties〉列出的编号属性，仅用于验证生成器本身可用。
/// </summary>
public class GeneratorsSmokeTests
{
    [Fact]
    public void HexCoord_SamplesWithinBounds()
    {
        Generators.HexCoord.Sample(c =>
            c.Q >= Generators.CoordMin && c.Q <= Generators.CoordMax &&
            c.R >= Generators.CoordMin && c.R <= Generators.CoordMax);
    }

    [Fact]
    public void UnitTemplate_AttackAndDefenseDivideResilience()
    {
        Generators.UnitTemplate.Sample(t =>
            t.Resilience > 0 &&
            t.Attack % t.Resilience == 0 &&
            t.Defense % t.Resilience == 0);
    }

    [Fact]
    public void UnitState_CurrentValuesConsistentWithDecay()
    {
        Generators.UnitState.Sample(u =>
            u.Resilience0 > 0 &&
            u.ResilienceLeft >= 1 && u.ResilienceLeft <= u.Resilience0 &&
            u.Attack == u.AttackDecay * u.ResilienceLeft &&
            u.Defense == u.DefenseDecay * u.ResilienceLeft);
    }

    [Fact]
    public void GameState_UnitsInBoundsAndStackingWithinLimit()
    {
        Generators.GameState.Sample(s =>
        {
            var allInBounds = s.Units.All(u => s.Map.Contains(u.Position));
            var maxStack = s.Units
                .GroupBy(u => u.Position)
                .Select(g => g.Count())
                .DefaultIfEmpty(0)
                .Max();
            var uniqueIds = s.Units.Select(u => u.Id).Distinct().Count() == s.Units.Count;
            return allInBounds && maxStack <= Generators.StackLimit && uniqueIds;
        });
    }

    [Fact]
    public void CardState_CpWithinCpMax()
    {
        Generators.CardState.Sample(c => c.Cp >= 0 && c.Cp <= c.CpMax);
    }

    [Fact]
    public void TurnCommands_Samples()
    {
        Generators.TurnCommands.Sample(_ => true);
    }

    [Fact]
    public void CommandSet_Samples()
    {
        Generators.CommandSet.Sample(_ => true);
    }

    [Fact]
    public void NightFlags_IsSubsetOfDefinedBits()
    {
        const int definedMask = (int)(NightFlags.NightVisionKeep | NightFlags.NightRangeKeep |
            NightFlags.NightAttackKeep | NightFlags.NightMoveKeep | NightFlags.IgnoreZoc |
            NightFlags.NightZocKeep);

        Generators.NightFlags.Sample(f => ((int)f & ~definedMask) == 0);
    }

    // ---- EDGE_CASE 生成器冒烟 ----------------------------------------

    [Fact]
    public void EdgeCase_EmptyTurnCommands_IsEmpty()
    {
        Generators.EdgeCaseEmptyTurnCommands.Sample(c =>
            c.Orders.Count == 0 && c.Repositions.Count == 0 && c.CardPlays.Count == 0);
    }

    [Fact]
    public void EdgeCase_EmptyGameState_HasNoUnits()
    {
        Generators.EdgeCaseEmptyGameState.Sample(s => s.Units.Count == 0 && s.Map.Cells.Count == 1);
    }

    [Fact]
    public void EdgeCase_NonAsciiTypeKey_Samples()
    {
        Generators.EdgeCaseNonAsciiTypeKeyTemplate.Sample(_ => true);
    }

    [Fact]
    public void EdgeCase_OutOfRangeDirection_IsOutside0To5()
    {
        Generators.EdgeCaseOutOfRangeDirection.Sample(d => d < 0 || d > 5);
    }

    [Fact]
    public void EdgeCase_OverStacked_ExceedsStackLimit()
    {
        Generators.EdgeCaseOverStackedUnits.Sample(units =>
        {
            var maxStack = units.GroupBy(u => u.Position).Select(g => g.Count()).Max();
            return maxStack > Generators.StackLimit;
        });
    }

    [Fact]
    public void EdgeCase_InsufficientCp_HasZeroCpAndNonEmptyHand()
    {
        Generators.EdgeCaseInsufficientCpCardState.Sample(c => c.Cp == 0 && c.Hand.Count > 0);
    }

    [Fact]
    public void EdgeCase_Surrounded_HasSixEnemiesOnNeighbors()
    {
        Generators.EdgeCaseSurroundedNoRetreatUnits.Sample(units =>
        {
            var center = units[0];
            var neighbors = center.Position.Neighbors().ToHashSet();
            var surrounders = units.Skip(1).ToList();
            return surrounders.Count == 6 &&
                surrounders.All(u => u.Owner != center.Owner && neighbors.Contains(u.Position));
        });
    }

    [Fact]
    public void EdgeCase_NonDivisibleTemplate_HasNonZeroRemainder()
    {
        Generators.EdgeCaseNonDivisibleTemplate.Sample(t =>
            t.Attack % t.Resilience != 0 || t.Defense % t.Resilience != 0);
    }
}
