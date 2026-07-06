using System;
using Godot;

namespace Xjdl.Game.Menu;

/// <summary>
/// 主菜单场景根节点（<see cref="Control"/>，Req 14.1）。
/// <para>
/// 提供三枚入口按钮：
/// </para>
/// <list type="bullet">
///   <item><b>开始新对局</b>（<c>BtnNewGame</c>）：切换到对局启动场景 <c>MatchSetup.tscn</c>（任务 15.2 实现），
///     在其中经 <c>ConfigLoader</c> 载入配置并构造初始 <c>GameState</c>（Req 14.2–14.5）。</item>
///   <item><b>读取存档</b>（<c>BtnLoad</c>）：发起读档流程。存读档由 <c>SaveLoadController</c>（任务 16.1）承担；
///     在其接线完成前，本按钮先切换到 <c>MatchSetup.tscn</c> 作为占位入口（见 <see cref="OnLoadPressed"/>）。</item>
///   <item><b>退出</b>（<c>BtnQuit</c>）：调用 <see cref="SceneTree.Quit()"/> 退出游戏。</item>
/// </list>
/// <para>
/// <b>场景约定</b>：配套 <c>res://scenes/MainMenu.tscn</c> 中以唯一名（<c>unique_name_in_owner</c>）声明
/// <c>%BtnNewGame</c>/<c>%BtnLoad</c>/<c>%BtnQuit</c> 三枚 <see cref="Button"/>，本脚本在 <see cref="_Ready"/>
/// 中解析并接线其 <c>Pressed</c> 信号。若场景中缺少这些节点（例如脚本被裸实例化），则在代码中构建一套
/// 默认按钮，保证节点自成可运行的主菜单。<c>project.godot</c> 的 <c>run/main_scene</c> 已指向本场景。
/// </para>
/// <para>
/// <b>解耦约定</b>：本菜单对外抛出 <see cref="NewGameRequested"/>/<see cref="LoadGameRequested"/>/
/// <see cref="QuitRequested"/> 语义事件；若无订阅者则执行内置默认导航（切场景/退出）。这样既可独立作为
/// 主场景直接试玩，也便于后续（15.2/16.1）由上层接管流程而不改动本类。
/// </para>
/// </summary>
public partial class MainMenu : Control
{
    /// <summary>对局启动场景路径（任务 15.2 新建 <c>MatchSetup.tscn</c>）。</summary>
    [Export]
    public string MatchSetupScenePath { get; set; } = "res://scenes/MatchSetup.tscn";

    private Button? _newGameButton;
    private Button? _loadButton;
    private Button? _quitButton;

    /// <summary>「开始新对局」被按下时触发（Req 14.1）。无订阅者时默认切换到 <c>MatchSetup.tscn</c>。</summary>
    public event Action? NewGameRequested;

    /// <summary>
    /// 「读取存档」被按下时触发（Req 14.1）。存读档流程由 <c>SaveLoadController</c>（任务 16.1）接管；
    /// 无订阅者时默认切换到 <c>MatchSetup.tscn</c> 作为占位入口。
    /// </summary>
    public event Action? LoadGameRequested;

    /// <summary>「退出」被按下时触发（Req 14.1）。无订阅者时默认调用 <see cref="SceneTree.Quit()"/>。</summary>
    public event Action? QuitRequested;

    public override void _Ready()
    {
        _newGameButton = GetNodeOrNull<Button>("%BtnNewGame");
        _loadButton = GetNodeOrNull<Button>("%BtnLoad");
        _quitButton = GetNodeOrNull<Button>("%BtnQuit");

        // 场景未提供按钮（例如裸实例化本脚本）时，构建一套默认 UI，保证可运行。
        if (_newGameButton is null || _loadButton is null || _quitButton is null)
        {
            BuildDefaultUi();
        }

        _newGameButton!.Pressed += OnNewGamePressed;
        _loadButton!.Pressed += OnLoadPressed;
        _quitButton!.Pressed += OnQuitPressed;
    }

    /// <summary>「开始新对局」处理：切换到对局启动场景（Req 14.1 → 14.2）。</summary>
    private void OnNewGamePressed()
    {
        if (NewGameRequested is not null)
        {
            NewGameRequested.Invoke();
            return;
        }

        ChangeToScene(MatchSetupScenePath);
    }

    /// <summary>
    /// 「读取存档」处理（Req 14.1）。完整读档由 <c>SaveLoadController</c>（任务 16.1）经 <c>user://</c>
    /// 反序列化 <c>GameState</c>；在其接线完成前，默认切换到 <c>MatchSetup.tscn</c> 作为占位入口。
    /// </summary>
    private void OnLoadPressed()
    {
        if (LoadGameRequested is not null)
        {
            LoadGameRequested.Invoke();
            return;
        }

        // 占位：读档 UI/流程接线完成前，先进入对局启动场景。
        ChangeToScene(MatchSetupScenePath);
    }

    /// <summary>「退出」处理：退出游戏（Req 14.1）。</summary>
    private void OnQuitPressed()
    {
        if (QuitRequested is not null)
        {
            QuitRequested.Invoke();
            return;
        }

        GetTree().Quit();
    }

    private void ChangeToScene(string scenePath)
    {
        if (string.IsNullOrEmpty(scenePath))
        {
            GD.PushWarning("MainMenu: 目标场景路径为空，忽略切换请求。");
            return;
        }

        Error error = GetTree().ChangeSceneToFile(scenePath);
        if (error != Error.Ok)
        {
            // MatchSetup.tscn 于任务 15.2 才创建；在此之前切换会失败，属预期，记录告警即可。
            GD.PushWarning($"MainMenu: 切换到场景 '{scenePath}' 失败（{error}）。该场景可能尚未创建（任务 15.2）。");
        }
    }

    private void BuildDefaultUi()
    {
        var layout = new VBoxContainer
        {
            Name = "MenuLayout",
        };
        layout.SetAnchorsPreset(LayoutPreset.Center);
        AddChild(layout);

        if (GetNodeOrNull<Label>("%Title") is null)
        {
            layout.AddChild(new Label { Name = "Title", Text = "新旧大陆" });
        }

        _newGameButton ??= AddMenuButton(layout, "BtnNewGame", "开始新对局");
        _loadButton ??= AddMenuButton(layout, "BtnLoad", "读取存档");
        _quitButton ??= AddMenuButton(layout, "BtnQuit", "退出");
    }

    private static Button AddMenuButton(Node parent, string name, string text)
    {
        var button = new Button
        {
            Name = name,
            Text = text,
        };
        button.SetUniqueNameInOwner(true);
        parent.AddChild(button);
        return button;
    }
}
