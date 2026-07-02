# 贡献指南

感谢参与《新旧大陆》。本文件说明协作流程；完整工程约定见 [`docs/04-工程约定.md`](docs/04-工程约定.md)，技术选型见 [`docs/adr/0001-引擎与核心语言选型.md`](docs/adr/0001-引擎与核心语言选型.md)，设计规则见 [`docs/`](docs/README.md)。

技术栈：**C#/.NET 引擎无关核心**（`Xjdl.Core`）+ **Godot 4 表现层**（`game/`，后期加入）。核心可在无 Godot 的情况下用 `dotnet` 独立开发与测试。

## 环境准备

```bash
# 1) 安装 .NET 8 SDK（LTS）：https://dotnet.microsoft.com/download/dotnet/8.0
dotnet --info            # 验证 SDK 已安装

# 2) 还原依赖
dotnet restore

# 3) 二进制资源走 Git LFS，首次克隆前请安装
git lfs install

# 4)（表现层阶段才需要）安装 Godot 4 .NET 版：https://godotengine.org/download
```

## 开发命令

```bash
dotnet build     # 构建
dotnet test      # 单元测试（xUnit，纯核心，无需 Godot）
dotnet format    # 格式化 + 分析器
```

> Godot 相关的运行、场景编辑与导出在 Godot 编辑器内完成；核心逻辑的开发/测试全程只需 `dotnet`。

## 工作流

1. 从 `main` 切分支：功能 `feat/*`、修复 `fix/*`、文档 `docs/*`。
2. 小步提交，提交信息建议遵循 Conventional Commits（`feat:` / `fix:` / `docs:` / `test:` / `refactor:`），正文说明改了哪条规则/数据及对应文档章节。
3. 提交前本地通过 `dotnet format --verify-no-changes`、`dotnet test`、`dotnet build`。
4. 发起 PR，附改动摘要与测试说明；CI 全绿后方可合并。**不直接推 `main`。**

## 红线（务必遵守）

- **core 纯净**：`src/Xjdl.Core/**` 不得依赖 Godot、渲染、文件/网络 IO、计时器，或非确定性随机/时间源（无种子 `System.Random`、`DateTime.Now`、`Guid.NewGuid()`）。
- **确定性**：掷骰用注入的带种子 RNG；相同输入+种子必须产出字节级一致结果。核心避免浮点，用整数/定点。改 core 规则必须配套确定性测试。
- **文档同步**：改动规则数值时同步更新 `docs/` 对应章节——文档是规则的唯一事实来源。
- **数据驱动**：新增兵种/卡牌/地形/关卡应只改 `src/Xjdl.Data/`，不改 core 逻辑。
- **大文件用 Git LFS**，不要提交 `bin/`、`obj/`、`.godot/` 或个人 IDE 配置。
