using Xjdl.Core.Hex;
using Xjdl.Core.State;

namespace Xjdl.Game.Presentation.ViewModels;

/// <summary>
/// 敌方单位视图：按 <see cref="Visibility"/> 过滤（Req 10.2/10.3/10.4、17.2）。
/// <para><see cref="Visibility.Identified"/>：兵种与攻/防/韧性/堆叠全字段可见。</para>
/// <para><see cref="Visibility.Spotted"/>：<see cref="Class"/>/<see cref="Attack"/>/<see cref="Defense"/>/
/// <see cref="ResilienceLeft"/>/<see cref="StackCount"/> 均为 <c>null</c>——数据源头不泄露，仅暴露坐标与"有敌"。</para>
/// <para><see cref="Visibility.Hidden"/>：不产出任何条目。</para>
/// </summary>
public sealed record EnemyView(
    UnitId Id,
    Visibility Visibility,
    Vector2D Center,
    HexCoord Position,
    UnitClass? Class,
    int? Attack,
    int? Defense,
    int? ResilienceLeft,
    int? StackCount);
