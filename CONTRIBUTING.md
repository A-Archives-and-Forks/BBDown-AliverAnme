# 贡献指南

感谢你对 BBDown 项目的兴趣！在提交贡献之前，请阅读本指南。

## 开发环境

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download)
- 支持的操作系统：Windows / Linux / macOS

## 构建

```bash
# 还原依赖
dotnet restore

# 编译（Debug）
dotnet build

# 编译（Release）
dotnet build -c Release

# 发布单文件（示例：win-x64）
dotnet publish BBDown -r win-x64 -c Release
```

## 代码风格

- 缩进：4 空格
- 编码：UTF-8
- 换行符：跨平台，由 Git 自动处理
- 遵循 `.editorconfig` 中的配置

## 分支策略（强制）

> ⚠️ **master 分支受保护，禁止直接推送。** 所有变更必须通过 Pull Request 合并。

### 分支命名规范

| 前缀 | 用途 | 示例 |
|------|------|------|
| `feature/` | 新功能开发 | `feature/drm-auto-fetch` |
| `fix/` | Bug 修复 | `fix/muxer-directory-creation` |
| `refactor/` | 代码重构 | `refactor/split-program-methods` |
| `docs/` | 仅文档更新 | `docs/api-server-guide` |
| `deps/` | 依赖升级 | `deps/protobuf-3-35` |

### 开发流程

```bash
# 1. 从最新 master 切分支
git checkout master
git pull origin master
git checkout -b feature/my-feature

# 2. 开发、提交（遵循 Commit 规范，见下文）
git add .
git commit -m "feat: add auto cookie refresh"

# 3. 推送到远端
git push origin feature/my-feature

# 4. 在 GitHub 提交 Pull Request，等待审查
# 5. CI 通过 + 至少 1 人 approve 后，由 maintainer 合并到 master
```

### Commit 规范

遵循 [Conventional Commits](https://www.conventionalcommits.org/zh-hans/v1.0.0/)：

```
<type>(<scope>): <description>

[optional body]

[optional footer]
```

| type | 含义 |
|------|------|
| `feat` | 新功能 |
| `fix` | Bug 修复 |
| `refactor` | 代码重构 |
| `perf` | 性能优化 |
| `docs` | 文档更新 |
| `deps` | 依赖升级 |
| `test` | 测试相关 |
| `chore` | 构建/工具链改动 |

示例：
```
feat(drm): add auto key fetch from WVD device

Previously users had to manually provide --key and --kid.
Now the tool attempts to extract keys automatically when
a device.wvd file is present.

Closes #123
```

## 提交 Issue

- 提交前请先搜索，避免重复
- Bug 报告请使用 Bug Report 模板，并提供 `--debug` 日志
- 功能请求请使用 Feature Request 模板

## 提交 Pull Request

1. Fork 本仓库并创建你的分支：`git checkout -b feature/fooBar`
2. 修改代码，确保 `dotnet build` 通过
3. 如有必要，更新相关文档（README、CHANGELOG 等）
4. 提交并推送到你的 Fork：`git push origin feature/fooBar`
5. 在 GitHub 提交 Pull Request，并填写 PR 模板

## PR 审查原则

- **原子性**：一个 PR 只做一件事
- **向后兼容**：不破坏现有 CLI 参数和配置文件格式
- **文档同步**：代码变更需同步更新文档
- **构建通过**：`dotnet build` 必须在 CI 中通过（0 Error）
- **Review 要求**：非文档类 PR 需要至少 1 名 reviewer 批准
