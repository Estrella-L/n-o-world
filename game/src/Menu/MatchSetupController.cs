using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Godot;
using Xjdl.Core.Cards;
using Xjdl.Core.Hex;
using Xjdl.Core.Random;
using Xjdl.Core.State;
using Xjdl.Data.Loading;
using Xjdl.Game.Presentation;
using Xjdl.Game.Turn;
using Side = Xjdl.Core.State.Side;

namespace Xjdl.Game.Menu;

/// <summary>
/// 对局启动控制器（<see cref="Control"/>，Req 14.2–14.5）。
/// <para>
/// 玩家在 <c>MatchSetup.tscn</c> 选择规模档位、（可选）种子与对手模式后点击开始：本控制器经
/// <see cref="ConfigLoader"/> 载入并校验配置（Req 14.2）；<b>成功</b>则构造合法的初始
/// <see cref="GameState"/>（Req 14.4）、以固定或玩家指定的种子初始化 <see cref="PcgRng"/>（Req 14.5），
/// 把这些数据经 <see cref="MatchBootstrap"/> 交接给对局场景并切换到 <c>Match.tscn</c>；<b>失败/数据非法</b>
/// 则捕获 <see cref="InvalidDataException"/>，在 <c>ErrorLabel</c> 显示错误并<b>中止启动、不进入对局</b>（Req 14.3）。
/// </para>
/// <para>
/// <b>关于配置来源</b>：本工程当前未附带外部配置数据文件，故本控制器内置一份<b>最小但合法</b>的
/// 占位配置 JSON（<see cref="DefaultConfigJson"/>），并<b>确实经 <see cref="ConfigLoader.Load"/></b>
/// 载入以贯通 Req 14.2 的配置载入路径与 Req 14.3 的失败处理（<see cref="LoadConfigOrShowError"/>
/// 捕获 <see cref="InvalidDataException"/>）。待接入正式数据文件后，只需替换 JSON 来源即可，其余流程不变。
/// 全部规则计算仍委托 <c>Xjdl.Core</c>，本控制器仅做启动装配（Req 1.5）。
/// </para>
/// </summary>
public partial class MatchSetupController : Control
{
    /// <summary>对局场景路径（本任务新建的 <c>Match.tscn</c>）。</summary>
    [Export]
    public string MatchScenePath { get; set; } = "res://scenes/Match.tscn";

    /// <summary>无玩家指定种子时使用的固定默认种子，保证可重放（Req 14.5）。</summary>
    private const ulong DefaultSeed = 20240517UL;

    /// <summary>玩家观察方（默认蓝方）。</summary>
    private const Side Viewer = Side.Blue;

    private OptionButton? _scaleSelector;
    private LineEdit? _seedInput;
    private OptionButton? _opponentMode;
    private Button? _startButton;
    private Label? _errorLabel;

    public override void _Ready()
    {
        _scaleSelector = GetNodeOrNull<OptionButton>("%ScaleSelector");
        _seedInput = GetNodeOrNull<LineEdit>("%SeedInput");
        _opponentMode = GetNodeOrNull<OptionButton>("%OpponentMode");
        _startButton = GetNodeOrNull<Button>("%BtnStart");
        _errorLabel = GetNodeOrNull<Label>("%ErrorLabel");

        // 场景未提供控件（例如裸实例化本脚本）时构建一套默认 UI，保证可运行。
        if (_scaleSelector is null || _seedInput is null || _opponentMode is null ||
            _startButton is null || _errorLabel is null)
        {
            BuildDefaultUi();
        }

        PopulateScaleOptions();
        PopulateOpponentOptions();
        ClearError();

        _startButton!.Pressed += OnStartPressed;
    }

    /// <summary>
    /// 「开始对局」处理（Req 14.2–14.5）：载入配置 → 构造初始状态 → 交接 → 切场景。
    /// 任一步失败（配置非法）时中止并显示错误，不进入对局（Req 14.3）。
    /// </summary>
    private void OnStartPressed()
    {
        ClearError();

        // Req 14.2/14.3：经 ConfigLoader 载入并校验；失败则显示错误并中止。
        GameData? data = LoadConfigOrShowError();
        if (data is null)
        {
            return;
        }

        var scale = SelectedScale();
        if (!data.ScaleProfiles.TryGetValue(scale, out var scaleProfile))
        {
            ShowError($"配置缺少规模档位 '{scale}'，无法开始对局。");
            return;
        }

        var seed = ResolveSeed();
        var mode = SelectedOpponentMode();
        var fog = new FogConfig(BlipRingEnabled: true, NightVisionDivisor: 2);

        GameState initialState;
        try
        {
            // Req 14.4/14.5：构造合法初始状态并以种子初始化 RNG（RngState 取自 PcgRng 初始快照）。
            var rng = new PcgRng(seed);
            initialState = BuildInitialState(data, scale, scaleProfile, rng.State);
        }
        catch (Exception ex)
        {
            // 构造失败（配置内容不足以组出合法对局等）视为数据非法：中止并提示（Req 14.3）。
            ShowError($"初始对局构造失败：{ex.Message}");
            return;
        }

        // Req 14.4/14.5：经交接单例把数据传给 Match 场景。
        MatchBootstrap.Set(data, initialState, seed, fog, Viewer, mode);

        Error error = GetTree().ChangeSceneToFile(MatchScenePath);
        if (error != Error.Ok)
        {
            MatchBootstrap.Clear();
            ShowError($"切换到对局场景失败（{error}）。");
        }
    }

    /// <summary>
    /// 经 <see cref="ConfigLoader"/> 载入并校验配置（Req 14.2）。数据非法时捕获
    /// <see cref="InvalidDataException"/>，显示错误并返回 <c>null</c>（Req 14.3）。
    /// </summary>
    private GameData? LoadConfigOrShowError()
    {
        try
        {
            return new ConfigLoader().Load(DefaultConfigJson);
        }
        catch (InvalidDataException ex)
        {
            ShowError($"配置载入失败：{ex.Message}");
            return null;
        }
    }

    // ── 初始状态构造（Req 14.4）────────────────────────────────────────

    /// <summary>
    /// 构造一个<b>合法且可试玩</b>的初始对局状态：小型地图 + 若干蓝/红单位 + 双方卡牌经济。
    /// 单位属性取自配置的兵种模板，保证攻/防整除韧性 N 等 Core 约束（由 <c>DataLoader</c> 校验）。
    /// </summary>
    private static GameState BuildInitialState(
        GameData data,
        MapScale scale,
        MapScaleProfile scaleProfile,
        ulong rngState)
    {
        const int width = 9;
        const int height = 7;

        var map = BuildMap(width, height, scale);
        var units = BuildUnits(data);
        var cards = BuildCards(scaleProfile);

        return new GameState(
            SchemaVersion: 1,
            Map: map,
            Units: units,
            DayIndex: 0,
            Phase: DayNightPhase.Morning,
            Cards: cards,
            RngState: rngState,
            TurnLog: Array.Empty<TurnRecordEntry>());
    }

    /// <summary>构造一个以平原为主、点缀若干地形的矩形六角网格。</summary>
    private static GameMap BuildMap(int width, int height, MapScale scale)
    {
        var cells = new Dictionary<HexCoord, MapCell>();
        for (var q = 0; q < width; q++)
        {
            for (var r = 0; r < height; r++)
            {
                var coord = new HexCoord(q, r);
                cells[coord] = new MapCell(coord, TerrainAt(q, r));
            }
        }

        return new GameMap(cells, scale);
    }

    /// <summary>为占位地图挑选可区分的地形（无规则含义，仅为试玩时可见地形差异）。</summary>
    private static TerrainType TerrainAt(int q, int r) => (q, r) switch
    {
        (3, 2) => TerrainType.Forest,
        (3, 4) => TerrainType.Forest,
        (4, 3) => TerrainType.Hill,
        (5, 1) => TerrainType.City,
        (5, 5) => TerrainType.City,
        (2, 3) => TerrainType.River,
        (6, 3) => TerrainType.River,
        _ => TerrainType.Plain,
    };

    /// <summary>
    /// 放置若干蓝/红单位。使用配置中的兵种模板作为属性来源（不足时回退到一份内置合法模板），
    /// 保证 <see cref="UnitState"/> 满足 Core 约束（攻/防整除韧性 N）。
    /// </summary>
    private static IReadOnlyList<UnitState> BuildUnits(GameData data)
    {
        var template = PickTemplate(data);

        var placements = new (Side Side, HexCoord Pos)[]
        {
            (Side.Blue, new HexCoord(1, 3)),
            (Side.Blue, new HexCoord(2, 2)),
            (Side.Blue, new HexCoord(2, 4)),
            (Side.Red, new HexCoord(7, 3)),
            (Side.Red, new HexCoord(6, 2)),
            (Side.Red, new HexCoord(6, 4)),
        };

        var units = new List<UnitState>(placements.Length);
        for (var i = 0; i < placements.Length; i++)
        {
            var (side, pos) = placements[i];
            units.Add(NewUnit(new UnitId(i), side, pos, template));
        }

        return units;
    }

    /// <summary>由兵种模板与位置构造一个满编（当前值=初始值）的单位实例。</summary>
    private static UnitState NewUnit(UnitId id, Side owner, HexCoord pos, UnitTemplate t) => new(
        id,
        owner,
        t.TypeKey,
        t.Class,
        InitAttack: t.Attack,
        InitDefense: t.Defense,
        Resilience0: t.Resilience,
        Attack: t.Attack,
        Defense: t.Defense,
        ResilienceLeft: t.Resilience,
        Movement: t.Movement,
        Vision: t.Vision,
        SupportRange: t.SupportRange,
        Position: pos,
        Command: Command.Hold,
        Flags: t.DefaultFlags);

    /// <summary>取配置中的首个兵种模板；配置无模板时回退到一份内置合法模板。</summary>
    private static UnitTemplate PickTemplate(GameData data)
    {
        if (data.UnitTemplates.Count > 0)
        {
            return data.UnitTemplates[0];
        }

        // 回退模板：攻 6 / 防 6 均整除韧性 3（满足 Core 约束）。
        return new UnitTemplate(
            "unit.fallback",
            UnitClass.LineHold,
            Attack: 6,
            Defense: 6,
            Movement: 4,
            Resilience: 3,
            Vision: 3,
            SupportRange: 0,
            SupportShift: null,
            DefaultFlags: NightFlags.None);
    }

    /// <summary>为双方按规模档位初始化卡牌经济，并产出一回合 CP 使临机机动可试玩（Req 12/13）。</summary>
    private static IReadOnlyDictionary<Side, CardState> BuildCards(MapScaleProfile profile)
    {
        CardState Fresh() => CardSystem.GainCp(CardSystem.Init(profile), profile.CpPerTurn, profile.CpMax);

        return new Dictionary<Side, CardState>
        {
            [Side.Blue] = Fresh(),
            [Side.Red] = Fresh(),
        };
    }

    // ── 输入解析 ──────────────────────────────────────────────────────

    private MapScale SelectedScale()
    {
        var idx = _scaleSelector?.Selected ?? 0;
        return idx switch
        {
            1 => MapScale.Medium,
            2 => MapScale.Large,
            _ => MapScale.Small,
        };
    }

    private OpponentMode SelectedOpponentMode()
        => (_opponentMode?.Selected ?? 0) == 1 ? OpponentMode.HotSeat : OpponentMode.Scripted;

    /// <summary>解析玩家种子输入；为空或非法则用固定默认种子（Req 14.5）。</summary>
    private ulong ResolveSeed()
    {
        var text = _seedInput?.Text?.Trim();
        if (!string.IsNullOrEmpty(text) &&
            ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed))
        {
            return seed;
        }

        return DefaultSeed;
    }

    // ── UI 装配与错误显示 ─────────────────────────────────────────────

    private void PopulateScaleOptions()
    {
        if (_scaleSelector is null || _scaleSelector.ItemCount > 0)
        {
            return;
        }

        _scaleSelector.AddItem("小型");
        _scaleSelector.AddItem("中型");
        _scaleSelector.AddItem("大型");
        _scaleSelector.Select(0);
    }

    private void PopulateOpponentOptions()
    {
        if (_opponentMode is null || _opponentMode.ItemCount > 0)
        {
            return;
        }

        _opponentMode.AddItem("脚本化占位对手");
        _opponentMode.AddItem("本地热座");
        _opponentMode.Select(0);
    }

    private void ShowError(string message)
    {
        if (_errorLabel is not null)
        {
            _errorLabel.Text = message;
            _errorLabel.Visible = true;
        }

        GD.PushWarning($"MatchSetup: {message}");
    }

    private void ClearError()
    {
        if (_errorLabel is not null)
        {
            _errorLabel.Text = string.Empty;
            _errorLabel.Visible = false;
        }
    }

    private void BuildDefaultUi()
    {
        var box = new VBoxContainer { Name = "SetupLayout" };
        box.SetAnchorsPreset(LayoutPreset.Center);
        box.AddThemeConstantOverride("separation", 12);
        AddChild(box);

        box.AddChild(new Label { Text = "对局设置" });

        _scaleSelector ??= AddUniqueChild(box, new OptionButton { Name = "ScaleSelector" });
        _seedInput ??= AddUniqueChild(box, new LineEdit
        {
            Name = "SeedInput",
            PlaceholderText = "随机种子（留空则用默认）",
        });
        _opponentMode ??= AddUniqueChild(box, new OptionButton { Name = "OpponentMode" });
        _startButton ??= AddUniqueChild(box, new Button { Name = "BtnStart", Text = "开始对局" });
        _errorLabel ??= AddUniqueChild(box, new Label
        {
            Name = "ErrorLabel",
            Visible = false,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
        });
    }

    private static T AddUniqueChild<T>(Node parent, T node)
        where T : Node
    {
        node.SetUniqueNameInOwner(true);
        parent.AddChild(node);
        return node;
    }

    /// <summary>
    /// 内置最小但合法的占位配置 JSON（枚举以字符串序列化）。经 <see cref="ConfigLoader.Load"/>
    /// 载入以贯通配置载入/校验路径（Req 14.2/14.3）；含一份兵种模板、六种地形、一条合法学说、
    /// 空卡牌集与三档规模档位。
    /// </summary>
    private const string DefaultConfigJson = """
{
  "units": [
    {
      "typeKey": "unit.line-battalion",
      "class": "LineHold",
      "attack": 6,
      "defense": 6,
      "movement": 4,
      "resilience": 3,
      "vision": 3,
      "supportRange": 0,
      "supportShift": null,
      "defaultFlags": "None"
    }
  ],
  "terrains": [
    { "terrain": "Plain",  "moveCost": 1, "defensiveDrm": 0, "forbiddenClasses": [], "enterAndStop": false },
    { "terrain": "Forest", "moveCost": 2, "defensiveDrm": 1, "forbiddenClasses": [], "enterAndStop": false },
    { "terrain": "Hill",   "moveCost": 2, "defensiveDrm": 1, "forbiddenClasses": [], "enterAndStop": false },
    { "terrain": "City",   "moveCost": 1, "defensiveDrm": 2, "forbiddenClasses": [], "enterAndStop": false },
    { "terrain": "River",  "moveCost": 3, "defensiveDrm": 0, "forbiddenClasses": [], "enterAndStop": true },
    { "terrain": "Swamp",  "moveCost": 3, "defensiveDrm": 0, "forbiddenClasses": [ "FireSupport" ], "enterAndStop": true }
  ],
  "doctrines": [
    {
      "key": "doctrine.standard",
      "modifiers": [],
      "signatureFlags": "None",
      "specializations": [
        { "key": "doctrine.standard.a", "modifiers": [ { "stat": "Attack",  "value": 0, "pointCost": 4 } ], "budget": 4 },
        { "key": "doctrine.standard.b", "modifiers": [ { "stat": "Defense", "value": 0, "pointCost": 4 } ], "budget": 4 }
      ]
    }
  ],
  "cards": [],
  "scales": [
    { "scale": "Small",  "cpPerTurn": 2, "cpMax": 6,  "deckSize": 10, "handLimit": 3 },
    { "scale": "Medium", "cpPerTurn": 3, "cpMax": 9,  "deckSize": 14, "handLimit": 4 },
    { "scale": "Large",  "cpPerTurn": 4, "cpMax": 12, "deckSize": 18, "handLimit": 5 }
  ]
}
""";
}
