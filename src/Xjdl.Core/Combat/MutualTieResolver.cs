namespace Xjdl.Core.Combat;

/// <summary>
/// 表二（对攻）1:1 平局裁定（Req 6.6）。
/// <para>
/// 当双方进攻战斗力相等（火力比 1:1）时，表二本身无固定优劣归属，改由该回合
/// 两次 3D6 读数决定：点数较低的一方承受「劣-N」（<see cref="State.ResultCode.LoserN"/> 系列）结果；
/// 若两次读数相等则无优劣归属，按对称的「互-N」（<see cref="State.ResultCode.MutualN"/>）处理。
/// </para>
/// <para>
/// 本类型是一个纯函数式裁定器，只比较两个整数读数、不掷骰、不改变任何状态，
/// 供阶段 5/6 结算在识别出表二 1:1 对攻后调用。掷骰本身由注入的
/// <see cref="Random.ISeededRng"/> 完成（Req 2.4），本裁定器不涉及随机源。
/// </para>
/// </summary>
public static class MutualTieResolver
{
    /// <summary>表二 1:1 对攻中承受「劣-N」的一方：进攻方 A。</summary>
    public const int SideA = 0;

    /// <summary>表二 1:1 对攻中承受「劣-N」的一方：进攻方 B。</summary>
    public const int SideB = 1;

    /// <summary>
    /// 裁定表二 1:1 对攻中承受「劣-N」结果的一方。
    /// </summary>
    /// <param name="rollA">进攻方 A 本回合的 3D6 读数（正常 3..18）。</param>
    /// <param name="rollB">进攻方 B 本回合的 3D6 读数（正常 3..18）。</param>
    /// <returns>
    /// 承受「劣-N」的一方：<see cref="SideA"/>（A 读数较低）或 <see cref="SideB"/>（B 读数较低）；
    /// 若两次读数相等（平局）则返回 <c>null</c>，表示无优劣归属、按对称「互-N」处理。
    /// </returns>
    public static int? LoserSide(int rollA, int rollB)
    {
        if (rollA < rollB)
        {
            return SideA;
        }

        if (rollB < rollA)
        {
            return SideB;
        }

        return null;
    }
}
