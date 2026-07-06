namespace Xjdl.Game.Presentation.ViewModels;

/// <summary>
/// 纯层自带的二维向量（不使用 <c>Godot.Vector2</c>，保持纯 C# 表现逻辑层无引擎依赖）。
/// 节点层在边界处将 <see cref="Vector2D"/> 转换为 Godot <c>Vector2</c>（Req 17.1/17.4）。
/// </summary>
public readonly record struct Vector2D(double X, double Y);
