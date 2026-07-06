using System.Collections.Generic;
using Godot;
using Xjdl.Game.Presentation.ViewModels;

namespace Xjdl.Game.Render;

/// <summary>
/// 计划箭头渲染器（节点层，Req 6.2/6.5）。
/// <para>
/// 消费纯层 <see cref="PathPlanner"/>/<see cref="PresentationMapper"/> 产出的 <see cref="ArrowView"/>：
/// 沿 <see cref="ArrowView.Points"/> 逐格中心画折线，并在末段画箭头（Req 6.2）；在箭头末端附近以文本
/// 展示已用消耗与剩余机动点（<see cref="ArrowView.UsedCost"/> 与
/// <c>MovementBudget - UsedCost</c>，Req 6.5）。
/// </para>
/// <para>
/// 视觉区分（Req 6.5）：<see cref="ArrowView.IsAttackPrep"/> 为真时以进攻准备样式（醒目红 + 虚线）绘制，
/// 与普通移动箭头（蓝实线）区分；<see cref="ArrowView.BlockedAhead"/> 为真时以警示色 + 末端禁入标记提示
/// 超机动点或禁入地形。
/// </para>
/// <para>
/// 新快照即重绘（Req 6.2）：<see cref="Render"/> 存下 <see cref="ArrowView"/> 并 <c>QueueRedraw</c>；
/// 传入 <c>null</c> 表示无箭头，清空绘制。纯层的 <see cref="Vector2D"/> 仅在此边界转换为 Godot
/// <see cref="Vector2"/>（Req 17.4）。规则计算全部委托 Core，本渲染器仅消费已映射视图 DTO。
/// </para>
/// </summary>
public partial class MovementArrowNode : Node2D
{
    private const float LineWidth = 4f;

    // 末段箭头几何（px）。
    private const float ArrowHeadLength = 18f;
    private const float ArrowHeadHalfWidth = 10f;

    // 虚线（进攻准备样式）分段长度（px）。
    private const float DashLength = 12f;
    private const float GapLength = 8f;

    private const int LabelFontSize = 13;

    // 普通移动：蓝实线。
    private static readonly Color MoveColor = new(0.25f, 0.55f, 0.95f, 0.95f);

    // 进攻准备：醒目橙红虚线。
    private static readonly Color AttackPrepColor = new(0.95f, 0.35f, 0.20f, 0.95f);

    // 超限/禁入：警示黄红。
    private static readonly Color BlockedColor = new(0.95f, 0.20f, 0.15f, 1.0f);

    private static readonly Color LabelColor = new(1f, 1f, 1f);
    private static readonly Color LabelShadow = new(0f, 0f, 0f, 0.7f);

    private ArrowView? _arrow;

    /// <summary>
    /// 载入/更新箭头快照即重绘（Req 6.2）：存下 <see cref="ArrowView"/> 并请求重绘。
    /// 传入 <c>null</c> 清空箭头（不绘制任何内容）。
    /// </summary>
    public void Render(ArrowView? arrow)
    {
        _arrow = arrow;
        QueueRedraw();
    }

    public override void _Draw()
    {
        ArrowView? snapshot = _arrow;
        if (snapshot is null)
        {
            return;
        }

        ArrowView arrow = snapshot;
        Vector2[] points = ToGodotPoints(arrow.Points);
        if (points.Length == 0)
        {
            return;
        }

        // 进攻准备与普通移动样式区分；超限/禁入以警示色覆盖（Req 6.5）。
        Color lineColor = arrow.BlockedAhead
            ? BlockedColor
            : arrow.IsAttackPrep ? AttackPrepColor : MoveColor;

        // 折线主体：进攻准备用虚线区分，其余用实线（Req 6.2/6.5）。
        DrawPath(points, lineColor, arrow.IsAttackPrep);

        // 末段箭头（Req 6.2）：需要至少两个点确定方向。
        Vector2 tip = points[^1];
        if (points.Length >= 2)
        {
            Vector2 from = points[^2];
            DrawArrowHead(from, tip, lineColor);
        }

        // 超限/禁入的末端禁入标记（Req 6.5）：在终点画一个醒目的叉号。
        if (arrow.BlockedAhead)
        {
            DrawBlockedMarker(tip);
        }

        // 已用消耗 / 剩余机动点文本（Req 6.5）。
        int remaining = arrow.MovementBudget - arrow.UsedCost;
        string label = arrow.BlockedAhead
            ? $"已用 {arrow.UsedCost}/{arrow.MovementBudget}（受阻）"
            : $"已用 {arrow.UsedCost}/{arrow.MovementBudget}（余 {remaining}）";
        DrawLabel(tip + new Vector2(ArrowHeadLength, -ArrowHeadLength), label);
    }

    /// <summary>
    /// 画折线主体：<paramref name="dashed"/> 为真时逐段以虚线绘制（进攻准备样式）。
    /// </summary>
    private void DrawPath(Vector2[] points, Color color, bool dashed)
    {
        if (points.Length == 1)
        {
            // 单点：画一个小标记表示起点。
            DrawCircle(points[0], LineWidth, color);
            return;
        }

        for (int i = 0; i < points.Length - 1; i++)
        {
            if (dashed)
            {
                DrawDashedSegment(points[i], points[i + 1], color);
            }
            else
            {
                DrawLine(points[i], points[i + 1], color, LineWidth, true);
            }
        }
    }

    private void DrawDashedSegment(Vector2 from, Vector2 to, Color color)
    {
        Vector2 delta = to - from;
        float length = delta.Length();
        if (length <= 0.001f)
        {
            return;
        }

        Vector2 dir = delta / length;
        float traveled = 0f;
        while (traveled < length)
        {
            float segStart = traveled;
            float segEnd = Mathf.Min(traveled + DashLength, length);
            DrawLine(from + (dir * segStart), from + (dir * segEnd), color, LineWidth, true);
            traveled += DashLength + GapLength;
        }
    }

    /// <summary>在 <paramref name="tip"/> 处按 <paramref name="from"/>→<paramref name="tip"/> 方向画填充三角箭头。</summary>
    private void DrawArrowHead(Vector2 from, Vector2 tip, Color color)
    {
        Vector2 dir = (tip - from);
        float len = dir.Length();
        if (len <= 0.001f)
        {
            return;
        }

        dir /= len;
        var normal = new Vector2(-dir.Y, dir.X);
        Vector2 baseCenter = tip - (dir * ArrowHeadLength);
        Vector2 left = baseCenter + (normal * ArrowHeadHalfWidth);
        Vector2 right = baseCenter - (normal * ArrowHeadHalfWidth);

        DrawColoredPolygon(new[] { tip, left, right }, color);
    }

    /// <summary>在终点画醒目叉号，标记超机动点/禁入地形（Req 6.5）。</summary>
    private void DrawBlockedMarker(Vector2 center)
    {
        const float r = 9f;
        DrawLine(center + new Vector2(-r, -r), center + new Vector2(r, r), BlockedColor, 3f, true);
        DrawLine(center + new Vector2(-r, r), center + new Vector2(r, -r), BlockedColor, 3f, true);
    }

    private void DrawLabel(Vector2 origin, string text)
    {
        Font font = ThemeDB.FallbackFont;
        // 阴影 + 主文本，保证在任意地形色上可读。
        DrawString(font, origin + new Vector2(1f, 1f), text, HorizontalAlignment.Left, -1f, LabelFontSize, LabelShadow);
        DrawString(font, origin, text, HorizontalAlignment.Left, -1f, LabelFontSize, LabelColor);
    }

    private static Vector2[] ToGodotPoints(IReadOnlyList<Vector2D> points)
    {
        if (points is null)
        {
            return System.Array.Empty<Vector2>();
        }

        var result = new Vector2[points.Count];
        for (int i = 0; i < points.Count; i++)
        {
            result[i] = new Vector2((float)points[i].X, (float)points[i].Y);
        }

        return result;
    }
}
