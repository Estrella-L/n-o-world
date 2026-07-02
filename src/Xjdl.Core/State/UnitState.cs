using Xjdl.Core.Hex;

namespace Xjdl.Core.State;

/// <summary>
/// 战场单位实例（不可变；每次状态变换产生新实例，Req 2.1、2.7）。
/// 初始值 <see cref="InitAttack"/>/<see cref="InitDefense"/>/<see cref="Resilience0"/> 作为衰减分母来源保持不变，
/// 当前值 <see cref="Attack"/>/<see cref="Defense"/>/<see cref="ResilienceLeft"/> 随战损衰减。
/// 每次战损按整除衰减量线性递减，归零即阵亡（Req 8.1、8.2）。
/// </summary>
public sealed record UnitState(
    UnitId Id,
    Side Owner,
    string TypeKey,
    UnitClass Class,
    int InitAttack, // 初始值（衰减分母来源）
    int InitDefense,
    int Resilience0,
    int Attack, // 当前值（随战损衰减）
    int Defense,
    int ResilienceLeft,
    int Movement,
    int Vision,
    int SupportRange,
    HexCoord Position,
    Command Command,
    NightFlags Flags)
{
    /// <summary>每次战损的进攻衰减量（整除保证整数，Req 8.1）。</summary>
    public int AttackDecay => InitAttack / Resilience0;

    /// <summary>每次战损的防御衰减量（整除保证整数，Req 8.1）。</summary>
    public int DefenseDecay => InitDefense / Resilience0;
}
