using Xjdl.Core.Hex;
using Xjdl.Core.State;

namespace Xjdl.Game.Presentation.ViewModels;

/// <summary>
/// 己方单位视图：完整信息（Req 4.1/4.2/4.3）。
/// 己方总是识别态，展示当前攻/防/韧性、机动/侦察、当前命令与同格堆叠数量。
/// </summary>
public sealed record UnitView(
    UnitId Id,
    Side Side,
    UnitClass Class,
    Vector2D Center,
    HexCoord Position,
    int Attack,
    int Defense,
    int ResilienceLeft,
    int Movement,
    int Vision,
    Command CurrentCommand,
    int StackCount,
    bool IsMain);
