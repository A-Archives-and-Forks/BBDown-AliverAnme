# BBDown 优化方案（OPTIMIZATION_PLAN）

> 创建时间：2026-08-31
> 审查范围：`BBDown/`（CLI / Application / Infrastructure / Configuration / Utilities）+ `BBDown.Core/`（Parser / Fetcher / Util / DRM / Entity）+ `BBDown.Tests` + `.github/workflows` + `Directory.Build.props` / `global.json` / `.editorconfig`
> 审查方式：全量源码精读（`bin/obj` 除外）+ 构建/CI 配置核验 + 测试覆盖盲区扫描
> 基线：`dotnet build -c Release 0 警告 0 错误` / `dotnet test` PR 门禁过滤器全绿 / Native AOT `PublishAot=true` 生效

---

## 1. 总体结论

项目整体工程成熟度高，已具备多轮生产问题收敛痕迹（代码中 `RF-` 系列注释覆盖安全/韧性/正确性）。核心亮点：

- `BBDown.Core/Util/HTTPUtil.cs:124` 客户端池隔离（9 个 `Lazy<HttpClient>` 池实例支撑 6 个访问器属性：校验/不安全 × App/Media/Streaming + `VerifiedNoRedirectClient` / `NoRedirectClient` ×2），`AllowAutoRedirect=false` 对媒体与 gRPC 凭据载荷的逐跳校验一致
- `BBDown/Infrastructure/BBDownDownloadUtil.cs:150` `ArrayPool<byte>` + 原子进度 + `Content-Range` 交叉校验 + `Range/If-Range` 续传清单
- `BBDown/Infrastructure/BBDownApiServer.cs:173` 认证限速（滑动窗口+裁剪）/ CSRF（Origin 回环校验）/ `Host` 回环白名单（`IsLoopbackHost`）/ 路径穿越收口
- `BBDown.Core/Config.cs:15` `AsyncLocal<AppSettings>` 隔离 `serve` 并发任务的凭据/Host 污染

**无致命缺陷。** 现存债务集中在三类：**单体过重导致的可维护性债、构建可重现性与依赖新鲜度、少量同步 IO 与重复逻辑**。本方案按 ROI 分级，所有条目均给出位置锚点、处置建议与工作量估算。

---

## 2. 分级优化清单

### P0 — 高收益 / 低风险，建议优先落地

#### P0-1 拆分 `Program` 局部类巨石

- **部分消纳（2026-09-24）**：`DownloadPageAssets.cs` 承载封面/字幕准备、SubOnly 早退、弹幕处理、CoverOnly 与已有产物跳过；`DownloadPageSetup.cs` 承载预览策略和调试响应持久化；`DownloadTrackPreparation.cs` 负责 DASH 识别、轨道过滤/排序/展示，以及 FLV DRM 检查、交互选档和轨道展示。`DownloadPageExecution.cs` 现为独立 `DownloadPageExecutor`，通过 `DownloadPageExecutionServices` 接收路径格式化、弹幕/封面、轨道下载、DRM 与 PCDN 操作；单页执行上下文也已移出 `Program`。DASH/FLV 共用 `DownloadDanmakuAsync`、`DownloadCoverOnlyAsync`、`TrySkipExistingOutput`，保留零产物失败、任务产物登记、取消传播及各自的空目录清理条件。当前 Release 构建零警告、格式检查通过；win-x64 AOT 发布成功，仅有已知 Spectre.Console.Cli `IL3053`。
- **部分消纳（2026-09-24）**：serve 任务模式标记并入 `AppSettings.IsServeMode`，由 `Config` 的 `AsyncLocal` 快照按请求流隔离；API 下载入口显式标记 serve 流，CLI 工作流保留并传递该标记。`SinglePageDefaultSavePath` / `MultiPageDefaultSavePath` 改为编译期常量，移除未使用的可变静态保存路径。
- **部分消纳（2026-09-24）**：页面列表调度移入可构造的 `DownloadOrchestrator`，分页器、归档读写、单页执行、通知及日志均由构造参数提供；`Program.DownloadPagesAsync` 只负责组装依赖。`ArchiveTracker` 从 `Program` 嵌套类型移为独立生产类型。
- **性能优化（2026-09-24）**：分P选择筛选将 `List.Contains` 的线性查找改为 ordinal `HashSet` 查找，最坏 100k 选择项下由平方级比较降为线性构建 + 线性筛选，并保持原页面顺序。
- **部分消纳（2026-09-24）**：混流提交与临时轨道清理移入 `DownloadFinalizer`，muxer、任务工作路径和日志从构造函数注入；章节文件清理移入无状态 `DownloadFileCleanup`。DASH/FLV 仍在原有最终路径锁中调用 finalizer，事务化临时输出、锁内跳过、取消与 finally 清理语义保持。
- **位置**：`BBDown/Application/Download.cs` 的 `DownloadPageAsync` 仍持有单页重试、解析和准备逻辑；`DownloadPageExecutor` 的生产组装仍绑定 `Program` 当前的页面辅助方法，日志、路径锁、媒体工具仍调用静态设施。`Workflow.cs` / `Options.cs` 等 **12 个文件**仍为 `partial class Program` 扩散；`BBDown/Infrastructure/BBDownDownloadUtil.cs` 为 `internal static class`
- **问题**：页面调度、DASH/FLV 执行、混流收尾已有独立构造边界，但页面解析/重试主体与部分全局设施尚未迁出；当前注入可替换页面操作回调，外部进程、日志及媒体工具仍需进一步收敛
- **建议**：
 1. 继续把 `DownloadPageAsync` 的重试、解析和轨道准备流程移入单页处理器，并逐步将页面辅助方法从 `Program` 回调组装中搬出
 2. 评估把 `BBDownDownloadUtil` 从 `internal static` 改为 `IDownloadService`，并注入外部进程与日志边界；保持 CLI 与 `serve` 的共享语义
 3. `Options.cs` 的 `HandleDeprecatedOptions` / `ParseEncodingPriority` / `FindBinaries` 抽为 `OptionNormalizer` 纯静态工具，后续可再去掉对 `Program` 默认路径常量的直接依赖
- **收益**：页面调度器已可通过假分页器、归档存储和通知器独立构造；后续继续把单页执行器与媒体服务迁入接口，逐步减少 `Program` 的静态入口；serve 模式标记已收敛为 `AppSettings` 字段
- **工作量**：3–5 天（含回归用例补齐）

#### P0-2 同步 IO 阻塞异步路径

- **已消纳（2026-09-24）**：
  - `Download.cs` 与 `BBDownMuxer.cs` 的字幕空内容判断改为 `BBDownUtil.HasTextContentAsync`，只异步解码首字符，保留 BOM-only 文件视为空内容的语义。
  - `BBDownDownloadUtil` 的续传清单读写、aria2c 预检及多线程轨道身份检查改为异步，并传递下载取消令牌；写入仍保留同目录临时文件 + 原子改名。
  - `BBDownMuxer` 章节元数据、`Download.cs` 调试 JSON、`Options.cs` 本地凭据读取改为异步并可取消。
  - CLI 启动时的 `BBDown.config` 合并改为可取消异步读取；启动阶段捕获 Ctrl+C 取消并返回 130。
  - `Archive.cs` 归档读写改用异步 I/O 与 `SemaphoreSlim` 串行化；归档成员检查改为 span 扫描，避免 `Split` 为每个条目分配字符串数组。
  - `BBDownUtil.CombineMultipleFilesIntoSingleFileAsync` 移除 `Directory.Exists` / `File.Exists` 前置检查：目录创建与文件删除本身具备幂等语义，避免在异步复制前后额外同步探测文件系统，并消除检查与操作之间的竞态窗口。
- `SubscriptionStore` 的清单/历史读取与原子写入改为异步；CLI add/list/remove 命令也迁移到 `AsyncCommand`，读改写仍由 `SemaphoreSlim` 串行化。
- serve 启动时的历史任务读取移到 `RunAsync` 并改为可取消异步读取；通过 `SemaphoreSlim` 保证同一服务实例只恢复一次。恢复被取消时跳过最终写回，避免未加载的空列表覆盖磁盘上的历史记录。
- **剩余位置**：未发现已审阅异步文件内容读写周围可安全移除的重复存在性探测；仍保留需要做分支判断的同步文件元数据 API（没有对应异步接口）。
- **保留的同步操作**：`BBDownApiServer.PersistFinishedTasks` 使用 `Flush(flushToDisk: true)` 保证持久化语义；当前没有等价的可取消异步 fsync API，不纳入机械替换。
- **问题**：`async` 链上同步阻塞线程池；`serve` 并发下放大（启动期一次性读取不在此列）
- **建议**：对应改为 `ReadAllTextAsync` / `ReadAllLinesAsync` 并透传 `CancellationToken`；`File.Exists` 保留同步（无异步替代）但避免在热路径重复 `GetFileName` / `GetFullPath`
- **工作量**：半天

#### P0-3 依赖停滞与供应链可重现性

- **已消纳（2026-09-24）**：
  - 全仓没有 SharpZipLib API 或类型引用，移除未使用的 `SharpZipLib 1.4.2` 包。
  - 新增根目录 `Directory.Packages.props` 集中管理现有 NuGet 版本，并提交三项目的 `packages.lock.json`；应用与 Core 锁文件覆盖 win/linux/osx 的 x64 与 arm64 RID。
  - PR、release、latest workflow 中的 `setup-dotnet` 开启 NuGet 缓存；Docker 构建上下文复制中央包版本文件。
  - AOT 警告审计发现 `TypeRegistrar.Register` 与 `ITypeRegistrar` 未标注的动态注册契约，将 IL2067 局部抑制在该方法；Spectre CLI 的 IL3050 仅在 `Program.Main` 局部抑制，并说明静态根保留的命令类型。两项从全局 `NoWarn` 移除。
- **仍待处理**：`NoWarn` 仍包含 Spectre.Console.Cli 产生的 IL3000/IL3001/IL3002/IL2104；清空屏蔽后观察到 IL2104、IL3000、IL3053 均来自该依赖。CI 暂未强制 NuGet locked mode。当前 AOT 项目按宿主机推导 RID，NuGet locked mode 会把锁文件与单个宿主 RID 绑定，需先调整 restore/publish 流程再启用。
- **位置**：
  - `BBDown/BBDown.csproj` 中的剩余 `NoWarn` 依赖诊断抑制
- **建议**：
 1. SharpZipLib 已确认没有源码引用，外部依赖已移除。
 2. 中央版本、RID lock files 与 Actions NuGet 缓存已落地；后续需用适配 AOT RID 的 restore/publish 流程启用 locked mode。
 3. 升级或替换 Spectre.Console.Cli 后重新审计其 trimming/AOT 诊断，清理剩余全局抑制；CI 增加 `dotnet publish -p:TreatWarningsAsErrors=true` 定时审计。
- **工作量**：1–2 天

#### P0-4 `Sdk="Microsoft.NET.Sdk.Web"` 收敛

- **已消纳（2026-09-24）**：主项目改用 `Microsoft.NET.Sdk`，显式引用 `Microsoft.AspNetCore.App` 并移除 `EnableStaticWebAssets`；补齐 Web SDK 原先带入的 DI/Hosting 命名空间，serve 以 `IHost.RunAsync` 保持取消与关停流程。Release 构建及 Windows Native AOT 发布通过。
- **位置**：`BBDown/BBDown.csproj:1`
- **问题**：主程序是 CLI + 嵌入式 Kestrel（`BBDownApiServer.cs` 仅需 Minimal API），Web SDK 隐式引入静态资源/Razor 等与 AOT 无关的裁剪分析；`Directory.Build.props:6` 已 `PublishAot=true`，Web SDK 的 AOT 兼容面比普通 SDK 窄；`EnableStaticWebAssets:false` 是为此打的补丁
- **处置**：按上面的消纳记录收敛 SDK；Minimal API 所需框架通过 `FrameworkReference` 显式引用。
- **工作量**：半天（含 `dotnet publish -r win-x64/linux-x64` 冒烟）

---

### P1 — 中收益，建议排期

#### P1-1 Fetcher 重复解析逻辑

- **部分消纳（2026-09-24）**：新增 `FetcherJson.ThrowIfApiError`，Normal/Cheese/Bangumi/Intl/Fav/MediaList/Series 共用顶层 code 校验和消息净化；修复 Bangumi、Intl 与 Fav 的多个错误分支只读取 message 却继续解析的问题。MediaList 在 `data` 缺失时仍保留 Series 回退；SpaceVideo 保留针对登录风控 code 的专用提示。
- **位置**：`BBDown.Core/Fetcher/NormalInfoFetcher.cs:16` / `BangumiInfoFetcher.cs:16` / `CheeseInfoFetcher.cs` / `IntlBangumiInfoFetcher.cs` / `FavListFetcher.cs` / `MediaListFetcher.cs` / `SeriesListFetcher.cs` / `SpaceVideoFetcher.cs` 共 8 实现
- **问题**：各接口响应节点和回退策略不同，不宜强制成单一 `ParseVInfo` 模板；SpaceVideo 的 -352/-403 错误还需专门文案。Fetcher 离线夹具覆盖仍不足，接口结构回归风险未消除。
- **建议**：保留逐接口解析，继续用共享 envelope helper 收敛 code/消息处理；为每个 Fetcher 补离线 JSON 快照用例（仿 `ParserFixtureTests` 的 `Fixtures/parser/` 夹具回放，已有 `FakeBilibiliApiServer` 先例）
- **工作量**：2–3 天

#### P1-2 `Parser.cs` JsonDocument 生命周期与解析性能

- **部分消纳（2026-09-24）**：主播放响应统一由一个 `try/finally` 释放，移除校验失败手动释放与正常返回前重复释放；DASH/FLV 最高清晰度重请求的临时文档在失败、取消、无效响应路径释放，只有接管成功时才转交主响应所有权。
- **位置**：`BBDown.Core/Parser.cs:235` 主响应生命周期与响应替换；`intl` 分支的 `code=0/1` 是两次不同 API 请求，各响应只解析一次，不属于同一 JSON 的重复解析。其余解析继续通过 `JsonElement` 扩展方法访问字段。
- **问题**：响应替换涉及 `JsonElement` 对所属 `JsonDocument` 的引用，异常路径的释放和所有权转移容易漏改；大响应上的属性访问成本尚无基准数据。
- **建议**：先用代表性播放响应建立解析耗时/分配基准，再决定是否对固定结构引入 `JsonSerializerContext` 源生成反序列化（AOT 兼容，`Program.cs:63` 已有 `MyOptionJsonContext` 范例）；基准未证明收益前保留现有 `JsonElement` 解析方式。
- **工作量**：1–2 天

#### P1-3 CI 重复与 EOL 镜像

- **位置**：`pr.yml` 6 个 job 各自 `checkout + setup-dotnet + restore`；`build_latest.yml:93-96` 在 `ubuntu:18.04` 容器内（arm64 交叉构建同系镜像 `:122`）`wget` SDK 无缓存且 18.04 已 EOL；`pr.yml:102` `network-integration` `continue-on-error:true`
- **建议**：抽 `composite action` 复用 `setup`；`build_latest.yml` 的 glibc 兼容构建改 `ubuntu:20.04` 或 `dotnet-buildtools/prereqs:ubuntu-22.04` 并加 `actions/cache` 缓存 SDK tarball；`network-integration` 改为仅 `schedule` 或 `workflow_dispatch` 跑，避免 PR 噪音
- **工作量**：半天

#### P1-4 重试参数双轨收敛

- **已消纳（2026-09-24）**：新增 `RetryPolicy` 集中定义 CLI/serve 的重试次数与延迟范围。CLI 仍拒绝越界值并保留原错误信息；serve 仍将不可信输入钳制到较窄范围，两种入口共用边界常量，避免限制漂移。
- **位置**：CLI `BBDown/Application/Options.cs:239` `ValidateNumericOptions` 与 serve `BBDown/Infrastructure/BBDownApiServer.cs:882` `SanitizeUntrustedOptions`；二者现统一调用 `RetryPolicy`，CLI 范围为 `1–100 / 0–600000ms`，serve 为 `1–3 / 0–5000ms`
- **处置**：`RetryPolicy.NormalizeForCli` 负责 CLI 验证，`NormalizeForServe` 负责 API 输入钳制；其它 serve 数值限制仍由各自安全边界处理。
- **工作量**：半天

---

### P2 — 低收益 / 长期项

| 编号 | 位置 | 说明 | 建议 |
|------|------|------|------|
| P2-1 | `BBDown.Core/Util/SubUtil.cs:540-541`（`SubTagRegex` 定义）/ `BBDown/Utilities/BBDownUtil.cs:437,439` | `SubTagRegex` 的 BCP-47 规范化已充分，正则已全部 `GeneratedRegex` | 维持现状，无需优化 |
| P2-2 | `BBDown.Core/Util/PathUtil.cs:26` | `ReservedNames` + `TrimEnd('.',' ')` + `maxBaseNameLength=100` 已覆盖 Windows 保留名/尾点空格/超长标题 | 维持现状 |
| P2-3 | `BBDown.Core/DRM/WvdDevice.cs` / `WidevineCdm.cs` | 内存中私钥处理 | 现状（第 13 轮核实）：`WidevineCdm` 已用 `CryptographicOperations.ZeroMemory`（4 处），`WvdDevice`/`WidevineCrypto` 未用（`WvdDevice` 私钥字节从不清零，仅 RSA 释放）；`DrmDecryptor`（`CkcDecryptor.cs:3`，对 `WidevineCdm.GetKeysAsync` 的薄封装）零测试是盲区，补 1 个向量用例 |
| P2-4 | `BBDown/Infrastructure/ExternalProcessRunner.cs` | 进程执行边界已加 5s 管道兜底与 `Kill(entireProcessTree:true)` | 维持现状（RF-22 探针已修复为 `CheckFFmpegDOVIAsync`） |
| P2-5 | `BBDown/Configuration/BBDownConfigParser.cs` | `BuildAliasMap` 反射扫描 `CommandOptionAttribute` | 可加静态缓存（已在 P0-1 拆分时一并处理），单独优化收益低 |
| P2-6 | 日志 | `Logger.cs` 静态文本日志 + `SensitiveDataMasker` 已覆盖全面 | `serve` 长驻场景可选 `Microsoft.Extensions.Logging` + JSON 行，便于上游收集 |

---

## 3. 已确认无需优化项（避免重复评估）

- `HTTPUtil` 客户端池隔离（9 个 `Lazy<HttpClient>` 实例支撑 6 个访问器属性：`_appHttpClient` / `_insecureAppHttpClient` / `_mediaHttpClient` / `_verifiedNoRedirectClient` 等）已正确隔离校验/不安全与重定向策略，不建议引入 `IHttpClientFactory`（会破坏现有 AOT 友好的静态池设计）。
- `Config.AsyncLocal<AppSettings>` 双写（`Config.cs:32` `Apply` 同时写 `_contextSettings` 与 `_settings`）是 `serve` 并发凭据隔离的正确实现，已有 `ConfigPropagationTests` 覆盖，不应重构为 `IConfigProvider`。
- `BBDownDownloadUtil` 的 `Interlocked` 原子进度 + `ArrayPool<byte>.Shared.Rent(256KB)` 已避免 LOH 与 `ConcurrentDictionary.Values` 快照开销，无需改为 `Channel`。

---

## 4. 路线图

| 阶段 | 工作 | 产出 | 依赖 |
|------|------|------|------|
| 第 1 周 | P0-3 依赖与供应链：清理未用包、CPM/RID lock files/CI 缓存、本地 AOT 警告收敛已落地；剩余第三方警告升级与 AOT locked restore 设计 | PR 1（部分完成） | 无 |
| 第 1 周 | P0-2 同步 IO 异步化 + P0-4 Sdk.Web 收敛 | PR 2 | 无 |
| 第 2–3 周 | P0-1 巨石拆分（`DownloadOrchestrator` + `IDownloadService`） | PR 3（大） | 需补回归用例 |
| 第 4 周 | P1-1 Fetcher 基类 + 快照用例 + P1-2 Parser 源生成 | PR 4 | PR 3 合入后 |
| 持续 | P1-3 CI 镜像升级 / P1-4 重试收敛 / P2-3 DRM 向量用例 | 小 PR | 按需 |

> 约束重申：`global.json`  pinned `10.0.300` / `PublishAot=true` / `dotnet format --verify-no-changes` 硬门禁 / `failSkips:true` / LF + UTF-8 + 4 空格（`.editorconfig`）在所有改动中保持。

---

## 5. 归档信息

- 归档位置：`docs/OPTIMIZATION_PLAN.md`（本文件）
- 关联文档：`docs/REVIEW_PLAN.md`（剩余修复排期）/ `docs/REVIEW_FINDINGS.md`（RF-1..RF-29 处置结论）/ `docs/MAINTENANCE_PLAN.md`（Parser 护栏与文档同步）
- 下一步：按路线图建分支（`refactor/` / `fix/` / `deps/` 前缀，Conventional Commits），每 PR 关联本文件对应 P 编号
