using System.Collections.Generic;
using Godot;
using Xjdl.Core.Hex;
using Xjdl.Game.Presentation.ViewModels;

namespace Xjdl.Game.Render;

/// <summary>
/// 战斗位置标识的<b>可替换绘制样式</b>（Req 8.5 扩展）。默认实现为脉动红环
/// （<see cref="PulsingRingStyle"/>）；后续要换别的图标（如交叉刀剑贴图、感叹号等），
/// 只需实现本接口并赋给 <see cref="CombatMarkerLayer.MarkerStyle"/>，无需改动图层与映射层。
/// </summary>
public interface ICombatMarkerStyle
{
    /// <summary>在 <paramref name="canvas"/> 的 <c>_Draw</c> 期间绘制单个标识。</summary>
    /// <param name="canvas">承载绘制的 <see cref="Node2D"/>（提供 <c>DrawArc</c> 等即时绘制 API）。</param>
    /// <param name="center">标识中心（像素）。</param>
    /// <param name="radius">建议半径（像素）。</param>
    /// <param name="pulse">脉动相位（随时间递增，用于呼吸动画）。</param>
    /// <param name="focused">是否为当前正在结算的那一场（应更醒目）。</param>
    void Draw(Node2D canvas, Vector2 center, float radius, float pulse, bool focused);
}

/// <summary>默认标识样式：脉动红环；聚焦时转橙、加粗并叠一圈内环。</summary>
public sealed class PulsingRingStyle : ICombatMarkerStyle
{
    /// <summary>常规（未聚焦）环色。</summary>
    public Color RingColor { get; set; } = new(0.90f, 0.15f, 0.15f, 0.95f);

    /// <summary>聚焦（当前结算中）环色。</summary>
    public Color FocusedColor { get; set; } = new(1.0f, 0.55f, 0.10f, 1.0f);

    /// <summary>基础线宽（像素）。</summary>
    public float BaseWidth { get; set; } = 3.0f;

    /// <inheritdoc />
    public void Draw(Node2D canvas, Vector2 center, float radius, float pulse, bool focused)
    {
        // 呼吸系数 t ∈ [0,1]：驱动半径与透明度的周期变化。
        var t = 0.5f * (1f + Mathf.Sin(pulse * Mathf.Tau));
        var r = radius * (0.72f + (0.18f * t));

        var color = focused ? FocusedColor : RingColor;
        if (!focused)
        {
            color.A *= 0.6f + (0.4f * t);
        }

        var width = BaseWidth * (focused ? 1.7f : 1.0f);
        canvas.DrawArc(center, r, 0f, Mathf.Tau, 48, color, width, true);

        if (focused)
        {
            // 聚焦叠一圈内环，进一步突出当前结算的战斗。
            canvas.DrawArc(center, r * 0.60f, 0f, Mathf.Tau, 36, color, width * 0.7f, true);
        }
    }
}

/// <summary>
/// 战斗位置标识层（节点层，Req 8.5 扩展）。在发生战斗的格上绘制脉动标识：
/// 接触阶段一次性亮出全部战斗标识，逐场结算时聚焦当前场、算完消除该场标识。
/// <para>
/// 本层只消费映射层产出的 <see cref="CombatMarkerView"/>（纯坐标 DTO），具体图标由可替换的
/// <see cref="ICombatMarkerStyle"/> 决定（默认脉动红环）。每帧在 <see cref="_Process"/> 推进
/// 脉动相位并请求重绘，所有绘制在 <see cref="_Draw"/> 内完成，不做任何规则计算。
/// </para>
/// </summary>
public partial class CombatMarkerLayer : Node2D
{
    private readonly List<CombatMarkerView> _markers = new();
    private HexCoord? _focus;
    private float _pulse;
    private ICombatMarkerStyle _style = new PulsingRingStyle();

    /// <summary>脉动速度（相位/秒）。</summary>
    [Export]
    public float PulseSpeed { get; set; } = 0.9f;

    /// <summary>
    /// 可替换的标识绘制样式（默认 <see cref="PulsingRingStyle"/>）。赋 <c>null</c> 时回退到默认样式。
    /// 换图标只改这里，图层与映射层不动。
    /// </summary>
    public ICombatMarkerStyle MarkerStyle
    {
        get => _style;
        set
        {
            _style = value ?? new PulsingRingStyle();
            QueueRedraw();
        }
    }

    /// <summary>亮出一批战斗标识（整体替换当前集合）并请求重绘。</summary>
    public void ShowMarkers(IReadOnlyList<CombatMarkerView> markers)
    {
        _markers.Clear();
        if (markers is not null)
        {
            _markers.AddRange(markers);
        }

        QueueRedraw();
    }

    /// <summary>设置当前聚焦（正在结算）的战斗格；<c>null</c> 表示无聚焦。</summary>
    public void SetFocus(HexCoord? cell)
    {
        _focus = cell;
        QueueRedraw();
    }

    /// <summary>消除某格的战斗标识（该场结算完毕后调用）。</summary>
    public void ResolveMarker(HexCoord? cell)
    {
        if (cell is not { } c)
        {
            return;
        }

        _markers.RemoveAll(m => m.Cell.Equals(c));
        if (_focus is { } f && f.Equals(c))
        {
            _focus = null;
        }

        QueueRedraw();
    }

    /// <summary>清除全部战斗标识与聚焦并请求重绘。</summary>
    public void Clear()
    {
        _markers.Clear();
        _focus = null;
        QueueRedraw();
    }

    /// <inheritdoc />
    public override void _Process(double delta)
    {
        if (_markers.Count == 0)
        {
            return;
        }

        _pulse += (float)delta * PulseSpeed;
        if (_pulse > 1_000f)
        {
            _pulse -= 1_000f;
        }

        QueueRedraw();
    }

    /// <inheritdoc />
    public override void _Draw()
    {
        foreach (var marker in _markers)
        {
            var center = new Vector2((float)marker.Center.X, (float)marker.Center.Y);
            var focused = _focus is { } f && f.Equals(marker.Cell);
            _style.Draw(this, center, (float)marker.Radius, _pulse, focused);
        }
    }
}
