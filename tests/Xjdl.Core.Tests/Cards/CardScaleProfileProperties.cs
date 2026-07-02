using CsCheck;
using Xjdl.Core.Cards;
using Xjdl.Core.State;

namespace Xjdl.Core.Tests.Cards;

/// <summary>
/// 规模档配置套用的属性测试（CsCheck，一属性一测试，至少 100 次迭代）。
/// </summary>
public class CardScaleProfileProperties
{
    // 随机地图规模档：CP 产出 / cpMax / 牌库大小 / 手牌上限。
    private static readonly Gen<MapScaleProfile> GenProfile =
        from cpPerTurn in Gen.Int[0, 20]
        from cpMax in Gen.Int[0, 100]
        from deckSize in Gen.Int[0, 60]
        from handLimit in Gen.Int[0, 12]
        select new MapScaleProfile(cpPerTurn, cpMax, deckSize, handLimit);

    // Feature: core-rules-engine, Property 59: 规模档配置套用
    // For any MapScale, CardSystem.Init applies the corresponding MapScaleProfile.
    // Init 的具体保证：CpMax 取自 profile.CpMax，Cp 从 0 起累积（牌库/手牌初始为空，
    // 其内容由数据层加载注入，本层不持有卡池）。故断言 CpMax 映射与 Cp==0。
    // **Validates: Requirements 19.1**
    [Fact]
    public void Property59_InitAppliesScaleProfile()
    {
        GenProfile.Sample(
            profile =>
            {
                var state = CardSystem.Init(profile);

                Assert.Equal(profile.CpMax, state.CpMax);
                Assert.Equal(0, state.Cp);
                Assert.Empty(state.Deck);
                Assert.Empty(state.Hand);
            },
            iter: 100);
    }
}
