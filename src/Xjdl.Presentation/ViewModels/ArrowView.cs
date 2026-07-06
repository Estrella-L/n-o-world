using Xjdl.Core.State;

namespace Xjdl.Game.Presentation.ViewModels;

/// <summary>
/// 计划箭头视图（<c>MovementArrow</c>，Req 6.2/6.5）。
/// 逐格中心连成折线，末端画箭头；展示已用消耗/机动点与超限/禁入提示。
/// </summary>
public sealed record ArrowView(
    UnitId Unit,
    IReadOnlyList<Vector2D> Points,
    int UsedCost,
    int MovementBudget,
    bool IsAttackPrep,
    bool BlockedAhead);
