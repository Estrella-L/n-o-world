using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Godot;
using Xjdl.Core.Save;
using Xjdl.Core.State;

namespace Xjdl.Game.Save;

/// <summary>
/// <see cref="SaveLoadController"/> 的回放分部（节点层，Req 16）。
/// <para>
/// 录制（<see cref="RecordReplay"/>，Req 16.1）：经 Core 的 <see cref="Replays.RecordReplay"/>
/// 把「初始状态 + 每回合命令流 + 种子」打包为不可变 <see cref="Replay"/>，序列化为纯数据文本后写入
/// Godot 跨平台用户目录 <c>user://</c>。<em>只存命令流而非逐帧状态</em>，存档体积小且可确定性重演。
/// </para>
/// <para>
/// 重放（<see cref="RunReplay"/>，Req 16.2）：从 <c>user://</c> 读回文本，经 Core 的
/// <see cref="Replays.RunReplay"/> 从初始状态出发、以同一种子逐回合折叠 <c>RulesEngine.NextState</c>，
/// 还原出逐回合的 <see cref="GameState"/> 序列并返回。规则计算全部委托 <c>Xjdl.Core</c>，本类<b>不重算规则</b>。
/// </para>
/// <para>
/// 回放的<em>呈现</em>（Req 16.3）由调用方（回放 UI / TurnController，任务 15.2）负责：把本方法返回的
/// 连续状态序列依次喂给 <c>PhaseAnimator</c> 与既有渲染器逐回合动画呈现。本类只提供状态序列，不驱动动画。
/// </para>
/// <para>
/// 稳健性（Req 16.x）：文件不存在、IO 失败或内容非法时不崩溃——捕获异常、经
/// <see cref="ErrorOccurred"/> 抛出可读信息并返回空序列，由调用方保持当前对局不变。
/// </para>
/// <para>
/// 持久化形状：<see cref="Replay.Initial"/> 复用 Core 的 <see cref="SaveSystem"/>（其内部转换器可正确处理
/// <c>GameMap</c> 的 <c>HexCoord</c> 键字典）序列化为文本并内嵌；命令流不含此类字典，随外层 DTO 直接序列化。
/// 读写共用 <see cref="ReplayJsonOptions"/>，保证 JSON 形状一致、可无损往返。
/// </para>
/// </summary>
public sealed partial class SaveLoadController : Node
{
    /// <summary>
    /// 回放文件的读写选项：枚举以字符串名序列化（可读、稳定）；命令流中的
    /// <see cref="TurnCommands"/>/<see cref="UnitOrder"/> 等均为不可变值记录，
    /// 属性名保持 PascalCase 以匹配 record 主构造函数参数名，保证参数化构造反序列化（.NET 8）。
    /// </summary>
    private static readonly JsonSerializerOptions ReplayJsonOptions = CreateReplayJsonOptions();

    /// <summary>
    /// 录制回放并落盘（Req 16.1）：经 <see cref="Replays.RecordReplay"/> 打包为 <see cref="Replay"/>，
    /// 序列化后写入 <c>user://</c> 下的 <paramref name="fileName"/>。写入失败不崩溃，改经
    /// <see cref="ErrorOccurred"/> 提示。
    /// </summary>
    /// <param name="initial">回放起点的完整 <see cref="GameState"/> 快照。</param>
    /// <param name="cmds">按回合顺序排列的命令序列（每项为一个完整 WEGO 回合的输入）。</param>
    /// <param name="seed">驱动确定性随机源的种子。</param>
    /// <param name="fileName">回放文件名（相对于 <c>user://</c>），如 <c>"replay1.json"</c>。</param>
    public void RecordReplay(GameState initial, IReadOnlyList<TurnCommands> cmds, ulong seed, string fileName)
    {
        ArgumentNullException.ThrowIfNull(initial);
        ArgumentNullException.ThrowIfNull(cmds);
        ArgumentException.ThrowIfNullOrEmpty(fileName);

        var path = ToUserPath(fileName);

        try
        {
            // 规则/回放语义委托 Core：只打包 (初始状态, 命令流, 种子)，不逐帧存状态。
            var replay = Replays.RecordReplay(initial, cmds, seed);

            // 初始状态经 SaveSystem 序列化（正确处理 GameMap 的 HexCoord 键字典）后内嵌，
            // 命令流不含此类字典，随外层 DTO 一并序列化。
            var dto = new ReplayFileDto(
                replay.Seed,
                SaveSystem.Serialize(replay.Initial),
                replay.Commands);

            var json = JsonSerializer.Serialize(dto, ReplayJsonOptions);

            using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);
            if (file is null)
            {
                Report($"无法写入回放「{fileName}」：{DescribeError(Godot.FileAccess.GetOpenError())}");
                return;
            }

            file.StoreString(json);
        }
        catch (Exception ex) when (ex is IOException or JsonException or NotSupportedException)
        {
            Report($"录制回放「{fileName}」失败：{ex.Message}");
        }
    }

    /// <summary>
    /// 读取并重放回放（Req 16.2）：从 <c>user://</c> 下的 <paramref name="fileName"/> 读回文本，
    /// 经 <see cref="Replays.RunReplay"/> 逐回合还原并返回状态序列（第 <c>i</c> 项对应第 <c>i</c> 回合执行后的状态）。
    /// <para>
    /// 返回序列供调用方（回放 UI / TurnController，任务 15.2）依次喂给 <c>PhaseAnimator</c> 与既有渲染器呈现（Req 16.3）；
    /// 本方法不驱动动画、不重算规则。
    /// </para>
    /// <para>
    /// 文件不存在或内容非法时不崩溃：捕获异常、经 <see cref="ErrorOccurred"/> 提示并返回空序列（Req 16.x）。
    /// </para>
    /// </summary>
    /// <param name="fileName">回放文件名（相对于 <c>user://</c>）。</param>
    /// <returns>逐回合的结果状态序列；失败时为空序列（绝不为 <see langword="null"/>）。</returns>
    public IReadOnlyList<GameState> RunReplay(string fileName)
    {
        ArgumentException.ThrowIfNullOrEmpty(fileName);

        var path = ToUserPath(fileName);

        if (!Godot.FileAccess.FileExists(path))
        {
            Report($"读取回放失败：文件「{fileName}」不存在。");
            return Array.Empty<GameState>();
        }

        try
        {
            using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
            if (file is null)
            {
                Report($"无法读取回放「{fileName}」：{DescribeError(Godot.FileAccess.GetOpenError())}");
                return Array.Empty<GameState>();
            }

            var json = file.GetAsText();

            var dto = JsonSerializer.Deserialize<ReplayFileDto>(json, ReplayJsonOptions)
                ?? throw new JsonException("回放文件为空或不表示合法回放。");

            if (string.IsNullOrEmpty(dto.Initial))
            {
                throw new JsonException("回放文件缺少初始状态。");
            }

            // 初始状态经 SaveSystem 反序列化还原；命令流与种子重组为 Replay 后交由 Core 重演。
            var initial = SaveSystem.Deserialize(dto.Initial);
            var replay = new Replay(initial, dto.Commands ?? Array.Empty<TurnCommands>(), dto.Seed);

            return Replays.RunReplay(replay);
        }
        catch (Exception ex) when (ex is IOException or JsonException or ArgumentException or InvalidDataException)
        {
            Report($"读取回放「{fileName}」失败：内容非法或无法解析（{ex.Message}）。");
            return Array.Empty<GameState>();
        }
    }

    /// <summary>构造回放读写共用的序列化选项（枚举转字符串、缩进便于检视）。</summary>
    private static JsonSerializerOptions CreateReplayJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = false,
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    /// <summary>
    /// 回放文件的落盘形状（节点层内部 DTO）。
    /// <see cref="Initial"/> 为经 <see cref="SaveSystem.Serialize"/> 得到的初始状态 JSON 文本（内嵌），
    /// <see cref="Commands"/> 为每回合命令流，<see cref="Seed"/> 为确定性随机源种子。
    /// </summary>
    private sealed record ReplayFileDto(
        ulong Seed,
        string Initial,
        IReadOnlyList<TurnCommands> Commands);
}
