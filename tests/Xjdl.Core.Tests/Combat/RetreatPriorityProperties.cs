using CsCheck;
using Xjdl.Core.Combat;

namespace Xjdl.Core.Tests.Combat;

// Feature: core-rules-engine, Property 29: 退优先冲突消解
//
// Req 3.7：WHEN 同一单位被不同战斗结果同时要求「退」与「不退/推进」,
// THE CombatResolver SHALL 以退/撤离优先处理。
//
// ---- 建模选择（Modeling choice）----------------------------------------------------
// 完整流水线（阶段 6/7/8 将多场 ResultCode 应用到 GameState）在任务 15.20 落地。
// 此刻以纯函数 ConflictResolution.Resolve(mustRetreat, mustStayOrAdvance) 捕获 Req 3.7
// 的核心裁定语义：撤退/撤离优先——只要有撤退要求即为撤退，覆盖任何不退/推进要求。
// 输入空间恰为两个布尔（是否被要求退 × 是否被要求不退/推进），故用属性测试穷举全部组合。
public class RetreatPriorityProperties
{
    /// <summary>
    /// Property 29（退优先 · ≥100 迭代）：对任意 (mustRetreat, mustStayOrAdvance) 布尔组合，
    ///  1) 当 mustRetreat 为真时，结果恒为 <see cref="ConflictOutcome.Retreat"/>——与另一输入无关（退优先）；
    ///  2) 仅当 mustRetreat 为假时，才由 mustStayOrAdvance 决定 StayOrAdvance / StayInPlace。
    /// **Validates: Requirements 3.7**
    /// </summary>
    [Fact]
    public void Resolve_RetreatTakesPriority_OverStayOrAdvance()
    {
        Gen.Select(Gen.Bool, Gen.Bool).Sample((mustRetreat, mustStayOrAdvance) =>
        {
            var outcome = ConflictResolution.Resolve(mustRetreat, mustStayOrAdvance);

            if (mustRetreat)
            {
                // 退优先：无论是否同时被要求不退/推进，结果都必须是撤退。
                return outcome == ConflictOutcome.Retreat;
            }

            // mustRetreat 为假：不得产出撤退；由 mustStayOrAdvance 决定余下结果。
            var expected = mustStayOrAdvance
                ? ConflictOutcome.StayOrAdvance
                : ConflictOutcome.StayInPlace;
            return outcome == expected;
        }, iter: 1000);
    }

    /// <summary>
    /// Property 29（示例断言）：显式锁定「同时要求退与不退/推进」这一冲突焦点情形——
    /// 结果为撤退，作为随机属性之外的可读回归锚点。
    /// **Validates: Requirements 3.7**
    /// </summary>
    [Fact]
    public void Resolve_BothRetreatAndAdvance_YieldsRetreat()
    {
        Assert.Equal(ConflictOutcome.Retreat, ConflictResolution.Resolve(mustRetreat: true, mustStayOrAdvance: true));
    }

    /// <summary>
    /// Property 29（示例断言）：仅被要求不退/推进（无撤退要求）时结果为 StayOrAdvance；
    /// 无任何位移要求时为 StayInPlace。
    /// **Validates: Requirements 3.7**
    /// </summary>
    [Fact]
    public void Resolve_WithoutRetreat_FollowsStayOrAdvance()
    {
        Assert.Equal(ConflictOutcome.StayOrAdvance, ConflictResolution.Resolve(mustRetreat: false, mustStayOrAdvance: true));
        Assert.Equal(ConflictOutcome.StayInPlace, ConflictResolution.Resolve(mustRetreat: false, mustStayOrAdvance: false));
    }
}
