using Xjdl.Core.State;

namespace Xjdl.Game.Presentation.ViewModels;

/// <summary>
/// 昼夜视图（Req 11.1）：当前昼夜阶段与其中文显示名。
/// </summary>
public sealed record DayNightView(DayNightPhase Phase, string DisplayName);
