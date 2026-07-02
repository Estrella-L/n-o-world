# 新旧大陆 / Old Land, New Land

六边形格、**WEGO 同步回合制**的轻量级战役兵棋游戏。推演粒度为旅级（每个棋子＝营/连级战斗群），操作复杂度介于 KARDS 与钢铁雄心之间——既能指挥部队，上手难度又不过高，并通过技能卡与作战学说引入可玩性。

> 背景设定见 [`docs/03-游戏背景故事设计.md`](docs/03-游戏背景故事设计.md)。玩家是欧盟 PMC「康科迪亚（Concordia Holdings）」下属一个师的师长，所辖部队代号「灰线」。

## 设计支柱

- **确定性结算**：WEGO「背对背下令、同时结算」，全程战力快照，结果不依赖结算次序。相同「初始状态 + 命令序列 + 种子」产出字节级一致的结果。
- **数据驱动**：兵种、地形、学说、卡牌、昼夜修正全部走可配置数据与统一修正管线（移档受 ±2 总封顶，地形走独立 DRM 轴）。
- **逻辑与渲染分离**：`src/Xjdl.Core` 为纯规则引擎，**引擎无关**、不含任何 Godot/渲染/IO 依赖，可在无 Godot 的情况下用 `dotnet` 独立单元测试。

## 技术栈

- **C#/.NET 8（LTS）** —— 引擎无关的纯规则内核 `Xjdl.Core`。复杂规则系统需要强类型；确定性用整数运算 + 带种子 RNG 实现，不依赖引擎的浮点/更新循环。
- **Godot 4（.NET 版）** —— 表现层（渲染、输入、UI、音频、本地化、打包），引用同一个 `Xjdl.Core`，逻辑代码 1:1 复用、零重写。后期加入。
- **xUnit** —— 核心的无头单元测试与属性测试，验证规则引擎的确定性与边界条件。

> 选型理由见 [ADR 0001 · 引擎与核心语言选型](docs/adr/0001-引擎与核心语言选型.md)。核心刻意与表现层解耦：核心全程只需 `dotnet`，Godot 仅负责呈现。

## 目录结构

```
.
├── Xjdl.sln
├── docs/                    设计文档（世界观 / 战斗机制 / 棋子 / 工程约定 / ADR）
├── src/
│   ├── Xjdl.Core/           纯规则引擎（.NET 类库，无 Godot/渲染/IO 依赖，确定性、可测试）
│   │   ├── Hex/             六角坐标与几何：相邻、距离、视野范围
│   │   ├── State/           GameState、Unit、MapCell 等不可变状态类型
│   │   ├── Random/          SeededRng：带种子确定性随机源
│   │   ├── Turn/            WEGO 阶段流水线（阶段 0–9）、双阶段、临机机动
│   │   ├── Combat/          选表、火力比、读表、战损、堆叠、撤退/推进结算
│   │   ├── Modifiers/       统一移档管线（source 标识、±2 档总封顶）
│   │   ├── Fog/             战争迷雾：三级可见度与刷新时点
│   │   ├── Terrain/         地形系统：移动消耗与防御 DRM
│   │   ├── Doctrine/        作战学说：模板 + 修正、预算校验、签名标志
│   │   ├── Cards/           技能卡系统与 CP 经济
│   │   └── Save/            序列化、SchemaVersion、迁移、回放
│   └── Xjdl.Data/           可配置数据：兵种、地形、学说、卡牌、规模档位（引用 Core 类型）
├── game/                    Godot 4 表现层（后期加入，引用 Xjdl.Core）
└── tests/
    └── Xjdl.Core.Tests/     xUnit 单元测试 + 属性测试（重点验证 WEGO 结算的确定性）
```

> **依赖方向单向**：`Xjdl.Data → Xjdl.Core`；`Xjdl.Core` 永不引用 Godot、`Xjdl.Data`、渲染或 IO。

## 开发

需要 **.NET 8 SDK**；表现层阶段另需 **Godot 4（.NET 版）**。详细环境准备见 [`CONTRIBUTING.md`](CONTRIBUTING.md)。

```bash
dotnet restore   # 还原依赖
dotnet build     # 构建
dotnet test      # 单元测试（xUnit，纯核心，无需 Godot）
dotnet format    # 格式化 + 分析器
```

> Godot 相关的运行、场景编辑与导出在 Godot 编辑器内完成；核心逻辑的开发/测试全程只需 `dotnet`。

## 工程约定与贡献

- 工程约定（架构/确定性/数据/测试/协作）：[`docs/04-工程约定.md`](docs/04-工程约定.md)
- 技术选型：[`docs/adr/0001-引擎与核心语言选型.md`](docs/adr/0001-引擎与核心语言选型.md)
- 贡献流程：[`CONTRIBUTING.md`](CONTRIBUTING.md)

## 文档导航

| 文档 | 内容 |
| ---- | ---- |
| [`docs/01-战斗机制.md`](docs/01-战斗机制.md) | 核心规则全集 |
| [`docs/02-棋子设计.md`](docs/02-棋子设计.md) | 棋子设计 |
| [`docs/03-游戏背景故事设计.md`](docs/03-游戏背景故事设计.md) | 世界观、势力、战役剧情 |
| [`docs/04-工程约定.md`](docs/04-工程约定.md) | 工程约定（唯一事实来源） |

完整索引见 [`docs/README.md`](docs/README.md)。
