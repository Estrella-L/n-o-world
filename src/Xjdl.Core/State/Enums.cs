namespace Xjdl.Core.State;

/// <summary>阵营（蓝/红）。见 docs/01-战斗机制.md。</summary>
public enum Side
{
    Blue,
    Red,
}

/// <summary>兵种类别：抗线/精锐/火力支援/特殊（Req 2.7）。</summary>
public enum UnitClass
{
    LineHold,
    Elite,
    FireSupport,
    Special,
}

/// <summary>阶段 0 下达的命令：移动/进攻准备/据守（Req 3.2）。</summary>
public enum Command
{
    Move,
    AttackPrep,
    Hold,
}

/// <summary>交战表：表一（进攻）/表二（对攻）/表三（遭遇）（Req 5.1–5.3）。</summary>
public enum CombatTable
{
    RegularAttack,
    MutualAttack,
    Encounter,
}

/// <summary>战斗结果代码，对应 docs/01 结果代码表（Req 7.x）。</summary>
public enum ResultCode
{
    MutualN,
    AttackerN,
    DefenderN,
    DefenderNRetreat,
    DefenderAnnihilate,
    LoserN,
    LoserNRetreat,
    LoserAnnihilate,
    Stalemate,
    Withdraw,
}

/// <summary>战争迷雾可见度分段：隐匿/侦得/识别（Req 14.1–14.4）。</summary>
public enum Visibility
{
    Hidden,
    Spotted,
    Identified,
}

/// <summary>地形类型：平原/森林/丘陵/城市/河流/沼泽（Req 15.1）。</summary>
public enum TerrainType
{
    Plain,
    Forest,
    Hill,
    City,
    River,
    Swamp,
}

/// <summary>移档修正来源，用于按来源精确抵消（Req 17.2、17.3）。</summary>
public enum ModifierSource
{
    Support,
    Night,
    Card,
    Doctrine,
}

/// <summary>技能卡可打出的时机：计划/结算前/反应（Req 19.3）。</summary>
public enum CardTiming
{
    Plan,
    PreResolve,
    Reaction,
}

/// <summary>昼夜阶段，按固定序推进：上午→下午→晚上（Req 18.1）。</summary>
public enum DayNightPhase
{
    Morning,
    Afternoon,
    Night,
}

/// <summary>地图规模档位，决定 CP/牌库/手牌等配置（Req 19.1）。</summary>
public enum MapScale
{
    Small,
    Medium,
    Large,
}

/// <summary>
/// 夜战相关标志位集合，可运行时增删（Req 18.8）。
/// 以位标志表示，支持按位增删的可逆一致集合语义。
/// </summary>
[Flags]
public enum NightFlags
{
    None = 0,
    NightVisionKeep = 1 << 0,
    NightRangeKeep = 1 << 1,
    NightAttackKeep = 1 << 2,
    NightMoveKeep = 1 << 3,
    IgnoreZoc = 1 << 4,
    NightZocKeep = 1 << 5,
}
