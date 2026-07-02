using System.Collections.Generic;

namespace Xjdl.Core.State;

/// <summary>
/// 阶段 4 全场冻结的战斗快照（Req 3.3）。
/// 在结算前对参战单位的攻/防/韧性等数值做一次性冻结，
/// 保证同一回合内多场战斗依据一致的快照结算。
/// </summary>
public sealed record BattleSnapshot(
    IReadOnlyList<UnitState> Units,          // 攻/防/韧性冻结值
    DayNightPhase Phase);
