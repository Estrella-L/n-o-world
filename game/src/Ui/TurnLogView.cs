using System;
using System.Collections.Generic;
using Godot;
using Xjdl.Core.Hex;
using Xjdl.Game.Presentation.ViewModels;

namespace Xjdl.Game.Ui;

/// <summary>
/// 回合日志显示面板（节点层，Req 9.1/9.2/9.3）。
/// <para>
/// 按 <see cref="LogLineView"/> 的给定顺序（即 <c>GameState.TurnLog</c> 记录顺序，Req 9.1）
/// 逐行显示由 <see cref="PresentationMapper.LogLines"/> 生成的中文文案（Req 9.2）。文案已由
/// 映射层依 <c>TurnRecordEntry.Kind</c> 生成，本面板<b>不再重复映射</b>，仅负责呈现与交互。
/// </para>
/// <para>
/// 关联某个格/单位的条目（<see cref="LogLineView.Locate"/> 有值）呈现为可点击行；玩家点击时
/// 触发 <see cref="LocateRequested"/> 并携带该坐标，由相机/高亮层（<c>Camera2D</c> /
/// <c>HighlightLayer</c>）响应以定位到地图对应位置（Req 9.3）。无 <see cref="LogLineView.Locate"/>
/// 的条目呈现为普通不可点击文本。
/// </para>
/// <para>
/// 渲染模型："清空并重建"——每次 <see cref="Show"/> 调用先清空既有行、再按输入顺序重建，
/// 与 <c>TurnController</c> "新快照即刷新" 的整体模型一致。子节点（滚动容器 + 纵向列表）
/// 在 <see cref="_Ready"/> 时按需创建，使本脚本无论经 <c>Match.tscn</c>（任务 15.2）挂接还是
/// 以代码实例化都可独立工作。
/// </para>
/// </summary>
public partial class TurnLogView : PanelContainer
{
    // 可点击（含定位坐标）行的占位配色：普通/悬停，仅为可区分的占位视觉，无最终美术。
    private static readonly Color LocatableColor = new(0.55f, 0.80f, 1.0f);
    private static readonly Color LocatableHoverColor = new(0.75f, 0.90f, 1.0f);

    private ScrollContainer? _scroll;
    private VBoxContainer? _list;

    /// <summary>
    /// 点击一条关联格/单位的日志行时触发，携带该条目的定位坐标（Req 9.3）。
    /// 由相机/高亮层订阅以将视图定位到地图对应位置。
    /// </summary>
    public event Action<HexCoord>? LocateRequested;

    public override void _Ready()
    {
        EnsureBuilt();
    }

    /// <summary>
    /// 按给定顺序显示回合日志行（Req 9.1/9.2）。先清空既有行再重建：
    /// <see cref="LogLineView.Locate"/> 有值的行为可点击（点击触发 <see cref="LocateRequested"/>，
    /// Req 9.3），否则为普通文本行。传入 <c>null</c> 视为清空。
    /// </summary>
    /// <param name="lines">已由映射层生成的中文日志行，顺序即显示顺序。</param>
    public void Show(IReadOnlyList<LogLineView>? lines)
    {
        EnsureBuilt();
        ClearRows();

        if (lines is null)
        {
            return;
        }

        foreach (var line in lines)
        {
            _list!.AddChild(line.Locate is { } coord
                ? CreateLocatableRow(line, coord)
                : CreatePlainRow(line));
        }
    }

    /// <summary>移除全部日志行（配合 <see cref="Show"/> 的"清空并重建"，也可单独清空）。</summary>
    public void Clear()
    {
        EnsureBuilt();
        ClearRows();
    }

    /// <summary>按需创建滚动容器与纵向列表容器（幂等）。</summary>
    private void EnsureBuilt()
    {
        if (_list is not null)
        {
            return;
        }

        _scroll = new ScrollContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };

        _list = new VBoxContainer
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };

        _scroll.AddChild(_list);
        AddChild(_scroll);
    }

    /// <summary>清空列表容器中的全部行并立即释放。</summary>
    private void ClearRows()
    {
        if (_list is null)
        {
            return;
        }

        foreach (var child in _list.GetChildren())
        {
            _list.RemoveChild(child);
            child.QueueFree();
        }
    }

    /// <summary>
    /// 创建一条可点击的定位行：以 <see cref="Button"/>（扁平、左对齐）承载文案，
    /// 点击时以携带的坐标触发 <see cref="LocateRequested"/>（Req 9.3）。
    /// </summary>
    private Button CreateLocatableRow(LogLineView line, HexCoord coord)
    {
        var button = new Button
        {
            Text = line.Text,
            Flat = true,
            Alignment = HorizontalAlignment.Left,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            TooltipText = $"定位到 {coord}",
            MouseDefaultCursorShape = Control.CursorShape.PointingHand,
        };

        button.AddThemeColorOverride("font_color", LocatableColor);
        button.AddThemeColorOverride("font_hover_color", LocatableHoverColor);

        // 捕获坐标值（HexCoord 为值类型），点击即请求定位。
        button.Pressed += () => LocateRequested?.Invoke(coord);

        return button;
    }

    /// <summary>创建一条普通（不可点击）文本行。</summary>
    private static Label CreatePlainRow(LogLineView line)
    {
        return new Label
        {
            Text = line.Text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
    }
}
