using Xjdl.Core.Random;
using Xjdl.Core.State;

namespace Xjdl.Core.Turn;

/// <summary>
/// WEGO 回合内的十个阶段编号（0..9），含义见 <c>docs/01-战斗机制.md</c>〈阶段流程〉与 Req 3.1。
/// 固定顺序执行，作为 <see cref="TurnPipeline"/> 的骨架轴。
/// </summary>
internal enum TurnPhase
{
    /// <summary>阶段 0：计划下令（每单位恰好一条命令，Req 3.2）。</summary>
    Plan = 0,

    /// <summary>阶段 1：揭示（背对背命令同时揭示）。</summary>
    Reveal = 1,

    /// <summary>阶段 2：机动（动画子循环、接敌锁定、临机机动，Req 4.x）。</summary>
    Maneuver = 2,

    /// <summary>阶段 3：接触选表（对每处接触定表，Req 5.x）。</summary>
    SelectTable = 3,

    /// <summary>阶段 4：战力快照（冻结全场攻/防/韧性，Req 3.3）。</summary>
    Snapshot = 4,

    /// <summary>阶段 5：掷骰读表（火力比→移档±2→3D6→地形DRM→读表仅记录，Req 3.4）。</summary>
    RollAndRead = 5,

    /// <summary>阶段 6：战损同步结算（累加后一次性扣除、以快照韧性判歼灭，Req 3.5/3.6）。</summary>
    ApplyCasualties = 6,

    /// <summary>阶段 7：撤退（在阶段 6 之后执行，Req 3.8）。</summary>
    Retreat = 7,

    /// <summary>阶段 8：推进（仅在冲突格腾空后由存活优势方占领，Req 3.9）。</summary>
    Advance = 8,

    /// <summary>阶段 9：回合结束（清进攻准备、切昼夜，Req 3.10/18.1）。</summary>
    EndTurn = 9,
}

/// <summary>
/// WEGO 回合流水线：按 0→1→2→…→9 的固定顺序执行十个阶段（Req 3.1）。
/// <para>
/// 本类型是 <see cref="RulesEngine.NextState"/> 的内部编排骨架：每个阶段实现为一个
/// <em>纯函数变换</em>（<see cref="PhaseTransform"/>），接收当前 <see cref="GameState"/> 与命令、
/// 返回新的 <see cref="GameState"/>，从不原地修改输入（Req 2.1）。
/// </para>
/// <para>
/// 阶段顺序以数据驱动的 <see cref="Phases"/> 列表表达，使阶段序列可被观察与断言
/// （见任务 15.2 的阶段流程序列测试）。后续任务（15.3/15.6/15.12/15.17/15.20/15.24）
/// 将各阶段的 pass-through 骨架替换为真实变换。
/// </para>
/// </summary>
internal static partial class TurnPipeline
{
    /// <summary>
    /// 单个阶段的纯函数变换签名：<c>(state, commands, rng) =&gt; nextState</c>。
    /// 实现必须返回新状态而非原地修改输入。
    /// </summary>
    internal delegate GameState PhaseTransform(GameState state, TurnCommands commands, ISeededRng rng);

    /// <summary>阶段编号与其变换函数的绑定，供 <see cref="Phases"/> 有序表达骨架。</summary>
    internal readonly record struct PhaseStep(TurnPhase Phase, PhaseTransform Transform);

    /// <summary>
    /// 固定顺序的阶段骨架（0..9）。<see cref="Run"/> 严格按本列表顺序逐阶段变换状态。
    /// 该列表对外（同程序集/测试）可见，使「阶段以 0..9 固定顺序执行」这一约束可被直接断言。
    /// </summary>
    internal static IReadOnlyList<PhaseStep> Phases { get; } = new[]
    {
        new PhaseStep(TurnPhase.Plan, Plan),
        new PhaseStep(TurnPhase.Reveal, Reveal),
        new PhaseStep(TurnPhase.Maneuver, Maneuver),
        new PhaseStep(TurnPhase.SelectTable, SelectTable),
        new PhaseStep(TurnPhase.Snapshot, Snapshot),
        new PhaseStep(TurnPhase.RollAndRead, RollAndRead),
        new PhaseStep(TurnPhase.ApplyCasualties, ApplyCasualties),
        new PhaseStep(TurnPhase.Retreat, Retreat),
        new PhaseStep(TurnPhase.Advance, Advance),
        new PhaseStep(TurnPhase.EndTurn, EndTurn),
    };

    /// <summary>
    /// 按固定顺序执行阶段 0..9，逐阶段以纯函数变换推进状态并返回最终 <see cref="GameState"/>。
    /// 不修改输入 <paramref name="s"/>（每个阶段均返回新状态或原不可变实例，Req 2.1）。
    /// </summary>
    /// <param name="s">回合初始状态。</param>
    /// <param name="cmds">本回合的全部输入命令。</param>
    /// <param name="rng">注入的确定性随机源。</param>
    /// <returns>执行完阶段 0..9 后的新状态。</returns>
    public static GameState Run(GameState s, TurnCommands cmds, ISeededRng rng)
    {
        // 回合日志按回合重置：TurnLog 仅承载「本回合」的审计/动画事件，绝不跨回合累加。
        // 否则历史的锁定/遭遇/战斗结果会随 s.TurnLog 被每个阶段整段拷贝并不断追加，导致：
        // (1) 表现层每回合把所有历史事件重放一遍（如“击退敌人后下一回合一堆单位莫名接敌锁定”）；
        // (2) 存档体积随回合无限膨胀。
        // 回放按 (初始状态 + 每回合命令流 + 种子) 重演、不依赖累加日志（见 Replays.RunReplay），故此处清空安全。
        var state = s with { TurnLog = Array.Empty<TurnRecordEntry>() };
        foreach (var step in Phases)
        {
            state = step.Transform(state, cmds, rng);
        }

        // 回合末快照注入随机源的当前状态到 GameState.RngState，保证字节级可重放（Req 2.2）。
        // 语义说明：战斗阶段一律以 rng.Fork((ulong)battleId) 派生独立子流掷骰，Fork 不推进父流（本注入的 rng），
        // 故此处 rng.State 恒等于回合入口时注入随机源的状态——这正是「相同 (state, commands, seed) 必产出
        // 字节级一致结果」所需的确定性锚点，且与战斗遍历次序完全无关（Req 2.3/2.6）。
        // 一旦将来引入直接推进主随机源的阶段，本快照会自然反映其推进后的状态，无需改动此处。
        return state with { RngState = rng.State };
    }

    // ── 阶段变换骨架（pass-through）────────────────────────────────────────
    // 当前为最小可编译骨架，保持「纯函数、不修改输入」的形态；
    // 后续任务将逐一填充各阶段的真实逻辑。

    /// <summary>
    /// 阶段 0：计划下令。校验每个在场单位恰好持有一条命令（Req 3.2），
    /// 并原样返回状态——计划阶段不消耗指挥点（Req 4.1）。
    /// </summary>
    private static GameState Plan(GameState s, TurnCommands cmds, ISeededRng rng)
    {
        ValidateOrders(s, cmds);
        // 计划阶段仅做校验，不推进数值：原样返回状态，Cards/CP 保持不变（Req 4.1）。
        return s;
    }

    /// <summary>阶段 1：揭示（骨架）。</summary>
    private static GameState Reveal(GameState s, TurnCommands cmds, ISeededRng rng) => s;

    /// <summary>
    /// 阶段 2：机动。逐格推进移动/进攻准备单位、接敌即锁定、结算临机机动（Req 4.2–4.8、15.2、21.6）。
    /// 实际逻辑见 <see cref="ManeuverPhase"/>（TurnPipeline.Maneuver.cs）。
    /// </summary>
    private static GameState Maneuver(GameState s, TurnCommands cmds, ISeededRng rng)
        => ManeuverPhase(s, cmds, rng);

    /// <summary>
    /// 阶段 3：接触选表（Req 5.1/5.2/5.5）。实际逻辑见 <see cref="SelectTablePhase"/>（TurnPipeline.Combat.cs）。
    /// </summary>
    private static GameState SelectTable(GameState s, TurnCommands cmds, ISeededRng rng)
        => SelectTablePhase(s, cmds, rng);

    /// <summary>
    /// 阶段 4：战力快照（Req 3.3）。实际逻辑见 <see cref="SnapshotPhase"/>（TurnPipeline.Combat.cs）。
    /// </summary>
    private static GameState Snapshot(GameState s, TurnCommands cmds, ISeededRng rng)
        => SnapshotPhase(s, cmds, rng);

    /// <summary>
    /// 阶段 5：掷骰读表（Req 3.4/3.5，仅记录不执行）。实际逻辑见 <see cref="RollAndReadPhase"/>（TurnPipeline.Combat.cs）。
    /// </summary>
    private static GameState RollAndRead(GameState s, TurnCommands cmds, ISeededRng rng)
        => RollAndReadPhase(s, cmds, rng);

    /// <summary>
    /// 阶段 6：战损同步结算（Req 3.5/3.6/8.4）。实际逻辑见 <see cref="ApplyCasualtiesPhase"/>（TurnPipeline.Combat.cs）。
    /// </summary>
    private static GameState ApplyCasualties(GameState s, TurnCommands cmds, ISeededRng rng)
        => ApplyCasualtiesPhase(s, cmds, rng);

    /// <summary>
    /// 阶段 7：撤退（Req 3.8/12.x）。实际逻辑见 <see cref="RetreatPhase"/>（TurnPipeline.Combat.cs）。
    /// </summary>
    private static GameState Retreat(GameState s, TurnCommands cmds, ISeededRng rng)
        => RetreatPhase(s, cmds, rng);

    /// <summary>
    /// 阶段 8：推进（Req 3.9/9.5/12.4）。实际逻辑见 <see cref="AdvancePhase"/>（TurnPipeline.Combat.cs）。
    /// </summary>
    private static GameState Advance(GameState s, TurnCommands cmds, ISeededRng rng)
        => AdvancePhase(s, cmds, rng);

    /// <summary>
    /// 阶段 9：回合结束（Req 3.10/18.1）。清除全部进攻准备并按固定序切换昼夜到下一回合。
    /// 实际逻辑见 <see cref="EndTurnPhase"/>（TurnPipeline.EndTurn.cs）；命令与 rng 不参与本阶段。
    /// </summary>
    private static GameState EndTurn(GameState s, TurnCommands cmds, ISeededRng rng)
        => EndTurnPhase(s);
}
