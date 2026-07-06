using System.Collections.Generic;
using Godot;
using Xjdl.Core.Hex;
using Xjdl.Core.State;
using Xjdl.Game.Presentation.ViewModels;
using Side = Xjdl.Core.State.Side;

namespace Xjdl.Game.Render;

/// <summary>
/// 迷雾呈现层（节点层，Req 10.1–10.5、11.2）。
/// <para>
/// 依据每个 <see cref="EnemyView.Visibility"/> 决定敌方单位的呈现：
/// <see cref="Visibility.Identified"/> 画完整棋子 + 当前攻/防/韧性 + 堆叠数量（Req 10.2）；
/// <see cref="Visibility.Spotted"/> 仅画"未明"标记（灰块 + "?"），不显示兵种与数值（Req 10.3）；
/// <see cref="Visibility.Hidden"/> 不产出条目、不绘制任何内容（Req 10.4 由 <c>PresentationMapper</c> 在源头保证）。
/// </para>
/// <para>
/// 复用与 <see cref="UnitRenderer"/> 一致的视觉语言（阵营色、兵种标签、攻/防/韧性格式、
/// 未明灰块 "?" 标记、堆叠角标）以避免分歧。
/// </para>
/// <para>
/// 刷新语义（Req 10.5）：观察方或 <see cref="GameState"/> 变化时，调用方
/// （<c>SelectionController</c>/<c>TurnController</c> 等，见后续任务）应先经
/// <c>PresentationMapper.EnemyUnits</c> 重新映射得到最新 <see cref="EnemyView"/> 列表，
/// 再调用 <see cref="ApplyFog"/> 使呈现与迷雾输出一致。夜晚视野折减来自
/// <c>FogSystem</c>（经 <c>PresentationMapper</c>），本视图不重算规则（Req 11.2）。
/// </para>
/// </summary>
public partial class FogView : Node2D
{
    // 棋子外接圆半径（px，占位视觉），与 UnitRenderer 保持一致。
    private const float TokenRadius = 18f;

    // 同格堆叠时每个后续棋子相对前一个的叠放偏移（px），与 UnitRenderer 保持一致。
    private static readonly Vector2 StackOffset = new(7f, -7f);

    private const int LabelFontSize = 14;
    private const int StatsFontSize = 11;
    private const int BadgeFontSize = 11;

    private IReadOnlyList<EnemyView> _enemies = System.Array.Empty<EnemyView>();

    /// <summary>
    /// 存下最新一批敌方视图并触发重绘（Req 10.1/10.5）。传入列表已由
    /// <c>PresentationMapper.EnemyUnits</c> 按 <c>FogSystem.Compute</c> 的可见度过滤：
    /// 仅含 <see cref="Visibility.Identified"/> / <see cref="Visibility.Spotted"/>，
    /// <see cref="Visibility.Hidden"/> 单位不在列表中（Req 10.4）。
    /// </summary>
    public void ApplyFog(IReadOnlyList<EnemyView> enemies)
    {
        _enemies = enemies ?? System.Array.Empty<EnemyView>();
        QueueRedraw();
    }

    public override void _Draw()
    {
        // 按所在格聚合，使同格多个敌方棋子以叠放偏移 + 数量角标呈现（Req 4.4 复用）。
        var groups = new Dictionary<HexCoord, List<Token>>();

        foreach (EnemyView e in _enemies)
        {
            // Hidden 不产出条目，此处列表内只有 Identified / Spotted。
            bool identified = e.Visibility == Visibility.Identified;
            AddToken(groups, new Token(
                Position: e.Position,
                Center: ToGodot(e.Center),
                FillColor: identified ? SideColor(Side.Red) : SpottedColor,
                ClassLabel: identified && e.Class.HasValue ? ClassLabel(e.Class.Value) : "?",
                Stats: identified ? FormatStats(e.Attack, e.Defense, e.ResilienceLeft) : null,
                StackCount: identified ? e.StackCount : null));
        }

        foreach (List<Token> stack in groups.Values)
        {
            for (int i = 0; i < stack.Count; i++)
            {
                Vector2 center = stack[i].Center + (StackOffset * i);
                DrawToken(stack[i], center);
            }
        }
    }

    private static void AddToken(Dictionary<HexCoord, List<Token>> groups, Token token)
    {
        if (!groups.TryGetValue(token.Position, out List<Token>? list))
        {
            list = new List<Token>();
            groups[token.Position] = list;
        }

        list.Add(token);
    }

    private void DrawToken(Token token, Vector2 center)
    {
        // Identified：阵营色棋子；Spotted：中性灰"未明"块（Req 10.2/10.3）。
        DrawCircle(center, TokenRadius, token.FillColor);
        DrawArc(center, TokenRadius, 0f, Mathf.Tau, 32, OutlineColor, 2f, true);

        Font font = ThemeDB.FallbackFont;

        // 兵种标签（Spotted 时为 "?" 未明标记）居中显示（Req 10.2/10.3）。
        DrawStringCentered(font, center + new Vector2(0f, LabelFontSize * 0.35f), token.ClassLabel, LabelFontSize, LabelColor);

        // 识别态：显示当前攻/防/韧性（Req 10.2）；未明态不显示数值（Req 10.3）。
        if (token.Stats is { } stats)
        {
            DrawStringCentered(font, center + new Vector2(0f, TokenRadius + StatsFontSize), stats, StatsFontSize, StatsColor);
        }

        // 识别态堆叠数量角标：>1 时以右上角标呈现，不逐一遮挡（Req 10.2）。
        if (token.StackCount is { } count && count > 1)
        {
            Vector2 badgeCenter = center + new Vector2(TokenRadius * 0.7f, -TokenRadius * 0.7f);
            DrawCircle(badgeCenter, BadgeFontSize, BadgeColor);
            DrawStringCentered(font, badgeCenter + new Vector2(0f, BadgeFontSize * 0.35f), count.ToString(), BadgeFontSize, LabelColor);
        }
    }

    private void DrawStringCentered(Font font, Vector2 baselineCenter, string text, int fontSize, Color color)
    {
        Vector2 size = font.GetStringSize(text, HorizontalAlignment.Left, -1f, fontSize);
        var origin = new Vector2(baselineCenter.X - (size.X * 0.5f), baselineCenter.Y);
        DrawString(font, origin, text, HorizontalAlignment.Left, -1f, fontSize, color);
    }

    private static Vector2 ToGodot(Vector2D v) => new((float)v.X, (float)v.Y);

    private static Color SideColor(Side side) => side == Side.Blue
        ? new Color(0.20f, 0.45f, 0.85f)   // 蓝方
        : new Color(0.85f, 0.25f, 0.25f);  // 红方

    // 未明（Spotted）敌方：中性灰块 + "?" 标记（Req 10.3），与 UnitRenderer 保持一致。
    private static readonly Color SpottedColor = new(0.45f, 0.45f, 0.48f);
    private static readonly Color OutlineColor = new(0.08f, 0.08f, 0.10f);
    private static readonly Color LabelColor = new(1f, 1f, 1f);
    private static readonly Color StatsColor = new(0.95f, 0.95f, 0.80f);
    private static readonly Color BadgeColor = new(0.12f, 0.12f, 0.14f);

    // 兵种占位标签（ASCII，保证 FallbackFont 可渲染），与 UnitRenderer 保持一致。
    private static string ClassLabel(UnitClass cls) => cls switch
    {
        UnitClass.LineHold => "L",
        UnitClass.Elite => "E",
        UnitClass.FireSupport => "F",
        UnitClass.Special => "S",
        _ => "?",
    };

    private static string? FormatStats(int? attack, int? defense, int? resilienceLeft)
    {
        if (attack is null || defense is null || resilienceLeft is null)
        {
            return null;
        }

        return $"{attack.Value}/{defense.Value}/{resilienceLeft.Value}";
    }

    /// <summary>单个敌方棋子的绘制信息（值语义）。</summary>
    private readonly record struct Token(
        HexCoord Position,
        Vector2 Center,
        Color FillColor,
        string ClassLabel,
        string? Stats,
        int? StackCount);
}
