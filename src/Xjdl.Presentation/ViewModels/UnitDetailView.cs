using Xjdl.Core.State;

namespace Xjdl.Game.Presentation.ViewModels;

/// <summary>
/// 己方单位详情（供 <c>InfoPanel</c> 显示，Req 5.2）。
/// 展示完整属性与当前命令。
/// </summary>
public sealed record UnitDetailView(
    UnitId Id,
    Side Side,
    UnitClass Class,
    int Attack,
    int Defense,
    int ResilienceLeft,
    int Movement,
    int Vision,
    Command CurrentCommand);
