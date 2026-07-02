using CsCheck;
using Xjdl.Core.State;
using Xjdl.Core.Tests.Support;
using Xjdl.Core.Turn;

namespace Xjdl.Core.Tests.Turn;

/// <summary>
/// Property 9（每单位恰好一条命令）的属性测试。见 design.md〈Property 9〉与 docs/01〈阶段流程 · 阶段 0〉。
/// <para>
/// 覆盖阶段 0 计划下令校验 <see cref="TurnPipeline.ValidateOrders"/>（Req 3.2）：
/// 阶段 0 结束后，每个在场单位恰好持有一条 <see cref="UnitOrder"/>；
/// 缺失（某在场单位无命令）或重复（同一单位多条命令）的命令集合均被拒绝。
/// </para>
/// <para>
/// 直接调用 internal 的 <see cref="TurnPipeline.ValidateOrders"/>（经 InternalsVisibleTo 暴露）以隔离
/// Property 9，避免随机 <see cref="GameState"/> 触发下游阶段（机动/战斗）的无关行为造成误判。
/// </para>
/// </summary>
public class PlanCommandProperties
{
    /// <summary>本回合仅校验阶段 0 命令，临机机动与技能卡置空。</summary>
    private static TurnCommands WithOrders(IReadOnlyList<UnitOrder> orders) =>
        new(orders, System.Array.Empty<RepositionCommand>(), System.Array.Empty<CardPlay>());

    /// <summary>为一个在场单位构造一条最小合法命令（据守，无路径/目标）。</summary>
    private static UnitOrder HoldOrder(UnitState unit) =>
        new(unit.Id, Command.Hold, null, null);

    // Feature: core-rules-engine, Property 9: 每单位恰好一条命令
    // VALID：为每个在场单位恰好下达一条命令 → ValidateOrders 不抛出（阶段 0 通过）。
    // Validates: Requirements 3.2
    [Fact]
    public void ExactlyOneOrderPerUnit_IsAccepted()
    {
        Generators.GameState.Sample(
            state =>
            {
                var orders = state.Units.Select(HoldOrder).ToList();
                var cmds = WithOrders(orders);

                // 恰好覆盖全部在场单位、无重复 → 合法计划，不应抛出。
                TurnPipeline.ValidateOrders(state, cmds);
            },
            iter: 200);
    }

    // Feature: core-rules-engine, Property 9: 每单位恰好一条命令
    // MISSING：遗漏某个在场单位的命令 → 抛出 InvalidOperationException（覆盖性校验）。
    // Validates: Requirements 3.2
    [Fact]
    public void MissingOrderForAnyUnit_IsRejected()
    {
        var gen =
            from state in Generators.GameState.Where(s => s.Units.Count > 0)
            from omit in Gen.Int[0, state.Units.Count - 1]
            select (state, omit);

        gen.Sample(
            t =>
            {
                // 为每个单位下令，但删去下标 omit 的那一条 → 该在场单位缺少命令。
                var orders = t.state.Units
                    .Where((_, i) => i != t.omit)
                    .Select(HoldOrder)
                    .ToList();
                var cmds = WithOrders(orders);

                Assert.Throws<InvalidOperationException>(
                    () => TurnPipeline.ValidateOrders(t.state, cmds));
            },
            iter: 200);
    }

    // Feature: core-rules-engine, Property 9: 每单位恰好一条命令
    // DUPLICATE：为同一在场单位下达两条命令 → 抛出 InvalidOperationException（重复校验）。
    // Validates: Requirements 3.2
    [Fact]
    public void DuplicateOrderForAnyUnit_IsRejected()
    {
        var gen =
            from state in Generators.GameState.Where(s => s.Units.Count > 0)
            from dup in Gen.Int[0, state.Units.Count - 1]
            select (state, dup);

        gen.Sample(
            t =>
            {
                // 覆盖每个单位一条命令，再为下标 dup 的单位追加一条重复命令。
                var orders = t.state.Units.Select(HoldOrder).ToList();
                orders.Add(HoldOrder(t.state.Units[t.dup]));
                var cmds = WithOrders(orders);

                Assert.Throws<InvalidOperationException>(
                    () => TurnPipeline.ValidateOrders(t.state, cmds));
            },
            iter: 200);
    }
}
