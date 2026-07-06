using Godot;

namespace Xjdl.Game.Render;

/// <summary>
/// 相机控制器（节点层，Req 3.5）：以平移与缩放浏览超出单屏范围的大地图。
///
/// 交互：
/// <list type="bullet">
///   <item>中键或右键拖动 → 平移视图。</item>
///   <item>方向键 / WASD → 平移视图。</item>
///   <item>鼠标滚轮 → 以光标为锚点缩放（钳制在合理上下限）。</item>
/// </list>
///
/// 说明：<c>Match.tscn</c> 的完整节点树组装（含把本脚本挂到 <c>Camera2D</c>）在任务 15.2
/// 统一完成；此处提供可复用的相机脚本，避免与 15.2 的场景组装冲突。仅处理浏览用的
/// 相机变换，不参与任何规则计算。
/// </summary>
public partial class CameraController : Camera2D
{
    /// <summary>缩放下限（视野最广，Zoom 越小看得越远）。</summary>
    [Export]
    public float MinZoom { get; set; } = 0.25f;

    /// <summary>缩放上限（放得最大）。</summary>
    [Export]
    public float MaxZoom { get; set; } = 4.0f;

    /// <summary>每一格滚轮的缩放系数。</summary>
    [Export]
    public float ZoomStep { get; set; } = 1.1f;

    /// <summary>键盘平移速度（像素/秒，未计缩放）。</summary>
    [Export]
    public float KeyPanSpeed { get; set; } = 600.0f;

    private bool _dragging;

    public override void _Ready()
    {
        // 作为对局的活动相机启用。
        MakeCurrent();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventMouseButton mouseButton:
                HandleMouseButton(mouseButton);
                break;
            case InputEventMouseMotion motion when _dragging:
                // 拖动平移：屏幕位移需除以缩放换算为世界位移。
                Position -= motion.Relative / Zoom;
                break;
        }
    }

    public override void _Process(double delta)
    {
        var dir = Vector2.Zero;

        // 完全限定 Godot.Input：本工程存在同级命名空间 Xjdl.Game.Input（选中/计划/临机机动
        // 控制器所在），未限定的 Input 会解析为该命名空间而非 Godot 全局输入类。
        if (Godot.Input.IsKeyPressed(Key.Left) || Godot.Input.IsKeyPressed(Key.A))
        {
            dir.X -= 1.0f;
        }

        if (Godot.Input.IsKeyPressed(Key.Right) || Godot.Input.IsKeyPressed(Key.D))
        {
            dir.X += 1.0f;
        }

        if (Godot.Input.IsKeyPressed(Key.Up) || Godot.Input.IsKeyPressed(Key.W))
        {
            dir.Y -= 1.0f;
        }

        if (Godot.Input.IsKeyPressed(Key.Down) || Godot.Input.IsKeyPressed(Key.S))
        {
            dir.Y += 1.0f;
        }

        if (dir != Vector2.Zero)
        {
            // 缩放越大（放得越近）平移越慢，保持手感一致。
            Position += dir.Normalized() * KeyPanSpeed * (float)delta / Zoom.X;
        }
    }

    private void HandleMouseButton(InputEventMouseButton mouseButton)
    {
        switch (mouseButton.ButtonIndex)
        {
            case MouseButton.Middle:
            case MouseButton.Right:
                _dragging = mouseButton.Pressed;
                break;
            case MouseButton.WheelUp when mouseButton.Pressed:
                ZoomAt(mouseButton.Position, ZoomStep);
                break;
            case MouseButton.WheelDown when mouseButton.Pressed:
                ZoomAt(mouseButton.Position, 1.0f / ZoomStep);
                break;
        }
    }

    /// <summary>
    /// 以给定屏幕坐标为锚点缩放，使锚点下的世界位置在缩放前后保持不动。
    /// 缩放值钳制在 [<see cref="MinZoom"/>, <see cref="MaxZoom"/>]。
    /// </summary>
    private void ZoomAt(Vector2 screenAnchor, float factor)
    {
        var oldZoom = Zoom.X;
        var newZoom = Mathf.Clamp(oldZoom * factor, MinZoom, MaxZoom);
        if (Mathf.IsEqualApprox(newZoom, oldZoom))
        {
            return;
        }

        var worldBefore = GetGlobalMousePosition();
        Zoom = new Vector2(newZoom, newZoom);
        var worldAfter = GetGlobalMousePosition();

        // 补偿因缩放导致的锚点世界位置漂移，实现"以光标为中心缩放"。
        Position += worldBefore - worldAfter;
    }
}
