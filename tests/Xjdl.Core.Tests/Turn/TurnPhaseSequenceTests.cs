using Xjdl.Core.Hex;
using Xjdl.Core.Random;
using Xjdl.Core.State;
using Xjdl.Core.Turn;

namespace Xjdl.Core.Tests.Turn;

/// <summary>
/// 任务 15.2：WEGO 回合<b>阶段流程序列</b>的单元测试（Req 3.1、3.4、3.5、3.8、3.9）。
/// 见 <c>docs/01-战斗机制.md</c>〈阶段流程〉与 design.md〈TurnPipeline〉。
/// <para>
/// 阶段顺序以数据驱动的 <see cref="TurnPipeline.Phases"/> 列表表达，经 InternalsVisibleTo 对测试可见，
/// 因此本测试<b>直接断言阶段序列</b>（0..9 固定顺序、7 在 6 之后、8 在 7 之后），无需插桩记录调用序列。
/// </para>
/// <para>
/// 阶段 5「五步序」（战力快照 → 火力比移档 ±2 → 3D6 → 地形 DRM → 读表<em>仅记录</em>）属战斗结算内部步骤，
/// 不可从公共 API 直接观测其逐步调用；本测试改为断言其<b>可观测后果</b>：
/// 逐阶段调用 <see cref="TurnPipeline.SnapshotPhase"/>→<see cref="TurnPipeline.RollAndReadPhase"/> 后，
/// 单位数值<em>保持冻结不变</em>且回合日志已记入一条战斗结果（仅记录、不执行，Req 3.4/3.5），
/// 且记录内容含「最终档位 col（移档产物）／3D6 读数 roll（掷骰+地形骰轴产物）／读表结果 result」，
/// 佐证「快照→移档→掷骰→地形→读表」这一步序确已发生；随后调用
/// <see cref="TurnPipeline.ApplyCasualtiesPhase"/>（阶段 6）才真正扣除战损，证实阶段 5 只记录不执行。
/// </para>
/// </summary>
public class TurnPhaseSequenceTests
{
    private static readonly TurnPhase[] ExpectedOrder =
    {
        TurnPhase.Plan,            // 0
        TurnPhase.Reveal,          // 1
        TurnPhase.Maneuver,        // 2
        TurnPhase.SelectTable,     // 3
        TurnPhase.Snapshot,        // 4
        TurnPhase.RollAndRead,     // 5
        TurnPhase.ApplyCasualties, // 6
        TurnPhase.Retreat,         // 7
        TurnPhase.Advance,         // 8
        TurnPhase.EndTurn,         // 9
    };

    // ── Req 3.1：阶段 0..9 以固定升序执行 ────────────────────────────────

    /// <summary>
    /// 阶段骨架恰有 10 个阶段，且其 <see cref="TurnPipeline.PhaseStep.Phase"/> 依次等于
    /// <see cref="TurnPhase.Plan"/>(0) … <see cref="TurnPhase.EndTurn"/>(9)，为固定升序（Req 3.1）。
    /// </summary>
    [Fact]
    public void Phases_AreExactlyTenInAscendingFixedOrder()
    {
        var phases = TurnPipeline.Phases;

        Assert.Equal(10, phases.Count);

        for (var i = 0; i < ExpectedOrder.Length; i++)
        {
            // 位置 i 的阶段编号恰为 i（0..9），即固定升序执行。
            Assert.Equal(ExpectedOrder[i], phases[i].Phase);
            Assert.Equal(i, (int)phases[i].Phase);
        }

        // 阶段编号严格递增（无重复、无乱序）。
        for (var i = 1; i < phases.Count; i++)
        {
            Assert.True((int)phases[i].Phase > (int)phases[i - 1].Phase,
                $"阶段序列在下标 {i} 处非严格递增。");
        }
    }

    // ── Req 3.8 / 3.9：撤退在战损之后、推进在撤退之后 ─────────────────────

    /// <summary>
    /// 撤退（阶段 7）在战损同步结算（阶段 6）之后，推进（阶段 8）又在撤退（阶段 7）之后（Req 3.8/3.9）。
    /// 「推进仅在冲突格腾空后」由阶段 8 排在阶段 6（战损）与阶段 7（撤退）之后的次序保证——
    /// 腾空只能由更早的战损/撤退阶段产生。
    /// </summary>
    [Fact]
    public void Retreat_ComesAfterCasualties_And_Advance_ComesAfterRetreat()
    {
        var order = TurnPipeline.Phases.Select(p => p.Phase).ToList();

        var casualties = order.IndexOf(TurnPhase.ApplyCasualties);
        var retreat = order.IndexOf(TurnPhase.Retreat);
        var advance = order.IndexOf(TurnPhase.Advance);

        Assert.True(casualties >= 0 && retreat >= 0 && advance >= 0);

        // 阶段 7（撤退）严格晚于阶段 6（战损结算）。
        Assert.True(retreat > casualties, "撤退阶段应排在战损结算阶段之后（Req 3.8）。");

        // 阶段 8（推进）严格晚于阶段 7（撤退）→ 只有腾空后方能推进（Req 3.9）。
        Assert.True(advance > retreat, "推进阶段应排在撤退阶段之后（Req 3.9）。");
    }

    // ── Req 3.4 / 3.5：阶段 5 仅记录、阶段 6 才执行 ──────────────────────

    /// <summary>
    /// 阶段 5（掷骰读表）对当前快照结算全部战斗但<b>只把结果写入回合日志、不改动任何单位</b>（Req 3.4/3.5）；
    /// 记录内容含最终档位（移档产物）、3D6 读数（掷骰+地形骰轴产物）与读表结果，
    /// 佐证「快照→移档→掷骰→地形→读表」步序；阶段 6 才真正扣除战损。
    /// </summary>
    [Fact]
    public void Phase5_RecordsCombatReadOnly_Phase6_AppliesCasualties()
    {
        var state = BuildOverwhelmingAttack();
        var cmds = new TurnCommands(
            new[]
            {
                new UnitOrder(new UnitId(1), Command.AttackPrep, null, new HexCoord(1, 0)), // 蓝方进攻 (1,0)
                new UnitOrder(new UnitId(2), Command.Hold, null, null),                     // 红方据守
            },
            System.Array.Empty<RepositionCommand>(),
            System.Array.Empty<CardPlay>());
        var rng = new PcgRng(12345UL);

        var defenderId = new UnitId(2);
        var defenderBefore = FindUnit(state, defenderId);

        // 阶段 4：战力快照（冻结数值）。
        var afterSnapshot = TurnPipeline.SnapshotPhase(state, cmds, rng);

        // 阶段 5：掷骰读表——仅记录。
        var afterRoll = TurnPipeline.RollAndReadPhase(afterSnapshot, cmds, rng);

        // (a) 读表阶段不改动任何单位（读-only）：单位集合逐一等值于快照入口状态。
        Assert.Equal(afterSnapshot.Units.Count, afterRoll.Units.Count);
        var defenderAfterRoll = FindUnit(afterRoll, defenderId);
        Assert.NotNull(defenderAfterRoll);
        Assert.Equal(defenderBefore!.ResilienceLeft, defenderAfterRoll!.ResilienceLeft);
        Assert.Equal(defenderBefore.Defense, defenderAfterRoll.Defense);

        // (b) 阶段 5 记录了一条战斗结果，且包含 col/roll/result（移档→掷骰→地形→读表的产物）。
        var combatRecords = afterRoll.TurnLog.Where(e => e.Kind == "CombatResult").ToList();
        var record = Assert.Single(combatRecords);
        Assert.NotNull(record.Detail);
        Assert.Contains("col=", record.Detail);
        Assert.Contains("roll=", record.Detail);
        Assert.Contains("result=", record.Detail);
        // 24:1 压倒性进攻 → 最终档位钳到最高档 5（移档/火力比产物）。
        Assert.Contains("col=5", record.Detail);
        // 表一最高档全为守方受创 → 记录的结果代码为 Defender* 类（读表产物）。
        Assert.Contains("result=Defender", record.Detail);
        // 3D6 读数落在合法骰段（地形 DRM=0，3..18）。
        var roll = ParseInt(record.Detail, "roll=");
        Assert.InRange(roll, 3, 18);

        // 阶段 6：战损同步结算——此刻才真正作用于单位（Req 3.5）。
        var afterCasualties = TurnPipeline.ApplyCasualtiesPhase(afterSnapshot, cmds, rng);
        var defenderAfterCas = FindUnit(afterCasualties, defenderId);

        // 守方要么被歼移除，要么剩余韧性被扣减——总之状态已改变（对比阶段 5 的“未改变”）。
        var defenderChanged = defenderAfterCas is null
            || defenderAfterCas.ResilienceLeft < defenderBefore.ResilienceLeft;
        Assert.True(defenderChanged, "阶段 6 应真正扣除战损（移除或降低守方剩余韧性）。");
    }

    // ── 构造辅助 ─────────────────────────────────────────────────────────

    /// <summary>
    /// 构造最小的「压倒性进攻」场景：蓝方 (0,0) 进攻力 24 对红方 (1,0) 防御力 1 → 24:1（表一最高档），
    /// 确保阶段 5 必产生一条守方受创的战斗记录、阶段 6 必对守方生效，使断言与掷骰无关地稳定成立。
    /// </summary>
    private static GameState BuildOverwhelmingAttack()
    {
        var cells = new Dictionary<HexCoord, MapCell>();
        for (var q = 0; q <= 2; q++)
        {
            var c = new HexCoord(q, 0);
            cells[c] = new MapCell(c, TerrainType.Plain);
        }

        var map = new GameMap(cells, MapScale.Small);

        var attacker = new UnitState(
            new UnitId(1), Side.Blue, "unit.armor", UnitClass.Elite,
            InitAttack: 24, InitDefense: 6, Resilience0: 1,
            Attack: 24, Defense: 6, ResilienceLeft: 1,
            Movement: 4, Vision: 3, SupportRange: 0,
            Position: new HexCoord(0, 0), Command: Command.AttackPrep, Flags: NightFlags.None);

        var defender = new UnitState(
            new UnitId(2), Side.Red, "unit.infantry", UnitClass.LineHold,
            InitAttack: 2, InitDefense: 2, Resilience0: 2,
            Attack: 2, Defense: 1, ResilienceLeft: 2,
            Movement: 2, Vision: 2, SupportRange: 0,
            Position: new HexCoord(1, 0), Command: Command.Hold, Flags: NightFlags.None);

        var cards = new Dictionary<Side, CardState>
        {
            [Side.Blue] = new CardState(0, 0, System.Array.Empty<CardId>(), System.Array.Empty<CardId>()),
            [Side.Red] = new CardState(0, 0, System.Array.Empty<CardId>(), System.Array.Empty<CardId>()),
        };

        return new GameState(
            SchemaVersion: 1,
            Map: map,
            Units: new[] { attacker, defender },
            DayIndex: 0,
            Phase: DayNightPhase.Morning,
            Cards: cards,
            RngState: 0UL,
            TurnLog: System.Array.Empty<TurnRecordEntry>());
    }

    private static UnitState? FindUnit(GameState state, UnitId id) =>
        state.Units.FirstOrDefault(u => u.Id == id);

    /// <summary>从形如 "...key=NN;..." 的日志明细中解析 <paramref name="key"/> 后的整数。</summary>
    private static int ParseInt(string detail, string key)
    {
        var start = detail.IndexOf(key, System.StringComparison.Ordinal) + key.Length;
        var end = start;
        while (end < detail.Length && (char.IsDigit(detail[end]) || detail[end] == '-'))
        {
            end++;
        }

        return int.Parse(detail[start..end], System.Globalization.CultureInfo.InvariantCulture);
    }
}
