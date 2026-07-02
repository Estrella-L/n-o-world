using CsCheck;
using Xjdl.Core.Save;
using Xjdl.Core.State;
using Xjdl.Core.Tests.Support;

namespace Xjdl.Core.Tests.Save;

// Feature: core-rules-engine, Property 63: 序列化 round-trip
public class SaveRoundTripProperties
{
    /// <summary>
    /// Property 63: 序列化 round-trip。
    /// 对任意合法 <see cref="GameState"/> s，<c>Deserialize(Serialize(s))</c> 等于 s
    /// （序列化/反序列化恒等）。
    /// <para>
    /// 等价性判定采用「规范形字节相等」（robust round-trip）：由于 <see cref="GameState"/>
    /// 的 record 相等性对 <see cref="GameMap.Cells"/>（<c>IReadOnlyDictionary</c>）及
    /// <c>IReadOnlyList</c> 集合按引用而非按值比较，直接 <c>==</c> 不能反映结构相等。
    /// 因此断言 <c>Serialize(Deserialize(Serialize(s))) == Serialize(s)</c>：
    /// 序列化本身是确定性规范形（地图按稳定序输出），若一次往返后再序列化仍产出同一字节序列，
    /// 则往返无损。此外附带比较关键结构字段以增强诊断可读性。
    /// </para>
    /// **Validates: Requirements 21.1**
    /// </summary>
    [Fact]
    public void Deserialize_Serialize_IsIdentity()
    {
        Generators.GameState.Sample(s =>
        {
            var json = SaveSystem.Serialize(s);
            var round = SaveSystem.Deserialize(json);

            // 主判定：规范形字节相等（对字典/列表结构鲁棒）。
            var canonicalEqual = SaveSystem.Serialize(round) == json;

            // 辅助结构字段比较（诊断用，且强化标量/计数层面的往返正确性）。
            var structuralEqual =
                round.SchemaVersion == s.SchemaVersion &&
                round.DayIndex == s.DayIndex &&
                round.Phase == s.Phase &&
                round.RngState == s.RngState &&
                round.Units.Count == s.Units.Count &&
                round.TurnLog.Count == s.TurnLog.Count &&
                round.Map.Cells.Count == s.Map.Cells.Count &&
                round.Map.Scale == s.Map.Scale &&
                round.Cards.Count == s.Cards.Count;

            return canonicalEqual && structuralEqual;
        }, iter: 1000);
    }
}
