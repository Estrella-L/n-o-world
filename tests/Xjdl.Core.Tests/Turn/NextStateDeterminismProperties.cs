using CsCheck;
using Xjdl.Core;
using Xjdl.Core.Random;
using Xjdl.Core.Save;
using Xjdl.Core.State;
using Xjdl.Core.Tests.Support;

namespace Xjdl.Core.Tests.Turn;

// Feature: core-rules-engine, Property 6: 单步确定性
/// <summary>
/// Property 6（单步确定性）的属性测试。见 design.md〈Property 6〉与 docs/04-工程约定.md · 第 2 条〈确定性优先〉。
/// <para>
/// 语义（事实来源 Req 2.2）：对任意 <c>(state, commands, seed)</c>，两次<em>独立</em>执行
/// <see cref="RulesEngine.NextState"/> 必产出字节级一致的结果 <see cref="GameState"/>。
/// 这是回放、测试与未来联机的基石——相同输入永远得到相同输出，不受时间、无种子随机或
/// 集合迭代顺序等非确定性来源影响。
/// </para>
/// <para>
/// 字节级一致的判定方法：对两次结果分别调用 <see cref="SaveSystem.Serialize"/>，断言其确定性
/// JSON 规范形（地图/单位按稳定序输出）逐字符相等。这是「字节相等」的直接刻画，也对 record
/// 集合字段按引用比较的局限免疫（同 Property 8 / Property 63 的做法）。
/// </para>
/// <para>
/// 命令构造：对当前状态<em>每个在场单位</em>各下达恰好一条 <see cref="Command.Hold"/> 命令
/// （临机机动 / 技能卡均为空），以满足阶段 0 的「每个在场单位恰好一条命令」校验（Req 3.2），
/// 使 <see cref="RulesEngine.NextState"/> 不因非法命令抛出，从而聚焦于「确定性」本身。
/// </para>
/// <para>
/// 两次执行各用一份 <em>fresh</em> <see cref="PcgRng"/>(seed)（相同种子、独立实例），
/// 以刻画「相同种子下随机源推进完全一致」——若引擎内部误用了共享/可变全局随机源或非种子源，
/// 该属性即被打破。
/// </para>
/// **Validates: Requirements 2.2**
/// </summary>
public class NextStateDeterminismProperties
{
    /// <summary>为在场单位各生成一条 Hold 命令；临机机动与技能卡为空（合法输入，Req 3.2）。</summary>
    private static TurnCommands AllHoldFor(GameState state) =>
        new(
            state.Units.Select(u => new UnitOrder(u.Id, Command.Hold, null, null)).ToList(),
            System.Array.Empty<RepositionCommand>(),
            System.Array.Empty<CardPlay>());

    /// <summary>
    /// Property 6：对任意合法 <see cref="GameState"/>、每单位一条 <see cref="Command.Hold"/> 命令与任意
    /// <c>ulong</c> 种子，两次独立执行 <see cref="RulesEngine.NextState"/>（各用 fresh <see cref="PcgRng"/>(seed)）
    /// 的结果序列化后字节级一致（Req 2.2）。
    /// **Validates: Requirements 2.2**
    /// </summary>
    [Fact]
    public void NextState_RunTwiceWithSameSeed_ProducesByteIdenticalState()
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

                // 两份 fresh PcgRng（相同种子、独立实例）——刻画「相同种子推进完全一致」。
                var r1 = RulesEngine.NextState(state, commands, new PcgRng(seed));
                var r2 = RulesEngine.NextState(state, commands, new PcgRng(seed));

                // 字节级一致：确定性 JSON 规范形逐字符相等（Req 2.2）。
                return SaveSystem.Serialize(r1) == SaveSystem.Serialize(r2);
            },
            iter: 200);
    }
}
