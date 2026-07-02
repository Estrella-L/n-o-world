using Xjdl.Core.Random;
using Xjdl.Core.State;
using Xjdl.Core.Turn;

namespace Xjdl.Core;

/// <summary>
/// 核心规则引擎的唯一状态推进入口（见 docs/04-工程约定.md · 第 2.1 条、Req 2.1）。
/// <para>
/// <see cref="NextState"/> 是纯函数：以 <c>(state, commands, rng) =&gt; nextState</c> 的形式
/// 推进一整个 WEGO 回合，返回<em>新的</em> <see cref="GameState"/> 而<em>绝不</em>原地修改输入
/// <paramref name="state"/>，从而天然支持快照、回放与撤销。
/// </para>
/// <para>具体阶段编排委托给内部的 <see cref="TurnPipeline"/>（阶段 0..9 固定顺序）。</para>
/// </summary>
public static class RulesEngine
{
    /// <summary>
    /// 推进一个完整回合：按固定顺序执行 WEGO 阶段 0..9，返回新的游戏状态。
    /// 输入 <paramref name="state"/> 不会被修改（Req 2.1）。
    /// </summary>
    /// <param name="state">当前不可变游戏状态。</param>
    /// <param name="commands">本回合的全部输入命令（下令、临机机动、技能卡）。</param>
    /// <param name="rng">注入的确定性随机源（Req 2.4）。</param>
    /// <returns>执行完本回合全部阶段后的新 <see cref="GameState"/>。</returns>
    /// <exception cref="ArgumentNullException">当任一参数为 <c>null</c> 时抛出。</exception>
    public static GameState NextState(GameState state, TurnCommands commands, ISeededRng rng)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(commands);
        ArgumentNullException.ThrowIfNull(rng);

        return TurnPipeline.Run(state, commands, rng);
    }
}
