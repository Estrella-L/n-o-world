using CsCheck;
using Xjdl.Core.Combat;
using Xjdl.Core.State;
using Xjdl.Core.Tests.Support;

namespace Xjdl.Core.Tests.Combat;

// Feature: core-rules-engine, Property 32: 主攻阵亡后梯次接替
public class StackingSuccessionProperties
{
    /// <summary>
    /// 一格内 &gt;=1 个单位、且 <see cref="UnitId"/> 两两不同的堆叠。
    /// 由合法 <see cref="Generators.UnitState"/> 派生并重编唯一 id，保证
    /// <see cref="Stacking.SelectMainUnit"/> / <see cref="Stacking.SucceedMainUnit"/> 的稳定 id 决胜键有意义。
    /// </summary>
    private static readonly Gen<IReadOnlyList<UnitState>> DistinctStack =
        from n in Gen.Int[1, 6]
        from units in Generators.UnitState.List[n]
        select (IReadOnlyList<UnitState>)units
            .Select((u, i) => u with { Id = new UnitId(i) })
            .ToList();

    /// <summary>
    /// Property 32: 主攻阵亡后梯次接替（Req 9.3、9.4）。
    /// 对任意非空、id 唯一的堆叠，令 main = <see cref="Stacking.SelectMainUnit"/>(stack)，
    /// 则 <see cref="Stacking.SucceedMainUnit"/>(stack, main.Id) 满足：
    ///  (a) 当且仅当该格恰有 1 个单位时返回 <c>null</c>（歼灭后整格空）；
    ///  (b) 否则等于对「去掉主攻后的剩余单位」重新选取的主攻——即仅移除当前主攻、
    ///      从幸存者中梯次接替，而非一次清空整格；
    ///  (c) 接替者不是被歼灭的原主攻，且仍是原堆叠中的成员。
    /// **Validates: Requirements 9.3, 9.4**
    /// </summary>
    [Fact]
    public void SucceedMainUnit_RemovesOnlyMainAndRedesignatesFromSurvivors()
    {
        DistinctStack.Sample(stack =>
        {
            var main = Stacking.SelectMainUnit(stack);
            // 非空堆叠必有主攻。
            if (main is null)
            {
                return false;
            }

            var successor = Stacking.SucceedMainUnit(stack, main.Id);

            // (a) 仅当整格只有 1 个单位时，歼灭后为空 → null。
            if (stack.Count == 1)
            {
                return successor is null;
            }

            // (b) 等于「去掉主攻后」对剩余单位重新选取的主攻（仅移除主攻，梯次接替）。
            var survivors = stack.Where(u => u.Id != main.Id).ToList();
            var expected = Stacking.SelectMainUnit(survivors);

            if (successor is null || expected is null)
            {
                return false;
            }

            if (successor != expected)
            {
                return false;
            }

            // (c) 接替者不是被歼灭的原主攻，且仍在原堆叠中。
            return successor.Id != main.Id
                && stack.Any(u => u.Id == successor.Id);
        }, iter: 1000);
    }
}
