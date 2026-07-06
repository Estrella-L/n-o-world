using System;
using Godot;
using Xjdl.Game.Input;

namespace Xjdl.Game.Ui;

/// <summary>
/// 指挥点（CP）可视化面板（节点层，Req 12.1/12.2）。
/// <para>
/// 显示<b>当前观察方</b>可用的指挥点数量（Req 12.1），并在发生临机机动（CP 待消耗量变化）后刷新
/// （Req 12.2）。本视图<b>不做权威扣费</b>——权威 CP 结算始终由 Core 完成；这里仅呈现
/// “基准可用 CP 减去本回合待提交临机机动预计消耗”的投影。
/// </para>
/// <para>
/// <b>接线（供任务 15.2 组装 <c>Match.tscn</c> 时完成）</b>：调用 <see cref="Bind"/> 注入
/// <see cref="RepositionController"/> 与“基准可用 CP”提供者（通常为
/// <c>() =&gt; mapper.CommandPoints(state, viewer)</c>）。绑定后，每当
/// <see cref="RepositionController.CommandPointsChanged"/> 触发，本视图重算
/// <c>基准 CP - reposition.PendingCpCost</c> 并刷新显示。上层在新回合结算后应重新调用
/// <see cref="Bind"/> 或直接调用 <see cref="Show(int)"/> 以反映新的基准 CP。
/// </para>
/// </summary>
public partial class CommandPointView : HBoxContainer
{
    private const string LabelPrefix = "指挥点：";

    private Label? _label;

    // 在 _Ready 之前调用 Show 时暂存，待节点就绪后套用，保持 null 安全。
    private int? _pendingCp;

    private RepositionController? _reposition;
    private Func<int>? _baseCpProvider;

    public override void _Ready()
    {
        _label = new Label { Name = "CpLabel" };
        AddChild(_label);

        if (_pendingCp is int cp)
        {
            _label.Text = LabelPrefix + cp;
            _pendingCp = null;
        }
    }

    /// <summary>
    /// 直接以一个可用 CP 数量刷新显示（Req 12.1）。
    /// </summary>
    /// <param name="cp">当前观察方可用的指挥点数量。</param>
    public void Show(int cp)
    {
        if (_label is null)
        {
            _pendingCp = cp;
            return;
        }

        _label.Text = LabelPrefix + cp;
    }

    /// <summary>
    /// 绑定临机机动控制器与基准 CP 提供者（Req 12.2）。绑定后订阅
    /// <see cref="RepositionController.CommandPointsChanged"/>，在临机机动增删时自动刷新为
    /// <c>基准 CP - PendingCpCost</c>，并立即刷新一次当前值。可安全重复调用（先解绑旧订阅）。
    /// </summary>
    /// <param name="reposition">临机机动控制器；提供 <c>PendingCpCost</c> 与变更事件。</param>
    /// <param name="baseCpProvider">基准可用 CP 提供者（通常委托纯层 <c>PresentationMapper.CommandPoints</c>）。</param>
    public void Bind(RepositionController reposition, Func<int> baseCpProvider)
    {
        ArgumentNullException.ThrowIfNull(reposition);
        ArgumentNullException.ThrowIfNull(baseCpProvider);

        Unbind();

        _reposition = reposition;
        _baseCpProvider = baseCpProvider;
        _reposition.CommandPointsChanged += OnCommandPointsChanged;

        Refresh();
    }

    /// <summary>
    /// 解除对 <see cref="RepositionController"/> 的订阅（避免过期快照/跨回合泄漏）。可安全重复调用。
    /// </summary>
    public void Unbind()
    {
        if (_reposition is not null)
        {
            _reposition.CommandPointsChanged -= OnCommandPointsChanged;
        }

        _reposition = null;
        _baseCpProvider = null;
    }

    public override void _ExitTree() => Unbind();

    private void OnCommandPointsChanged() => Refresh();

    private void Refresh()
    {
        if (_baseCpProvider is null)
        {
            return;
        }

        var pending = _reposition?.PendingCpCost ?? 0;
        Show(_baseCpProvider() - pending);
    }
}
