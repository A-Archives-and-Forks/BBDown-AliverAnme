# 变更日志

本文件遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/) 规范，版本号遵循 [Semantic Versioning](https://semver.org/lang/zh-CN/)。

## [Unreleased]

## [1.6.8] - 2026-08-01

### 安全

- **DRM 密钥进程列表暴露**：`mp4decrypt` 解密密钥改为通过临时文件传递（`--key-file`），避免命令行对同主机 `ps aux` 可见。临时文件使用后覆写并删除。
- **Debug 日志密钥脱敏**：Widevine 解密密钥在 debug 日志中仅显示前 8 字符。
- **SSL 跳过诊断增强**：`--insecure` 模式下记录被跳过的证书错误类型到 debug 日志，便于排查。

### 修复

- **WidevineCdm 异常吞噬**：protobuf 解析失败和 RSA 解密失败不再静默吞错，改为记录诊断日志并降级返回 null。
- **异步死锁防护**：`LoginCommand`、`LoginTVCommand`、`BBDownApiServer` 中 `.GetAwaiter().GetResult()` 调用改为 `Task.Run(...).GetAwaiter().GetResult()`，防止被 GUI 宿主复用时死锁。
- **调试日志文件清理**：仓库根目录遗留的 20 个 `debug_*.json` 已删除，并加入 `.gitignore`。

### 测试

- 新增 `WidevineCdmTests`：PSSH 解析边界、非法输入降级。
- 新增 `ParserTests`：`ThrowIfPlayLimited` 全覆盖、WbiSign、Codec 映射。
- `UrlResolverTests` 添加 `[Trait("Category", "Integration")]` 标记，CI 可通过 `--filter` 排除。

### 维护

- `.remember/`、`.pi-subagents/`、`debug_*.json`、`artifact/` 加入 `.gitignore`。
- `Parser.cs` Dispose 所有权说明注释。
- `WidevineCdm.cs` RSA OAEP fallback 从裸 `catch` 改为 `catch (CryptographicException)`。

## [1.6.7] - 2026-07-27

### 修复

- **TV 端解析健壮性**：处理 TV API 返回的 `result` 节点不是 JSON 对象（如 `null`/数组）时的解析错误。
- **登录错误提示增强**：WEB/TV 登录失败时返回非零退出码；提示信息区分“网络失败”与“二维码过期”。
- **取消 token 贯通** (`cancellationToken`)：
  - `CheckUpdateAsync` 可被取消，避免退出后仍访问 GitHub。
  - `BBDown serve` 收到 `Ctrl+C` 时优雅停止，正在处理的 HTTP 请求可被正常关闭。
  - `BBDown login` / `BBDown logintv` 的二维码轮询可被取消，不再每秒请求一次 B站登录接口。
- **silent-failure 补遗**：
  - 更新检查失败从 debug 日志升级到 warn 提示。
  - `ss:` 输入的番剧→课程 fallback 不再裸 `catch`，会打印 fallback 原因。
  - 章节信息、DRM license 元数据提取失败时改为 warn 级别。
  - 下载重试日志带上异常类型名，3 次失败后给出明确“已重试 N 次”提示。

### 变更

- `CheckUpdateAsync` 版本比较统一为 `vX.Y.Z` 格式，不再对同一版本误报“发现新版本”。
- 未登录提示增加 TV 登录用法说明：若已执行 `BBDown logintv`，请在下载命令中加上 `--use-tv-api`。

## [1.6.6] - 2026-07-27

### 新增

- **充电专属（试看）视频检测**：解析 UPower 接口时识别充电专属预览片段，默认跳过并提示；新增 `--allow-preview` 选项允许保存试看内容。
- 支持下载 UP 主全部投稿列表（`space` 下载模式）。
- 测试覆盖扩展：新增 DRM 私钥解析、选项绑定等回归测试。

### 安全

- 日志与控制台输出中对 Cookie、Token 等凭据进行脱敏处理。
- APP API gRPC 请求中的 Authorization 头在日志中脱敏。
- Widevine 解密私钥导入支持 PKCS#1 / PKCS#8 DER 格式；`mp4decrypt` 失败时显式抛异常，避免静默失败。

### 变更

- Release CI 流程：所有平台构建任务前必须先通过测试套件。
- Docker 构建目标切换到 .NET 10，并仅还原应用项目以加速构建。

### 修复

- 路径解析：从 `AppContext.BaseDirectory` 解析 `APP_DIR`，修复不同启动方式下的工作目录错误。
- 登录状态：区分“从未登录”与“Cookie 已过期”。
- 下载链路：消除静默失败、校验数值选项、正确传播单 P 失败状态。
- 分段下载：`MergeFLV` 仅合并本分段的片段并校验 ffmpeg 退出码。
- Fetcher：修复分页死锁、悬空 `JsonElement` 与循环中的静默中断。
- 配置解析：正确识别 `--opt=value` 形式的 `--config-file` 与命令行选项。
- aria2c：通过 `stdin input-file` 而非命令行传递 Cookie，避免特殊字符被 Shell 截断。
- 交互式选集：使用 `>=` 正确限制 track/quality 索引。
- Archive：仅当某 aid 的所有分 P 都成功后才记录该 aid。
- 字幕：保留超过 24 小时的时长，并防止空白行错误拆分 cue。
- 弹幕：对 ASS 输出中的控制字符进行转义。
- 选项默认值：让 `default-on` 标记真正默认为 `true`。
- 分 P 选择：支持混合 `-p` 语法，并拒绝完全匹配不到任何页面的选择。
- API 服务器：HTTP 响应完成后仍保持下载任务存活。

### 文档

- README 增加“更多常用选项”参考表。
- README 添加充电专属视频处理说明。
- 修复 CLI 示例与 README 资源链接。

## [1.6.5] - 2026-07-25

### 修复

- 修复 Native AOT 产物启动时 Spectre.Console.Cli 无法获取默认命令 settings 类型导致崩溃的问题。
- 兼容 `-help`、`-?`、`-version` 等单横线常见参数写法，避免 `-help` 被解析为短选项簇并误报 `encoding-priority` 缺值。
- 修复 `Av` 大小写视频 URL 解析问题。
- 优化区域限制等播放限制的错误提示，明确展示 `limit_play_reason` 与 `play_detail`。
- 修正 BV 转换与 SS URL 解析相关测试样例。
- 统一 GitHub issues 链接为小写仓库路径。

## [1.6.4] - 2026-05-29

### 新增

- **原生 C# Widevine DRM 解密**（完全替代 Python/pywidevine 依赖）
  - 实现 `WidevineCrypto.AesCmac` + `derive_keys` / `derive_context` 密钥派生
  - 完整的 HMAC-SHA256 签名校验 + AES 内容密钥解密
  - V2 WVD 格式支持 + B站服务证书 PKCS#1 公钥兼容
- GitHub Release 自动化工作流（推送 `v*` tag 自动构建 6 平台并创建 Release）
- API 服务器并发数自定义：`BBDown serve --max-concurrent <n>`
- CLI 自定义参数：
  - `--muxer-timeout <分钟>` — 混流超时（默认 30）
  - `--retry-count <n>` — 网络请求重试次数（默认 3）
  - `--retry-delay <毫秒>` — 重试间隔基数（默认 3000）
  - `--thread-segment-size <MB>` — 多线程下载分片大小（默认 20）
- Cookie 过期检测与明确提示（区分"未登录"vs"Cookie 已过期"）
- 下载链路 `CancellationToken` 贯通（CLI Ctrl+C / API 请求取消）
- `.tmp` 文件断点续传支持（完整临时文件自动移动，写入增量校验修复）
- API 服务器文件日志（`bbdown-api.log`）
- `JsonElementExtensions` 安全 JSON 访问器（10 个扩展方法）
- 单元测试骨架：`BBDown.Tests`（`BilibiliBvConverterTests` / `UrlResolverTests` / `FormatHelperTests`）
- 核心方法拆分：`UrlResolver.cs` / `ExternalToolHelper.cs`

### 变更

- **目标框架升级：.NET 9 → .NET 10**
- 升级依赖：QRCoder 1.6.0 → 1.8.0
- 升级依赖：Google.Protobuf 3.28.3 → 3.34.1
- 升级依赖：Grpc.Tools 2.67.0 → 2.80.0
- 迁移 CLI 框架：System.CommandLine（已归档）→ Spectre.Console.Cli 0.55.0
- `Config` 全局状态重构：`AppSettings` record + 线程安全读写锁
- `HttpClient` 连接池刷新：`SocketsHttpHandler.PooledConnectionLifetime = 5min`
- 规范化 API 文档文件名：`json-api-doc.md` → `API.md`
- 重试策略精细化：指数退避 + 不可重试异常短路（`ArgumentException` / `InvalidOperationException` / `NotSupportedException`）
- 清理冗余 NuGet 引用：`Microsoft.Extensions.DependencyInjection`（已由 `Microsoft.NET.Sdk.Web` 隐式提供）

### 修复

- **API server `dotnet run` 端口劫持**：移除 `launchSettings.json`，`serve --listen` 现在正确绑定自定义地址
- **Widevine proto 协议合规**：字段编号与 Google 标准对齐（`pssh_data=1`、`RequestType` 枚举、`key_control_nonce=uint32`）
- **Native AOT 运行时崩溃**：`MyOption` / `CommandSettings` / `Command` 类添加 `[DynamicallyAccessedMembers]` + `<TrimmerRootAssembly Include="BBDown" />`
- Windows 下 FFmpeg/MP4Box 混流时弹出命令行窗口（`CreateNoWindow = true`）
- 跨平台目录创建逻辑（`Path.GetDirectoryName` 替代 `Contains('/')`）
- 下载重试时的异常信息丢失问题（增加 `LogDebug`）
- API 服务器 Webhook 回调的未观察异常风险
- `Parser.GetMaxQn` 中 `int.Parse` 未处理非数字输入 → `int.TryParse`
- `BBDownMuxer.EscapeString` 双引号转义逻辑错误
- 多处 `First()` 调用在空序列时抛 `InvalidOperationException`
- `Page.bvid` getter 中 `long.Parse(aid)` 未处理非数字 aid
- `MergeFLV` 空数组保护
- `SpaceVideoFetcher` 中 `GetValidFileName` 与 `BBDownUtil` 的重复实现合并到 `BBDown.Core.Util.PathUtil`
- `Path.GetDirectoryName` 返回 null 时的安全防护
- `AppHelper.DoReqAsync` 参数未校验直接 `Convert.ToInt64`
- 文化敏感字符串操作（`ToLower()` → `ToLowerInvariant()`）防止土耳其 locale bug
- 多处 `JsonDocument` / `HttpResponseMessage` 资源泄漏
- `BBDownDownloadUtil` 进度回调中除零风险防护
- FFmpeg/MP4Box 混流死锁（消费 stdout 防止缓冲区满）
- 并发下载目标文件碰撞（按路径 `SemaphoreSlim` 排他锁）
- API 服务器错误信息泄露（默认隐藏 `ErrorMessage`，仅 debug 模式暴露详情）

## [1.6.3] - 2025-05-06

### 修复

- `DelayPerPage` 选项在 System.CommandLine beta4 下错误地要求必填

## [1.6.2] - 2025-03-16

### 修复

- Dockerfile 构建流程优化
- 多处 `JsonDocument` 未正确释放的问题
- `NormalInfoFetcher` 中 `TryGetProperty` 安全性

## [1.6.1] - 2025-02-08

### 新增

- 支持 ASS 弹幕格式输出
- 合集/系列链接新格式兼容（space.bilibili.com/*/lists/*）

### 修复

- 修正 `GetWebLocationAsync` HEAD 请求兼容性

## [1.6.0] - 2024-12-15

### 新增

- Widevine DRM 原生 C# 解密支持（无需 Python）
- API 服务器模式（`BBDown serve`）
- 配置文件支持（`BBDown.config`）

### 变更

- 重构 gRPC APP 接口请求体
- 增加对多音频轨（背景音频、配音）的支持

---

[Unreleased]: https://github.com/AliverAnme/BBDown/compare/v1.6.8...HEAD
[1.6.8]: https://github.com/AliverAnme/BBDown/compare/v1.6.7...v1.6.8
[1.6.7]: https://github.com/AliverAnme/BBDown/compare/v1.6.6...v1.6.7
[1.6.6]: https://github.com/AliverAnme/BBDown/compare/v1.6.5...v1.6.6
[1.6.5]: https://github.com/AliverAnme/BBDown/compare/v1.6.4...v1.6.5
[1.6.4]: https://github.com/AliverAnme/BBDown/compare/v1.6.3...v1.6.4
[1.6.3]: https://github.com/AliverAnme/BBDown/compare/v1.6.2...v1.6.3
[1.6.2]: https://github.com/AliverAnme/BBDown/compare/v1.6.1...v1.6.2
[1.6.1]: https://github.com/AliverAnme/BBDown/compare/v1.6.0...v1.6.1
[1.6.0]: https://github.com/AliverAnme/BBDown/releases/tag/v1.6.0
