using System;
using Godot;

namespace Xjdl.Game.Ui;

/// <summary>
/// 阶段控制栏（<see cref="HBoxContainer"/>，Req 6.11、8.7）。
/// <para>
/// 提供两枚按钮：
/// </para>
/// <list type="bullet">
///   <item>
///     <b>结束计划 / 提交本回合</b>：触发 <see cref="SubmitRequested"/>（Req 6.11）。任务 15.2 组装
///     <c>Match.tscn</c> 时将其接线到 <c>PlanningController</c> 收集己方 <c>UnitOrder</c> 集合并交
///     <c>TurnController.SubmitTurn</c> 结算。
///   </item>
///   <item>
///     <b>跳过动画</b>：触发 <see cref="SkipAnimationRequested"/>（Req 8.7）。任务 15.2 将其接线到
///     <c>PhaseAnimator.Skip</c>（或 <c>TurnController.SkipAnimation</c>）以直达结算后最终态。
///   </item>
/// </list>
/// <para>
/// <b>解耦约定</b>：本控制栏<b>不</b>持有对 <c>TurnController</c>/<c>PhaseAnimator</c> 的硬引用，只对外抛出
/// 语义事件；由任务 15.2 的场景组装负责把事件连到相应控制器，从而保持 UI 与结算/动画逻辑的单向依赖与可测性。
/// </para>
/// <para>
/// WEGO 双阶段下，按钮的可用性随阶段切换：计划阶段允许提交、禁用跳过；动画/结算阶段禁用提交、允许跳过。
/// 场景侧可调用 <see cref="SetPlanningPhase"/>/<see cref="SetResolutionPhase"/>（或细粒度的
/// <see cref="SetSubmitEnabled"/>/<see cref="SetSkipEnabled"/>）在阶段切换时更新按钮状态。
/// </para>
/// </summary>
public partial class PhaseControlBar : HBoxContainer
{
    private Button _submitButton = null!;
    private Button _skipButton = null!;

    /// <summary>
    /// “结束计划 / 提交本回合”按钮被按下时触发（Req 6.11）。
    /// 任务 15.2 接线至 <c>PlanningController.Submit()</c> → <c>TurnController.SubmitTurn</c>。
    /// </summary>
    public event Action? SubmitRequested;

    /// <summary>
    /// “跳过动画”按钮被按下时触发（Req 8.7）。
    /// 任务 15.2 接线至 <c>PhaseAnimator.Skip()</c>（或 <c>TurnController.SkipAnimation</c>）。
    /// </summary>
    public event Action? SkipAnimationRequested;

    public override void _Ready()
    {
        _submitButton = new Button
        {
            Name = "BtnSubmit",
            Text = "结束计划 / 提交本回合",
        };
        _submitButton.Pressed += OnSubmitPressed;
        AddChild(_submitButton);

        _skipButton = new Button
        {
            Name = "BtnSkipAnimation",
            Text = "跳过动画",
        };
        _skipButton.Pressed += OnSkipPressed;
        AddChild(_skipButton);

        // 默认进入计划阶段：可提交、不可跳过（无动画在播放）。
        SetPlanningPhase();
    }

    /// <summary>切换到计划阶段的按钮可用性：允许提交、禁用跳过（Req 6.11）。</summary>
    public void SetPlanningPhase()
    {
        SetSubmitEnabled(true);
        SetSkipEnabled(false);
    }

    /// <summary>切换到动画/结算阶段的按钮可用性：禁用提交、允许跳过（Req 8.7）。</summary>
    public void SetResolutionPhase()
    {
        SetSubmitEnabled(false);
        SetSkipEnabled(true);
    }

    /// <summary>单独设置“提交本回合”按钮是否可用。</summary>
    public void SetSubmitEnabled(bool enabled)
    {
        if (_submitButton is not null)
        {
            _submitButton.Disabled = !enabled;
        }
    }

    /// <summary>单独设置“跳过动画”按钮是否可用。</summary>
    public void SetSkipEnabled(bool enabled)
    {
        if (_skipButton is not null)
        {
            _skipButton.Disabled = !enabled;
        }
    }

    private void OnSubmitPressed() => SubmitRequested?.Invoke();

    private void OnSkipPressed() => SkipAnimationRequested?.Invoke();
}
