using System;
using System.Collections.Generic;
using System.Linq;
using CsCheck;
using Xjdl.Core;
using Xjdl.Core.Random;
using Xjdl.Core.Save;
using Xjdl.Core.State;
using Xjdl.Core.Tests.Support;

namespace Xjdl.Core.Tests.Turn;

// Feature: core-rules-engine, Property 7: 结算次序无关
/// <summary>
/// Property 7: 结算次序无关。
/// <para>
/// <em>For any</em> 合法 <see cref="GameState"/>，将其 <see cref="GameState.Units"/> 集合
/// （及由此驱动的战斗遍历顺序）任意重排后，以相同种子执行 <see cref="RulesEngine.NextState"/>，
/// 规范化后的结果 <see cref="GameState"/> 与原顺序字节级一致（Req 2.3、2.6）。
/// </para>
/// <para>
/// 引擎内部按稳定 <see cref="UnitId"/> 排序遍历、每场战斗以 <c>Fork(battleId)</c> 派生 RNG 子流，
/// 故输入 <see cref="GameState.Units"/> 列表的排列不应改变（规范化后的）输出。
/// </para>
/// <para>
/// <b>规范化</b>：因 <see cref="GameState.Units"/> 为 <see cref="IReadOnlyList{T}"/>，
/// <see cref="SaveSystem.Serialize"/> 按列表顺序写出，结果的 Units 顺序可能反映输入顺序。
/// 为做次序无关比对，将两侧结果各自按 <c>u.Id.Value</c> 升序重建 Units 后再序列化，
/// 断言两串字节级相等。命令流对两侧使用相同的「每单位一条 Hold」集合（命令按 UnitId 归属，
/// 与列表顺序无关），确保唯一变量是 Units 的排列。
/// </para>
/// **Validates: Requirements 2.3, 2.6**
/// </summary>
public class SettlementOrderIndependenceProperties
{
    /// <summary>为在场单位各生成一条 Hold 命令；临机机动与技能卡为空。</summary>
    private static TurnCommands AllHoldFor(GameState state) =>
        new(
            state.Units.Select(u => new UnitOrder(u.Id, Command.Hold, null, null)).ToList(),
            Array.Empty<RepositionCommand>(),
            Array.Empty<CardPlay>());

    /// <summary>按稳定 <see cref="UnitId"/> 升序重建 Units，得到与输入排列无关的规范形。</summary>
    private static GameState Normalize(GameState state) =>
        state with { Units = state.Units.OrderBy(u => u.Id.Value).ToList() };

    /// <summary>
    /// 依据生成的排序键对 Units 做确定性重排（key 升序、同 key 按原下标稳定）。
    /// </summary>
    private static IReadOnlyList<UnitState> Permute(IReadOnlyList<UnitState> units, int[] keys) =>
        units
            .Select((u, i) => (u, key: keys[i], index: i))
            .OrderBy(t => t.key)
            .ThenBy(t => t.index)
            .Select(t => t.u)
            .ToList();

    [Fact]
    public void NextState_IsInvariantToUnitOrderingAfterNormalization()
    {
        var gen =
            from initial in Generators.GameState
            from seed in Gen.ULong
            from keys in Gen.Int[0, 1_000_000].Array[initial.Units.Count]
            select (initial, seed, keys);

        gen.Sample(
            input =>
            {
                var (initial, seed, keys) = input;

                // 原顺序 state，与按生成键确定性重排 Units 的排列副本。
                var permuted = initial with { Units = Permute(initial.Units, keys) };

                // 两侧使用相同的「每单位一条 Hold」命令（命令按 UnitId 归属，列表顺序无关）。
                var commands = AllHoldFor(initial);

                // 相同种子分别推进一回合（各自 fresh PcgRng，避免共享可变状态）。
                var result1 = RulesEngine.NextState(initial, commands, new PcgRng(seed));
                var result2 = RulesEngine.NextState(permuted, commands, new PcgRng(seed));

                // 规范化（按 Id 升序重建 Units）后序列化，断言字节级一致。
                return SaveSystem.Serialize(Normalize(result1))
                    == SaveSystem.Serialize(Normalize(result2));
            },
            iter: 200);
    }
}
