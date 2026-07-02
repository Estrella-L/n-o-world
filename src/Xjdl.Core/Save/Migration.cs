using Xjdl.Core.State;

namespace Xjdl.Core.Save;

/// <summary>
/// 存档结构迁移（Req 21.3）。
/// <para>
/// 当加载 <see cref="GameState.SchemaVersion"/> 低于 <see cref="SaveSystem.CurrentSchemaVersion"/> 的旧档时，
/// 若存在对应的迁移函数，则逐版本（from → from+1）应用迁移，将其升级至当前版本，
/// 且保留原语义数据而非弃用（Req 21.3）。已是当前版本的存档原样返回。
/// </para>
/// <para>
/// <b>迁移机制</b>：迁移以「按起始版本键控的步进函数注册表」表达。每个 <see cref="MigrationStep"/>
/// 将某个 <c>fromVersion</c> 的状态升级为 <c>fromVersion + 1</c> 的状态，并负责把结果的
/// <see cref="GameState.SchemaVersion"/> 置为 <c>fromVersion + 1</c>。<see cref="Migrate(GameState)"/>
/// 从旧档当前版本起，逐步查找并应用 <c>from</c>、<c>from+1</c>… 直到抵达当前版本。
/// </para>
/// <para>
/// <b>扩展点</b>：当 <see cref="SaveSystem.CurrentSchemaVersion"/> 提升到 <c>N</c> 时，
/// 在 <see cref="DefaultSteps"/> 中为每个跨版本增量（<c>0→1</c>、<c>1→2</c>…直到 <c>N-1→N</c>）
/// 添加一个 <see cref="MigrationStep"/> 即可。<see cref="Migrate(GameState, IReadOnlyDictionary{int, MigrationStep})"/>
/// 的重载允许注入自定义注册表，供测试构造合成迁移（如 v0→v1）而不污染生产注册表（Req 21.3，任务 17.4）。
/// </para>
/// <para>
/// 当前 <see cref="SaveSystem.CurrentSchemaVersion"/> 为 1 且无历史版本，故 <see cref="DefaultSteps"/> 为空，
/// 生产环境下 <see cref="Migrate(GameState)"/> 对 v1 存档为恒等操作。
/// </para>
/// </summary>
public static class Migration
{
    /// <summary>
    /// 单步迁移函数：将 <c>fromVersion</c> 的状态升级为 <c>fromVersion + 1</c>，
    /// 并保证返回状态的 <see cref="GameState.SchemaVersion"/> 等于 <c>fromVersion + 1</c>。
    /// 迁移须保留原语义数据（Req 21.3），仅补齐/转换结构差异。
    /// </summary>
    public delegate GameState MigrationStep(GameState old);

    /// <summary>
    /// 生产迁移注册表：键为起始版本 <c>from</c>，值为将其升级到 <c>from + 1</c> 的步进函数。
    /// 每引入一个新的 <see cref="SaveSystem.CurrentSchemaVersion"/> 就补充对应键。
    /// 目前无历史版本，注册表为空。
    /// </summary>
    private static readonly IReadOnlyDictionary<int, MigrationStep> DefaultSteps =
        new Dictionary<int, MigrationStep>();

    /// <summary>
    /// 使用生产注册表将旧档迁移至 <see cref="SaveSystem.CurrentSchemaVersion"/>（Req 21.3）。
    /// </summary>
    /// <param name="old">待迁移的存档状态。</param>
    /// <returns>版本升至当前且语义保留的状态；若已是当前版本则原样返回。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="old"/> 为 <c>null</c>。</exception>
    /// <exception cref="NotSupportedException">
    /// 存在从某个低版本起缺失迁移函数，无法升级到当前版本；或存档版本高于当前版本。
    /// </exception>
    public static GameState Migrate(GameState old) => Migrate(old, DefaultSteps);

    /// <summary>
    /// 使用指定注册表将旧档迁移至 <see cref="SaveSystem.CurrentSchemaVersion"/>（Req 21.3）。
    /// 该重载便于测试注入合成迁移函数（任务 17.4）。
    /// </summary>
    /// <param name="old">待迁移的存档状态。</param>
    /// <param name="steps">按起始版本键控的步进迁移注册表。</param>
    /// <returns>版本升至当前且语义保留的状态；若已是当前版本则原样返回。</returns>
    /// <exception cref="ArgumentNullException"><paramref name="old"/> 或 <paramref name="steps"/> 为 <c>null</c>。</exception>
    /// <exception cref="NotSupportedException">
    /// 从某个低版本起缺失迁移函数无法抵达当前版本；或存档版本高于当前版本。
    /// </exception>
    public static GameState Migrate(GameState old, IReadOnlyDictionary<int, MigrationStep> steps)
    {
        ArgumentNullException.ThrowIfNull(old);
        ArgumentNullException.ThrowIfNull(steps);

        var target = SaveSystem.CurrentSchemaVersion;

        if (old.SchemaVersion == target)
        {
            // 已是当前版本：无需迁移，原样返回。
            return old;
        }

        if (old.SchemaVersion > target)
        {
            // 高于当前版本的存档由更新版本写出，本引擎无法向下迁移。
            throw new NotSupportedException(
                $"存档版本 {old.SchemaVersion} 高于当前支持版本 {target}，无法迁移。");
        }

        var current = old;
        // 逐版本步进升级：from → from+1，直到抵达目标版本。
        while (current.SchemaVersion < target)
        {
            var from = current.SchemaVersion;
            if (!steps.TryGetValue(from, out var step))
            {
                throw new NotSupportedException(
                    $"缺少从版本 {from} 升级到 {from + 1} 的迁移函数，无法将存档升级至版本 {target}。");
            }

            var migrated = step(current)
                ?? throw new InvalidOperationException($"版本 {from} 的迁移函数返回了 null。");

            if (migrated.SchemaVersion != from + 1)
            {
                // 步进函数契约：必须恰好将版本推进一格，避免死循环或跳版本。
                throw new InvalidOperationException(
                    $"版本 {from} 的迁移函数应产出版本 {from + 1}，实际为 {migrated.SchemaVersion}。");
            }

            current = migrated;
        }

        return current;
    }
}
