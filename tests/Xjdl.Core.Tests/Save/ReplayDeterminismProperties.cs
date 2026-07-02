using CsCheck;
using Xjdl.Core;
using Xjdl.Core.Random;
using Xjdl.Core.Save;
using Xjdl.Core.State;
using Xjdl.Core.Tests.Support;

namespace Xjdl.Core.Tests.Save;

// Feature: core-rules-engine, Property 8: 回放序列确定性
/// <summary>
/// Property 8: 回放序列确定性。
/// <para>
/// <em>For any</em> 由初始 <see cref="GameState"/>、命令序列与种子构成的 <see cref="Replay"/>，
/// 两次 <see cref="Replays.RunReplay"/> 产出的 <c>IReadOnlyList&lt;GameState&gt;</c> 序列字节级一致
/// （Req 21.5）。
/// </para>
/// <para>
/// 字节级一致的判定方法：对两条结果序列逐元素调用 <see cref="SaveSystem.Serialize"/>，
/// 断言序列等长且每个下标处的序列化字符串（确定性 JSON 规范形，地图按稳定序输出）逐一相等。
/// 这既是「字节相等」的直接刻画，也对 record 集合字段按引用比较的局限免疫（同 Property 63）。
/// </para>
/// <para>
/// 命令流构造：每个 <see cref="TurnCommands"/> 对当回合<em>在场单位</em>各下达恰好一条
/// <see cref="Command.Hold"/> 命令（临机机动 / 技能卡均为空），以满足阶段 0 的
/// 「每个在场单位恰好一条命令」校验（Req 3.2），使 <see cref="RulesEngine.NextState"/> 不抛异常。
/// 因每回合的起始状态取决于种子驱动的确定性推进，命令流以一次「建档折叠」
/// （fresh <see cref="PcgRng"/>(seed)）沿真实中间状态生成，从而每回合命令恰好匹配该回合在场单位；
/// 随后 <see cref="Replays.RunReplay"/> 以相同种子复现同一线程，命令与在场单位始终对齐。
/// 注：全 Hold 且无接敌时单位归属 / 位置通常不变，但确定性无论是否发生战斗都必须成立。
/// </para>
/// **Validates: Requirements 21.5**
/// </summary>
public class ReplayDeterminismProperties
{
    /// <summary>为在场单位各生成一条 Hold 命令；临机机动与技能卡为空。</summary>
    private static TurnCommands AllHoldFor(GameState state) =>
        new(
            state.Units.Select(u => new UnitOrder(u.Id, Command.Hold, null, null)).ToList(),
            System.Array.Empty<RepositionCommand>(),
            System.Array.Empty<CardPlay>());

    /// <summary>
    /// 沿真实中间状态构造全 Hold 命令流并录制为 <see cref="Replay"/>。
    /// 用一份 fresh <see cref="PcgRng"/>(seed) 折叠 <see cref="RulesEngine.NextState"/>，
    /// 使第 i 回合命令恰好覆盖第 i 回合的在场单位（与 <see cref="Replays.RunReplay"/> 的线程一致）。
    /// </summary>
    private static Replay BuildAllHoldReplay(GameState initial, ulong seed, int turns)
    {
        var rng = new PcgRng(seed);
        var current = initial;
        var commands = new List<TurnCommands>(turns);
        for (var i = 0; i < turns; i++)
        {
            var turnCommands = AllHoldFor(current);
            commands.Add(turnCommands);
            current = RulesEngine.NextState(current, turnCommands, rng);
        }

        return Replays.RecordReplay(initial, commands, seed);
    }

    /// <summary>逐元素序列化比对，刻画「两条状态序列字节级一致」。</summary>
    private static bool SequencesByteIdentical(
        IReadOnlyList<GameState> left,
        IReadOnlyList<GameState> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (var i = 0; i < left.Count; i++)
        {
            if (SaveSystem.Serialize(left[i]) != SaveSystem.Serialize(right[i]))
            {
                return false;
            }
        }

        return true;
    }

    [Fact]
    public void RunReplay_Twice_ProducesByteIdenticalStateSequences()
    {
        var gen =
            from initial in Generators.GameState
            from turns in Gen.Int[1, 3]
            from seed in Gen.ULong
            select (initial, turns, seed);

        gen.Sample(
            input =>
            {
                var (initial, turns, seed) = input;
                var replay = BuildAllHoldReplay(initial, seed, turns);

                var first = Replays.RunReplay(replay);
                var second = Replays.RunReplay(replay);

                // 结果序列与命令序列等长（每回合一项），且两次重放字节级一致（Req 21.5）。
                return first.Count == turns
                    && second.Count == turns
                    && SequencesByteIdentical(first, second);
            },
            iter: 200);
    }
}
