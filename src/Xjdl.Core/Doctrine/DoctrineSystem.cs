using Xjdl.Core.State;

namespace Xjdl.Core.Doctrine;

/// <summary>
/// 作战学说子系统（Req 16）。以「基础值 + 学说修正」表达单位属性，中立模板保持不变（Req 16.1）；
/// 加载期校验预算合计恰为 4 点（Req 16.2、16.5）与修正后攻/防仍整除韧性 N（Req 16.3）；
/// 组队时按学说批量赋予签名夜战标志（Req 16.4）。
/// 全程整数、纯函数、无副作用，契合 docs/04 纯核心约定；非法数据一律快速失败（fail-fast）。
/// </summary>
public static class DoctrineSystem
{
    /// <summary>学说 + 专精的预算点数合计必须恰为该值（Req 16.2、16.5）。</summary>
    public const int RequiredBudget = 4;

    /// <summary>
    /// 将学说与所选专精分支叠加到中立模板，返回带修正的新模板（Req 16.1）。
    /// 修正恒为加法叠加；由于 <see cref="UnitTemplate"/> 为不可变 record，
    /// 输入 <paramref name="baseline"/> 对象不被修改（返回新实例）。
    /// <para>
    /// 施加修正前先经 <see cref="Validate"/> 校验预算；施加后校验修正结果——
    /// 若修正改变了攻或防，则其仍须能被韧性 N 整除，否则抛 <see cref="InvalidDataException"/>
    /// （Req 16.3），以保证每次战损的整数衰减（Req 8.1）。
    /// </para>
    /// </summary>
    /// <param name="baseline">中立兵种模板（不被修改）。</param>
    /// <param name="doctrine">所选作战学说。</param>
    /// <param name="spec">所选专精分支（A/B 之一）。</param>
    /// <returns>叠加学说修正后的新 <see cref="UnitTemplate"/>。</returns>
    /// <exception cref="InvalidDataException">
    /// 预算合计不等于 <see cref="RequiredBudget"/>，或修正后攻/防不整除韧性 N。
    /// </exception>
    public static UnitTemplate Apply(UnitTemplate baseline, Doctrine doctrine, Specialization spec)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(doctrine);
        ArgumentNullException.ThrowIfNull(spec);

        // 先校验预算合法性（Req 16.2、16.5）。
        Validate(doctrine, spec);

        var attack = baseline.Attack;
        var defense = baseline.Defense;
        var movement = baseline.Movement;
        var vision = baseline.Vision;
        var supportRange = baseline.SupportRange;

        Accumulate(doctrine.Modifiers, ref attack, ref defense, ref movement, ref vision, ref supportRange);
        Accumulate(spec.Modifiers, ref attack, ref defense, ref movement, ref vision, ref supportRange);

        // 修正后攻/防仍须整除韧性 N（Req 16.3），否则拒绝加载。
        var resilience = baseline.Resilience;
        if (resilience <= 0)
        {
            throw new InvalidDataException(
                $"兵种模板 '{baseline.TypeKey}' 的韧性 N 必须为正数，实际为 {resilience}。");
        }

        if (attack % resilience != 0)
        {
            throw new InvalidDataException(
                $"学说 '{doctrine.Key}' + 专精 '{spec.Key}' 修正后，兵种 '{baseline.TypeKey}' " +
                $"的进攻 {attack} 不能被韧性 N={resilience} 整除。");
        }

        if (defense % resilience != 0)
        {
            throw new InvalidDataException(
                $"学说 '{doctrine.Key}' + 专精 '{spec.Key}' 修正后，兵种 '{baseline.TypeKey}' " +
                $"的防御 {defense} 不能被韧性 N={resilience} 整除。");
        }

        return baseline with
        {
            Attack = attack,
            Defense = defense,
            Movement = movement,
            Vision = vision,
            SupportRange = supportRange,
        };
    }

    /// <summary>
    /// 校验学说与专精分支的预算（Req 16.2、16.5）。
    /// 要求：专精声明的 <see cref="Specialization.Budget"/> 与其修正点数合计一致；
    /// 且学说修正 + 专精修正的预算点数合计恰等于 <see cref="RequiredBudget"/>（4 点）。
    /// 两条专精分支（A/B）适用同一规则，故任一分支均须使合计为 4（Req 16.5）。
    /// 不满足即抛 <see cref="InvalidDataException"/>（fail-fast）。
    /// </summary>
    /// <param name="doctrine">待校验的作战学说。</param>
    /// <param name="spec">待校验的专精分支。</param>
    /// <exception cref="InvalidDataException">
    /// 专精声明预算与其点数合计不一致，或学说 + 专精点数合计不等于 <see cref="RequiredBudget"/>。
    /// </exception>
    public static void Validate(Doctrine doctrine, Specialization spec)
    {
        ArgumentNullException.ThrowIfNull(doctrine);
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(doctrine.Modifiers);
        ArgumentNullException.ThrowIfNull(spec.Modifiers);

        var specPoints = SumPoints(spec.Modifiers);
        if (spec.Budget != specPoints)
        {
            throw new InvalidDataException(
                $"专精 '{spec.Key}' 声明预算 {spec.Budget} 与其修正点数合计 {specPoints} 不一致。");
        }

        var total = SumPoints(doctrine.Modifiers) + specPoints;
        if (total != RequiredBudget)
        {
            throw new InvalidDataException(
                $"学说 '{doctrine.Key}' + 专精 '{spec.Key}' 的收益点数合计为 {total}，" +
                $"必须等于 {RequiredBudget}。");
        }
    }

    /// <summary>
    /// 组队时按学说批量为单位赋予其签名夜战标志（Req 16.4）。
    /// 在单位现有 <see cref="UnitState.Flags"/> 上按位并入 <see cref="Doctrine.SignatureFlags"/>，
    /// 返回新 <see cref="UnitState"/>（不修改输入）。
    /// </summary>
    /// <param name="u">目标单位实例。</param>
    /// <param name="doctrine">提供签名标志的作战学说。</param>
    /// <returns>已并入签名标志的新单位实例。</returns>
    public static UnitState ApplySignatureFlags(UnitState u, Doctrine doctrine)
    {
        ArgumentNullException.ThrowIfNull(u);
        ArgumentNullException.ThrowIfNull(doctrine);

        return u with { Flags = u.Flags | doctrine.SignatureFlags };
    }

    /// <summary>将一组修正的加法增量按属性维度累加到当前值上。</summary>
    private static void Accumulate(
        IReadOnlyList<StatModifier> modifiers,
        ref int attack,
        ref int defense,
        ref int movement,
        ref int vision,
        ref int supportRange)
    {
        for (var i = 0; i < modifiers.Count; i++)
        {
            var m = modifiers[i];
            switch (m.Stat)
            {
                case StatKind.Attack:
                    attack += m.Value;
                    break;
                case StatKind.Defense:
                    defense += m.Value;
                    break;
                case StatKind.Movement:
                    movement += m.Value;
                    break;
                case StatKind.Vision:
                    vision += m.Value;
                    break;
                case StatKind.SupportRange:
                    supportRange += m.Value;
                    break;
                default:
                    throw new InvalidDataException($"未知的属性维度 {m.Stat}。");
            }
        }
    }

    /// <summary>累加一组修正的预算点数合计。</summary>
    private static int SumPoints(IReadOnlyList<StatModifier> modifiers)
    {
        var sum = 0;
        for (var i = 0; i < modifiers.Count; i++)
        {
            sum += modifiers[i].PointCost;
        }

        return sum;
    }
}
