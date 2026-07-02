namespace Xjdl.Core.State;

/// <summary>
/// 单位稳定标识符。作为集合稳定排序键，保证结算次序无关与遍历确定性
/// （Req 2.6）。值类型、不可变，天然可序列化，契合确定性核心（Req 2.7）。
/// </summary>
public readonly record struct UnitId(int Value);
