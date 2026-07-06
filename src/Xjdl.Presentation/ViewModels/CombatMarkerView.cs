using Xjdl.Core.Hex;

namespace Xjdl.Game.Presentation.ViewModels;

/// <summary>
/// 战斗位置标识视图（供 <c>CombatMarkerLayer</c> 绘制，Req 8.5 扩展）。
/// 表示"本回合某处发生战斗"的一个可视标识：其所在格、格中心像素与建议标识半径。
/// <para>
/// 纯投影 DTO，不含任何绘制样式——具体图标（默认脉动红环）由节点层的
/// <c>ICombatMarkerStyle</c> 决定，便于后续替换为别的图标而不改本类型与映射层。
/// </para>
/// </summary>
/// <param name="Cell">战斗发生的六角格坐标（用于聚焦/消除单个标识）。</param>
/// <param name="Center">该格中心像素坐标（标识锚点与镜头居中目标）。</param>
/// <param name="Radius">建议标识半径（像素，取格中心到顶点的距离）。</param>
public sealed record CombatMarkerView(
    HexCoord Cell,
    Vector2D Center,
    double Radius);
