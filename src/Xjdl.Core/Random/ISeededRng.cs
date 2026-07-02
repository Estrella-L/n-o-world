namespace Xjdl.Core.Random;

/// <summary>
/// 确定性随机源接口（见 docs/04-工程约定.md · 第 2.5 条、Req 2.4）。
/// 核心引擎只允许通过注入本接口取随机数，禁止使用无种子 <c>System.Random</c>、
/// <c>DateTime.Now</c>、<c>Guid.NewGuid()</c> 或 <c>Environment.TickCount</c>，
/// 以保证「相同 (state, commands, seed) 必产出字节级一致结果」的可重放性。
/// 全程纯整数运算，不使用 <c>float</c>/<c>double</c>。
/// </summary>
public interface ISeededRng
{
    /// <summary>
    /// 返回闭区间 <c>[minInclusive, maxInclusive]</c> 内的一个整数，并推进内部状态。
    /// </summary>
    /// <param name="minInclusive">下界（含）。</param>
    /// <param name="maxInclusive">上界（含）。</param>
    /// <exception cref="ArgumentOutOfRangeException">当 <paramref name="minInclusive"/> 大于 <paramref name="maxInclusive"/> 时抛出。</exception>
    int NextInt(int minInclusive, int maxInclusive);

    /// <summary>
    /// 掷 3D6：三颗独立 d6 之和，返回 3..18（读表所需的三角分布）。
    /// </summary>
    int Roll3D6();

    /// <summary>
    /// 当前内部状态。可快照进 <c>GameState</c> 以支持存档与回放。
    /// </summary>
    ulong State { get; }

    /// <summary>
    /// 从当前状态与 <paramref name="salt"/> 确定性派生一条独立子流，
    /// 用于多场战斗互不干扰（如以 battleId 作 salt）。不改变本实例的状态。
    /// </summary>
    /// <param name="salt">派生盐值（如战斗 id）。</param>
    ISeededRng Fork(ulong salt);
}
