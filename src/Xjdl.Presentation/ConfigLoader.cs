using System.IO;
using Xjdl.Data.Loading;

namespace Xjdl.Game.Presentation;

/// <summary>
/// 经 <c>Xjdl.Data</c> 载入兵种/地形/学说/卡牌/规模档位配置的适配器（纯层，Req 1.4/14.2）。
/// 本身不重复定义规则数据，也不重算校验：直接委托 <see cref="DataLoader.Load(string)"/>
/// 反序列化并校验配置，产出核心类型聚合 <see cref="GameData"/>。
/// <para>
/// fail-fast 语义（Req 14.2/14.3）：JSON 解析失败、缺失必需分节、内容非法（含不整除韧性 N、
/// 学说预算非法等），一律由 <see cref="DataLoader"/> 抛 <see cref="InvalidDataException"/>，
/// 绝不静默接受。<see cref="Xjdl.Game.Presentation"/> 的调用方（如 MatchSetupController）
/// 捕获该异常后中止启动并向玩家显示错误。
/// </para>
/// 不 <c>using Godot</c>，从而可脱离引擎编译。
/// </summary>
public sealed class ConfigLoader
{
    /// <summary>
    /// 载入并校验一份 JSON 配置，产出校验通过的 <see cref="GameData"/>（Req 14.2）。
    /// </summary>
    /// <param name="json">配置 JSON 文本。</param>
    /// <returns>校验通过的 <see cref="GameData"/>。</returns>
    /// <exception cref="InvalidDataException">
    /// 数据非法，或校验过程本身无法完成（含 JSON 解析失败、缺失分节等，fail-fast，Req 14.3）。
    /// </exception>
    public GameData Load(string json) => DataLoader.Load(json);
}
