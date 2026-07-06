using Godot;
using Xjdl.Core.State;

// 消歧：本文件的“昼夜视图 DTO”一律指纯表现层的不可变投影
// Xjdl.Game.Presentation.ViewModels.DayNightView（record），
// 与本 Godot 节点类 Xjdl.Game.Ui.DayNightView 同短名但异命名空间。
using DayNightViewModel = Xjdl.Game.Presentation.ViewModels.DayNightView;

namespace Xjdl.Game.Ui;

/// <summary>
/// 昼夜状态可视化面板（节点层，Req 11.1/11.3）。
/// <para>
/// 依据 <see cref="Presentation.PresentationMapper.PhaseView"/> 产出的
/// <see cref="DayNightViewModel"/>（含 <c>Phase</c> 与中文 <c>DisplayName</c>）显示当前昼夜阶段
/// （上午/下午/晚上），并在昼夜阶段随回合推进变化时更新（Req 11.3）。本视图<b>不重算规则</b>，
/// 仅呈现纯层映射器的投影。
/// </para>
/// <para>
/// <b>接线（供任务 15.2 组装 <c>Match.tscn</c> 时完成）</b>：每回合结算后由上层以新的
/// <see cref="Presentation.PresentationMapper.PhaseView"/> 结果调用 <see cref="Show(DayNightViewModel)"/>。
/// </para>
/// </summary>
public partial class DayNightView : HBoxContainer
{
    private const string LabelPrefix = "阶段：";

    private Label? _label;

    // 在 _Ready 之前调用 Show 时暂存，待节点就绪后套用，保持 null 安全。
    private string? _pendingText;

    public override void _Ready()
    {
        _label = new Label { Name = "PhaseLabel" };
        AddChild(_label);

        if (_pendingText is not null)
        {
            _label.Text = _pendingText;
            _pendingText = null;
        }
    }

    /// <summary>
    /// 以纯层昼夜视图 DTO 刷新显示（Req 11.1/11.3）：展示其中文 <c>DisplayName</c>（上午/下午/晚上）。
    /// </summary>
    /// <param name="phase">
    /// 由 <see cref="Presentation.PresentationMapper.PhaseView"/> 产出的昼夜视图投影；
    /// 全限定为 <see cref="DayNightViewModel"/> 以与本节点类消歧。
    /// </param>
    public void Show(DayNightViewModel phase)
    {
        if (phase is null)
        {
            return;
        }

        SetText(LabelPrefix + phase.DisplayName);
    }

    /// <summary>
    /// 以昼夜枚举与其显示名刷新显示（Req 11.1/11.3）。当调用方尚未持有 DTO 时的便捷重载。
    /// </summary>
    /// <param name="phase">当前昼夜阶段。</param>
    /// <param name="displayName">该阶段的中文显示名（上午/下午/晚上）。</param>
    public void Show(DayNightPhase phase, string displayName)
        => SetText(LabelPrefix + (displayName ?? phase.ToString()));

    private void SetText(string text)
    {
        if (_label is null)
        {
            _pendingText = text;
            return;
        }

        _label.Text = text;
    }
}
