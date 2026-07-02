using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Xjdl.Core.Modifiers;
using Xjdl.Core.State;
using Xjdl.Core.Turn;
using Xjdl.Data.Loading;

namespace Xjdl.Core.Tests.Architecture;

/// <summary>
/// 架构 / 静态约束测试（design.md〈架构 / 静态检查（SMOKE）〉，任务 19.1）。
/// <para>
/// 这些是一次性、非迭代的类型与依赖约束，用反射 + 源扫描断言，不依赖第三方架构框架
/// （NetArchTest），以在 <c>TreatWarningsAsErrors=true</c> 下零新增依赖地保持核心纯净：
/// </para>
/// <list type="bullet">
/// <item>核心纯净：<c>Xjdl.Core</c> 源码不出现无种子 <c>System.Random</c>/<c>DateTime.Now</c>/
/// <c>Guid.NewGuid()</c>/<c>Environment.TickCount</c>（Req 2.4）。</item>
/// <item>整数运算：战斗与 State 类型无 <c>float</c>/<c>double</c>/<c>decimal</c> 字段（Req 2.5/6.5）。</item>
/// <item>状态不可变：<c>Xjdl.Core.State</c> 类型为不可变 record/readonly struct（Req 2.7/17.2）。</item>
/// <item>依赖方向：<c>Xjdl.Core</c> 不引用 Godot/<c>Xjdl.Data</c>/IO；<c>Xjdl.Data</c> 仅引用
/// <c>Xjdl.Core</c>（Req 20.1/20.3/20.4）。</item>
/// <item>夜战可配置：<see cref="NightConfig"/> 参数变化引起修正随之变化（Req 18.7）。</item>
/// </list>
/// <para>
/// 源扫描先剥离注释（<c>//</c> 行注释、<c>///</c> 文档注释与 <c>/* */</c> 块注释）再按大小写敏感
/// 匹配，避免命中文档中对被禁 API 的说明性引用（如 <c>PcgRng</c>/<c>ISeededRng</c> 的 XML 注释）。
/// </para>
/// </summary>
public class ArchitectureTests
{
    private const string CoreStateNamespace = "Xjdl.Core.State";
    private const string CoreCombatNamespace = "Xjdl.Core.Combat";

    private static readonly Assembly CoreAssembly = typeof(RulesEngine).Assembly;
    private static readonly Assembly DataAssembly = typeof(DataLoader).Assembly;

    // ---- 源码定位（对工作目录健壮）：从测试运行目录向上找到 src/Xjdl.Core ----

    private static string FindCoreSourceDirectory()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Xjdl.Core");
            if (Directory.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            $"未能从 '{AppContext.BaseDirectory}' 向上定位 src/Xjdl.Core 源目录。");
    }

    private static IEnumerable<string> EnumerateCoreSourceFiles(string? subFolder = null)
    {
        var root = FindCoreSourceDirectory();
        var start = subFolder is null ? root : Path.Combine(root, subFolder);

        // 排除生成物：obj/bin（如 GlobalUsings、AssemblyInfo）不属于手写核心源。
        var objSegment = $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}";
        var binSegment = $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}";

        return Directory
            .EnumerateFiles(start, "*.cs", SearchOption.AllDirectories)
            .Where(p => !IsGenerated(p));

        bool IsGenerated(string path) =>
            path.Contains(objSegment, StringComparison.Ordinal)
            || path.Contains(binSegment, StringComparison.Ordinal);
    }

    // 剥离 /* */ 块注释与 // 行注释（含 /// 文档注释），保留代码本体。
    private static string StripComments(string source)
    {
        var withoutBlock = Regex.Replace(source, @"/\*.*?\*/", string.Empty, RegexOptions.Singleline);
        return Regex.Replace(withoutBlock, @"//[^\n]*", string.Empty);
    }

    // ---- 核心纯净：禁用无种子随机 / 墙钟 / 全局唯一 id（Req 2.4） ----

    [Theory]
    [InlineData("System.Random")]
    [InlineData("new Random(")]
    [InlineData("DateTime.Now")]
    [InlineData("DateTime.UtcNow")]
    [InlineData("Guid.NewGuid(")]
    [InlineData("Environment.TickCount")]
    public void Core_source_has_no_nondeterministic_apis(string forbidden)
    {
        var offenders = EnumerateCoreSourceFiles()
            .Where(path => StripComments(File.ReadAllText(path)).Contains(forbidden, StringComparison.Ordinal))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"Xjdl.Core 源码不得出现非确定性 API '{forbidden}'，命中文件：{string.Join(", ", offenders)}");
    }

    // ---- 整数运算：战斗与 State 类型无浮点字段/属性（Req 2.5/6.5） ----

    [Fact]
    public void Combat_and_state_types_have_no_floating_point_members()
    {
        var floatTypes = new[] { typeof(float), typeof(double), typeof(decimal) };

        var offenders = new List<string>();
        foreach (var type in CoreAssembly.GetTypes()
                     .Where(t => t.Namespace is CoreCombatNamespace or CoreStateNamespace)
                     .Where(t => !t.IsEnum && !IsCompilerGenerated(t)))
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

            foreach (var field in type.GetFields(flags).Where(f => !f.IsStatic))
            {
                if (floatTypes.Contains(field.FieldType))
                {
                    offenders.Add($"{type.FullName}.{field.Name} : {field.FieldType.Name}");
                }
            }

            foreach (var prop in type.GetProperties(flags))
            {
                if (floatTypes.Contains(prop.PropertyType))
                {
                    offenders.Add($"{type.FullName}.{prop.Name} : {prop.PropertyType.Name}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"战斗/State 类型必须使用整数（Req 2.5/6.5），发现浮点成员：{string.Join(", ", offenders)}");
    }

    // ---- 状态不可变：Xjdl.Core.State 类型只读（Req 2.7） ----

    [Fact]
    public void State_types_are_immutable()
    {
        var mutableMembers = new List<string>();

        foreach (var type in CoreAssembly.GetTypes()
                     .Where(t => t.Namespace == CoreStateNamespace)
                     .Where(t => (t.IsClass || (t.IsValueType && !t.IsEnum)) && !IsCompilerGenerated(t)))
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;

            // 非只读的公共实例字段即可变。
            foreach (var field in type.GetFields(flags).Where(f => !f.IsInitOnly && !f.IsLiteral))
            {
                mutableMembers.Add($"{type.FullName}.{field.Name} (公共可写字段)");
            }

            // 带非 init-only setter 的公共属性即可变。record/readonly struct 仅生成 init-only setter。
            foreach (var prop in type.GetProperties(flags))
            {
                var setter = prop.SetMethod;
                if (setter is not null && setter.IsPublic && !IsInitOnly(setter))
                {
                    mutableMembers.Add($"{type.FullName}.{prop.Name} (可写属性)");
                }
            }
        }

        Assert.True(
            mutableMembers.Count == 0,
            $"State 类型必须不可变（Req 2.7），发现可变成员：{string.Join(", ", mutableMembers)}");
    }

    // ColumnShift 移档必须携带来源（Req 17.2）。
    [Fact]
    public void ColumnShift_carries_a_source()
    {
        var sourceProp = typeof(ColumnShift).GetProperty(nameof(ColumnShift.Source));
        Assert.NotNull(sourceProp);
        Assert.Equal(typeof(ModifierSource), sourceProp!.PropertyType);
    }

    // ---- 依赖方向（Req 20.1/20.3/20.4） ----

    [Fact]
    public void Core_does_not_reference_Godot_or_Data()
    {
        var offenders = CoreAssembly
            .GetReferencedAssemblies()
            .Where(a => a.Name is not null
                     && (a.Name.StartsWith("Godot", StringComparison.OrdinalIgnoreCase)
                      || a.Name.Equals("Xjdl.Data", StringComparison.Ordinal)))
            .Select(a => a.Name!)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"Xjdl.Core 不得引用 Godot 或 Xjdl.Data（Req 20.3/20.4），发现：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void Core_source_does_not_reference_Godot_Data_or_IO()
    {
        var ioTokens = new[]
        {
            "Godot", "Xjdl.Data", "System.IO.", "File.", "Directory.",
            "StreamReader", "StreamWriter", "FileStream",
        };

        var offenders = new List<string>();
        foreach (var path in EnumerateCoreSourceFiles())
        {
            var code = StripComments(File.ReadAllText(path));
            foreach (var token in ioTokens.Where(t => code.Contains(t, StringComparison.Ordinal)))
            {
                offenders.Add($"{Path.GetFileName(path)} -> '{token}'");
            }
        }

        Assert.True(
            offenders.Count == 0,
            $"Xjdl.Core 源码不得引用 Godot/Xjdl.Data/IO（Req 20.1/20.3/20.4），命中：{string.Join(", ", offenders)}");
    }

    [Fact]
    public void Data_references_Core_only_and_not_reverse()
    {
        var dataRefs = DataAssembly.GetReferencedAssemblies().Select(a => a.Name).ToList();
        Assert.Contains("Xjdl.Core", dataRefs);

        // 反向不成立：Core 不引用 Data（Req 20.4）。
        var coreRefs = CoreAssembly.GetReferencedAssemblies().Select(a => a.Name);
        Assert.DoesNotContain("Xjdl.Data", coreRefs);
    }

    // ---- 夜战可配置示例（Req 18.7） ----

    [Fact]
    public void NightConfig_parameters_change_night_modifiers()
    {
        const int baseVision = 10;
        const int baseRange = 10;
        const NightFlags noFlags = NightFlags.None;
        const DayNightPhase night = DayNightPhase.Night;

        var configA = new NightConfig(NightAttackShift: -1, NightRangeMod: -1, NightVisionDivisor: 2);
        var configB = new NightConfig(NightAttackShift: -2, NightRangeMod: -3, NightVisionDivisor: 5);

        // 视野除数可配置：10/2=5 与 10/5=2 不同。
        var visionA = TurnPipeline.EffectiveVision(baseVision, noFlags, night, configA);
        var visionB = TurnPipeline.EffectiveVision(baseVision, noFlags, night, configB);
        Assert.Equal(5, visionA);
        Assert.Equal(2, visionB);
        Assert.NotEqual(visionA, visionB);

        // 支援射程修正可配置：10-1=9 与 10-3=7 不同。
        var rangeA = TurnPipeline.EffectiveSupportRange(baseRange, noFlags, night, configA);
        var rangeB = TurnPipeline.EffectiveSupportRange(baseRange, noFlags, night, configB);
        Assert.Equal(9, rangeA);
        Assert.Equal(7, rangeB);
        Assert.NotEqual(rangeA, rangeB);

        // 进攻移档档数可配置：-1 与 -2 不同。
        var shiftA = TurnPipeline.NightAttackColumnShift(noFlags, night, configA);
        var shiftB = TurnPipeline.NightAttackColumnShift(noFlags, night, configB);
        Assert.NotNull(shiftA);
        Assert.NotNull(shiftB);
        Assert.Equal(-1, shiftA!.Value.Delta);
        Assert.Equal(-2, shiftB!.Value.Delta);
        Assert.NotEqual(shiftA.Value.Delta, shiftB.Value.Delta);
    }

    // ---- 反射辅助 ----

    private static bool IsCompilerGenerated(Type type) =>
        type.Name.Contains('<', StringComparison.Ordinal)
        || type.GetCustomAttribute<CompilerGeneratedAttribute>() is not null;

    private static bool IsInitOnly(MethodInfo setter) =>
        setter.ReturnParameter
            .GetRequiredCustomModifiers()
            .Any(m => m.FullName == "System.Runtime.CompilerServices.IsExternalInit");
}
