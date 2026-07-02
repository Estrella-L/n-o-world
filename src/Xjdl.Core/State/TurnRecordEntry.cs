namespace Xjdl.Core.State;

/// <summary>
/// 回合日志条目（不可变，Req 21.6）。作为回放与审计的最小记录单元。
/// <see cref="TriggerTick"/> 记录动画阶段临机机动的触发时点，供确定性回放对齐（Req 4.7/21.6）；
/// 非机动条目该字段为 <c>null</c>。<see cref="Unit"/> 为可选关联单位；
/// <see cref="Kind"/> 为无文案的条目类别键（i18n 由表现层负责）。
/// 结构保持最小，后续任务可按需扩展字段。
/// </summary>
public sealed record TurnRecordEntry(
    int Turn,
    string Kind,          // 条目类别键（无文案）
    UnitId? Unit,         // 可选关联单位
    int? TriggerTick,     // 临机机动触发时点（Req 21.6）；非机动条目为 null
    string? Detail);      // 可选补充信息（无文案）
