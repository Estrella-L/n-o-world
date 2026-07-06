# 《新旧大陆》Godot 表现层运行说明

本目录（`game/`）是《新旧大陆》（Xjdl）的 **Godot 4 表现层**工程，负责渲染、输入、UI、动画、存读档界面与对局启动流程。它通过项目引用（`ProjectReference`）**复用**纯规则引擎 `src/Xjdl.Core` 与配置层 `src/Xjdl.Data`，不重写任何规则逻辑——一切规则计算仍由 `RulesEngine.NextState` 承担。

工程约定见 [`../docs/04-工程约定.md`](../docs/04-工程约定.md)，技术选型见 [`../docs/adr/0001-引擎与核心语言选型.md`](../docs/adr/0001-引擎与核心语言选型.md)，协作流程见 [`../CONTRIBUTING.md`](../CONTRIBUTING.md)。

## 前置条件

| 依赖 | 版本要求 | 说明 |
| ---- | ---- | ---- |
| **Godot 4（.NET/C# 版）** | 4.4.x（工程以 Godot `4.4` 特性集创建，见 `project.godot`） | 必须是带 **.NET/C# 支持**的版本（下载页标注 “.NET”），标准版无法编译 C# 脚本。 |
| **.NET 8 SDK** | 8.0.x（由根 [`../global.json`](../global.json) 锁定为 `8.0.422`，`rollForward: latestFeature`） | 表现层 `TargetFramework` 为 `net8.0`，与既有解决方案一致。 |

获取方式：

- Godot 4 .NET 版：<https://godotengine.org/download>（选择 **.NET** 版本）。
- .NET 8 SDK（LTS）：<https://dotnet.microsoft.com/download/dotnet/8.0>。

安装后可用 `dotnet --info` 验证 SDK；在 Godot 编辑器 `编辑器 → 编辑器设置 → .NET` 中确认已正确识别 .NET SDK 路径。

## 工程结构一览

```
game/
├── project.godot          Godot 工程配置（程序集名 Xjdl.Game，主场景待接入）
├── Xjdl.Game.csproj       Godot.NET.Sdk 工程，net8.0，引用 Xjdl.Core / Xjdl.Data
├── scenes/                .tscn 场景（MainMenu / MatchSetup / Match）
├── src/                   Godot 节点脚本（依赖 Godot）
├── presentation/          纯 C# 表现逻辑层（不 using Godot，可脱离引擎测试）
└── assets/                占位色块/几何形状（无最终美术）
```

> 单向依赖铁律：`game/` → `Xjdl.Core` / `Xjdl.Data`，核心与数据层**永不反向依赖 Godot**。

## 在 Godot 编辑器中导入 `game/`

1. 启动 **Godot 4 .NET 版**编辑器。
2. 在项目管理器中点击 **导入（Import）**。
3. 选择本仓库的 `game/project.godot` 文件（或选中 `game/` 目录后由编辑器自动识别）。
4. 点击 **导入并编辑（Import & Edit）**。首次导入时 Godot 会生成 `game/.godot/` 缓存目录（已被 `.gitignore` 排除，无需提交）。

## 构建 C# 解决方案

表现层的 C# 代码可由 Godot 编辑器构建，也可用 `dotnet` 命令行构建。

**方式一：命令行（推荐用于 CI 与快速校验）**

在仓库根目录执行：

```bash
dotnet restore
dotnet build            # 一并构建 Xjdl.sln 中的 game/Xjdl.Game.csproj
```

`game/Xjdl.Game.csproj` 已加入根解决方案 `Xjdl.sln`，`dotnet build` 会连同核心与数据层一起编译。核心/数据层的单元测试仍可纯 `dotnet test` 运行，无需 Godot。

**方式二：Godot 编辑器内构建**

首次打开工程后，点击编辑器右上角的 **构建（Build，锤子图标）** 按钮生成 C# 程序集。之后每次修改 C# 脚本，运行前 Godot 会自动重新构建；如遇脚本未生效，可手动再次点击构建。

> 若在 Godot 编辑器内看到与生成代码相关的告警，属预期：表现层工程已在 `Xjdl.Game.csproj` 中放开 `TreatWarningsAsErrors`，避免 Godot 注入的生成代码导致构建失败。

## 指定并运行主场景（主菜单）

Godot 工程的“启动主场景”由 `project.godot` 中 `[application]` 段的 `run/main_scene` 指定。当前该值为占位空串（`run/main_scene=""`），主菜单场景将在后续任务（对局启动流程）中接入。

设定主场景为主菜单的两种方式：

- **编辑器 UI（推荐）**：`项目 → 项目设置（Project → Project Settings）→ 应用程序 → 运行（Application → Run）→ 主场景（Main Scene）`，选择 `scenes/MainMenu.tscn`。保存后 `project.godot` 会写入：

  ```ini
  [application]
  run/main_scene="res://scenes/MainMenu.tscn"
  ```

- **直接编辑 `project.godot`**：将 `run/main_scene=""` 改为 `run/main_scene="res://scenes/MainMenu.tscn"`。

## 运行主场景

- **运行主场景**：按 <kbd>F5</kbd> 或点击编辑器右上角 **运行项目（Run Project）**。Godot 会启动上面指定的主场景（主菜单）。
- **运行当前编辑的场景**：按 <kbd>F6</kbd> 或点击 **运行当前场景（Run Current Scene）**，用于在开发中单独试玩某个 `.tscn`（如 `Match.tscn`）。

运行前请确保 C# 已成功构建（见上一节），否则脚本节点不会生效。

## 验证取向

表现层以**在 Godot 编辑器中手动试玩**为主要验证手段（渲染、输入、动画、迷雾呈现、存读档、回放、对局启动等按设计文档〈手动验收清单〉逐条人工核对）。仅对少量纯 C# 逻辑接缝保留可选的自动化测试。规则正确性由 `Xjdl.Core` 的既有测试保障，表现层不重复测试核心规则。

## 版本控制说明

`game/.godot/`、`bin/`、`obj/`、`export_presets.cfg` 等 Godot 缓存与构建产物已由仓库根 [`../.gitignore`](../.gitignore) 排除，请勿提交。二进制资源走 Git LFS（见 `CONTRIBUTING.md`）。
