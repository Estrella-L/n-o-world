using Xjdl.Core.State;
using Xjdl.Data.Loading;
using Side = Xjdl.Core.State.Side;

namespace Xjdl.Game.Turn;

/// <summary>
/// 对局启动交接（静态单例，任务 15.2）。
/// <para>
/// Godot 的 <see cref="Godot.SceneTree.ChangeSceneToFile(string)"/> 不便于向新场景传参，故
/// <see cref="Xjdl.Game.Menu.MatchSetupController"/> 在切换到 <c>Match.tscn</c> 之前，把构造好的
/// 合法初始 <see cref="GameState"/>、已校验的配置聚合 <see cref="GameData"/>、随机种子、迷雾配置、
/// 观察方阵营与对手模式暂存于此；<c>MatchController</c> 在 <see cref="Godot.Node._Ready"/> 时读取
/// 并接线整个对局场景（Req 14.4/14.5）。
/// </para>
/// <para>
/// 这是<b>纯数据交接</b>，不承载任何规则逻辑；全部规则计算仍委托 <c>Xjdl.Core</c>（Req 1.5）。
/// 读取后应调用 <see cref="Clear"/> 释放引用，避免跨对局泄漏。
/// </para>
/// </summary>
public static class MatchBootstrap
{
    /// <summary>已校验的配置聚合（经 <c>ConfigLoader</c> 载入，Req 14.2）。</summary>
    public static GameData? Data { get; private set; }

    /// <summary>合法的初始对局状态（Req 14.4）。</summary>
    public static GameState? InitialState { get; private set; }

    /// <summary>初始化 <c>PcgRng</c> 的种子，保证同一对局可重放（Req 14.5）。</summary>
    public static ulong Seed { get; private set; }

    /// <summary>战争迷雾配置，供 <c>PresentationMapper</c> 的可见度过滤使用（Req 10.x）。</summary>
    public static FogConfig? Fog { get; private set; }

    /// <summary>玩家观察方阵营（默认蓝方）。</summary>
    public static Side Viewer { get; private set; } = Side.Blue;

    /// <summary>占位对手模式（脚本化/本地热座，Req 7.1/7.2）。</summary>
    public static OpponentMode Mode { get; private set; } = OpponentMode.Scripted;

    /// <summary>是否已完成一次有效交接（供 <c>MatchController</c> 判定是否有数据可读）。</summary>
    public static bool IsReady =>
        Data is not null && InitialState is not null && Fog is not null;

    /// <summary>
    /// 暂存本局启动所需的全部数据（由 <c>MatchSetupController</c> 在切场景前调用，Req 14.4/14.5）。
    /// </summary>
    public static void Set(
        GameData data,
        GameState initialState,
        ulong seed,
        FogConfig fog,
        Side viewer,
        OpponentMode mode)
    {
        Data = data;
        InitialState = initialState;
        Seed = seed;
        Fog = fog;
        Viewer = viewer;
        Mode = mode;
    }

    /// <summary>清空交接引用（<c>MatchController</c> 读取后调用，避免跨对局泄漏）。</summary>
    public static void Clear()
    {
        Data = null;
        InitialState = null;
        Fog = null;
        Seed = 0UL;
        Viewer = Side.Blue;
        Mode = OpponentMode.Scripted;
    }
}
