using CsCheck;
using Xjdl.Core;
using Xjdl.Core.Random;
using Xjdl.Core.Save;
using Xjdl.Core.State;
using Xjdl.Core.Tests.Support;

namespace Xjdl.Core.Tests.Turn;

// Feature: core-rules-engine, Property 5: NextState 不修改输入状态
/// <summary>
/// Property 5: NextState 不修改输入状态。
/// <para>
/// <em>For any</em> <see cref="GameState"/> 与 <see cref="TurnCommands"/>，调用
/// <see cref="RulesEngine.NextState"/> 前后对输入 <c>state</c> 的序列化结果字节一致
/// （输入未被原地修改，Req 2.1）。
/// </para>
/// <para>
/// 判定方法：调用前对输入 <c>state</c> 快照 <c>json0 = SaveSystem.Serialize(state)</c>；
/// 调用 <c>RulesEngine.NextState(state, commands, new PcgRng(seed))</c>（返回值刻意丢弃，
/// 只关心输入是否被原地改动）；调用后再取 <c>json1 = SaveSystem.Serialize(state)</c>；
/// 断言 <c>json0 == json1</c>。<see cref="SaveSystem.Serialize"/> 产出确定性 JSON 规范形
/// （地图按稳定序输出），故字符串相等即刻画「输入状态字节级未变」。
/// </para>
/// <para>
/// 命令流构造：对当回合<em>在场单位</em>（即 <see cref="GameState.Units"/> 全体）各下达恰好一条
/// <see cref="Command.Hold"/> 命令，临机机动与技能卡均为空，以满足阶段 0 的
/// 「每个在场单位恰好一条命令」校验（Req 3.2），使 <see cref="RulesEngine.NextState"/> 不抛异常。
/// </para>
/// **Validates: Requirements 2.1**
/// </summary>
public class NextStateNoMutationProperties
{
    /// <summary>为在场单位各生成一条 Hold 命令；临机机动与技能卡为空。</summary>
    private static TurnCommands AllHoldFor(GameState state) =>
        new(
            state.Units.Select(u => new UnitOrder(u.Id, Command.Hold, null, null)).ToList(),
            System.Array.Empty<RepositionCommand>(),
            System.Array.Empty<CardPlay>());

    [Fact]
    public void NextState_DoesNotMutateInputState()
    {
        var gen =
            from state in Generators.GameState
            from seed in Gen.ULong
            select (state, seed);

        gen.Sample(
            input =>
            {
                var (state, seed) = input;
                var commands = AllHoldFor(state);

                // 调用前快照输入序列化。
                var json0 = SaveSystem.Serialize(state);

                // 执行状态变换；返回值刻意丢弃——本属性只关心输入是否被原地改动。
                _ = RulesEngine.NextState(state, commands, new PcgRng(seed));

                // 调用后再取输入序列化，断言字节一致（输入未被原地修改，Req 2.1）。
                var json1 = SaveSystem.Serialize(state);

                return json0 == json1;
            },
            iter: 200);
    }
}
