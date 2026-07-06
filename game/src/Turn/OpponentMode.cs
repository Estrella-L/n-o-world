namespace Xjdl.Game.Turn;

/// <summary>
/// 占位对手命令来源模式（Req 7.1/7.2）。
/// <para>
/// <see cref="Scripted"/>：默认脚本化，敌方全部据守（无智能决策）；
/// <see cref="HotSeat"/>：本地热座，接受玩家为红方逐单位下达的命令。
/// </para>
/// 由 <c>MatchSetupController</c>（任务 15.2）在启动对局时选择，注入 <see cref="TurnController"/>。
/// </summary>
public enum OpponentMode
{
    /// <summary>脚本化占位对手（默认）：敌方全部 <c>Hold</c>。</summary>
    Scripted,

    /// <summary>本地热座：采用玩家为对手方逐单位下达的命令。</summary>
    HotSeat,
}
