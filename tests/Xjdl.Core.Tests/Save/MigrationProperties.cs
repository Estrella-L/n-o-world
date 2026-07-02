using CsCheck;
using Xjdl.Core.Save;
using Xjdl.Core.State;
using Xjdl.Core.Tests.Support;

namespace Xjdl.Core.Tests.Save;

// Feature: core-rules-engine, Property 64: 存档迁移保语义升级
public class MigrationProperties
{
    /// <summary>
    /// 合成迁移注册表（任务 17.4）：键 <c>0</c> 对应将 v0 升级到 v1 的步进函数。
    /// 该函数仅把 <see cref="GameState.SchemaVersion"/> 推进一格，保留其余全部语义字段，
    /// 从而在 <see cref="SaveSystem.CurrentSchemaVersion"/> 仍为 1、生产注册表尚为空时，
    /// 也能验证「低版本存档存在迁移函数则升级并保语义」这一属性（Req 21.3）。
    /// 使用注入重载 <see cref="Migration.Migrate(GameState, IReadOnlyDictionary{int, Migration.MigrationStep})"/>
    /// 避免污染生产注册表。
    /// </summary>
    private static readonly IReadOnlyDictionary<int, Migration.MigrationStep> SyntheticSteps =
        new Dictionary<int, Migration.MigrationStep>
        {
            [0] = old => old with { SchemaVersion = old.SchemaVersion + 1 },
        };

    /// <summary>
    /// Property 64: 存档迁移保语义升级。
    /// <para>
    /// 对任意合法 <see cref="GameState"/>，将其版本置为低于当前的 v0 并提供 0→1 迁移函数后，
    /// <see cref="Migration.Migrate(GameState, IReadOnlyDictionary{int, Migration.MigrationStep})"/> 应：
    /// (1) 把 <see cref="GameState.SchemaVersion"/> 升至 <see cref="SaveSystem.CurrentSchemaVersion"/>；
    /// (2) 保留 <c>Map/Units/DayIndex/Phase/Cards/RngState/TurnLog</c> 等语义数据而非弃用。
    /// </para>
    /// 语义保留以序列化等价判定：迁移结果与「仅把版本号置为当前」的旧档序列化后逐字节一致，
    /// 即除版本号外无任何字段被丢弃或篡改。
    /// <para><b>Validates: Requirements 21.3</b></para>
    /// </summary>
    [Fact]
    public void Migrate_UpgradesVersionAndPreservesSemantics()
    {
        Generators.GameState.Sample(
            generated =>
            {
                // 强制成为低于当前版本的旧档（v0）。
                var old = generated with { SchemaVersion = 0 };

                var migrated = Migration.Migrate(old, SyntheticSteps);

                // (1) 版本升至当前。
                var versionUpgraded = migrated.SchemaVersion == SaveSystem.CurrentSchemaVersion;

                // (2) 语义数据保留：与仅置换版本号后的旧档在序列化层面完全一致
                //     （无字段被弃用/改写）。
                var expected = old with { SchemaVersion = SaveSystem.CurrentSchemaVersion };
                var semanticsPreserved =
                    SaveSystem.Serialize(migrated) == SaveSystem.Serialize(expected);

                return versionUpgraded && semanticsPreserved;
            },
            iter: 100);
    }
}
