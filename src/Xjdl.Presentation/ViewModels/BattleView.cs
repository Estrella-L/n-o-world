using Xjdl.Core.Hex;

namespace Xjdl.Game.Presentation.ViewModels;

/// <summary>
/// 一场战斗的分组视图（供逐场结算动画，Req 8.5 扩展）。
/// 由 <c>PresentationMapper.Battles</c> 按 <c>battle</c> id 从回合日志聚合而成，
/// 把同一场战斗的选表、掷骰读表结果、撤退、推进等条目归到一起，供
/// <c>PhaseAnimator</c> 一场一场地聚焦呈现。
/// <para>
/// 全部字段为已本地化好的中文文案与纯坐标：<see cref="Narrative"/> 是"描述双方交战"的
/// 一段引导文本，<see cref="Steps"/> 是该场按顺序的分步结果行（复用日志文案）。
/// <see cref="Cell"/>/<see cref="Center"/> 供镜头居中与战斗标识定位；无关联格（罕见的
/// 无法归属余波）时为 <c>null</c>。
/// </para>
/// </summary>
/// <param name="BattleId">Core 写入的战斗标识（或表现层为遭遇/余波合成的键）。</param>
/// <param name="Cell">战斗发生的六角格；无法定位时为 <c>null</c>。</param>
/// <param name="Center">该格中心像素（镜头居中目标）；无格时为 <c>null</c>。</param>
/// <param name="Radius">建议标识半径（像素）；无格时为 0。</param>
/// <param name="Narrative">描述双方交战的一段引导文本（本地化中文）。</param>
/// <param name="Steps">该场按顺序的分步结果文案（选表/读表/撤退/推进等）。</param>
public sealed record BattleView(
    string BattleId,
    HexCoord? Cell,
    Vector2D? Center,
    double Radius,
    string Narrative,
    IReadOnlyList<string> Steps);
