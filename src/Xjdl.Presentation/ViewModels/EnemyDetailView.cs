using Xjdl.Core.Hex;
using Xjdl.Core.State;

namespace Xjdl.Game.Presentation.ViewModels;

/// <summary>
/// 敌方单位详情（供 <c>InfoPanel</c> 显示，按可见度裁字段，Req 5.3/5.4）。
/// <see cref="Class"/>/<see cref="Attack"/>/<see cref="Defense"/>/<see cref="ResilienceLeft"/>
/// 仅在 <see cref="Visibility.Identified"/> 时有值；不含敌方命令等应保密项。
/// </summary>
public sealed record EnemyDetailView(
    Visibility Visibility,
    HexCoord Position,
    UnitClass? Class,
    int? Attack,
    int? Defense,
    int? ResilienceLeft);
