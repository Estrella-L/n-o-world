# 贡献指南

感谢参与《新旧大陆》。本文件说明协作流程；完整工程约定见 [`docs/04-工程约定.md`](docs/04-工程约定.md)，设计规则见 [`docs/`](docs/README.md)。

## 环境准备

```bash
# 锁定的 Node 版本见 .nvmrc / package.json engines
npm install

# 二进制资源走 Git LFS，首次克隆前请安装
git lfs install
```

## 开发命令

```bash
npm run dev      # 开发服务器（请在本地终端手动运行）
npm run test     # 单元测试（Vitest）
npm run lint     # 代码检查
npm run build    # 构建
```

## 工作流

1. 从 `main` 切分支：功能 `feat/*`、修复 `fix/*`、文档 `docs/*`。
2. 小步提交，提交信息建议遵循 Conventional Commits（`feat:` / `fix:` / `docs:` / `test:` / `refactor:`），正文说明改了哪条规则/数据及对应文档章节。
3. 提交前本地通过 `npm run lint`、`npm run test`、`npm run build`。
4. 发起 PR，附改动摘要与测试说明；CI 全绿后方可合并。**不直接推 `main`。**

## 红线（务必遵守）

- **core 纯净**：`src/core/**` 不得依赖渲染/DOM/Node IO/计时器/`Math.random`/`Date.now`。
- **确定性**：掷骰用注入的带种子 RNG；相同输入+种子必须产出一致结果。改 core 规则必须配套确定性测试。
- **文档同步**：改动规则数值时同步更新 `docs/` 对应章节——文档是规则的唯一事实来源。
- **数据驱动**：新增兵种/卡牌/地形/关卡应只改 `src/data/`，不改 core 逻辑。
- **大文件用 Git LFS**，不要提交 `node_modules/`、构建产物或个人 IDE 配置。
