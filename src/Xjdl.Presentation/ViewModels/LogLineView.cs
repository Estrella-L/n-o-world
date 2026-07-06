using Xjdl.Core.Hex;

namespace Xjdl.Game.Presentation.ViewModels;

/// <summary>
/// 回合日志行（供 <c>TurnLogView</c> 显示，Req 9.1/9.2/9.3）。
/// <see cref="Kind"/> 为原始 <c>TurnRecordEntry.Kind</c>（供分组/图标）；
/// <see cref="Text"/> 为表现层生成的中文文案；<see cref="Locate"/> 为关联格/单位坐标，可点击定位。
/// </summary>
public sealed record LogLineView(
    int Turn,
    string Kind,
    string Text,
    HexCoord? Locate);
