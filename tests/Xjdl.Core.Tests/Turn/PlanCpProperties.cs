using CsCheck;
using Xjdl.Core;
using Xjdl.Core.Random;
using Xjdl.Core.State;
using Xjdl.Core.Tests.Support;

namespace Xjdl.Core.Tests.Turn;

// Feature: core-rules-engine, Property 11: 计划阶段下令不消耗 CP
/// <summary>
/// Property 11（计划阶段下令不消耗 CP）的属性测试。见 design.md〈Property 11〉与 docs/01
/// 〈计划—动画双阶段与临机机动〉。
/// <para>
/// 「画蓝图免费、改蓝图付费」：计划阶段（阶段 0–1）自由下令不消耗指挥点（Req 4.1），
/// 指挥点只在动画阶段被接受的临机机动上消费（见 <c>TurnPipeline.Maneuver.cs</c>）。
/// </para>
/// <para>
/// 隔离手法：为每个在场单位下达一条合法的 <see cref="Command.Hold"/> 命令
/// （满足「每单位恰好一条命令」的输入合法性，Req 3.2），并让
/// <see cref="TurnCommands.Repositions"/> 与 <see cref="TurnCommands.CardPlays"/> 均为空。
/// 如此 <c>NextState</c> 的机动阶段不会有任何被接受的临机机动，故 CP 不应被扣减；
/// 战斗（阶段 3–8）与回合结束（阶段 9）本身不产出/消耗 CP。于是每一方的 CP 在整回合前后应保持不变。
/// </para>
/// **Validates: Requirements 4.1**
/// </summary>
public class PlanCpProperties
{
    /// <summary>
    /// 对任意合法 <see cref="GameState"/>，以「每单位恰好一条 <see cref="Command.Hold"/>」命令、
    /// 空临机机动、空技能卡运行整回合 <see cref="RulesEngine.NextState"/> 后，
    /// 每一方（蓝/红）的 CP 与输入相同（Req 4.1）。
    /// </summary>
    [Fact]
    public void NextState_WithNoRepositionsOrCards_LeavesEachSideCpUnchanged()
    {
        var gen =
            from state in Generators.GameState
            from seed in Gen.ULong
            select (state, seed);

        gen.Sample(
            t =>
            {
                var (state, seed) = t;

                // 每个在场单位恰好一条 Hold 命令（合法输入，Req 3.2）；空临机机动、空技能卡。
                var orders = state.Units
                    .Select(u => new UnitOrder(u.Id, Command.Hold, null, null))
                    .ToArray();

                var commands = new TurnCommands(
                    orders,
                    System.Array.Empty<RepositionCommand>(),
                    System.Array.Empty<CardPlay>());

                ISeededRng rng = new PcgRng(seed);

                var next = RulesEngine.NextState(state, commands, rng);

                // 每一方 CP 前后不变（计划阶段不消耗 CP，Req 4.1）。
                foreach (var side in state.Cards.Keys)
                {
                    Assert.Equal(state.Cards[side].Cp, next.Cards[side].Cp);
                }
            },
            iter: 100);
    }
}
