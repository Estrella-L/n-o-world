using System.Collections.Generic;
using Godot;
using Xjdl.Core.Hex;
using Xjdl.Core.State;
using Xjdl.Game.Presentation.ViewModels;
using Side = Xjdl.Core.State.Side;

namespace Xjdl.Game.Render;

/// <summary>
/// 单位棋子渲染器（节点层，Req 4.1–4.5、10.2）。
/// <para>
/// 以占位几何 + 色块呈现己方与可见敌方棋子：阵营（<see cref="Side"/>）以颜色区分、
/// 兵种（<see cref="UnitClass"/>）以形内标签区分（Req 4.2）；识别态单位显示当前攻/防/韧性
/// 与同格堆叠数量（Req 4.3、10.2）；同格堆叠以叠放偏移 + 数量角标呈现、不逐一遮挡（Req 4.4）。
/// </para>
/// <para>
/// 新快照语义（Req 4.5）：每次 <see cref="Render"/> 用传入的视图列表整体替换绘制集合并
/// <c>QueueRedraw</c>，全部绘制在 <see cref="_Draw"/> 内完成——由此位置/状态与新快照同步，
/// 且已阵亡（不在新快照中）的棋子自然被移除。规则计算全部委托 Core，本渲染器仅消费已映射视图 DTO。
/// </para>
/// </summary>
public partial class UnitRenderer : Node2D
{
    // 棋子外接圆半径（px，占位视觉）。
    private const float TokenRadius = 18f;

    // 同格堆叠时每个后续棋子相对前一个的叠放偏移（px）。
    private static readonly Vector2 StackOffset = new(7f, -7f);

    private const int LabelFontSize = 14;
    private const int StatsFontSize = 11;
    private const int BadgeFontSize = 11;

    private IReadOnlyList<UnitView> _friendly = System.Array.Empty<UnitView>();
    private IReadOnlyList<EnemyView> _enemies = System.Array.Empty<EnemyView>();

    // 动画位置覆盖（UnitId.Value → 像素中心）：阶段动画期间由 PhaseAnimator 逐帧写入，
    // 使被移动的棋子沿轨迹平滑移动，而非等结算后瞬移到落点（Req 8.4/8.5）。
    // 有覆盖的单位在 _Draw 中脱离同格分组、按覆盖位单独绘制于最上层。
    private readonly Dictionary<int, Vector2> _animOverrides = new();

    /// <summary>
    /// 用新快照的己方与敌方视图整体替换绘制集合并触发重绘（Req 4.1/4.5）。
    /// 敌方列表已由 <c>PresentationMapper</c> 按可见度过滤：仅含 Identified / Spotted，
    /// Hidden 不在列表中（Req 10.4 由映射层保证）。
    /// </summary>
    public void Render(IReadOnlyList<UnitView> friendly, IReadOnlyList<EnemyView> enemies)
    {
        _friendly = friendly ?? System.Array.Empty<UnitView>();
        _enemies = enemies ?? System.Array.Empty<EnemyView>();
        QueueRedraw();
    }

    /// <summary>
    /// 设置某单位的动画位置覆盖并重绘（Req 8.4/8.5）：阶段动画中由 <c>PhaseAnimator</c> 沿移动轨迹
    /// 逐帧调用，使棋子平滑移动。收尾/跳过时应调用 <see cref="ClearAnimationOverrides"/> 清除，
    /// 之后按结算后快照重绘。
    /// </summary>
    public void SetAnimationOverride(UnitId unit, Vector2 center)
    {
        _animOverrides[unit.Value] = center;
        QueueRedraw();
    }

    /// <summary>清除全部动画位置覆盖并重绘，使棋子回到其快照落点（Req 8.4/8.6/8.7）。</summary>
    public void ClearAnimationOverrides()
    {
        if (_animOverrides.Count == 0)
        {
            return;
        }

        _animOverrides.Clear();
        QueueRedraw();
    }

    public override void _Draw()
    {
        // 按所在格聚合，使同格多个棋子以叠放偏移 + 数量角标呈现（Req 4.4）。
        var groups = new Dictionary<HexCoord, List<Token>>();

        // 有动画覆盖的单位：脱离分组、按覆盖位单独绘制于最上层（阶段动画中的移动棋子）。
        var moving = new List<(Token Token, Vector2 Center)>();

        foreach (UnitView u in _friendly)
        {
            var token = new Token(
                Position: u.Position,
                Center: ToGodot(u.Center),
                FillColor: SideColor(u.Side),
                ClassLabel: ClassLabel(u.Class),
                Stats: FormatStats(u.Attack, u.Defense, u.ResilienceLeft),
                StackCount: u.StackCount,
                Unknown: false);

            if (_animOverrides.TryGetValue(u.Id.Value, out Vector2 overrideCenter))
            {
                moving.Add((token, overrideCenter));
            }
            else
            {
                AddToken(groups, token);
            }
        }

        foreach (EnemyView e in _enemies)
        {
            bool identified = e.Visibility == Visibility.Identified;
            var token = new Token(
                Position: e.Position,
                Center: ToGodot(e.Center),
                FillColor: identified ? SideColor(Side.Red) : SpottedColor,
                ClassLabel: identified && e.Class.HasValue ? ClassLabel(e.Class.Value) : "?",
                Stats: identified ? FormatStats(e.Attack, e.Defense, e.ResilienceLeft) : null,
                StackCount: identified ? e.StackCount : null,
                Unknown: !identified);

            if (_animOverrides.TryGetValue(e.Id.Value, out Vector2 overrideCenter))
            {
                moving.Add((token, overrideCenter));
            }
            else
            {
                AddToken(groups, token);
            }
        }

        foreach (List<Token> stack in groups.Values)
        {
            for (int i = 0; i < stack.Count; i++)
            {
                Vector2 center = stack[i].Center + (StackOffset * i);
                DrawToken(stack[i], center);
            }
        }

        // 移动中的棋子绘制在最上层，直接落在动画覆盖位（不参与同格叠放偏移）。
        foreach ((Token token, Vector2 center) in moving)
        {
            DrawToken(token, center);
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
        // 阵营色填充 + 深色描边，作为可区分占位棋子（Req 4.2）。
        DrawCircle(center, TokenRadius, token.FillColor);
        DrawArc(center, TokenRadius, 0f, Mathf.Tau, 32, OutlineColor, 2f, true);

        Font font = ThemeDB.FallbackFont;

        // 兵种标签（Unknown 时为 "?"）居中显示，作为兵种/未明的可区分视觉（Req 4.2/10.3）。
        DrawStringCentered(font, center + new Vector2(0f, LabelFontSize * 0.35f), token.ClassLabel, LabelFontSize, LabelColor);

        // 识别态：显示当前攻/防/韧性（Req 4.3、10.2）。
        if (token.Stats is { } stats)
        {
            DrawStringCentered(font, center + new Vector2(0f, TokenRadius + StatsFontSize), stats, StatsFontSize, StatsColor);
        }

        // 堆叠数量角标：>1 时以右上角标呈现，不逐一遮挡（Req 4.4）。
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

    // 未明（Spotted）敌方：中性灰块 + "?" 标记（Req 10.3）。
    private static readonly Color SpottedColor = new(0.45f, 0.45f, 0.48f);
    private static readonly Color OutlineColor = new(0.08f, 0.08f, 0.10f);
    private static readonly Color LabelColor = new(1f, 1f, 1f);
    private static readonly Color StatsColor = new(0.95f, 0.95f, 0.80f);
    private static readonly Color BadgeColor = new(0.12f, 0.12f, 0.14f);

    // 兵种占位标签（ASCII，保证 FallbackFont 可渲染）。
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

    private static string FormatStats(int attack, int defense, int resilienceLeft)
        => $"{attack}/{defense}/{resilienceLeft}";

    /// <summary>单个棋子的绘制信息（值语义）。</summary>
    private readonly record struct Token(
        HexCoord Position,
        Vector2 Center,
        Color FillColor,
        string ClassLabel,
        string? Stats,
        int? StackCount,
        bool Unknown);
}
