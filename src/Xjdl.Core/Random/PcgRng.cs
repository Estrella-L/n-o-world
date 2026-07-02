namespace Xjdl.Core.Random;

/// <summary>
/// 确定性随机源实现：PCG-XSH-RR 64/32 变体（O'Neill, 2014）。
/// 64 位线性同余状态 + 输出置换，纯整数运算、种子化、可快照，
/// 契合确定性核心（Req 2.4/2.5）。不依赖 <c>System.Random</c>/<c>DateTime</c>/<c>Guid</c>。
/// </summary>
public sealed class PcgRng : ISeededRng
{
    // LCG 乘子（PCG 标准常量）。
    private const ulong Multiplier = 6364136223846793005UL;

    // 用于 Fork 派生的混合常量（取自 SplitMix64 / 黄金比）。
    private const ulong MixA = 0x9E3779B97F4A7C15UL;
    private const ulong MixB = 0xBF58476D1CE4E5B9UL;

    private ulong _state;
    private readonly ulong _increment; // 流选择增量，必须为奇数。

    /// <summary>
    /// 以种子构造。<paramref name="sequence"/> 选择独立流（不同流互不重叠）。
    /// </summary>
    /// <param name="seed">初始种子。</param>
    /// <param name="sequence">流选择序号（默认 0）。</param>
    public PcgRng(ulong seed, ulong sequence = 0UL)
    {
        // 增量取奇数（左移一位后置最低位为 1），保证 LCG 满周期。
        _increment = (sequence << 1) | 1UL;
        _state = 0UL;
        Step();
        _state += seed;
        Step();
    }

    /// <inheritdoc />
    public ulong State => _state;

    /// <summary>推进 64 位 LCG 状态一步。</summary>
    private void Step()
    {
        unchecked
        {
            _state = (_state * Multiplier) + _increment;
        }
    }

    /// <summary>
    /// 生成下一个 32 位无符号随机数（PCG-XSH-RR 输出置换），并推进状态。
    /// </summary>
    private uint NextUInt32()
    {
        unchecked
        {
            ulong old = _state;
            Step();
            // XSH：异或位移压缩到 32 位。
            uint xorshifted = (uint)(((old >> 18) ^ old) >> 27);
            // RR：按高 5 位做循环右移，抹平低位规律。
            int rot = (int)(old >> 59);
            return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
        }
    }

    /// <summary>
    /// 返回 <c>[0, bound)</c> 内的无符号整数（<paramref name="bound"/> 为 0 表示整 32 位域）。
    /// 用拒绝采样消除取模偏差，保证均匀分布。
    /// </summary>
    private uint NextBoundedUInt32(uint bound)
    {
        if (bound == 0U)
        {
            // 整个 32 位域，无需约束。
            return NextUInt32();
        }

        // threshold = (2^32 - bound) % bound，落在 [0, threshold) 的样本会被拒绝。
        uint threshold = unchecked(0U - bound) % bound;
        while (true)
        {
            uint r = NextUInt32();
            if (r >= threshold)
            {
                return r % bound;
            }
        }
    }

    /// <inheritdoc />
    public int NextInt(int minInclusive, int maxInclusive)
    {
        if (minInclusive > maxInclusive)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minInclusive),
                minInclusive,
                $"minInclusive ({minInclusive}) 不得大于 maxInclusive ({maxInclusive})。");
        }

        // 闭区间跨度，用 long 计算避免 int 溢出；整 int 域时跨度为 2^32。
        ulong span = (ulong)((long)maxInclusive - minInclusive + 1L);

        uint offset = span > uint.MaxValue
            ? NextUInt32()                 // 整 32 位域（min=int.Min, max=int.Max）。
            : NextBoundedUInt32((uint)span);

        return unchecked((int)((long)minInclusive + offset));
    }

    /// <inheritdoc />
    public int Roll3D6()
    {
        return NextInt(1, 6) + NextInt(1, 6) + NextInt(1, 6);
    }

    /// <inheritdoc />
    public ISeededRng Fork(ulong salt)
    {
        // 从当前状态与 salt 确定性派生独立子流：混合出新种子与新流序号，
        // 不改变本实例状态，故父流与子流互不干扰。
        unchecked
        {
            ulong derivedSeed = _state ^ (salt * MixA);
            ulong derivedSequence = _increment ^ (salt * MixB) ^ MixA;
            return new PcgRng(derivedSeed, derivedSequence);
        }
    }
}
