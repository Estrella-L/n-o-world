using Xjdl.Core.Random;

namespace Xjdl.Core.Tests.Random;

/// <summary>
/// 确定性随机源 <see cref="PcgRng"/> 的单元测试（Task 4.2、Req 2.4）。
/// 覆盖三点：同种子重放序列一致；<c>Roll3D6</c> 落在 3..18；<c>Fork</c> 派生流独立。
/// </summary>
public class PcgRngTests
{
    // ---------------------------------------------------------------
    // 1) 同种子重放序列一致（确定性）
    // ---------------------------------------------------------------

    [Fact]
    public void SameSeed_NextIntSequence_IsIdentical()
    {
        var a = new PcgRng(seed: 12345UL);
        var b = new PcgRng(seed: 12345UL);

        for (int i = 0; i < 1000; i++)
        {
            Assert.Equal(a.NextInt(0, 1_000_000), b.NextInt(0, 1_000_000));
        }
    }

    [Fact]
    public void SameSeed_Roll3D6Sequence_IsIdentical()
    {
        var a = new PcgRng(seed: 99UL);
        var b = new PcgRng(seed: 99UL);

        for (int i = 0; i < 1000; i++)
        {
            Assert.Equal(a.Roll3D6(), b.Roll3D6());
        }
    }

    [Fact]
    public void SameSeedAndSequence_StateEvolvesIdentically()
    {
        var a = new PcgRng(seed: 7UL, sequence: 3UL);
        var b = new PcgRng(seed: 7UL, sequence: 3UL);

        Assert.Equal(a.State, b.State);
        for (int i = 0; i < 100; i++)
        {
            a.NextInt(1, 6);
            b.NextInt(1, 6);
            Assert.Equal(a.State, b.State);
        }
    }

    [Fact]
    public void DifferentSeed_ProducesDifferentSequence()
    {
        var a = new PcgRng(seed: 1UL);
        var b = new PcgRng(seed: 2UL);

        // 极小概率整段完全相同；取一段序列比较，只要有任一差异即证明不同流。
        bool anyDifferent = false;
        for (int i = 0; i < 100; i++)
        {
            if (a.NextInt(0, int.MaxValue) != b.NextInt(0, int.MaxValue))
            {
                anyDifferent = true;
            }
        }

        Assert.True(anyDifferent);
    }

    [Fact]
    public void DifferentSequence_ProducesDifferentStream()
    {
        var a = new PcgRng(seed: 42UL, sequence: 0UL);
        var b = new PcgRng(seed: 42UL, sequence: 1UL);

        bool anyDifferent = false;
        for (int i = 0; i < 100; i++)
        {
            if (a.NextInt(0, int.MaxValue) != b.NextInt(0, int.MaxValue))
            {
                anyDifferent = true;
            }
        }

        Assert.True(anyDifferent);
    }

    // ---------------------------------------------------------------
    // 2) Roll3D6 落在 3..18（三角分布定义域）
    // ---------------------------------------------------------------

    [Fact]
    public void Roll3D6_AlwaysWithinInclusiveRange3To18()
    {
        var rng = new PcgRng(seed: 2024UL);

        for (int i = 0; i < 100_000; i++)
        {
            int roll = rng.Roll3D6();
            Assert.InRange(roll, 3, 18);
        }
    }

    [Fact]
    public void Roll3D6_CoversBothExtremes_AcrossManySamples()
    {
        var rng = new PcgRng(seed: 555UL);

        bool sawMin = false;
        bool sawMax = false;
        for (int i = 0; i < 500_000 && !(sawMin && sawMax); i++)
        {
            int roll = rng.Roll3D6();
            if (roll == 3)
            {
                sawMin = true;
            }
            else if (roll == 18)
            {
                sawMax = true;
            }
        }

        Assert.True(sawMin, "在大量采样中应至少出现一次最小值 3。");
        Assert.True(sawMax, "在大量采样中应至少出现一次最大值 18。");
    }

    // ---------------------------------------------------------------
    // 3) NextInt 边界与非法参数
    // ---------------------------------------------------------------

    [Fact]
    public void NextInt_SingletonRange_AlwaysReturnsThatValue()
    {
        var rng = new PcgRng(seed: 1UL);
        for (int i = 0; i < 1000; i++)
        {
            Assert.Equal(4, rng.NextInt(4, 4));
        }
    }

    [Fact]
    public void NextInt_AlwaysWithinRequestedBounds()
    {
        var rng = new PcgRng(seed: 314UL);
        for (int i = 0; i < 100_000; i++)
        {
            Assert.InRange(rng.NextInt(-10, 10), -10, 10);
        }
    }

    [Fact]
    public void NextInt_MinGreaterThanMax_Throws()
    {
        var rng = new PcgRng(seed: 1UL);
        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt(5, 4));
    }

    // ---------------------------------------------------------------
    // 4) Fork 派生流独立，且不改变父流状态
    // ---------------------------------------------------------------

    [Fact]
    public void Fork_DoesNotAdvanceOrAlterParentState()
    {
        var parent = new PcgRng(seed: 7777UL);
        ulong before = parent.State;

        _ = parent.Fork(salt: 42UL);

        Assert.Equal(before, parent.State);

        // 派生后父流继续产出的序列，与未派生时应完全一致。
        var reference = new PcgRng(seed: 7777UL);
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(reference.NextInt(0, 1_000_000), parent.NextInt(0, 1_000_000));
        }
    }

    [Fact]
    public void Fork_IsDeterministic_SameSaltSameStream()
    {
        var parent = new PcgRng(seed: 88UL);

        var childA = parent.Fork(salt: 5UL);
        var childB = parent.Fork(salt: 5UL);

        for (int i = 0; i < 500; i++)
        {
            Assert.Equal(childA.NextInt(0, int.MaxValue), childB.NextInt(0, int.MaxValue));
        }
    }

    [Fact]
    public void Fork_DifferentSalts_ProduceIndependentStreams()
    {
        var parent = new PcgRng(seed: 88UL);

        var childA = parent.Fork(salt: 1UL);
        var childB = parent.Fork(salt: 2UL);

        bool anyDifferent = false;
        for (int i = 0; i < 200; i++)
        {
            if (childA.NextInt(0, int.MaxValue) != childB.NextInt(0, int.MaxValue))
            {
                anyDifferent = true;
            }
        }

        Assert.True(anyDifferent, "不同 salt 派生的子流应彼此独立、序列不同。");
    }

    [Fact]
    public void Fork_ChildStream_DiffersFromParentStream()
    {
        var parent = new PcgRng(seed: 100UL);
        var child = parent.Fork(salt: 9UL);

        bool anyDifferent = false;
        for (int i = 0; i < 200; i++)
        {
            if (parent.NextInt(0, int.MaxValue) != child.NextInt(0, int.MaxValue))
            {
                anyDifferent = true;
            }
        }

        Assert.True(anyDifferent, "子流应独立于父流，序列不应逐项相同。");
    }
}
