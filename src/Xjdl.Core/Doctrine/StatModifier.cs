namespace Xjdl.Core.Doctrine;

/// <summary>
/// 可被作战学说修正的单位属性维度（Req 16.1）。
/// 仅列出数值型、可加法叠加的属性；韧性 N 作为战损衰减分母不由学说改动，
/// 以保持模板固定的整除关系（Req 8.1、8.3）。
/// </summary>
public enum StatKind
{
    /// <summary>进攻战斗力。</summary>
    Attack,

    /// <summary>防御战斗力。</summary>
    Defense,

    /// <summary>机动力。</summary>
    Movement,

    /// <summary>视野半径。</summary>
    Vision,

    /// <summary>火力支援射程。</summary>
    SupportRange,
}

/// <summary>
/// 一条作战学说修正（Req 16.1）：对某个属性维度施加加法增量，并声明其占用的预算点数。
/// 学说以「基础值 + 学说修正」表达单位属性，修正恒为加法叠加、不改中立模板本身。
/// <para>
/// <see cref="Value"/> 为对 <see cref="Stat"/> 的加法增量（可正可负）；
/// <see cref="PointCost"/> 为该修正占用的预算点数，学说 + 专精的点数合计须等于 4（Req 16.2、16.5）。
/// 全程整数，无浮点（Req 2.5）。
/// </para>
/// </summary>
/// <param name="Stat">被修正的属性维度。</param>
/// <param name="Value">对该属性的加法增量（正增益、负削弱）。</param>
/// <param name="PointCost">该修正占用的预算点数。</param>
public readonly record struct StatModifier(StatKind Stat, int Value, int PointCost);
