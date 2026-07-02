namespace Xjdl.Core.State;

/// <summary>
/// 状态根：一局对战的完整不可变快照（Req 2.7）。
/// 无 Godot 类型、无循环引用，可直接 <c>System.Text.Json</c> 序列化（Req 21.1）。
/// <see cref="SchemaVersion"/> 承载存档版本以支持迁移（Req 21.2）；
/// <see cref="RngState"/> 快照 RNG 状态以保证字节级可重放；
/// <see cref="TurnLog"/> 记录含临机机动触发时点的回合日志（Req 21.6）。
/// </summary>
public sealed record GameState(
    int SchemaVersion,                        // 存档版本（Req 21.2）
    GameMap Map,
    IReadOnlyList<UnitState> Units,           // 按稳定 id 排序遍历（Req 2.6）
    int DayIndex,
    DayNightPhase Phase,                       // 昼夜（Req 18.1）
    IReadOnlyDictionary<Side, CardState> Cards,
    ulong RngState,                            // 快照 RNG 状态，保证可重放
    IReadOnlyList<TurnRecordEntry> TurnLog);   // 含临机机动触发时点（Req 21.6）
