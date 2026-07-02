using Xjdl.Core.Random;
using Xjdl.Core.State;

namespace Xjdl.Core.Save;

/// <summary>
/// 回放数据（Req 21.4）：由初始 <see cref="GameState"/>、每回合命令序列与种子构成，
/// <em>优先存命令流而非逐帧状态</em>——只需 <c>(Initial, Commands, Seed)</c> 即可确定性重演整局，
/// 存档体积小且天然可复盘（Req 21.4/21.5）。
/// <para>
/// 每条临机机动指令及其触发时点由 <see cref="TurnCommands.Repositions"/> 中的
/// <see cref="RepositionCommand.TriggerTick"/> 承载，故命令流本身即完整记录了阶段 2 的
/// 临机机动与触发时点（Req 21.6），无需额外结构。
/// </para>
/// <para>
/// 不可变值语义、无引擎类型、无循环引用，可经 <see cref="SaveSystem"/> 直接序列化（Req 21.1）。
/// </para>
/// </summary>
/// <param name="Initial">回放起点的完整不可变状态快照。</param>
/// <param name="Commands">按回合顺序排列的命令序列（每项为一个完整 WEGO 回合的输入）。</param>
/// <param name="Seed">驱动确定性随机源的种子；重放时以之新建 <see cref="PcgRng"/>（Req 2.4）。</param>
public sealed record Replay(
    GameState Initial,
    IReadOnlyList<TurnCommands> Commands,
    ulong Seed);

/// <summary>
/// 回放的录制与重放设施（Req 21.4/21.5/21.6）。
/// <para>
/// 独立于 <see cref="SaveSystem"/> 的静态类，方法命名为 <see cref="RecordReplay"/> 与
/// <see cref="RunReplay"/>：后者刻意<em>不</em>叫 <c>Replay</c>，以规避与 <see cref="Replay"/>
/// 记录类型的名称冲突，同时保留设计文档中「录制 / 重放」两个动作的语义。
/// </para>
/// </summary>
public static class Replays
{
    /// <summary>
    /// 录制一段回放（Req 21.4）：把初始状态、命令流与种子打包为不可变 <see cref="Replay"/>，
    /// 只存命令流而非逐帧状态。
    /// </summary>
    /// <param name="initial">回放起点状态。</param>
    /// <param name="cmds">按回合顺序排列的命令序列。</param>
    /// <param name="seed">确定性随机源种子。</param>
    /// <returns>可用于 <see cref="RunReplay"/> 重演的回放数据。</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="initial"/> 或 <paramref name="cmds"/> 为 <c>null</c> 时抛出。</exception>
    public static Replay RecordReplay(GameState initial, IReadOnlyList<TurnCommands> cmds, ulong seed)
    {
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(cmds);

        return new Replay(initial, cmds, seed);
    }

    /// <summary>
    /// 重放一段回放（Req 21.5）：从 <see cref="Replay.Initial"/> 出发、以 <see cref="Replay.Seed"/>
    /// 新建 <see cref="PcgRng"/>，依次对每个 <see cref="TurnCommands"/> 折叠
    /// <see cref="RulesEngine.NextState"/>，产出逐回合的结果状态序列。
    /// <para>
    /// 因 <see cref="RulesEngine.NextState"/> 为纯函数且随机仅来自注入的种子 RNG，
    /// 相同 <c>(Initial, Commands, Seed)</c> 两次重放必产出字节级一致的结果序列（Req 21.5）。
    /// </para>
    /// <para>
    /// 返回序列<em>不含</em>初始状态，仅含每回合执行后的结果状态：第 <c>i</c> 项对应
    /// <c>Commands[i]</c> 执行后的 <see cref="GameState"/>；命令流为空时返回空序列。
    /// </para>
    /// </summary>
    /// <param name="replay">待重放的回放数据。</param>
    /// <returns>与命令序列等长的逐回合结果状态序列。</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="replay"/> 为 <c>null</c> 时抛出。</exception>
    public static IReadOnlyList<GameState> RunReplay(Replay replay)
    {
        ArgumentNullException.ThrowIfNull(replay);

        // 单一 RNG 实例贯穿整局：其内部状态随每回合结算连续推进，
        // 保证与原局逐回合取数序列完全一致（Req 21.5）。
        var rng = new PcgRng(replay.Seed);

        var states = new List<GameState>(replay.Commands.Count);
        var current = replay.Initial;
        foreach (var commands in replay.Commands)
        {
            current = RulesEngine.NextState(current, commands, rng);
            states.Add(current);
        }

        return states;
    }
}
