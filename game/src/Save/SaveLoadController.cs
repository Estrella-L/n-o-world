using System;
using System.IO;
using System.Text.Json;
using Godot;
using Xjdl.Core.Save;
using Xjdl.Core.State;

namespace Xjdl.Game.Save;

/// <summary>
/// 存读档与回放控制器（节点层，Req 15/16）。
/// <para>
/// 存读档职责：将当前 <see cref="GameState"/> 经 Core 的 <see cref="SaveSystem.Serialize"/>
/// 写入 Godot 跨平台用户目录 <c>user://</c>（Req 15.1），并从 <c>user://</c> 读取文本经
/// <see cref="SaveSystem.Deserialize"/> 还原 <see cref="GameState"/>，供调用方使显示与之一致
/// （Req 15.2/15.4）。
/// </para>
/// <para>
/// 稳健性（Req 15.3）：文件不存在、IO 失败或内容非法时不崩溃——捕获异常、返回
/// <see langword="null"/> 并通过 <see cref="ErrorOccurred"/> 事件抛出可读错误信息，
/// 由调用方保持当前对局状态不变。规则计算全部委托 <c>Xjdl.Core</c>，本类不实现任何规则逻辑。
/// </para>
/// <para>
/// 本类以 <c>partial</c> 定义，回放能力（<c>RecordReplay</c>/<c>RunReplay</c>，任务 16.2）
/// 在同名分部文件中扩展，避免修改本文件即可增补功能。
/// </para>
/// </summary>
public sealed partial class SaveLoadController : Node
{
    /// <summary>
    /// 当存读档发生可恢复错误（文件不存在、IO 失败、内容非法）时触发，
    /// 携带面向玩家的可读错误信息（Req 15.3）。调用方据此提示玩家并保持当前对局不变。
    /// </summary>
    public event Action<string>? ErrorOccurred;

    /// <summary>
    /// 保存当前对局（Req 15.1）：经 <see cref="SaveSystem.Serialize"/> 序列化为纯数据文本，
    /// 写入 <c>user://</c> 下的 <paramref name="fileName"/>。
    /// 写入失败（如目录不可写）不崩溃，改经 <see cref="ErrorOccurred"/> 提示。
    /// </summary>
    /// <param name="state">要保存的当前 <see cref="GameState"/> 快照。</param>
    /// <param name="fileName">存档文件名（相对于 <c>user://</c>），如 <c>"save1.json"</c>。</param>
    public void Save(GameState state, string fileName)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentException.ThrowIfNullOrEmpty(fileName);

        var path = ToUserPath(fileName);

        try
        {
            var json = SaveSystem.Serialize(state);

            using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);
            if (file is null)
            {
                Report($"无法写入存档「{fileName}」：{DescribeError(Godot.FileAccess.GetOpenError())}");
                return;
            }

            file.StoreString(json);
        }
        catch (Exception ex) when (ex is IOException or JsonException or NotSupportedException)
        {
            Report($"保存存档「{fileName}」失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 读取存档（Req 15.2/15.4）：从 <c>user://</c> 下的 <paramref name="fileName"/> 读取文本，
    /// 经 <see cref="SaveSystem.Deserialize"/> 还原 <see cref="GameState"/> 并返回，
    /// 调用方据此使显示与之一致。
    /// <para>
    /// 文件不存在或内容非法时不崩溃：捕获异常、经 <see cref="ErrorOccurred"/> 提示并返回
    /// <see langword="null"/>，由调用方保持当前对局状态不变（Req 15.3）。
    /// </para>
    /// </summary>
    /// <param name="fileName">存档文件名（相对于 <c>user://</c>）。</param>
    /// <returns>还原后的 <see cref="GameState"/>；失败时为 <see langword="null"/>。</returns>
    public GameState? Load(string fileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);

        var path = ToUserPath(fileName);

        if (!Godot.FileAccess.FileExists(path))
        {
            Report($"读取存档失败：文件「{fileName}」不存在。");
            return null;
        }

        try
        {
            using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
            if (file is null)
            {
                Report($"无法读取存档「{fileName}」：{DescribeError(Godot.FileAccess.GetOpenError())}");
                return null;
            }

            var json = file.GetAsText();
            return SaveSystem.Deserialize(json);
        }
        catch (Exception ex) when (ex is IOException or JsonException or ArgumentException or InvalidDataException)
        {
            Report($"读取存档「{fileName}」失败：内容非法或无法解析（{ex.Message}）。");
            return null;
        }
    }

    /// <summary>组合 <c>user://</c> 路径（Godot 跨平台用户目录，Req 15.1/15.2）。</summary>
    private static string ToUserPath(string fileName) => $"user://{fileName}";

    /// <summary>将 Godot <see cref="Error"/> 码转为可读描述。</summary>
    private static string DescribeError(Error error) => $"错误码 {error}";

    /// <summary>抛出错误事件；同时写入 Godot 日志便于诊断。</summary>
    private void Report(string message)
    {
        GD.PushError(message);
        ErrorOccurred?.Invoke(message);
    }
}
