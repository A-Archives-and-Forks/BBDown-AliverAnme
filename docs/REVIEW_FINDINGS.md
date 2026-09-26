# 审查发现与建议跟踪（REVIEW_FINDINGS）

> **用途**：记录代码审查轮次中**经评估后需要决策 / 持续跟踪**的发现与建议（含已判定"不实施"的条目，避免后续重复评估）。
> **与 REVIEW_PLAN.md 互补**：`docs/REVIEW_PLAN.md` 跟踪"剩余未处理修复项"的排期；本文件跟踪"已分析、留有处置结论"的条目（采纳 / 技术债 / 维持现状 / 待议）。
> **创建时间**：2026-08-19（自 v1.6.14 起的审查轮次结项时建立）。

## 状态总览

| 编号 | 主题 | 评估级别 | 判定 | 状态 |
|------|------|---------|------|------|
| RF-1 | 分片扩展名判定一致性（`IsVideoClipPath`） | Low | 采纳（1 行修复） | ✅ 已修复（本轮） |
| RF-2 | CLI 命令层 Async-over-Sync 迁移 `AsyncCommand` | Medium（对应 REVIEW_PLAN I8） | 技术债，一次性批量重构 | ✅ 已修复（第 10 轮） |
| RF-3 | serve 已完成任务历史"无界堆积" | —（建议前提不成立） | 不实施（现有防护已覆盖） | ⭕ 维持现状 |
| RF-4 | Widevine 许可证请求跟随重定向 | Low（一致性） | 改进提议 | ✅ 已修复（第 9 轮） |
| RF-5 | ffmpeg `creation_time` 元数据格式文化敏感 | Low | 一行修复（InvariantCulture） | ✅ 已修复（第 9 轮） |
| RF-6 | mp4box `-itags` cover 值未走 EscapeString | Low（一致性） | 一行修复（补 EscapeString） | ✅ 已修复（第 9 轮） |
| RF-7 | 配置合并 cliHasUrl 把选项值误判为 URL | Low | 启发式收紧（仅扫描位置参数） | ✅ 已修复（第 9 轮） |
| RF-8 | FLV 跳过路径不清理封面/字幕/章节（残留累积） | Medium | 与 DASH 分支清理对齐 | ✅ 已修复（第 11 轮） |
| RF-9 | serve 认证失败限速字典无界增长 | Medium | 超上限按最后失败时间裁剪 | ✅ 已修复（第 11 轮） |
| RF-10 | serve 已完成任务溢出裁剪按完成顺序误删 | Low | 按 TaskCreateTime 保留最新 | ✅ 已修复（第 11 轮） |
| RF-11 | Parser 大会员回退硬编码域名 + 子串判定脆弱 | Medium | EpHost 配置化 + JSON message 解析 | ✅ 已修复（第 11 轮） |
| RF-12 | `BaseUrlRegex` 贪婪匹配误判 query 为端口 | Low | 正则收紧 | ✅ 已修复（第 11 轮） |
| RF-13 | 登录轮询跟随重定向缺逐跳校验（凭据外发面） | Low | NoRedirect + 每跳可信校验 | ✅ 已修复（第 11 轮） |
| RF-14 | 下载管线两级 catch 过滤器缺口（NotSupportedException/AggregateException 逃逸） | Medium | 采纳（改抛 InvalidOperationException + 过滤器补 AggregateException） | ✅ 已修复（第 12 轮） |
| RF-15 | serve 读端点无 Host 校验（DNS rebinding 读面） | Medium | 采纳（isApi 加 Host 回环白名单） | ✅ 已修复（第 12 轮） |
| RF-16 | 文档正确性族（wiki 退出码表虚构 2/3、CLI-Reference 缺 4 选项、README serve 列举不完整） | Medium（文档） | 采纳（修正文档） | ✅ 已修复（第 12 轮） |
| RF-17 | Parser 免二压重发吞用户取消（两处 catch 缺取消守卫） | Low | 采纳（补 OperationCanceledException 重抛守卫） | ✅ 已修复（第 12 轮） |
| RF-18 | 服务器可控 lan/audio_id 未净化直拼文件路径 + SubOnly ASS 改名 .srt | Low | 采纳（GetValidFileName 净化 + 按源扩展名改名） | ✅ 已修复（第 12 轮） |
| RF-19 | publishDate/videoDate 占位符 culture 敏感且替换值未净化 | Low | 采纳（InvariantCulture + GetValidFileName） | ✅ 已修复（第 12 轮） |
| RF-20 | 跳过路径清理一致性残留（锁内 Skipped 漏 coverPath、dash 跳过路径裸删） | Low | 采纳（与 flv 分支对齐） | ✅ 已修复（第 12 轮） |
| RF-21 | aria2c stdin input-file 换行注入面 | Low | 采纳（写前剔除 \r\n） | ✅ 已修复（第 12 轮） |
| RF-22 | 进程执行边界（探针未观察管道任务；成功路径 5s 兜底翻转成功） | Low | 部分采纳（探针改异步执行器；成功路径语义先确认） | ✅ 探针已修复（第 12 轮）；成功路径 ⭕ 维持现状 |
| RF-23 | mp4box 输出 `.muxing-{guid}` 未知扩展名兼容性 | Low | 待议（需 GPAC 实测后定） | ✅ 已修复（第 12 轮补遗：临时名补 .mp4 后缀） |
| RF-24 | SanitizeUntrustedOptions 漏 interactive | Low | 采纳（一行清零） | ✅ 已修复（第 12 轮） |
| RF-25 | 解析失败日志 option.Url 未单行化（两处） | Low | 采纳（补 SanitizeLogString） | ✅ 已修复（第 12 轮） |
| RF-26 | Core 解析/网络健壮性低危族（5 小项） | Low | 采纳（随批次逐项落地） | ✅ 已修复（第 12 轮） |
| RF-27 | FindBinaries 进程级静态工具路径（serve 并发理论面） | Low | 待议（倾向维持现状：Sanitize 已清零路径字段） | ⭕ 维持现状（第 12 轮定案） |
| RF-28 | HTTP 响应体无大小上限 | Low | 采纳（逐块读取设总量上限） | ✅ 已修复（第 12 轮） |
| RF-29 | .editorconfig 存量违规 4 文件（BOM/末尾换行） | Low | 采纳（重存文件；可补轻量检查） | ✅ 已修复（第 12 轮） |
| RF-30 | sub check 逐 aid 过滤器吞 `SubscriptionDataCorruptException`（历史清零→全量重下） | Medium | 采纳（补专用重抛守卫） | ✅ 已修复（第 13 轮消纳批） |
| RF-31 | `SortTracks` 裸 `Convert.ToInt32(v.id)` 异常穿透两级过滤器（整批中止） | Medium | 采纳（TryParse + 过滤器补 FormatException/OverflowException） | ✅ 已修复（第 13 轮消纳批） |
| RF-32 | sub check 用户取消语义违约（吞取消记失败→1 / 穿透→130，文档约定 0） | Medium | 采纳（补 token 守卫 + 方法级 OCE catch） | ✅ 已修复（第 13 轮消纳批） |
| RF-33 | API.md `DownloadTask` 字段清单缺 `ErrorMessage`/`SavePaths`（承诺的错误原因无字段可查） | Medium（文档） | 采纳（补 2 行） | ✅ 已修复（第 13 轮消纳批） |
| RF-34 | DOVI 探针 `Win32Exception` 未捕获（--skip-mux + 无 ffmpeg + 杜比视界→整批死） | Low | 采纳（过滤器补 Win32Exception 或 SkipMux 跳过探针） | ✅ 已修复（第 13 轮消纳批） |
| RF-35 | DRM 取钥链缺 `CancellationToken`（serve /cancel 最长约 6 分钟不可中断） | Low | 采纳（DrmDecryptor 透传 token） | ✅ 已修复（第 13 轮消纳批） |
| RF-36 | `ArticleCommand` 用弱净化 `SanitizeFileName`（Windows 保留名未防护→CON.md） | Low | 采纳（改 `GetValidFileName`） | ✅ 已修复（第 13 轮消纳批） |
| RF-37 | TV 登录轮询仍自动跟随重定向（RF-4/RF-13 收口族残留） | Low | 采纳（NoRedirect + 3xx 拦截） | ✅ 已修复（第 13 轮消纳批） |
| RF-38 | `ServeCommand` OCE 无 token 守卫（内部超时取消→退出码 0 掩盖失败） | Low | 采纳（补 when 守卫） | ✅ 已修复（第 13 轮消纳批） |
| RF-39 | CLI-Reference 弹幕格式文档错误（`protobuf` 示例不可用 + 默认值描述错） | Low（文档） | 采纳（改 `xml,ass` / 默认双格式） | ✅ 已修复（第 13 轮消纳批） |
| RF-40 | README `--show-all` 描述错误（实为展示所有分 P 标题） | Low（文档） | 采纳（对齐 MyOption） | ✅ 已修复（第 13 轮消纳批） |
| RF-41 | 模板文档缺 `<videoDate>` 占位符且计数 18→19 | Low（文档） | 采纳（补行 + 改计数） | ✅ 已修复（第 13 轮消纳批） |
| RF-42 | README serve 子选项表缺 `--notify-webhook`（与同页 :333 自相矛盾） | Low（文档） | 采纳（补一行） | ✅ 已修复（第 13 轮消纳批） |
| RF-43 | 非 DOVI 进程启动点 `Win32Exception` 穿透两级过滤器（第 13 轮遗留观察①定案） | Medium | 采纳（启动点规范化为 InvalidOperationException） | ✅ 已修复（第 14 轮消纳批） |
| RF-44 | `UnauthorizedAccessException` 不在两级过滤器 + 清理点只捕 IOException（单页本地权限错误中止整批） | Medium | 采纳（过滤器补 UA + 裸删/清理点对齐） | ✅ 已修复（第 14 轮消纳批） |
| RF-45 | 免二压重发降级丢失杜比/Hi-Res 音轨（RF-26 守卫旁支：列表重置与标记重置不同步） | Medium | 采纳（降级路径保持 pass0 列表或重置标记+去重） | ✅ 已修复（第 14 轮消纳批） |
| RF-46 | 直播录制 `KeyNotFoundException` 逃逸重连过滤器（畸形 live 响应终止整场录制，违背"不设重试上限"承诺） | Medium | 采纳（改 TryGetPropertySafe 逐级判空走瞬态退避） | ✅ 已修复（第 14 轮消纳批） |
| RF-47 | AppHelper `ArgumentException`/`InvalidProtocolBufferException` 穿透两级过滤器（RF-31 同族，SubUtil 姊妹接口已防） | Medium | 采纳（DoReqAsync 源头转译为 InvalidOperationException） | ✅ 已修复（第 14 轮消纳批） |
| RF-48 | `Page.bvid` getter 对服务器可控 aid 抛 AOORE（"0"/负数/超界穿透两级过滤器） | Medium | 采纳（getter 包 try/catch 回落原始 aid） | ✅ 已修复（第 14 轮消纳批） |
| RF-49 | `TimeoutException` 未入三处逐条降级过滤器（FavList/SpaceVideo/BuvidProvider；E1 类型统一后的旁支） | Medium | 采纳（三处补 TimeoutException + 修过时注释） | ✅ 已修复（第 14 轮消纳批） |
| RF-50 | `GetWebSourceCoreAsync`（携 SESSDATA）仍自动跟随重定向——NoRedirect 收口族（RF-4/13/37）凭据最重的漏网成员 | Low | 采纳（sendCookie 路径切 NoRedirect + 逐跳可信校验） | ✅ 已修复（第 14 轮消纳批） |
| RF-51 | RF-28 消纳缺口：普通响应体（GetWebSourceCoreAsync/AnonymousChecked）仍无 64MB 上限，登记记录需勘误 | Low | 采纳（两处改 ReadContentBoundedAsync + FINDINGS 勘误） | ✅ 已修复（第 14 轮消纳批） |
| RF-52 | Series/MediaList fetcher 先 GetPropertySafe 后查 code——精心编写的错误诊断不可达；MediaList 回退过滤器缺 KeyNotFoundException | Low | 采纳（取节点后移 + 过滤器补类型） | ✅ 已修复（第 14 轮消纳批） |
| RF-53 | `GetPropertySafe` 异常消息拼服务器可控"全部键名"清单（控制字符注入面 B3-L3 族 + 巨型消息） | Low | 采纳（键名过控制字符剥离/截断） | ✅ 已修复（第 14 轮消纳批） |
| RF-54 | serve/CLI 日志注入残留：RF-25 只收口 req.Url 调用点，UrlResolver 派生串（aidOri 含 CRLF）未脱敏落日志 | Low | 采纳（净化下沉到 ResolveAsync 返回前） | ✅ 已修复（第 14 轮消纳批） |
| RF-55 | webhook 域名空解析数组"校验空过"+ `addresses[0]` 越界——已成功任务误报"异常终止" | Low | 采纳（两侧补空数组分支 + 过滤器放宽） | ✅ 已修复（第 14 轮消纳批） |
| RF-56 | `SanitizeUntrustedOptions` 漏 `Area`：任意值拼进官方 API query + 跳过登录检查 | Low | 采纳（hk/tw/th 白名单回落 ""） | ✅ 已修复（第 14 轮消纳批） |
| RF-57 | `ToolFinder` 在 CWD 搜索 mp4decrypt/device.wvd（违背 FindExecutable 建立的可执行劫持信任边界） | Low | 采纳（CWD 移出搜索或复用 FindExecutable） | ✅ 已修复（第 14 轮消纳批） |
| RF-58 | `FormatSavePath` 轨道元数据占位符（dfn/res/fps/codecs/bandwidth）不过 GetValidFileName（RF-18 同族） | Low | 采纳（该分支统一净化） | ✅ 已修复（第 14 轮消纳批） |
| RF-59 | 登录轮询 3xx 无 Location 被误报为"重定向跳数超过上限"（单跳无目标≠超限） | Low | 采纳（无 Location 分支读 body 返回） | ✅ 已修复（第 14 轮消纳批） |
| RF-60 | `Audio.shortCodecs` 文化敏感 `ToUpper()`（tr-TR 查表失败静默退化选轨优先级） | Low | 采纳（ToUpperInvariant 一行） | ✅ 已修复（第 14 轮消纳批） |
| RF-61 | 文档族 6 项：archives.txt 文件名/位置 ×2、README 缺 `<videoDate>`、API.md 缺 413、忽略清单缺 configFile ×2、API.md 引用不存在的 serve `--work-dir`、"单行化"措辞 | Low（文档） | 采纳（随文档批消纳） | ✅ 已修复（第 14 轮消纳批） |
| RF-62 | DRM 取钥链 `CryptographicException` 穿透两级过滤器（RSA 二次解密无 catch + 过滤器白名单缺口 → 整批中止） | Medium | ⭕ **前提不成立**（消纳亲验：`WidevineCdm.GetKeysAsync:51` 已有 `catch (Exception) → return null` 吞掉该类，异常不出库） | ⭕ 维持现状（第 16 轮消纳：登记失实，无改动） |
| RF-63 | `FormatSavePath` 的 `res`/`fps` 占位符未过 `GetValidFileName`（RF-58 消纳缺口，与同处意图注释自相矛盾） | Low | 采纳（两行对齐 :63/:66 同构净化） | ✅ 已修复（第 16 轮消纳批） |
| RF-64 | 评论保存 catch 白名单窄于页面级过滤器（评论 API 超时 → 成功页被误判失败） | Low | 采纳（补 TimeoutException/AggregateException/UA） | ✅ 已修复（第 16 轮消纳批） |
| RF-65 | fetcher 顶层 `GetPropertySafe` 未经 `code` 先行检查——精心构造的中文诊断不可达（RF-52 同族残留 6 处） | Low | 采纳（对齐 code/TryGetProperty 守卫） | ✅ 已修复（第 16 轮消纳批） |
| RF-66 | `NoRedirectClient` 超时 1 分钟——超时矩阵唯一非 2 分钟项，RF-50 切池后头阶段上限隐性减半 | Low | 采纳（不变量对齐 `FromMinutes(2)`） | ✅ 已修复（第 16 轮消纳批） |
| RF-67 | PR CI `vulnerability-scan` 门禁失效：`dotnet list package --vulnerable` 退出码恒为 0，永不阻断 PR | Medium | 采纳（`--format=json` + jq 判定） | ✅ 已修复（第 16 轮消纳批） |
| RF-68 | `EntityTests` RF-60 回归测试假绿：输入 `"e-ac-3"` 不含 `'i'`，tr-TR 规则不触发，回退到 `ToUpper()` 测试仍通过 | Low（测试） | 采纳（改用含 `'i'` 的 codecs 串） | ✅ 已修复（第 16 轮消纳批） |
| RF-69 | 测试套件未隔离系统代理：`ServeApiHttpTests` 的 `HttpClient` 未设 `UseProxy=false`，本机代理在线时回环测试假红 | Low（测试） | 采纳（`SocketsHttpHandler { UseProxy = false }`） | ✅ 已修复（第 16 轮消纳批） |
| RF-70 | 日志注入旁支：`WatchLater`/`Live` 命令把服务器可控 `title`/`Uname` 未脱敏写入日志（RF-54 同族） | Low | 采纳（过 `SanitizeLogString`） | ✅ 已修复（第 16 轮消纳批） |
| RF-71 | `API.md` 时间戳字段标注"本机时区"——实现为 UTC 纪元秒（`ToUnixTimeSeconds()`），措辞误导 | Low（文档） | 采纳（改"UTC 纪元秒（与时区无关）"） | ✅ 已修复（第 16 轮消纳批） |
| RF-72 | `InvalidDataException` 穿透下载两级过滤器（RF-28/RF-51 新增的 64MB 上限与 gRPC 帧校验成为新的整批中止杠杆） | Medium | 采纳（过滤器补类型） | ✅ 已修复（第 16 轮消纳批） |
| RF-73 | 服务器可控 `aid`/`cid`/`epid` 未净化直拼工作区路径与 `<aid>`/`<cid>` 占位符（RF-18/RF-58 只净化了同表达式的叶子元数据） | Medium | 采纳（Page 属性 setter 单一收口） | ✅ 已修复（第 16 轮消纳批） |
| RF-74 | serve 401 日志 sink 未脱敏且未限速（未认证即可写 `bbdown-api.log`，可灌盘/伪造日志行） | Medium | 采纳（`TruncateForLog` 脱敏+截断；限速分支只记 IP） | ✅ 已修复（第 16 轮消纳批） |
| RF-75 | 假绿测试族：`ArchiveGranularityTests`/`DownloadProgressAggregationTests` 复刻实现的副本而非被测代码 | Medium | 采纳（抽生产 `ArchiveTracker`/`ProgressAggregator` 后测之） | ✅ 已修复（第 16 轮消纳批） |
| RF-76 | AOT 绑定防线只覆盖 3/10 个 Settings 类（子命令参数类型改动不会失败） | Medium | 采纳（补齐 10 个类型） | ✅ 已修复（第 16 轮消纳批） |
| RF-77 | `local-integration` 门禁可静默执行 0 个测试（ffmpeg 缺失时测试 early-return，job 仍绿） | Medium | 采纳（CI 断言 ffmpeg 存在） | ✅ 已修复（第 16 轮消纳批） |
| RF-78 | `WvdDevice.Load` 空文件抛 `IndexOutOfRangeException`（诊断退化，非崩溃） | Low | 采纳（补空文件分支） | ✅ 已修复（第 16 轮消纳批） |
| RF-79 | 有界响应体改造未覆盖 `WidevineCdm`（2 处）与 `BBDownLoginUtil`（2 处）的裸 `ReadAs*Async` | Low | 采纳（改 `ReadContentBoundedAsync`，已提为 public） | ✅ 已修复（第 16 轮消纳批） |
| RF-80 | fetcher 的服务器 `message` 未净化直拼异常消息落日志（B3-L3/RF-25 同族新实例） | Low | 采纳（过 `SanitizeServerText`） | ✅ 已修复（第 16 轮消纳批） |
| RF-81 | serve 下 `selectPage`/`danmakuFilter` 无接受上限且 `MaxExpandedPages` 仅按段（内存/CPU 放大 + 多 MB 日志行） | Low | 采纳（累计上限 + 日志截断 + serve 忽略弹幕过滤） | ✅ 已修复（第 16 轮消纳批） |
| RF-82 | 隐藏废弃开关可绕过 serve "FilePattern 已清零"不变量（当前不可穿越，但防线不密闭） | Low | 采纳（SanitizeUntrustedOptions 清零废弃开关） | ✅ 已修复（第 16 轮消纳批） |
| RF-83 | `/add-task` 队列满 429 缺 `Retry-After`（与 :195/:243 及代码注释自相矛盾） | Low | 采纳（补 `RetryAfter="60"`） | ✅ 已修复（第 16 轮消纳批） |
| RF-84 | 文档族 6 项：wiki API 样例虚构字段/状态、Authentication "退出码 2"、Danmaku "protobuf"、Subcommands/Home 漂移、wiki 缺 413/415 | Low（文档） | 采纳（随文档批消纳） | ✅ 已修复（第 16 轮消纳批） |
| RF-85 | Docker 配方挂载 BBDown 从不使用的路径（下载/凭据落容器可写层）+ token 走 CLI 参数 | Low | 采纳（改挂 `/app` 用 `BBDOWN_SERVE_TOKEN`） | ✅ 已修复（第 16 轮消纳批） |
| RF-86 | `DownloadTask.Snapshot()` 在锁外读 `Status`/`IsSuccessful`（与自身契约注释不符，可能返回短暂不一致快照） | Low | 采纳（整段入锁） | ✅ 已修复（第 16 轮消纳批） |
| RF-87 | CI 卫生：PR CI 从不构建 Docker 镜像；`build_latest.yml` 无 `concurrency` 组 | Low | 采纳（PR CI 加 docker smoke + concurrency） | ✅ 已修复（第 16 轮消纳批） |
| RF-88 | 测试假绿/名实不符：`WvdDeviceKeyTests` 用 `ThrowsAny<Exception>`；`WidevineCdmTests` 名含 Logs 却不断言日志 | Low（测试） | 采纳（改精确异常类型/改名） | ✅ 已修复（第 16 轮消纳批） |

---

## RF-1：分片扩展名判定一致性（`IsVideoClipPath`）

- **位置**：`BBDown/Infrastructure/BBDownDownloadUtil.cs` — 轨道分片类型判定（`.vclip`/`.aclip`），收敛后统一入口 `IsVideoClipPath(path)`。
- **来源建议**：仅匹配 `.mp4` 导致非 DASH 容器（FLV/MKV/TS 等）下视频分片被误判为音频 `.aclip`，建议扩展后缀表或改用显式轨道类型参数。
- **分析**：
  - 实际触发面接近零：`IsVideoClipPath` 的输入是**轨道最终产物路径**，BBDown 输出固定为视频 `xxx.mp4`、音频 `xxx.m4a`（`Download.cs:365-366`）；非 DASH 的 FLV 分段也命名为 `{i}.mp4`（`Download.cs:962`），分类正确；项目不存在非 mp4 视频输出选项（无 `--mkv`）。
  - **但核实发现第 6 处漏网**：`DownloadClipsAsync` 返回值构造处（原 844 行）仍用区分大小写的 `Path.GetExtension(path).EndsWith(".mp4")`，与 `IsVideoClipPath`（`OrdinalIgnoreCase`）行为不一致——是"统一 5 处大小写"重构时遗漏的一处。当前产品不会产出大写 `.MP4` 文件名故无运行时错位，属代码一致性问题。
- **结论**：采纳一行修复（走 `IsVideoClipPath`）；来源建议的"扩展后缀表"不采纳（对不存在的场景过度设计），未来若引入多容器输出，按建议后半句**改用显式轨道类型参数**而非格式猜测。
- **状态**：✅ 已修复（2026-08-19，844 行改为 `IsVideoClipPath(path)`，全库 5 处判定统一，无残留大小写敏感判断）。

---

## RF-2：CLI 命令层 Async-over-Sync 迁移 `AsyncCommand`

- **位置**：8 处命令 `Task.Run(...).GetAwaiter().GetResult()` — `LoginCommand`、`LoginTVCommand`、`ArticleCommand`、`LiveCommand`、`SubCommand`、`WatchLaterCommand`、`DefaultCommand`、`ServeCommand`；对应 REVIEW_PLAN I8。
- **来源建议**：迁移至 `Spectre.Console.Cli` 的 `AsyncCommand<TSettings>`（`ExecuteAsync`），消除线程池线程同步阻塞与潜在死锁/饥饿。
- **分析**：
  - **死锁面不存在**：BBDown 为纯控制台进程，无 `SynchronizationContext`，`GetResult()` 无死锁条件。
  - **饥饿面极低**：CLI 单次执行，每命令生命周期仅额外占用 1 个线程池线程；唯一长驻点是 serve 的 `StartServer`（serve 全程占 1 线程），线程池可伸缩无实际危害；serve 的并发请求处理本身为 async，不经过此路径。
- **结论**：方向正确（Spectre 官方推荐写法、占用更少线程），但定性应降为"高质量重构"而非隐患；建议作为**一次性批量重构**（8 命令 + 注册 + 保持 ExitCode 语义），不零散进行；不列入当前发版。
- **状态**：✅ 已修复（2026-08-29，第 10 轮，一次性批量落地）：
  - 迁移 **7** 个命令至 `AsyncCommand<TSettings>`（Login/LoginTV/Article/Live/SubCheck/WatchLater/Serve）。原清单列 8 处，核实 `DefaultCommand` 已是 `AsyncCommand`（清单漂移修正，实际迁移面 7 处）。
  - serve 链路：`BBDownApiServer.Run` 拆为同步前置校验 `ValidateListenUrl`（测试可用 `Assert.Throws` 同步断言、ServeCommand 快速失败路径保留同步异常语义）+ 真异步 `RunAsync`（`await app.RunAsync`，不再 `Task.Run` + `GetResult()` 让一个线程池线程阻塞整个服务生命周期）；`Program.StartServer` → `StartServerAsync`。关停段的 30s `Task.WaitAll` 有界同步等待**保留**：仅发生在进程退出路径，与生命周期阻塞不同，且避免 `WhenAll`+`WaitAsync` 改变超时/异常类型语义（代码注释说明）。
  - 各命令原有 catch/退出码语义**逐字保留**（cancel→0、超时→1、批量失败计数→1）。原建议中的 `ExitCodeFor` 评估后**不抽取**：四个命令的取消/超时/部分失败分支消息与过滤条件各不相同，强行共享 helper 会掩盖差异。
  - 计划外残留：`ExternalToolHelper` 一处 `GetAwaiter().GetResult()` 为短进程探针的 stdout/stderr 同步读取，非命令生命周期阻塞，维持现状。
  - 测试适配：`ServeApiHttpTests.RunningServer` 直用 `RunAsync`（去 `Task.Run` 包装）；`NonLoopbackListen_WithoutToken_Throws` 改断言 `ValidateListenUrl` 同步异常语义（真实回环启动路径由各 RunningServer 用例继续覆盖）。
  - **勘误（第 13 轮，RF-32）**：本条"逐字保留（cancel→0）"对 SubCheck 当时并不成立——SubCheckCommand 在第 13 轮前没有任何取消语义处理（Ctrl+C 被记为订阅失败/穿透全局 handler 返回 130）。已在第 13 轮消纳批补齐，与 watchlater/文档契约对齐。

---

## RF-3：serve 已完成任务历史"无界堆积"

- **位置**：`BBDown/Infrastructure/BBDownApiServer.cs` — `finishedTasks` 内存 + `bbdown-tasks.json` 落盘。
- **来源建议**：类比 `SubscriptionStore`（保留上限 5000）为已完成任务设定上限，阻止内存/快照/落盘线性增长。
- **分析**：**建议前提不成立**，现有防护已覆盖：
  - `MaxFinishedTasks = 1000`（:588）+ `FinishedTaskRetention = 30 天`（:589）。
  - `PersistFinishedTasks()` 每次序列化前在 `_persistLock` 内先执行 `TrimFinishedTasksLocked()`（清超龄 + `RemoveRange` 至 1000）（:610, :643-653）。
  - 三个副作用断言（内存增长 / 快照耗时 / 磁盘线性增加）均被 bound 在 ≤1000 条 + 30 天，不随运行时长无界增长。
- **真实残留（有界、可选优化）**：`PersistFinishedTasks` 有 10 个调用点，每次任务完成/删除/关停**全量序列化 ≤1000 条写 tmp + 原子替换**；高频批量任务下属固定 O(1000)/次的非必要 IO。优化方向为**写盘节流**（dirty 标记 + 延时合并，如 30s 窗口多次变更只落盘一次）。
- **结论**：按原文重复"加上限"不采纳；写盘节流列为可选低优先级优化，待有实测压力数据或用户反馈后再评估，不做预防性优化。
- **状态**：⭕ 维持现状（现有上限有效；节流优化列为可选后续项）。

---

## RF-4：Widevine 许可证请求跟随重定向（一致性提议）

- **位置**：`BBDown.Core/DRM/WidevineCdm.cs` — `SendRequestAsync` 使用 `HTTPUtil.VerifiedAppHttpClient`（`AllowAutoRedirect = true`）。
- **发现**：携带设备签名 `challenge` 的许可证 POST 会随 3xx 重放 body；与本轮 B3-F2"凭据载荷禁跟随重定向"原则（gRPC POST 已改用 `NoRedirectClient` 显式拦 3xx）不一致。
- **定性**：预存问题（非本批引入）；实际可利用性极低 —— 恒校验 TLS 排除 MITM、`LicenseUrl` 为硬编码可信端点、服务器若被攻破可直接伪造响应无需重定向（无增量攻击面）。
- **结论**：为原则一致性建议改为禁重定向客户端并显式处理 3xx（与 gRPC POST 同构收口）。副作用极小，可随任意后续安全批次一并落地。
- **状态**：✅ 已修复（2026-08-29，第 9 轮）：新增 `HTTPUtil.VerifiedNoRedirectClient`（始终校验证书 + `AllowAutoRedirect=false`，独立池不受 `--insecure` 降级），`WidevineCdm.SendRequestAsync` 切换并在 3xx 显式拦截报错（状态码 <500 不满足重试谓词，按确定性失败立即抛出）。新增 `VerifiedNoRedirectClientTests` 3 例：身份稳定性（不随 SkipSslCheck 路由）、GET 307 不跟随（对照自动跳转客户端跟随）、POST+body 307 不重放。

---

## RF-5：ffmpeg `creation_time` 元数据格式文化敏感

- **位置**：`BBDown/Infrastructure/BBDownMuxer.cs:375` — `$"creation_time={DateTimeOffset.FromUnixTimeSeconds(pubTime):yyyy-MM-ddTHH:mm:ss.ffffffZ}"`。
- **发现**：字符串插值的自定义日期格式默认按 CurrentCulture 解析，其中 `:` 是"时间分隔符"占位符而非字面字符。在时间分隔符非 `:` 的区域设置（如 fi-FI 用 `.`）下，产出的 ISO-8601 时间戳形如 `2026-08-29T19.30.00.000000Z`，ffmpeg 的 `av_parse_time` 无法按 ISO-8601 解析，发布时间元数据静默丢失/告警。
- **定性**：预存问题（上游继承）；影响面窄（仅 `--sub-only` 之外的正常混流且区域设置特殊的用户），仅元数据丢失不损坏流。
- **核实排他性**：全库扫描其余 `yyyy-MM-dd HH:mm` 用法均为日志/控制台展示或文件名场景，文化敏感可接受（或无分隔符安全）；仅此一处喂给机器可读协议。
- **结论**：一行修复——格式化追加 `CultureInfo.InvariantCulture`（与 277b138 批次的"文化不变解析"原则同构收口）。
- **状态**：✅ 已修复（2026-08-29，第 9 轮）：`DateTimeOffset.FromUnixTimeSeconds(pubTime).ToString("yyyy-MM-ddTHH:mm:ss.ffffffZ", CultureInfo.InvariantCulture)`，全库唯一喂给机器可读协议的时间戳收口。

---

## RF-6：mp4box `-itags` cover 值未走 EscapeString

- **位置**：`BBDown/Infrastructure/BBDownMuxer.cs:146` — `metaArg.Append($":cover=\"{pic}\"")`。
- **发现**：`MuxByMp4box` 顶部对 desc/title/episodeId/author/lang 统一 `EscapeString`（依据其注释：mp4box itags 值内 `"` 与 `\` 必须转义），但同为 itags 值的 `pic`（封面图本地路径）未转义。Windows 路径天然含 `\`（如 `C:\Users\...\cover.jpg`），按代码自述规则会被 mp4box 当转义序列消费，杜比视界自动切 mp4box 的场景下封面可能静默丢失。
- **定性**：预存问题（上游继承）；mp4box 对裸 `\` 的实际容忍度未实测（GPAC 解析器可能宽松），故定 Low/一致性而非确认缺陷。
- **结论**：为与同函数其它 itags 值的转义规则保持一致，补 `EscapeString(pic)` 即可（`EscapeString` 对正常路径无副作用——仅翻倍 `\` 与 `"`）。
- **状态**：✅ 已修复（2026-08-29，第 9 轮）：`metaArg.Append($":cover=\"{EscapeString(pic)}\"")`，与同函数顶部 desc/title/episodeId/author/lang 的转义规则一致。

---

## RF-7：配置合并 cliHasUrl 把选项值误判为 URL

- **位置**：`BBDown/Configuration/BBDownConfigParser.cs:136` — `bool cliHasUrl = cliArgs.Any(a => UrlLikeToken().IsMatch(a))`。
- **发现**：判定"命令行是否已显式给出 URL"时扫描**全部** argv（含选项的值）。误报场景：URL 写在 `BBDown.config` 中、命令行携带值形似 URL 的选项（如 `--aria2c-proxy http://127.0.0.1:7890`、`--work-dir av123`）→ `cliHasUrl` 误判为 true → 配置文件里的 URL 位置参数被丢弃 → Spectre 报缺少必填参数，用户难以定位。
- **定性**：预存启发式的精度问题；触发需要"URL 在配置文件 + 命令行恰好有 URL 形值选项"的组合，真实概率低。
- **结论**：收紧方向——只对"首个非选项 token"（Spectre 位置参数的位置）应用 UrlLikeToken，或先按 aliasMap 跳过带值选项再扫描（`IsSubCommandInvocation` 已有同构跳过逻辑可复用）。改动属行为微调，建议带回归用例（CLI 传 `--aria2c-proxy http://...` + 配置含 URL）单独落地。
- **状态**：✅ 已修复（2026-08-29，第 9 轮）：新增 `GetPositionalTokens`（与 `IsSubCommandInvocation` 同构：带值选项吞下一 token、bool 开关与 `--opt=value` 不吞），`cliHasUrl` 只对位置参数应用 UrlLikeToken。`ConfigMergeTests` 新增 3 例回归：`--aria2c-proxy` URL 形值/`--work-dir` av123 形值不再压制配置文件 URL（配置 URL 与选项值均正确合并）、位置参数提取器跳值语义。

---

## RF-8：FLV 跳过路径不清理封面/字幕/章节（残留累积）

- **位置**：`BBDown/Application/Download.cs` — FLV 分支"文件已存在跳过"路径（约 :947-958）。
- **发现**：DASH 分支的跳过清理（:683-704）会清理本次已下载的封面/字幕/章节并删除空 aid 目录；FLV 分支的跳过路径**只**尝试删空目录，不清理封面/字幕/章节。用户重跑已下载视频时，封面/字幕/章节文件在 aid 工作目录反复累积，且目录非空导致空目录删除逻辑永远不触发。
- **定性**：真实残留累积（每重跑一次多一套文件）；非安全/数据损坏问题。
- **核实附带发现**：清理章节用固定名 `chapters`（`Path.Combine(dir, "chapters")`），而 muxer 写入的是**唯一名** `chapters-{basename}`（`BBDownMuxer.cs:136,324`，防并发混流互相覆盖）——旧清理路径根本删不到实际写入的文件，属预存不一致（`BBDownMuxer` 自身 finally 会清理自己的产物，但跳过路径不经过 muxer）。
- **结论**：收敛为 `Program.DeleteResidualChapterFiles(dir)`：按 `chapters*` 前缀匹配两种命名兜底清理；单文件清理失败静默（IO/句柄异常不掩盖主流程结果）。FLV 跳过路径补封面/字幕清理，与 DASH 分支对齐；fastSkipChecked 跳过路径（:208-223）补章节清理。
- **状态**：✅ 已修复（2026-08-30，第 11 轮）+2 测试（前缀清理含固定名/唯一名/不误删其它文件；目录缺失不抛）。

---

## RF-9：serve 认证失败限速字典无界增长

- **位置**：`BBDown/Infrastructure/BBDownApiServer.cs` — `IsAuthLockedOut` / `_authFailures`（:49, :511）。
- **发现**：`IsAuthLockedOut` 的字典清理只移除**窗口过期**条目（`now - last > 1min`）。攻击者用大量一次性 IP/XFF 值轰炸时，每条都是"最近失败"永不过期——仅删过期条目约束不住字典大小，字典随攻击 IP 数线性增长（内存 DoS）。
- **定性**：Medium（攻击者可控输入面的无界增长；需要持续伪造新 XFF 值，但 serve 已放行 `--trusted-proxy` 场景下 XFF 由代理注入，攻击者不可直接控制——真实触发需攻击者能控制直连来源 IP 或代理透传，触发面中低，但修复成本极低）。
- **结论**：超过 `MaxTrackedAuthFailureIps` 时先清过期，仍超限则按最后失败时间裁剪回上限（保留最近活跃的 N 条）。O(n log n) 仅在异常规模触发，正常路径零开销。
- **状态**：✅ 已修复（2026-08-30，第 11 轮）+1 测试（反射验证 1.2 倍上限独立 IP 轰炸后字典 ≤ 上限+1）。

---

## RF-10：serve 已完成任务溢出裁剪按完成顺序误删

- **位置**：`BBDown/Infrastructure/BBDownApiServer.cs` — `TrimFinishedTasksLocked`（:675）。
- **发现**：`finishedTasks` 列表按**完成顺序**追加（`finishedTasks.Add`），与 `TaskCreateTime`（创建顺序）无关。旧实现溢出时 `RemoveRange(0, count - MaxFinishedTasks)` 删除列表头部——若某任务"后创建但先完成"排在头部，会被误删，而更旧的尾部任务被保留，与"保留最新任务"的意图相反。
- **定性**：Low（需任务完成顺序与创建顺序显著倒挂才可见；真实场景批量并发任务下确有概率）。
- **结论**：溢出裁剪改为按 `TaskCreateTime` 排序，仅移除最旧创建的溢出条目，保留其余任务原顺序（API 按完成顺序展示）。
- **状态**：✅ 已修复（2026-08-30，第 11 轮）+1 测试（构造"后创建先完成"在头部的列表，验证保留最新创建、裁剪最旧创建）。

---

## RF-11：Parser 大会员回退硬编码域名 + 子串判定脆弱

- **位置**：`BBDown.Core/Parser.cs` — 大会员回退（:87-101）。
- **发现**（两处）：
  - 回退抓取网页源硬编码 `https://www.bilibili.com/bangumi/play/ep{epId}`，忽略 `Config.Current.EpHost`（镜像站/BiliPlus 配置）——镜像站用户该回退必然失败（被重定向回可能不可达的官方域名）。
  - 大会员判定用裸子串 `webJson.Contains("\"大会员专享限制\"")`——B 站改文案即失效（历史上文案从"大会员专享限制"演进来过）。
- **定性**：Medium（镜像站用户的真实功能缺陷 + 文案漂移脆弱性）。
- **结论**：回退 host 跟随配置（默认配置行为逐字节不变：`EpHost == "api.bilibili.com"` 时用 `www.bilibili.com`，否则用配置的镜像主机）；判定改解析 JSON 根 `message` 字段（`code:-10403, message:大会员专享限制`），非 JSON 响应（风控 HTML）才回退子串兜底。
- **状态**：✅ 已修复（2026-08-30，第 11 轮）+2 测试（`IsVipRestrictedResponse` JSON message 5 例 + 非 JSON 兜底 2 例）。

---

## RF-12：`BaseUrlRegex` 贪婪匹配误判 query 为端口

- **位置**：`BBDown.Core/Parser.cs` — `BaseUrlRegex`（:751）。
- **发现**：原正则 `http.*:\d+` 未锚定 scheme 后立即匹配主机:端口，会把 `http://host/path?x=1:2` 这类 URL 的 query 中 `:数字` 误判为端口——若该 URL 实际无端口，基址推导错误（虽然实际使用中 CDN URL 通常带端口，但 query 参数含时间戳/签名时可能误判）。
- **定性**：Low（当前使用点 `PickTrackBaseUrl` 的输入为合法 CDN URL，query 带 `:数字` 的场景罕见；正则语义与"提取主机:端口"意图不符是确定性代码缺陷）。
- **结论**：收紧为 `^https?://[^/:]+:\d+`（锚定起点、主机段不含 `/`/`:`、冒号后必须数字）。
- **状态**：✅ 已修复（2026-08-30，第 11 轮）+1 测试（6 例：带端口匹配 / query `:数字` 不误判 / 无端口不匹配 / 缺 scheme 不匹配）。

---

## RF-13：登录轮询跟随重定向缺逐跳校验（凭据外发面）

- **位置**：`BBDown.Core/Util/HTTPUtil.cs` — `GetWebSourceWithSetCookiesAsync`（:217）。
- **发现**：登录轮询（扫码后轮询二维码状态）携带操作者 Cookie 且响应 `Set-Cookie` 是新凭证下发通道，却使用自动跟随重定向的 `AppHttpClient`——被攻破的 passport 域名或开放重定向可把带凭据的请求与响应 `Set-Cookie` 凭证引向任意主机。与同文件 `GetWebSourceAnonymousCheckedAsync`（匿名逐跳校验）、B3-F2 gRPC POST、RF-4 Widevine 的收口原则不一致。
- **定性**：Low（纵深防御缺口；入口 URL 为硬编码可信 passport 端点，无当前可利用面；原则一致性修复）。
- **结论**：改用 `NoRedirectClient` 手动逐跳，每跳 Location 在发起下一跳前必须通过 `IsTrustedCookieHost`，上限 `MaxRedirectHops=10`；与现有 `GetWebSourceCoreAsync` 的 5xx 重试/时钟校准语义保持一致。
- **状态**：✅ 已修复（2026-08-30，第 11 轮）+2 测试（非可信重定向抛错且不访问下一跳 / 可信同主机重定向跟随成功返回 body）。

---

## RF-14：下载管线两级 catch 过滤器缺口（NotSupportedException/AggregateException 逃逸）

- **位置**：`BBDown/Infrastructure/BBDownDownloadUtil.cs:797-801`（抛出点）、`BBDown/Application/Download.cs:1067`（页面级重试过滤器）、`Download.cs:94`（批级失败上报过滤器）。
- **发现**：多线程下载路径在"服务器以 200 响应多线程 Range 请求"时**刻意抛出** `NotSupportedException`（真实可发生的运行时条件：某 CDN 忽略 Range），`:799-801` 还原样重抛 `ArgumentException`；但页面级与批级两级过滤器白名单均只有 `HttpRequestException or JsonException or IOException or InvalidOperationException or TimeoutException (or TaskCanceledException)`。`Parallel.ForEachAsync` 多分片同时失败聚合出的 `AggregateException` 同样不在内（`IsSizeArtifactFailure` 在 :914-916 专门处理过它，证明作者知晓其存在）。异常经 `:830/:837` `ExceptionDispatchInfo` 原样重抛，途中不会被包装。
- **影响**：多 P 批量中一 P 命中即穿透 `DownloadPagesAsync` 的 foreach，剩余分 P 全弃、`NotifyWebhook` 与 `failedPages` 汇总全丢、退出码语义与单 P 失败不一致——与管线精心建立的"单 P 失败隔离"设计矛盾。`EnsureToolAvailable` 的注释（BBDownMuxer.cs:56-59）记录了同构问题（FileNotFoundException 当时正是为此改抛 InvalidOperationException），`NotSupportedException` 是漏网的同族。
- **结论**：采纳修复，三选一（按侵入度递增）——(a) :797 改抛 `InvalidOperationException`（与 EnsureToolAvailable 同一先例，最小改动）；(b) 扩充两级过滤器加入 `NotSupportedException or ArgumentException or AggregateException`（AggregateException 拆包取首个 InnerException 判断）；(c) 在 MultiThreadDownloadCoreAsync 抛出点统一规范化为页面级白名单类型。建议 (a) + 过滤器补 AggregateException。
- **状态**：✅ 已修复（2026-08-30，第 12 轮消纳批）：采纳 (a)——BBDownDownloadUtil 抛出点改 `InvalidOperationException`（消息不变），两级过滤器补 `AggregateException`；既有测试无 NotSupportedException 断言（grep 零命中）无需适配。
---

## RF-15：serve 读端点无 Host 校验（DNS rebinding 读面）

- **位置**：`BBDown/Infrastructure/BBDownApiServer.cs:171-216`（中间件：只对写端点校验 Origin，:172 注释明确豁免读端点）、`:238-279`（读端点返回完整快照）、`BBDown/Application/Download.cs:211`（`AddSavePath(savePath)` 存入 `PathUtil.ResolveWorkPath` 解析后的**绝对路径**）。
- **发现**：默认部署（回环监听、无 token）下，`/get-tasks*` 响应含 SavePaths（服务器绝对文件路径）、标题、URL、错误消息，但无任何 Host 头校验（全仓库无 AllowedHosts 过滤，Kestrel 默认 `AllowedHosts=*`）。攻击者网页经 DNS rebinding（攻击者域名 → 127.0.0.1）后，页面源即 `http://evil.com:23333`，对 `/get-tasks` 的 GET 是"同源"请求——不携带 Origin（fetch 同源 GET 不发 Origin），写端点的 Origin 校验对读端点不生效，且单加 Origin 校验也堵不住。写端点本身无恙：POST 必带 Origin（Fetch 规范非 GET/HEAD 必发），被 `IsLoopbackOrigin` 拦截。
- **定性**：Medium（信息泄露面真实；前提是用户浏览器访问攻击者页面 + 本机 serve 正在运行，泄露的是文件系统布局与任务元数据，非凭据）。
- **结论**：在 isApi 分支增加 **Host 头白名单**（`context.Request.Host.Host` 必须解析为回环地址或 localhost，否则 403/404）——curl/脚本直连 127.0.0.1/localhost 不受影响，同时封死 rebinding 的读写两条路；作为纵深也可给读端点加"非回环 Origin 即 403"，或将 `SavePaths` 从无 token 模式的响应中脱敏。
- **状态**：✅ 已修复（2026-08-30，第 12 轮消纳批）：无 token 时 `isApi` 强制 Host 为字面回环（新增 `IsLoopbackHost`，localhost/127/8/::1，刻意不做 DNS 解析），有 token 时跳过校验保反代部署；+3 端点测试（evil Host 403 读写两路 / 回环放行 / 带 token 跳过校验）+1 纯函数测试。
---

## RF-16：文档正确性族（wiki 退出码表 / CLI-Reference 缺项 / README 列举不完整）

- **位置**：`docs/wiki/CLI-Reference.md:112-121`（退出码表）、`:1,:27`（自称"完整参数详解/完整参数速查总表"）、`README.md:333`。
- **发现**（三处）：
  - 退出码表声称 `2 = Permission Denied（充电专属视频）`、`3 = Tool Missing`，并把"用户主动 Ctrl+C"归入 `0`。但全仓库 `return 2/3;`、`Environment.Exit(2/3)` **零命中**：充电专属无 `--allow-preview` 时走 `Download.cs:489-493` 的 `return false`（跳过分 P，进程正常 0 退出）；工具缺失（FindBinaries 的 FileNotFoundException）走全局 handler 退出 **1**；默认命令 Ctrl+C 返回 **130**（`Program.cs:159-164`），仅 serve/live 等子命令 catch OCE 返回 0。依赖退出码做自动化判断（CI/脚本包装器）的用户会误判。
  - CLI-Reference 对照 MyOption 65 个选项，反引号精确匹配差集为 `--host`、`--ep-host`、`--tv-host`、`--area`（INTL/TV 端点覆盖选项）4 项缺失；README"核心参数速查"同样缺。
  - README:333 serve 配置注入说明括号内仅列 `-l`/`--max-concurrent`/`--serve-token`，而 `ServeSettings` 还有 `--trusted-proxy`、`--notify-webhook`（同页 :157-158 表格已列出，前后不一致）。
- **结论**：采纳修正——(a) 退出码表删除虚构的 2/3 行并改写 Ctrl+C 归属（或实现退出码 2/3，需先定语义）；(b) 补 4 选项行或把标题/总表口径改为"常用参数"；(c) README:333 补全或改"等选项"。
- **状态**：✅ 已修复（2026-08-30，第 12 轮消纳批，文档修正）：退出码表删除 2/3 行、Ctrl+C 改为主命令 130 / 子命令 0、工具缺失归入 1；补 `--host`/`--ep-host`/`--tv-host`/`--area` 4 行（语义取自 MyOption Description）；README serve 选项列举补全。

---

## RF-17：Parser 免二压重发吞用户取消（两处 catch 缺取消守卫）

- **位置**：`BBDown.Core/Parser.cs:324`、`:526`（免二压重新请求的 catch 过滤器）。
- **发现**：两处 `catch (Exception ex) when (ex is ... or TaskCanceledException)` 无 `!token.IsCancellationRequested` 守卫。`:518-519` 注释断言"真正的用户取消（OperationCanceledException，非 TaskCanceledException）不被过滤器捕获"——**前提是错的**：`HttpClient.SendAsync` 在用户 token 取消时抛的正是 `TaskCanceledException`（OperationCanceledException 的子类）。Ctrl+C / serve 关停落在重发请求窗口会被吞掉、记为"降级沿用第一轮结果"，继续走完解析并产生误导性 Warn。项目既有正确模式（`Download.cs:1159`、`UrlResolver.cs:239`、`FavListFetcher.cs:108`）都是先 `catch (OperationCanceledException) when (token.IsCancellationRequested) throw;` 或在过滤器补 `!token.IsCancellationRequested`，唯独这两处漏了。
- **结论**：采纳一行修复——两个 catch 前插入取消重抛守卫（或在 TaskCanceledException 分支加 `&& !token.IsCancellationRequested` 语义）。
- **状态**：✅ 已修复（2026-08-30，第 12 轮消纳批：代码修复 + 回归测试 + CHANGELOG 未发布条目）。

---

## RF-18：服务器可控 lan/audio_id 未净化直拼文件路径 + SubOnly ASS 改名 .srt

- **位置**：`BBDown.Core/Util/SubUtil.cs:247,286,319,357,407`、`BBDown.Core/Parser.cs:502`、`BBDown/Application/Download.cs:444-445`。
- **发现**（两处）：
  - `lan`（`lang_key`）、`audio_id` 全部来自响应体（B 站接口 / 镜像站 EpHost / `--insecure` 下的中间人——后两者正是本项目在其他防御中明确采纳的对抗源），未净化直接进 `PathUtil.ResolveWorkPath($"{aid}/{aid}.{cid}.{lan}...")`；`ResolveWorkPath` 只做 Combine 不过滤（含 `..` 与分隔符原样保留），标题类文本都走了 `GetValidFileName`（InvalidChars 含 `/`、`\`），此处是缺口——恶意 `lang_key` 含 `..\` 可把字幕写出 workDir 之外。
  - SubOnly 分支无条件 `Path.ChangeExtension(_outSubPath, $".{s.lan}.srt")`：ASS 内容字幕（按 URL 形态落盘为 `.ass`）被改名为 `.srt`，播放器无法渲染。`Download.cs:444` 处 `s.lan` 同样未净化进最终产物路径。
- **结论**：采纳——对 `lan`/`audio_id` 应用 `PathUtil.GetValidFileName`（或白名单 `[A-Za-z0-9_-]`）后再拼路径；SubOnly 按源文件扩展名决定目标扩展名（`.ass` → `.{lan}.ass`）。
- **状态**：✅ 已修复（2026-08-30，第 12 轮消纳批：代码修复 + 回归测试 + CHANGELOG 未发布条目）。

---

## RF-19：publishDate/videoDate 占位符 culture 敏感且替换值未净化

- **位置**：`BBDown/Program.cs:48`（`ToString(format)` 未指定 culture）、`BBDown/Application/PathHelper.cs:67-68`（替换值未过 `GetValidFileName`，对照 :51/:54/:58 的 title/pageTitle/ownerName 都过了）。
- **发现**：`<publishDate:...>`/`<videoDate:...>` 的自定义格式串是用户输入，格式化用 CurrentCulture（`:` 是"时间分隔符"占位符而非字面字符）：en-US 下 `<publishDate:yyyy-MM-dd HH:mm:ss>` 产出含 `:` 的串直接进 savePath——Windows 上可写入 NTFS 备用数据流（`File.Exists` 为真但资源管理器不可见）；fi-FI 等区域设置下 `:` 被替换为本地分隔符导致跨机器产物路径漂移。格式非法串有 FormatException 兜底，但 `:` 产出的是合法格式化结果，兜不住。
- **结论**：采纳——`FormatTimeStamp` 加 `CultureInfo.InvariantCulture`（与 RF-5 的 creation_time 收口同构）；对 publishDate/videoDate 的替换值再过一次 `GetValidFileName`。
- **状态**：✅ 已修复（2026-08-30，第 12 轮消纳批：代码修复 + 回归测试 + CHANGELOG 未发布条目）。

---

## RF-20：跳过路径清理一致性残留（锁内 Skipped 漏 coverPath、dash 跳过路径裸删）

- **位置**：`BBDown/Application/Download.cs:208-225`（锁内权威 Skipped 分支）、`:709`（dash 快速跳过删封面）及 `:613,618,637,459,688`（dash 分支其余裸删）。
- **发现**（两处）：
  - 锁内 Skipped 分支清理了 videoPath/audioPath/字幕/音频素材/章节并删空 aid 目录，但**漏了 `coverPath`**——两条锁外快速跳过路径（:709、:976）都删封面，唯独这条锁内权威跳过不删 → 每次走此路径 aid 目录残留一张封面、永远非空删不掉。
  - dash 分支跳过/提前返回路径的多处裸 `File.Delete`/`Directory.Delete` 无 try/catch，而 flv 分支对应位置（:976-977、:939、:962、:988）全部包了 `catch (IOException or UnauthorizedAccessException)`——封面恰被杀软/索引器持有时裸删抛 IOException → 进入页面级重试（过滤器含 IOException）→ 整页（含已存在产物判定）重跑，占用持续则重试耗尽、已完成的分 P 被记为失败。dash 分支是 RF-8 修复族里漏网的一致性问题。
- **结论**：采纳——锁内 Skipped 分支补 coverPath 清理；dash 分支裸删统一改用 flv 分支同款包裹（或收敛到统一清理函数）。
- **状态**：✅ 已修复（2026-08-30，第 12 轮消纳批：代码修复 + 回归测试 + CHANGELOG 未发布条目）。

---

## RF-21：aria2c stdin input-file 换行注入面

- **位置**：`BBDown/Infrastructure/BBDownAria2c.cs:45-50`。
- **发现**：aria2c `--input-file=-` 语法是"URI 行 + 缩进行为选项行"，而写入 stdin 的 URL（来自 API 响应的 CDN 地址）与 Cookie（操作者配置）未剔除 `\r`/`\n`——包含换行的 URL 可注入任意新指令行（新 URI + `  dir=`/`  all-proxy=` 等）。合法的 `  dir=`/`  out=` 行在注入点之后会对后续 URI 重新生效，实际危害以行为扰动/自 DoS 为主，但注入面真实存在，与项目"参数一律走 ArgumentList/stdin 防注入"的总体思路不符。
- **结论**：采纳——URL 与 Cookie 写入前剔除 `\r`/`\n`（一行防御，正常输入零影响）。
- **状态**：✅ 已修复（2026-08-30，第 12 轮消纳批：代码修复 + 回归测试 + CHANGELOG 未发布条目）。

---

## RF-22：进程执行边界（探针未观察管道任务；成功路径 5s 兜底翻转成功）

- **位置**：`BBDown/Utilities/ExternalToolHelper.cs:26-33`、`BBDown/Infrastructure/ExternalProcessRunner.cs:90-92`。
- **发现**（两处）：
  - `CheckFFmpegDOVI` 用 `WaitForExit(5000)` + `outTask.GetAwaiter().GetResult()` 同步探针（RF-2 迁移时的"短进程探针例外"，但杜比视界命中时每 P 最多卡 5 秒）；超时分支 `process.Kill(true); return false;` 直接返回，`outTask`/`errTask` 未被观察——Kill 后管道断裂可能成为 UnobservedTaskException（ExternalProcessRunner/Decrypt 同类问题都做了观察兜底，此处没有）。
  - `ExternalProcessRunner` 进程成功退出后 `await Task.WhenAll(pipeTasks).WaitAsync(5s)`——子进程若（罕见地）派生继承 stdout 句柄的孙进程，管道 EOF 超过 5 秒即抛 TimeoutException 进 catch 重抛，混流退出码 0 的成功结果被误报为失败。注释表明作者知晓此权衡，但成功路径与失败路径共用同一超时，语义上把"输出句柄未关"等同于"执行失败"。
- **结论**：采纳（探针部分）——`CheckFFmpegDOVI` 改真异步（`WaitForExitAsync` + `WaitAsync` 5s 超时，改名 `CheckFFmpegDOVIAsync`），超时分支 Kill 整树后补观察 stdout/stderr 管道任务（防 UnobservedTaskException），调用点 `Download.cs` 同步改 await。成功路径 5s 管道兜底**维持现状**：代码注释已说明"管道任务异常应向上传播"的权衡，正常场景 ffmpeg 不派生继承句柄的孙进程，避免为不存在的场景加分支。
- **状态**：✅ 探针部分已修复（2026-08-30，第 12 轮消纳批）；ExternalProcessRunner 成功路径 ⭕ 维持现状（有意设计，注释在位）。

---

## RF-23：mp4box 输出 `.muxing-{guid}` 未知扩展名兼容性（需实测）

- **位置**：`BBDown/Application/Download.cs:236,240`（混流事务化临时名）、`BBDown/Infrastructure/BBDownMuxer.cs:184`（mp4box 分支把该路径直接作为 `-new` 输出参数）。
- **发现**：混流事务化把输出统一改为 `savePath + ".muxing-{guid:N}"`。ffmpeg 用 `-f mp4` 强制格式不受影响；但 GPAC 按扩展名推断输出封装格式，`.muxing-xxx` 属未知扩展名——旧版 GPAC（gf_isom_open 直写）无碍，较新的 filter-based MP4Box 行为随版本而异（可能告警回退 mp4，也可能直接失败）。仓库内无针对此的测试或注释；若目标 GPAC 版本严格，则所有 mp4box 路径（`--use-mp4box` 与杜比视界自动切换）都会失败。
- **结论**：待议——先在装有 GPAC 的环境实测确认；若不兼容，让 mp4box 分支输出到 `Path.ChangeExtension(muxingPath, ".mp4")` 的临时名（保持唯一性）。
- **状态**：✅ 已修复（2026-08-30，第 12 轮补遗）：临时名改为 `.muxing-{guid:N}.mp4`——不再依赖 GPAC 对未知扩展名的容忍度，新旧版本全部确定性走 ISOM 封装（ffmpeg 分支本就用 `-f mp4` 强制格式，不受影响）；`muxingPath` 仅被精确路径引用（无模式清理、无测试断言格式名），改动零波及。无需 GPAC 实测即可定案。

---

## RF-24：SanitizeUntrustedOptions 漏 interactive（serve 任务阻塞占死并发槽）

- **位置**：`BBDown/Infrastructure/BBDownApiServer.cs:734-805`（SanitizeUntrustedOptions 未清除 `Interactive`）、`BBDown/Application/Display.cs:88`（`Console.ReadLine()`）、`Download.cs:588-591,851-854`（serve 任务流中触发）。
- **发现**：客户端 POST `/add-task` 携带 `{"interactive":true}`，任务进入下载阶段后 `SelectTrackManually` → `Console.ReadLine` 同步阻塞。该调用不在 await 点、不可被 CancellationToken 中断，`/cancel/{id}` 无法释放它占用的并发槽；`--max-concurrent`（默认 3）个此类任务即可把并发闸门占满直至进程重启。若操作者在终端前台运行 serve，任务还会直接消费操作者的键盘输入。仅持 API 权限的本地客户端可触发（写端点 CSRF 已防护），故 Low。
- **结论**：采纳一行修复——`SanitizeUntrustedOptions` 中加 `req.Interactive = false;`（与 `req.Debug = false` 同类处理）。
- **状态**：✅ 已修复（2026-08-30，第 12 轮消纳批：代码修复 + 回归测试 + CHANGELOG 未发布条目）。

---

## RF-25：解析失败日志 option.Url 未单行化（日志注入残留）

- **位置**：`BBDown/Infrastructure/BBDownApiServer.cs:1091`、`:1120`。
- **发现**：日志单行化机制已建（`SanitizeLogString`），但仅用于 :312 队列满路径；解析异常路径把客户端完全可控的原始 URL 直接拼进日志（`$"...{option.Url}..."`）。请求体 URL 可含 CR/LF（64KB 限额内），可伪造 `bbdown-api.log` 日志行。
- **结论**：采纳——两处包一层 `SanitizeLogString(option.Url)`。
- **状态**：✅ 已修复（2026-08-30，第 12 轮消纳批：代码修复 + 回归测试 + CHANGELOG 未发布条目）。

---

## RF-26：Core 解析/网络健壮性低危族（5 小项）

- **位置与发现**：
  1. `BBDown.Core/Parser.cs:342-372` —— 免二压重发**失败降级**路径下，dash 无 `audio` 键 + 杜比/Hi-Res 存在时，音频在两次 pass 各追加一次且无去重（intl 分支 :229 有 Contains 去重）→ `AudioTracks` 重复条目、下游重复下载/混流。修复：pass 1 降级分支跳过重追加（`reparsePass == 0` 守卫）或补去重。
  2. `BBDown.Core/AppHelper.cs:284` —— PGC gRPC 请求 `Host` 头硬编码 `grpc.biliapi.net`，实际目标 `app.bilibili.com`（API2），TLS SNI 与 Host 头不一致，依赖基础设施忽略 Host 路由。修复：Host 取目标 URI Authority 或移除让 HttpClient 自动生成。
  3. `BBDown.Core/Fetcher/FavListFetcher.cs:141-157` —— pn 制收藏夹翻页无"空页/停滞"保护（MediaList/SeriesList/SpaceVideo 都有），受控响应源每页返回 code=0 且 medias 空时把 totalPage 数量的分页请求全部打完（media_count 畸形大时请求洪泛）。修复：页 medias 为空即 break。
  4. `BBDown.Core/Fetcher/IntlBangumiInfoFetcher.cs:19` —— `.Replace("\\/", "/")` 多余（`\/` 本是合法 JSON 转义，Parse 会正确解码）且可损坏数据（原文 `\\`+`/` 被错误归并）。修复：直接删除。
  5. `BBDown.Core/Util/SubUtil.cs:343`、`BBDown/Utilities/BBDownUtil.cs:227` —— `x/player/wbi/v2` 端点名带 wbi 却无 `w_rid/wts` 签名，当前 B 站容忍（未记录行为依赖），一旦收紧即静默降级。修复：登录路径下按现有模式补 WbiSign。
- **结论**：采纳，随批次逐项落地（各项独立、互不依赖）。
- **状态**：✅ 已修复（2026-08-30，第 12 轮消纳批：代码修复 + 回归测试 + CHANGELOG 未发布条目）。

---

## RF-27：FindBinaries 写进程级静态工具路径（serve 并发理论面）

- **位置**：`BBDown/Application/Options.cs:151-204`（经 `Workflow.cs:29` serve 下每任务调用）。
- **发现**：`FindBinaries` 直接写进程级静态 `BBDownMuxer.FFMPEG`/`MP4BOX`/`BBDownAria2c.ARIA2C`，两个并发任务理论上可互相覆盖/静默继承。**但核实实际触发面接近零**：serve 下 `SanitizeUntrustedOptions`（BBDownApiServer.cs:737-740）已清零 `Aria2cPath`/`FFmpegPath`/`Mp4boxPath`，任务无法携带不同路径，并发探测写入的是同一 PATH 探测结果（同值无冲突）；CLI 单进程单 URL 无并发。残余仅"任务 B 静默继承任务 A 探测的路径（同机同 PATH，语义等价）"与冗余探测跳过。
- **结论**：待议，倾向维持现状；若后续把工具路径纳入 `AppSettings`（与 Config 的 AsyncLocal 方案对齐）可顺路收编，不做预防性单独重构。
- **状态**：⭕ 维持现状（2026-08-30 第 12 轮定案：serve 下 `SanitizeUntrustedOptions:737-740` 已清零路径字段，覆盖场景实际不可达；CLI 单进程单 URL 无并发）。

---

## RF-28：HTTP 响应体无大小上限

- **位置**：`BBDown.Core/Util/HTTPUtil.cs:758`（gRPC `GetPostResponseAsync` 的 `ReadAsByteArrayAsync`）、`:467`（普通响应 `ReadAsStringAsync`）。
- **发现**：gzip 解压侧已有 48MB 上限（AppHelper.GzipDecompress，B3-S1），但**压缩响应体本身**与普通 GET 响应体（自动解压后）均无上限。被攻破端点或 `--insecure` 中间人可用分块慢发/gzip 炸弹直接打满进程内存——与 B3 已认可的威胁模型一致，只是解压上限没有覆盖到这一层。
- **结论**：采纳——读取前检查 `Content-Length`/逐块读取并设总量上限（如 64MB），超限抛 `InvalidDataException`。
- **状态**：✅ 已修复（2026-08-30，第 12 轮消纳批）→ **勘误（第 14 轮，RF-51）**：消纳实际只落地了 gRPC POST 与登录轮询两处，本条登记引用点中的"普通响应 `ReadAsStringAsync`"（GetWebSourceCoreAsync/GetWebSourceAnonymousCheckedAsync）从未改造，仍无界读取；接续修复登记为 RF-51。

---

## RF-29：.editorconfig 存量违规 4 文件（BOM/末尾换行，format 门禁不覆盖）

- **位置**：`BBDown.Core/BBDown.Core.csproj`（UTF-8 BOM）、`BBDown.Tests/BBDown.Tests.csproj`（UTF-8 BOM + 无末尾换行）、`.github/workflows/codeql.yml`（无末尾换行）、`.github/dependabot.yml`（无末尾换行）。均已逐字节验证。
- **发现**：`dotnet format` 不检查 charset BOM 与 `insert_final_newline`，故 pr.yml 的 format 硬门禁拦不住。RF-8~13 涉及的源/测试文件全部干净，此为存量。
- **结论**：采纳——按 .editorconfig 重存 4 个文件；可选补一个轻量检查（挂 format job 前置步骤）。
- **状态**：✅ 已修复（2026-08-30，第 12 轮消纳批：代码修复 + 回归测试 + CHANGELOG 未发布条目）。

---

## RF-30：sub check 逐 aid 过滤器吞 `SubscriptionDataCorruptException`（订阅历史清零→全量重下）

- **位置**：`BBDown/Commands/SubCommand.cs:179`（`RecordDownloaded` 在 per-aid try 内）、`:185-187`（per-aid 过滤器白名单含 `InvalidOperationException`）；`BBDown/Infrastructure/SubscriptionStore.cs:18`（`SubscriptionDataCorruptException : InvalidOperationException`）、`:198-231`（`RecordDownloaded` 损坏路径：`IsolateCorruptFile` 把历史文件**移走**后抛专用异常）。
- **发现**：`SubscriptionStore` 的文档化中止契约（SubscriptionStore.cs:14-16、:190-191 注释 + SubCommand.cs:195-201 专用重抛 catch）要求损坏异常必须终止整个 `sub check`。`LoadHistory`（:162）在专用 catch 保护内正确；但 `RecordDownloaded`（:179）在 **per-aid try** 内，其过滤器（:185-187）含 `InvalidOperationException`——专用异常是其子类被匹配吞掉，记为 `av{aid} 下载失败（继续下一个）`，外层专用重抛（:195）对此调用点不可达。后果正是契约注释描述的场景：历史文件已被隔离移走，下一个 aid 的 `RecordDownloaded` 见 `File.Exists == false` **静默重建仅含当前 aid 的历史并原子写回——全部订阅的下载历史清零，下次 check 全量重下**。触发面：`sub check` 运行中历史文件损坏（磁盘故障/并发写者）。
- **结论**：采纳——per-aid catch 前补 `catch (SubscriptionDataCorruptException) { throw; }`（与外层同款），或过滤器改 `... and ex is not SubscriptionDataCorruptException`；补 1 个回归测试（损坏历史 + RecordDownloaded 路径断言专用异常外抛、后续订阅不再执行）。
- **状态**：✅ 已修复（2026-08-31，第 13 轮消纳批：per-aid 专用重抛守卫落地。登记结论所提的回归测试未交付——命令层无注入缝（DoWorkAsync/ResolveAsync 静态直连网络，无法在单测中触达 RecordDownloaded 的损坏路径），以代码走查 + 全量编译验证，测试缝待 OPTIMIZATION_PLAN P0-1 拆分后补）。

---

## RF-31：`SortTracks` 裸 `Convert.ToInt32(v.id)` 异常穿透两级过滤器（RF-14 同族逃逸）

- **位置**：`BBDown/Application/TrackSort.cs:19`（`.ThenByDescending(v => Convert.ToInt32(v.id))`）；id 来源 `BBDown.Core/Parser.cs:398`（dash `video[].id`）/`:222-223`（intl）/`:604`（flv quality），均 `GetValueAsStringSafe`（缺失/非字符串返回 `""`）；过滤器 `BBDown/Application/Download.cs:1084-1085`（页面级）、`:94`（批级）。
- **发现**：`v.id` 是服务器可控字符串直进 `Convert.ToInt32`：缺失→`FormatException`、超 int32→`OverflowException`。两级过滤器白名单均无这两个类型（同仓库 `ExternalToolHelper.cs:53` 的探针过滤器都显式含 FormatException/OverflowException，此处没有），单个畸形 dash 节点即穿透两级过滤器中止整批多 P——剩余分 P 全弃、webhook/failedPages 丢失，与 RF-14 修复的"单 P 失败隔离"矛盾。触发面：降级/风控/`--ep-host` 镜像站响应中 `video[].id` 缺失或非数字。
- **结论**：采纳——(a) `int.TryParse(v.id, out var q) ? q : 0`（id 仅作优先级并列时的 tie-break，降级 0 无行为损失）；(b) 两级过滤器补 `FormatException or OverflowException` 作纵深（修复 (a) 后通常不触发）。
- **状态**：✅ 已修复（2026-08-31，第 13 轮消纳批）。

---

## RF-32：sub check 用户取消语义违约（吞取消记失败→1 / 穿透→130，文档约定 0）

- **位置**：`BBDown/Commands/SubCommand.cs:202-204`（per-sub 过滤器白名单含 `TaskCanceledException` 且无 token 守卫）、`:121-218`（`SubCheckCommand.ExecuteAsync` 无方法级 OCE catch）、`:146`（`InitializeRequestSessionAsync` 在任何 try 之外）；正确对照 `BBDown/Commands/WatchLaterCommand.cs:105-116`；全局 handler `BBDown/Program.cs:162-167`（→130）；文档约定 `docs/wiki/CLI-Reference.md:122`（子命令取消→0）；RF-2 记录（"cancel→0 逐字保留"）。
- **发现**（两条路径均违反契约）：① Ctrl+C 落在 HttpClient 调用（`DoWorkAsync`/`ResolveAsync`/`FetchAsync`）→ `TaskCanceledException` 被 per-aid :181-183 正确重抛后，又被 **per-sub 过滤器 :202-204（含 TaskCanceledException、无 token 守卫）吞掉**记"订阅检查失败"，后续每个订阅在已取消 token 上立即失败 → `failedSubs>0` → **退出码 1** + "N 个订阅失败"误导日志；② 取消落在 `ThrowIfCancellationRequested`（抛基类 `OperationCanceledException`，不匹配 :202 的 `TaskCanceledException`）或 :146 初始化段 → 直达全局 handler → **退出码 130**。结构性相同的 `watchlater` 用方法级 catch 区分 token 状态返回 0/1，`sub check` 是漏网。
- **结论**：采纳——per-sub 过滤器补 `&& !cancellationToken.IsCancellationRequested` 语义（或专用 OCE 重抛守卫前置），并对照 watchlater 补方法级 `catch (OperationCanceledException)`（token 已取消→"已取消"+0；未取消→1）；若产品判定 sub check 应与主命令同为 130，则改 wiki 行 + RF-2 记录勘误——二者取一，现状不一致是确定的。
- **状态**：✅ 已修复（2026-08-31，第 13 轮消纳批：拆 `CheckSubscriptionsAsync` + 方法级 OCE 分类 + per-sub 取消守卫 + 会话初始化纳入 try。同 RF-30：命令层无注入缝，回归以代码走查 + 全量编译验证，四条异常路径——损坏/Ctrl+C×2/超时——逐条推演确认）。

---

## RF-33：API.md `DownloadTask` 字段清单缺 `ErrorMessage`/`SavePaths`（承诺的错误原因无字段可查）

- **位置**：`API.md:118-131`（字段清单 12 项，无此二者）、`API.md:60`（承诺"凭 JobId 查询到失败任务及其错误原因"）；代码 `BBDown/Infrastructure/BBDownApiServer.cs:1131/:1153`（`task.ErrorMessage` 写入，经 `SanitizeErrorMessage` :480-482 净化）、`:263`（`SavePaths` 深拷贝入快照），随 `Snapshot()` 全量序列化。
- **发现**：`DownloadTask` 实际序列化含 `ErrorMessage`（失败原因）与 `SavePaths`（解析后的服务器本地绝对产物路径，RF-15 记录明确其信息泄露敏感面），API.md 字段清单两者均缺——按文档开发的客户端拿不到失败原因与产物路径，与同文件 :60 的承诺直接矛盾。属 RF-16 文档正确性族漏网（当时只修了 wiki 退出码表/README，未对照 API.md 字段清单）。
- **结论**：采纳——补 2 行：`ErrorMessage <string?>`（失败原因，成功时为空）、`SavePaths <List<string>>`（服务器本地绝对路径，注意非客户端路径）。
- **状态**：✅ 已修复（2026-08-31，第 13 轮消纳批）。

---

## RF-34：DOVI 探针 `Win32Exception` 未捕获（--skip-mux + 无 ffmpeg + 杜比视界 → 整批死）

- **位置**：`BBDown/Utilities/ExternalToolHelper.cs:53`（探针过滤器无 `Win32Exception`）、`BBDown/Application/Options.cs:166-189`（ffmpeg 解析被 `if (!SkipMux)` 门控）、`BBDown/Application/Download.cs:747`（探针调用无 SkipMux 门控）、`BBDown/Infrastructure/BBDownMuxer.cs:17`（`FFMPEG` 默认 `"ffmpeg"`）。
- **发现**：`--skip-mux` 时 `FindBinaries` 跳过 ffmpeg 解析（刻意：不混流不需要），但杜比视界（dfn 126）分 P 的版本探测仍无条件运行；ffmpeg 不在 PATH 时 `Process.Start` 抛 `Win32Exception`（找不到文件），探针过滤器与两级 catch 过滤器均不含该类型 → 整批中止。探针语义应是"探测失败 → return false → 走 mp4box"，异常逃逸使其变成致命错误。触发面：`--skip-mux`（含 serve 任务 `skip-mux:true`）+ 无 ffmpeg + 任意杜比视界视频。
- **结论**：采纳——过滤器补 `System.ComponentModel.Win32Exception`（return false），或 `SkipMux` 时直接跳过探针（更省一次进程启动）。
- **状态**：✅ 已修复（2026-08-31，第 13 轮消纳批）。

---

## RF-35：DRM 取钥链缺 `CancellationToken`（serve /cancel 最长约 6 分钟不可中断）

- **位置**：`BBDown/Application/Decrypt.cs:48`（调用未传 token；`DecryptDrmAsync` 自 :19 起持有 `token` 且 mp4decrypt 阶段已用）、`BBDown.Core/DRM/CkcDecryptor.cs:5/:13`（`DrmDecryptor.GetKeyWidevineAsync` 无 token 形参）、`BBDown.Core/DRM/WidevineCdm.cs:26`（`GetKeysAsync` **已有** `CancellationToken token = default`，下游全链支持）。
- **发现**：取钥这一环在 `DrmDecryptor` 薄封装处丢令牌：许可证客户端 2 分钟超时 × 3 次尝试 + 退避（HTTPUtil.cs:135），Ctrl+C / serve `/cancel` 期间取钥不可中断（约最长 6 分钟占住 `--max-concurrent` 槽位）。属 RF-17 取消吞没族的新位置（这里是"从未传递"而非"过滤器吞掉"）。
- **结论**：采纳——`GetKeyWidevineAsync` 补 `CancellationToken token = default` 形参并在 Decrypt.cs:48 传入（3 行改动，下游管道现成）。
- **状态**：✅ 已修复（2026-08-31，第 13 轮消纳批）。

---

## RF-36：`ArticleCommand` 用弱净化 `SanitizeFileName`（Windows 保留名未防护 → CON.md）

- **位置**：`BBDown/Commands/ArticleCommand.cs:42`；`BBDown/Infrastructure/LiveStreamUtil.cs:668-675`（`SanitizeFileName` 仅替换非法字符 + Trim，空名兜底"直播"）；对照 `BBDown.Core/Util/PathUtil.cs:55-60`（`GetValidFileName` 有保留名基名匹配 + `_` 前缀，含带扩展名变体 `CON.md` 的判定）。
- **发现**：专栏标题是服务器可控字符串（`data.title`），恰为 `CON`/`NUL`/`PRN`/`COM1`… 时产出 `CON.md`——Windows 上设备名语义无法作为普通文件创建，`SaveAsMarkdownAsync` 抛异常 → 命令以误导性"专栏获取失败"退出 1。`PathUtil.GetValidFileName` 的保留名防护未被用上。
- **结论**：采纳——:42 改 `BBDownUtil.GetValidFileName(article.Title)`（保留名自动 `_` 前缀）；LiveStreamUtil 内部消费 `SanitizeFileName` 的直播文件名同属此面，随批一并评估。
- **状态**：✅ 已修复（2026-08-31，第 13 轮消纳批）。

---

## RF-37：TV 登录轮询仍用自动跟随重定向客户端（RF-4/RF-13 收口族残留）

- **位置**：`BBDown/Infrastructure/BBDownLoginUtil.cs:243`（`HTTPUtil.AppHttpClient.PostAsync(pollUrl, ...)`）。
- **发现**：TV 登录轮询的 POST 携带按 appsecret 签名的参数体（auth_code/sign/ts），响应含新下发的 `access_token`，却走自动跟随重定向的 `AppHttpClient`——被攻破端点/开放重定向可把签名请求体重放到跨主机。WEB 登录轮询已按 RF-13 切 `NoRedirectClient` + 每跳 `IsTrustedCookieHost` 校验，gRPC POST（B3-F2）/Widevine 许可证（RF-4）同构收口，唯 TV 轮询漏网。入口 URL 硬编码可信端点、恒校验 TLS，无当前可利用面——与 RF-4/RF-13 同级的一致性/纵深防御项。
- **结论**：采纳——切 `NoRedirectClient` + 3xx 显式拦截（或逐跳校验），与 RF-13 修法同构。
- **状态**：✅ 已修复（2026-08-31，第 13 轮消纳批：auth_code 与轮询两处均切 `NoRedirectClient` + 3xx 显式拦截。附带行为变化：这两个请求的客户端超时由 2 分钟收紧至 1 分钟（NoRedirectClient 池的既定语义，与 WEB 登录轮询一致），已在 CHANGELOG 记录）。

---

## RF-38：`ServeCommand` OCE 无 token 守卫（内部超时取消 → 退出码 0 掩盖异常退出）

- **位置**：`BBDown/Commands/ServeCommand.cs:64-67`（`catch (OperationCanceledException) { return 0; }`）。
- **发现**：仓库自有的取消分类规则（LiveCommand.cs:100-103、WatchLaterCommand.cs:107-115、ArticleCommand.cs:48-58、BBDownApiServer.cs:1185-1188 四处同款注释）要求区分"token 已取消的用户取消"与"token 未取消的真实取消"（内部超时联动 CTS 等）；serve 此处无守卫，任何非根 token 的 OCE 从 `StartServerAsync` 逃出都以退出码 0"成功"结束——Docker `restart: unless-stopped`/systemd `on-failure`/CI 包装全部丢失崩溃信号。优雅关停（根 token）保持 0 不受影响。
- **结论**：采纳——补 `when (cancellationToken.IsCancellationRequested)`，未取消的 OCE 落到既有 `catch (Exception)`（:68-71）记日志 + 1。
- **状态**：✅ 已修复（2026-08-31，第 13 轮消纳批：取消 catch 补 when 守卫，未取消 OCE 落失败分支记日志返回 1。回归钉住"取消→0"主干；"未取消 OCE→1"的判别性用例需向 StartServerAsync 注入非根 token 的 OCE（无注入缝），该半边以代码走查为证）。

---

## RF-39：CLI-Reference 弹幕格式文档错误（`protobuf` 示例不可用 + 默认值描述错）

- **位置**：`docs/wiki/CLI-Reference.md:52-53`；代码 `BBDown/Models/BBDownEnums.cs:6-10,15`（枚举仅 `Xml`/`Ass`；`DefaultFormats = [Xml, Ass]`）、`BBDown/Application/Options.cs:115`（未知格式报"包含不支持的下载弹幕格式"）。
- **发现**：:53 示例 `xml,protobuf` 按文档操作即触发错误路径（1.6.16 已修正 README 与帮助文本为"仅支持 xml,ass"，wiki 总表漏改）；:52"默认保存为 XML"与默认值 `[Xml, Ass]` 不符（默认双格式）。
- **结论**：采纳——:53 示例改 `xml,ass`；:52 改"默认同时保存 XML 与 ASS"。
- **状态**：✅ 已修复（2026-08-31，第 13 轮消纳批）。

---

## RF-40：README `--show-all` 描述错误（实为展示所有分 P 标题）

- **位置**：`README.md:97`（"显示全部可用音视频流"）；代码 `BBDown/Configuration/MyOption.cs:42-44`（"展示所有分P标题"）、`BBDown/Application/Workflow.cs:166-180`（不带 flag 只打印前 5 个分 P 标题 + "......"）；`docs/wiki/CLI-Reference.md:40` 描述正确。
- **发现**：README 把 `--show-all` 与 `--hide-streams` 的反面混为一谈；实际控制的是分 P 标题打印截断。按 README 期望"列出全部流"的用户会得到完全不同的行为。
- **结论**：采纳——README:97 改"展示所有分 P 标题"（与 MyOption/CLI-Reference 对齐）。
- **状态**：✅ 已修复（2026-08-31，第 13 轮消纳批）。

---

## RF-41：模板文档缺 `<videoDate>` 占位符且计数 18→19

- **位置**：`docs/wiki/Configuration-and-Templates.md:69`（"18 种变量占位符"）及 `:73-92`（18 行表，无 videoDate）；代码 `BBDown/Application/PathHelper.cs:73`（`"videoDate"` 分支，随 RF-19 修复过格式化与净化）。
- **发现**：`PathHelper` 占位符 switch 实际 19 个键，文档表漏 `<videoDate>`（分 P 发布时间，与 `<publishDate>` 同格式）——该占位符正是 RF-19 修复对象，是活功能非死条目。计数与表格双重失实。
- **结论**：采纳——补 `<videoDate>` 行（分 P 发布时间，接受与 `<publishDate>` 相同的日期格式串），计数改 19。
- **状态**：✅ 已修复（2026-08-31，第 13 轮消纳批）。

---

## RF-42：README serve 子选项表缺 `--notify-webhook`（与同页 :333 自相矛盾）

- **位置**：`README.md:151-158`（表仅 4 行：`-l`/`--max-concurrent`/`--serve-token`/`--trusted-proxy`）；`BBDown/Commands/ServeCommand.cs:29`（`--notify-webhook` 定义）、`README.md:333`（RF-16 修正处已列举）。
- **发现**：`ServeSettings` 共 5 个选项，README serve 子选项表漏 `--notify-webhook`（任务完成回调 URL）；同页后文 :333 已列出它，前后自相矛盾。RF-16 修的是 :333 括号列举，此表是独立漏网点。
- **结论**：采纳——表补一行 `--notify-webhook`（任务完成回调 URL，受服务器侧 allowlist 校验）。
- **状态**：✅ 已修复（2026-08-31，第 13 轮消纳批）。

---

## RF-43：非 DOVI 进程启动点 `Win32Exception` 穿透两级过滤器（第 13 轮遗留观察①定案）

- **位置**：`BBDown/Infrastructure/ExternalProcessRunner.cs:61`（`p.Start()` 在 try 块外、无捕获——全仓库 ffmpeg/mp4box/aria2c 的唯一集中启动点）、`BBDown/Application/Decrypt.cs:151`（mp4decrypt 的独立 `Process.Start`，同样无捕获）；两级过滤器 `BBDown/Application/Download.cs:96`/`:1093` 均无 `System.ComponentModel.Win32Exception`（继承 ExternalException→SystemException，非 IOException 派生）；对照 RF-34 只收口了 `ExternalToolHelper.cs:58` 的 DOVI 探针。
- **发现**：页面处理路径上的全部 5 个外部进程启动点（ffmpeg 混流 `MuxAV`/mp4box `MuxByMp4box`/`MergeFLV`/aria2c `DownloadFileByAria2cAsync`/mp4decrypt）都经裸 `p.Start()`；"已解析但不可启动"的真实条件——Unix 下显式路径无执行位（`Options.cs:151-164` 与 `EnsureToolAvailable` 的 explicitPath 分支只查 `File.Exists`）、Windows/PATH 中存在损坏或错误架构的二进制（`FindExecutable` 只查 `File.Exists`，ERROR_BAD_EXE_FORMAT 193）——抛 `Win32Exception` → 穿透两级过滤器 → 剩余分 P、webhook、failedPages 汇总全丢、退出码 1，正是过滤器设计要防的逃逸面。旁证：`LiveStreamUtil.ConcatSegmentsAsync` 的 catch（LiveStreamUtil.cs:563）已显式含 `Win32Exception`，证明该类型在真实运行中发生过——唯独下载页面路径未收口。
- **结论**：采纳——`SystemProcessRunner.RunAsync` 把 `p.Start()` 的 Win32Exception 规范化为 `InvalidOperationException`（消息带工具名，与 RF-14 对 NotSupportedException 的处理同构），一步收口全部调用点；`Decrypt.cs:151` 同样包裹。
- **状态**：✅ 已修复（2026-09-15，第 14 轮消纳批）。

---

## RF-44：`UnauthorizedAccessException` 不在两级过滤器 + 清理点只捕 IOException

- **位置**：过滤器缺口 `Download.cs:96`/`:1093`（UA 非 IOException 派生，两级均无）；裸 `File.Delete`（无 try/catch）`BBDownDownloadUtil.cs:381/401/421/433/618/710`（处于下载重试循环内，`:459/:474` 的 catch 过滤器同缺 UA）；`Download.cs:532/536`（debug 文件写/删）、`:458`（SubOnly 产物 Move）、`:264`（混流产物 Move）；清理子句只捕 `catch (IOException)` 的不一致点 `Download.cs:674/962/985/1011`（对照同文件 `:473` 已用 `IOException or UnauthorizedAccessException`）。
- **发现**：Windows 上 `File.Delete` 对**只读属性**文件抛的正是 UnauthorizedAccessException（而非 IOException）；受控文件夹访问（Defender 对未签名 exe 写 Documents 等目录返回 Access Denied）、只读 `.tmp`、ACL 拒写的网络盘/容器卷——任一本地权限错误落在单 P 路径上即穿透两级过滤器中止整批（含 `File.Delete(tmpName)` 断点续传清理等高频点），与 RF-31 补 FormatException/OverflowException 的动机同构。
- **结论**：采纳——两级过滤器补 `UnauthorizedAccessException`；`:674/962/985/1011` 的 `catch (IOException)` 对齐为双类型；`SubCommand.cs:227/252`、`WatchLaterCommand.cs:92` 命令级过滤器同族一并评估。
- **状态**：✅ 已修复（2026-09-15，第 14 轮消纳批）。

---

## RF-45：免二压重发降级丢失杜比/Hi-Res 音轨（RF-26 守卫旁支）

- **位置**：`BBDown.Core/Parser.cs:345-348`（每轮无条件从 root 重赋值 video/audio 列表）、`:340-343`（重发失败降级 catch）、`:327-330`（新响应无 dash 的降级分支）、`:358-390`（dolby/flac 追加块，受 `!dolbyApplied`/`!flacApplied` 守卫；标记仅在 `:324-325` 新文档接管分支重置）。
- **发现**：pass 0 把 dolby/flac 音轨 `AddRange` 进 `audio` 并置标记 true；pass 1 无论走哪条降级路径（重发请求失败被 `:340` 过滤器吞掉、或新响应无 dash 节点 `:329` Dispose），执行到 `:345-348` 时都会从**同一份旧文档**重新生成一份不含 dolby/flac 的 `audio` 列表，而标记仍为 true → 追加块被跳过 → 最终音轨缺失杜比/Hi-Res，无任何日志提示。每个非 app 接口的 dash 视频都无条件发起 pass 1 重发——网络瞬断/风控 HTML/业务失败任一条即触发，"重试越忙、风控越紧，越容易丢杜比"。RF-26 的守卫只防了"重复追加"，没防"列表被重置但标记未重置"的不一致。
- **结论**：采纳二选一——(1) `:345-348` 的重赋值仅在"新文档接管"分支内执行，降级路径保持 pass 0 的列表（含已追加项）不动；(2) 降级时重置标记 + 追加前按 baseUrl 判重。建议补单测：构造含 dolby 的 dash 文档 + 重发抛 HttpRequestException，断言 AudioTracks 仍含 E-AC-3 轨。
- **状态**：✅ 已修复（2026-09-15，第 14 轮消纳批）。

---

## RF-46：直播录制 `KeyNotFoundException` 逃逸重连过滤器（违背"不设重试上限"承诺）

- **位置**：`BBDown/Infrastructure/LiveStreamUtil.cs:100`（`GetPropertySafe("data").GetPropertySafe("playurl_info").GetPropertySafe("playurl")` 链式取节点）、`:77`（info 的 data）；重连过滤器 `:319`（`HttpRequestException or JsonException or InvalidOperationException or TimeoutException or LiveStreamWriteException`——无 KeyNotFoundException）；异常源 `BBDown.Core/Util/JsonElementExtensions.cs:69`；外层 `:338` `catch (Exception) { …; throw; }`。
- **发现**：live API 返回 code=0 但 data/playurl_info 节点缺失（接口降级/灰度变更/风控 JSON 变体）→ `KeyNotFoundException` 不在 `:319` 白名单 → 落到 `:338` 重抛 → **整场录制终止**。与 `:219-222` 注释的设计承诺（"网络瞬断/API 故障期间持续退避重试（不设重试上限）……网络恢复后自动续录"）矛盾——"主播还在播、用户没取消"被服务器响应形状打断。`IsRoomLiveAsync`（`:235-246`）只 catch InvalidOperationException，经 `ResolveAsync` 同面穿透。
- **结论**：采纳——改 `TryGetPropertySafe` 逐级判空（缺节点 → 按"暂时无法获取流地址"的 InvalidOperationException 走既有瞬态退避路径），与文件既有防御风格一致；或 `:319` 白名单补 KeyNotFoundException。
- **状态**：✅ 已修复（2026-09-15，第 14 轮消纳批）。

---

## RF-47：AppHelper `ArgumentException`/`InvalidProtocolBufferException` 穿透两级过滤器（RF-31 同族）

- **位置**：`BBDown.Core/AppHelper.cs:59-68`（`ParseId` 对非数字 id 抛 `ArgumentException`）、`:88`（`ParseFrom(ReadMessage(data))` 可抛 `Google.Protobuf.InvalidProtocolBufferException`——直接继承 Exception）；两级过滤器 `Download.cs:96/:1093` 均无这两类；对照 `BBDown.Core/Util/SubUtil.cs:406-407`（TryParse 守卫）与 `:428-429`（显式 catch InvalidProtocolBufferException）——同一套 gRPC API 的 DmViewReply 姊妹路径已明确防护。
- **发现**：`--use-app-api` 下每个分 P 的 `ExtractTracksAsync` 都经 `Parser.GetPlayJsonAsync:47 → AppHelper.DoReqAsync`。服务器（或 `--insecure` 中间人）下发：含非数字/浮点 cid 的 view 响应 → `ArgumentException`；帧头合法但帧体为垃圾字节的 200 响应 → `InvalidProtocolBufferException`——两者穿透两级过滤器终止整批多 P。SubUtil 对姊妹接口的显式防御证明项目已知这两类异常是服务器可触发的，含 access_token 授权头的更重路径反而不设防。
- **结论**：采纳——`DoReqAsync` 源头转译：`ParseId` 的 throw 改为带可读中文的 `InvalidOperationException`（业务性确定性失败）；`ParseFrom` 包 try/catch 转 `InvalidOperationException("APP 接口响应反序列化失败…")`。不在两级过滤器加类型（避免白名单继续膨胀，与 RF-47 修复路线一致于 RF-14 的"源头规范化"先例）。
- **状态**：✅ 已修复（2026-09-15，第 14 轮消纳批）。

---

## RF-48：`Page.bvid` getter 对服务器可控 aid 抛 AOORE

- **位置**：`BBDown.Core/Entity/Entity.cs:26-27`（getter `long.TryParse(aid) → BilibiliBvConverter.Encode(aidNum)`）；`BBDown.Core/Util/BilibiliBvConverter.cs:31-38`（`avid < MIN_AID` / `>= MAX_AID` 抛 `ArgumentOutOfRangeException`）；消费点 `Workflow.cs:145`、`Download.cs:249`（混流元数据）、`PathHelper.cs:56`（文件名占位符）均无包裹；两级过滤器无 AOORE——且 `Download.cs:182/:782/:882` 注释三次明言"AOORE 不在下载重试的 catch 过滤内，直接中止整批"并逐处防护，此处是同族漏网。
- **发现**：收藏夹/合集/空间路径的 aid 来自服务器响应（`FavListFetcher.cs:123` `GetValueAsStringSafe("id")`）。畸形/被攻破端点把条目 id 填 `"0"`（`long.TryParse` 成功 → `Encode(0)` 抛 AOORE）、负数或超 2^51 大数 → 整批在文件名格式化或元数据混流阶段被英文 AOORE 中止。
- **结论**：采纳——getter 把 `Encode` 包 try/catch（AOORE → 回落返回原始 aid，与下方"非纯数字"分支同语义），3 行改动。
- **状态**：✅ 已修复（2026-09-15，第 14 轮消纳批）。

---

## RF-49：`TimeoutException` 未入三处逐条降级过滤器（E1 修复旁支）

- **位置**：`BBDown.Core/Fetcher/FavListFetcher.cs:112-113`（单稿件降级过滤器：`HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException or TaskCanceledException`——无 TimeoutException）、`BBDown.Core/Fetcher/SpaceVideoFetcher.cs:120-122`（同缺）+ `:118-119`（注释仍在陈述过时分类"请求超时抛 TaskCanceledException"）、`BBDown.Core/Util/BuvidProvider.cs:44`（过滤器仅四类，连 TaskCanceledException 也未列）；对照 `HTTPUtil.cs:574-579`——E1 修复后重试耗尽的超时**统一抛 TimeoutException**，TaskCanceledException 对这些路径已成死类型。
- **发现**：单稿件 HTTP 3 次有界重试耗尽后超时 → TimeoutException 逃过逐条降级过滤器：① `FavListFetcher.ProcessPageAsync`——一个稿件超时 → 整个收藏夹解析中止（`:105` 注释"单个稿件失败只记 failures 跳过"失效）；② `SpaceVideoFetcher.ExpandEntriesAsync`——千稿展开中途一次超时 → 不进 failures、不计 consecutiveFailures，前功尽弃；③ `BuvidProvider.EnsureAsync`——buvid3 是纯装饰性设备标识（失败本应"跳过注入"），超时却让整个空间抓取直接失败。
- **结论**：采纳——三处过滤器补 `TimeoutException`；SpaceVideoFetcher `:118-119` 过时注释一并修正。
- **状态**：✅ 已修复（2026-09-15，第 14 轮消纳批）。

---

## RF-50：`GetWebSourceCoreAsync`（携 SESSDATA）仍自动跟随重定向（NoRedirect 收口族漏网成员）

- **位置**：`BBDown.Core/Util/HTTPUtil.cs:522`（`AppHttpClient.SendAsync`，池定义 `:112-113` allowRedirect: true）、`:497`（仅初始 URL 过 `IsTrustedCookieHost`）。
- **发现**：`GetWebSourceCoreAsync(sendCookie:true)` 是全项目凭据最重、调用面最广的 GET 入口（全部 fetcher、Parser playurl、WbiSign 签名请求），初始 URL 有 B3-S1 主机白名单，但客户端 `AllowAutoRedirect=true`——可信入口主机返回 3xx 时，RedirectHandler 会把手工附加的 Cookie 头（完整 SESSDATA/bili_jct）发往 Location 指向的任意主机，白名单只拦第一跳。与 RF-4/RF-13/RF-37 同定性（Low：入口可信/TLS 恒校验/B 站 API 正常不重定向，一致性/纵深防御项），但它是收口族中剩余成员里载荷最重的一个，且入口含操作者配置的镜像主机。
- **结论**：采纳——`sendCookie:true` 改走 `NoRedirectClient` + 每跳 `IsTrustedCookieHost`（`GetWebSourceWithSetCookiesAsync:303-330` 已是现成逐跳模板，抽出复用即可）。
- **状态**：✅ 已修复（2026-09-15，第 14 轮消纳批）。

---

## RF-51：RF-28 消纳缺口——普通响应体仍无 64MB 上限（登记记录需勘误）

- **位置**：`BBDown.Core/Util/HTTPUtil.cs:532`（`GetWebSourceCoreAsync`——全部 fetcher/Parser/WbiSign 的主入口，仍 `ReadAsStringAsync` 无界）、`:415`（`GetWebSourceAnonymousCheckedAsync`——UrlResolver 泛抓取、目标可为不可信 URL，同样无界）；对照 `:336`（登录轮询）与 `:823`（gRPC POST）已改 `ReadContentBoundedAsync`。
- **发现**：RF-28 的登记引用点明确含"普通响应 `:467`"，消纳只落地了 gRPC POST 与登录轮询两处，其文字描述的主要引用面从未改造——属消纳验证遗漏而非新面。`GetWebSourceAnonymousCheckedAsync` 的 XML doc 自述"目标可能是任意网页"，被攻破端点/`--insecure` 中间人可用分块慢发/巨包打满内存，与 RF-28/B3-S1 已认可的威胁模型一致。
- **结论**：采纳——两处改 `ReadContentBoundedAsync` + `DecodeBodyBytes`（工具已就位，两行改动）；`GetWebSourceAnonymousCheckedAsync` 顺带补 `EnsureSuccessStatusCode`。同时本文件 RF-28 状态行补勘误："普通响应体一半未落地，第 14 轮 RF-51 接续"。
- **状态**：✅ 已修复（2026-09-15，第 14 轮消纳批）。

---

## RF-52：Series/MediaList fetcher 先 GetPropertySafe 后查 code（错误诊断不可达）

- **位置**：`BBDown.Core/Fetcher/SeriesListFetcher.cs:23`（先 `GetPropertySafe("data")`）/`:26-31`（code 诊断在后，对无 data 节点的错误响应不可达）；`MediaListFetcher.cs:22`（同序）/`:31`（回退过滤器只含 `HttpRequestException or InvalidOperationException`，吞不掉 SeriesListFetcher:23 抛的 KeyNotFoundException）；对照 NormalInfoFetcher:17-23 / BangumiInfoFetcher:20-27 / FavListFetcher:53-59 全部"先查 code"的正确序。
- **发现**：`x/v1/medialist/info` 返回 `{"code":-400,"message":"…"}`（无 data 键）时，用户看到的是英文裸 `KeyNotFoundException: JSON property not found: 'data' …` 而非设计好的"获取系列信息失败(code=-400)"；MediaList 的"误识别为系列"回退路径同面被 KeyNotFoundException 直接击穿。
- **结论**：采纳——两处 `GetPropertySafe` 后移到 code 检查之后（或改 TryGetProperty + 判空分支）；MediaListFetcher 回退过滤器补 `KeyNotFoundException`。
- **状态**：✅ 已修复（2026-09-15，第 14 轮消纳批）。

---

## RF-53：`GetPropertySafe` 异常消息拼服务器可控"全部键名"清单（控制字符注入面）

- **位置**：`BBDown.Core/Util/JsonElementExtensions.cs:69`——`throw new KeyNotFoundException($"JSON property not found: '{propertyName}' (available keys: {string.Join(", ", element.EnumerateObject().Select(p => p.Name))})")`。
- **发现**：异常消息把响应对象的全部键名（服务器可控，可含经 JSON 反转义后的 `\u001b[31m` 等控制字符）拼进消息，经顶层 `Console.Error.WriteLine(ex.Message)` 直写终端、经 LogStack 落日志——与 Parser.SanitizeServerText（B3-L3）同族的终端/日志投毒注入面；大响应的键名清单同时可把单行消息撑到极大。该消息也是 RF-46 的异常源。
- **结论**：采纳——键名清单过与 SanitizeServerText 同款的控制字符剥离，或截断保留前 N 个键名。
- **状态**：✅ 已修复（2026-09-15，第 14 轮消纳批）。

---

## RF-54：serve/CLI 日志注入残留（RF-25 旁支：派生串未脱敏）

- **位置**：来源 `BBDown/Utilities/UrlResolver.cs:100-104`（fid 取自 query 未净化拼 `aidOri`）、`:67-83`（listBizId/seriesBizId/sid 同构）、`BBDown/Utilities/BBDownUtil.cs:130-142`（`GetQueryString` 的 `[^&]+` 可匹配 CR/LF）；未脱敏 sink：`Workflow.cs:100/:126`、`Pages.cs:48`、`Options.cs:115`、`BBDownApiServer.cs:1199/:1210`（Logger 层无转义，serve 同写 `bbdown-api.log`）。
- **发现**：RF-25 只对 `/add-task` 的 `req.Url` 本身三个调用点应用了 `SanitizeLogString`；URL 在 `ResolveAsync` 拆解后，客户端可控的 query 值以原始 CRLF 形态进派生串 `aidOri`/`aid` 并落日志——serve 客户端（或 CLI 粘贴恶意链接）可伪造日志行/ANSI 序列（审计污染）。`SanitizeLogString` 当前零单测。
- **结论**：采纳——净化下沉到来源：`ResolveAsync` 返回前对结果统一单行化（比逐 sink 补丁收敛）；`Pages.cs:48`/`Options.cs:115` 含客户端原文的 LogError 一并套用；补单测。
- **状态**：✅ 已修复（2026-09-15，第 14 轮消纳批）。

---

## RF-55：webhook 域名空解析数组"校验空过"+ `addresses[0]` 越界误报

- **位置**：`BBDown/Infrastructure/BBDownApiServer.cs:953-959`（校验侧 foreach 空数组零次迭代直接 `return true`）、`:1300-1317`（连接侧再解析取 `addresses[0]`——空数组抛 `IndexOutOfRangeException`）、`:1352`（回调过滤器白名单无该类型）→ 冒泡 `:1060` `catch (Exception)`。
- **发现**：管理员配置域名 webhook 且回调时刻解析出零地址（部分 DNS 应答形态）时：校验"空过放行"，连接侧越界异常把**已成功且已持久化的任务**打成误导性的"任务异常终止"——正是 `:1352` 注释明确要避免的误报语义（此前只为 TimeoutException 修过一次的旁支遗漏）。
- **结论**：采纳——两侧对齐：`IsSafeCallbackUrlAsync` 对 `addresses.Length == 0` 返回 false；`SendCallbackAsync` 空数组记 Warn 跳过；过滤器放宽为 `catch (Exception)` + Warn（该 catch 目的只是"回调失败不影响任务"，无需类型白名单）。
- **状态**：✅ 已修复（2026-09-15，第 14 轮消纳批）。

---

## RF-56：`SanitizeUntrustedOptions` 漏 `Area`（未校验拼入官方 API query + 跳过登录检查）

- **位置**：`BBDown/Infrastructure/BBDownApiServer.cs:1089`（客户端 `option.Area` 原样 `Config.Apply`）；下游 `BBDown.Core/Parser.cs:68-69`（`area={Area}` 未编码裸拼 WEB playurl query）、`:138`（intl 同构）、`Workflow.cs:219`（`Area != ""` 跳过登录检查）、`Pages.cs:173-182`（akamaized 源强制替换触发）。
- **发现**：对照 `MyOption.cs` 全字段核查 `SanitizeUntrustedOptions`：Host/EpHost/TvHost/UposHost 四个"改变请求去向"的字段都已白名单 pin 官方域，唯 Area 未处理——serve 客户端可传任意文本。因 host 已 pin 官方、凭据不外发第三方（`IsTrustedCookieHost` 第二道闸），降 Low；实际影响：① 向官方 API 注入任意 query 参数语义（覆盖 qn/try_look 等）；② 跳过登录检测产生"已检测登录"误导日志；③ 与 `--area` 文档语义（hk|tw|th 枚举）不符的值畅通无阻。
- **结论**：采纳——与 host 字段同法：`req.Area` 仅接受 `hk`/`tw`/`th`（大小写不敏感），否则回落 `""`（一行 + 一个测试断言）。
- **状态**：✅ 已修复（2026-09-15，第 14 轮消纳批）。

---

## RF-57：`ToolFinder` 在 CWD 搜索 mp4decrypt/device.wvd（可执行劫持信任边界自相矛盾）

- **位置**：`BBDown/Application/ToolFinder.cs:23`（`localDirs = new[] { AppContext.BaseDirectory, Environment.CurrentDirectory }`）→ 调用点 `Decrypt.cs:45/:92`；对照 `ExternalToolHelper.cs:68-76` 注释明言"绝不搜索当前工作目录……会静默执行本地伪造文件（可执行文件劫持）"——ffmpeg/mp4box/aria2c 已按此收口，唯 DRM 路径漏网。
- **发现**：用户在不可信目录运行 `--decrypt-drm` 且目录中存在预置的伪造 `mp4decrypt(.exe)`、且用户未安装 Bento4（PATH 未命中，DRM 场景常见）时，CWD 命中被静默执行；`device.wvd`（密钥材料）同从 CWD 读取。需本地攻击者/不可信目录前置条件，DRM 属小众功能，定 Low。
- **结论**：采纳——`FindTool` 把 CWD 移出搜索（或复用 `ExternalToolHelper.FindExecutable`）；`--mp4decrypt-path/--wvd-path` 显式路径分支保留。
- **状态**：✅ 已修复（2026-09-15，第 14 轮消纳批）。

---

## RF-58：`FormatSavePath` 轨道元数据占位符不过 `GetValidFileName`（RF-18 同族）

- **位置**：`BBDown/Application/PathHelper.cs:61-67`——`<dfn>/<res>/<fps>/<videoCodecs>/<videoBandwidth>/<audioCodecs>/<audioBandwidth>` 裸替换；对照同文件 `:52/:55/:59/:72-73`（title/pageTitle/ownerName/publishDate/videoDate 全过 `GetValidFileName`）。
- **发现**：dfn/codecs 是服务器透传值（`GetValueAsStringSafe`），镜像站（`--host` 可配）或 `--insecure` 中间人下发含 `/` 或 `..` 的值即可穿越路径——与 RF-18 采纳的威胁模型（"镜像站与 --insecure 中间人正是项目明确采纳的对抗源"）同构。serve 下 FilePattern 已清零、经 `AddDfnSuffix` 复活为固定模板，数据源被 pin 官方 API 风险极低；主要面是 CLI + 镜像站。
- **结论**：采纳——该分支占位符统一套 `GetValidFileName`（与 title 族一致）。
- **状态**：✅ 已修复（2026-09-15，第 14 轮消纳批）。

---

## RF-59：登录轮询 3xx 无 Location 被误报为"重定向跳数超过上限"

- **位置**：`BBDown.Core/Util/HTTPUtil.cs:321-324`（`GetWebSourceWithSetCookiesAsync` 逐跳循环：`if (location is null) break;`）→ 落到方法尾 `:347` 抛 `InvalidOperationException("重定向跳数超过上限 (10)")`；对照同构方法 `GetWebSourceAnonymousCheckedAsync:403-404` 的处理是 `return current`。
- **发现**：3xx 响应缺 Location 头（如 300 Multiple Choices、或网关只回状态码）时，`break` 跳出循环后落到"超限"异常——单跳无目标被报成重定向超限，语义完全不符，且该确定性失败会抛给登录流程。RF-13 修复时引入的结构。
- **结论**：采纳——`location is null` 分支直接读 body 返回（与 2xx 同路径），或至少修正错误消息。
- **状态**：✅ 已修复（2026-09-15，第 14 轮消纳批）。

---

## RF-60：`Audio.shortCodecs` 文化敏感 `ToUpper()`

- **位置**：`BBDown.Core/Entity/Entity.cs:174`（`codecs.ToUpper()` 未指定 culture）；消费点 `TrackSort.cs:33`（`encodingPriority.GetValueOrDefault(a.shortCodecs, 100)`）。
- **发现**：tr-TR 区域下含 `'i'` 的服务器可控 codecs 串经 `ToUpper()` 变 `'İ'`（U+0130），选轨优先级查表失败静默退化为默认 100——与其它 culture 的选轨结果不同。现取值域（M4A/FLAC/E-AC-3）暂不含小写 i，属潜伏项；codecs 是服务器透传值，未来新编码（如小写别名）即触发。
- **结论**：采纳——`ToUpperInvariant()`（RF-19/RF-5 文化收口族，一行）。
- **状态**：✅ 已修复（2026-09-15，第 14 轮消纳批）。

---

## RF-61：文档族 6 项（Low）

- **①** `docs/wiki/CLI-Reference.md:91` + `docs/wiki/Batch-and-Automation.md:36`：`--save-archives-to-file` 产物写成"工作目录下的 `archives.txt`"——实际是**程序目录**（`Program.cs:42` `APP_DIR = AppContext.BaseDirectory`）下的 `BBDown.archives`（`Archive.cs:16/:29`，`aid|aid|` 追加格式），按文档去找必然扑空。
- **②** `README.md:295-314` 占位符表缺 `<videoDate>`：RF-41 只修了 wiki（Configuration-and-Templates.md:69/:92），README 同页表漏改，仍 18 行（实际 19 项）。
- **③** `API.md:57-60` `/add-task` Response 缺 413：64KB 上限 + `RequestBodyTooLargeException`→413 有代码（`BBDownApiServer.cs:306`）、有契约测试（`ServeApiHttpTests.cs:119`）、有注释，文档零提及。
- **④** `API.md:148` + `docs/wiki/API-Server-and-Docker.md:38`：忽略字段清单（17 项）缺 `configFile`——第 13 轮 Info③ 已防御性清零（`BBDownApiServer.cs:784`），权威口径未同步。
- **⑤** `API.md:113/:174`：指导用户"通过 serve 启动时的 `--work-dir` 指定默认工作目录"——`ServeSettings` 仅 5 个选项（ServeCommand.cs:13-32），`BBDown.config` 不合并子命令，该选项**不存在**；安全设计的"替代方案"实无此物。最小改动是修正文档为"启动 serve 前切换进程工作目录"（或给 ServeSettings 增加 `--work-dir`）。
- **⑥** `API.md:134`：称 `ErrorMessage`"文本已经过单行化净化"——实际 `SanitizeErrorMessage`（:480-486）只做绝对路径→文件名替换，不做 CRLF 折叠（JSON 序列化会转义 `\n`，无 API 面风险，但措辞与实现不符）。
- **结论**：采纳——随下一文档批一并消纳（⑥ 可与 RF-54 的日志单线化工作顺带对齐措辞）。
- **状态**：✅ 已修复（2026-09-15，第 14 轮消纳批）。

---

## RF-62：DRM 取钥链 `CryptographicException` 穿透两级过滤器（整批中止）

- **位置**：`BBDown.Core/DRM/WidevineCdm.cs:324`（`ParseResponse` 内 `_device.Rsa.Decrypt(encSessionKey, RSAEncryptionPadding.OaepSHA256)`——`:317-325` 的 try 只保护**第一次** OAEP-SHA1 尝试，回退分支自身无捕获）；`CkcDecryptor.cs:9-26`（`DrmDecryptor.GetKeyWidevineAsync` 无 try/catch，直接传播）；`BBDown/Application/Decrypt.cs:84`（取钥 catch 白名单 `IOException or InvalidOperationException or FormatException`）；两级过滤器 `Download.cs:98`/`:1097` 白名单亦无 `CryptographicException`。
- **发现**：RSA 会话密钥两种 padding 都解不开时（device.wvd 与服务器协商不匹配、Widevine 版本差异——区别于设备证书被吊销，后者走 `:305-311` 的"异常响应类型"分支 `return null`）抛 `CryptographicException`。该类型全仓库仅 4 处捕获（`WidevineCdm.cs:321/:401`、`WvdDevice.cs:164/:171`），取钥出口与下载页两级过滤器均无 → 异常从 `DownloadPageAsync` 冒泡穿过 `DownloadPagesAsync` 的 `foreach`，**剩余分 P 全部放弃下载，且 webhook 通知与 `failedPages` 汇总都不再执行**（正是过滤器设计要防的逃逸面，与 RF-43/47/48/49 同族）。对照 RR 设计意图：若在 `Decrypt.cs:84` 接住，会走 `:92-103` 的"密钥缺失"分支抛 `InvalidOperationException`（在两级白名单内）→ 单页记失败继续，符合 DRM 失败仅影响本页的语义。
- **结论**：采纳——`Decrypt.cs:84` 白名单补 `CryptographicException`（或与 RF-47 先例同构，在 `WidevineCdm.ParseResponse`/`DrmDecryptor` 源头转译为 `InvalidOperationException` 并带设备证书提示）；建议补单测：构造最小 wvd + 不可解的 session key，断言抛出的是被过滤器接住的类型。
- **状态**：⭕ **前提不成立，维持现状**（2026-09-18，第 16 轮消纳）。亲验：`WidevineCdm.GetKeysAsync`（`WidevineCdm.cs:26-56`）已在 `:51` 用 `catch (Exception ex) { …; return null; }` 包裹整个取钥链（blame 可追至 2026-05-29，早于本登记），`ParseResponse:324` 的 `CryptographicException` 在此被吞掉并返回 null——**异常根本不出 `WidevineCdm`，不可能到达 `Decrypt.cs:84` 或两级过滤器**，"整批中止"面不存在。故登记所述逃逸链失实，无改动；原拟在 `Decrypt.cs:84` 补类型的修改已回退（死代码）。

---

## RF-63：`FormatSavePath` 的 `res`/`fps` 占位符未净化（RF-58 消纳缺口）

- **位置**：`BBDown/Application/PathHelper.cs:64-65`（`"res" => videoTrack.res`、`"fps" => videoTrack.fps` 裸替换）；同分支 `:63`（`dfn`）、`:66`（`videoCodecs`）、`:68`（`audioCodecs`）均已过 `GetValidFileName(..., filterSlash: true)`；意图注释 `:61-62` 逐字列出"**dfn/res/fps/codecs** 是服务器透传值（镜像站 --host 或 --insecure 中间人可控，可含 '/' 或 '..'）……统一过 GetValidFileName"。
- **发现**：RF-58 的消纳记录声称"该分支统一净化"，实际只覆盖了 4 个占位符中的 3 个（`dfn`/`videoCodecs`/`audioCodecs`），`res`/`fps` 漏网——**修复与同处注释的声称自相矛盾**。两字段均为服务器透传值：`Parser.cs:419-420`（`width + "x" + height`、`frame_rate` 节点原文）、`BangumiInfoFetcher.cs:87` / `IntlBangumiInfoFetcher.cs:105`（`$"{w}x{h}"`）。恶意镜像站/中间人返回含 `/` 或 `..` 的 `width`/`frame_rate` 即可让 `--file-pattern` 中含 `<res>`/`<fps>` 的模板实现路径穿越或写出意外子目录，与 RF-58 登记的触发面完全同级。属"消纳批只验修复在位、未验是否覆盖登记声称的全部引用面"的又一实例（同 RF-51）。
- **结论**：采纳——`:64-65` 与 `:63/:66` 同构包 `GetValidFileName(..., filterSlash: true).Trim().TrimEnd('.').Trim()`。
- **状态**：✅ 已修复（2026-09-18，第 16 轮消纳批）：`PathHelper.cs:66-67` 补 `GetValidFileName(..., filterSlash: true).Trim().TrimEnd('.').Trim()`（含 null 合并）；+1 回归测试 `PathFormatTests.FormatSavePath_ResAndFps_AreSanitized`（已验证：把 res 改回裸值则该测试失败）。

---

## RF-64：评论保存 catch 白名单窄于页面级过滤器（成功页被误判失败）

- **位置**：`BBDown/Application/Download.cs:819-820`（评论保存的嵌套 catch：`HttpRequestException or JsonException or InvalidOperationException or IOException or TaskCanceledException or KeyNotFoundException or FormatException`）；意图注释 `:798-799`；上游页面级过滤器 `:98`。
- **发现**：`:798` 注释明确要求"评论是附加功能：任何失败都只降级为警告，绝不能触发页面级重试或中止整批"，但嵌套 catch 漏了 `TimeoutException`（RF-49/E1 统一后 HTTP 超时的主要抛型）、`AggregateException`、`UnauthorizedAccessException`。评论 API 超时（大评论区真实场景）时异常逃逸至页面级 `:98` 过滤器（其含 `TimeoutException`）→ 该页被 `:106-108` 记为失败并 `continue`。后果是**已成功下载并混流的页面被误判失败**：最终 `failedPages` 非空 → 退出码非 0 + `NotifyWebhook` 报失败 + `SaveArchivesToFile` 不入档——与注释声称的"只降级为警告"直接冲突。（注：不会重下该页——页面级 catch 是 `continue` 语义；风险点是结果误报而非重复下载。）
- **结论**：采纳——`:819` 的 `when` 补 `or TimeoutException or AggregateException or UnauthorizedAccessException`；更彻底的做法是把评论抓取整体隔离为"绝不外抛页面级"的独立块（语义更清晰，避免今后新增异常类型再次漏网）。
- **状态**：✅ 已修复（2026-09-18，第 16 轮消纳批）：评论 catch 补 `TimeoutException or AggregateException or UnauthorizedAccessException`（`Download.cs:819-821`）。

---

## RF-65：fetcher 顶层 `GetPropertySafe` 未经 `code` 先行检查——中文诊断不可达（RF-52 同族残留）

- **位置**：`SpaceVideoFetcher.cs:236`（`doc.RootElement.GetPropertySafe("data")`，其下 `:248-274` 精心构造了"响应中缺少 page 节点"/"page 中缺少 count 字段"等中文 `InvalidOperationException` 诊断——因 `:236` 先抛英文 `KeyNotFoundException` 而**永不可达**）；同族顶层取节点：`SpaceVideoFetcher.cs:41`、`CheeseInfoFetcher.cs:23`、`FavListFetcher.cs:36/:59`、`NormalInfoFetcher.cs:23/:95`、`IntlBangumiInfoFetcher.cs:50-52`；异常源 `JsonElementExtensions.cs:69`（`throw new KeyNotFoundException($"JSON property not found: '{propertyName}' (available keys: …)")`）；对照 `FavListFetcher.cs:148-157`（分页块）**已有** `code` 先行 + `TryGetProperty` 守卫。
- **发现**：RF-52 为 Series/MediaList fetcher 建立了"先查 code 再取 data"的范式，但仅修了那两个文件；同类模式在 6 处 fetcher 顶层仍在。后果分两档：① `SpaceVideoFetcher.cs:236`——响应缺 `data` 节点（镜像站/intl/中间人可构造 `code=0` 但结构缺失）时抛英文 KNFE，使 `:248-274` 的全部中文诊断失效，整个空间投稿抓取失败且用户拿不到可定位的消息（其请求洪泛面已由 `:204-208` 空页 break 防护，故非资源问题）；② 其余 5 处为单对象解析路径，失败面有限但同样牺牲诊断质量。属 RF-52 的"同类引用面漏网"，非新机制。
- **结论**：采纳——`FetchPageAsync`（`SpaceVideoFetcher.cs:236`）改用 `FavListFetcher.cs:148-157` 同款 `code` 先行 + `TryGetProperty` 守卫；其余 5 处随同批统一为"先 `code` 后 `data`"序（纯诊断质量改善，可降优先级）。
- **状态**：✅ 已修复（2026-09-18，第 16 轮消纳批）：6 处 fetcher 顶层改逐级判空/先 code 后 data（`NormalInfoFetcher` ×2、`CheeseInfoFetcher`、`FavListFetcher` ×2、`IntlBangumiInfoFetcher`、`SpaceVideoFetcher` ×2），缺节点给可读中文诊断。注：`NormalInfoFetcher` 主路径原本已先查 code，本轮只补 data 缺失诊断。

---

## RF-66：`NoRedirectClient` 超时 1 分钟——超时矩阵唯一非 2 分钟项

- **位置**：`BBDown.Core/Util/HTTPUtil.cs:222/:224`（`_noRedirectClient`/`_insecureNoRedirectClient` 均 `CreateClient(allowRedirect: false, TimeSpan.FromMinutes(1), …)`）；对照同一超时矩阵：`AppHttpClient:120/:122`、另一 noRedirect 客户端 `:127/:129`、`:142` 全部为 `FromMinutes(2)`，流式客户端 `:208/:210` 为 `Timeout.InfiniteTimeSpan`；上游超时预算 `AppSettings.cs:23 ApiTimeoutMs = 120000`。
- **发现**：`NoRedirectClient` 是矩阵中唯一的 1 分钟项，与 `ApiTimeoutMs`（120s）不一致。RF-50 把 `GetWebSourceCoreAsync` 的 `sendCookie` 路径从 `AppHttpClient` 切到该客户端后，虽然方法内 `timeoutCts.CancelAfter(ApiTimeoutMs)` 仍按 120s 控整体预算，但 `HttpClient.Timeout` 在 `ResponseHeadersRead` 下只约束"收到响应头"阶段——**带 Cookie 请求的头阶段实际上限被隐性砍半为 60s**，由 `catch (OperationCanceledException) when (!token.IsCancellationRequested)` 当作瞬时超时降级/重试。第 13 轮 TV 登录切池时该 1 分钟是**有意**设定（与 WEB 轮询同池，已在 CHANGELOG 记录），但 RF-50 的切换未评估同一超时差异，属隐性回退。
- **结论**：采纳——`_noRedirectClient`/`_insecureNoRedirectClient` 超时对齐为 `TimeSpan.FromMinutes(2)`（与 `AppHttpClient` 同不变量）；若确有理由保持 1 分钟，应在 `:221-224` 注释写明与 `ApiTimeoutMs` 的关系。
- **状态**：✅ 已修复（2026-09-18，第 16 轮消纳批）：`HTTPUtil.cs:225-228` 两池超时对齐 `TimeSpan.FromMinutes(2)`。

---

## RF-67：PR CI `vulnerability-scan` 门禁失效（退出码恒为 0）

- **位置**：`.github/workflows/pr.yml:49-52`（`run: dotnet list BBDown.sln package --vulnerable --include-transitive`——无任何失败判定）；对照同文件 `:70-71` 的 format 门禁用 `--verify-no-changes` 真失败语义。
- **发现**：`dotnet list package --vulnerable` **无论是否发现漏洞都返回退出码 0**（NuGet 官方跟踪项 NuGet/Home#11315，多个独立来源一致确认），该命令只是"打印报告"。因此 `vulnerability-scan` job 永不失败，作为 PR 检查项**形同虚设**——依赖出现已知漏洞时 PR 照常合并。（对比：`.NET 8+` 的 `dotnet restore` 会输出 NU1901-NU1904 警告，但本仓库 PR CI 的 `Build` 步骤未将警告提升为错误。）
- **结论**：采纳——改用可判定的形式，例如 `dotnet list BBDown.sln package --vulnerable --include-transitive --format=json > vuln.json` 后用 `jq -e '.projects | .. | .severity? // empty' vuln.json` 判定并 `exit 1`；或退一步用 `grep -q "has the following vulnerable packages"` 断言（注意避免误匹配项目名）。建议顺带评估是否把 NU1901-NU1904 提升为 build 错误（`TreatWarningsAsErrors` 的定向 subset）。
- **状态**：✅ 已修复（2026-09-18，第 16 轮消纳批）：`pr.yml` 改 `--format json` + `jq -e '[.. | objects | select(.vulnerabilities?)] | length == 0'` 真失败语义。

---

## RF-68：`EntityTests` 的 RF-60 回归测试假绿（输入不含 `'i'`）

- **位置**：`BBDown.Tests/EntityTests.cs:45`（`codecs = "e-ac-3"`）与 `:46`（`Assert.Equal("EAC3", a.shortCodecs)`）；注释 `:43` 自称"含 'i' 的编码串"；被测实现 `BBDown.Core/Entity/Entity.cs:189`（`codecs.ToUpperInvariant().Replace("-", string.Empty)`）。
- **发现**：tr-TR 的文化敏感陷阱是 `'i' → 'İ'`（U+0130），只有输入含小写 `'i'` 才会触发。测试输入 `"e-ac-3"` **不含 `'i'`**：`ToUpperInvariant()` 与回退后的 `ToUpper()` 在 tr-TR 下产出完全相同（`E-AC-3` → `EAC3`），断言恒成立。即**把实现改成有缺陷的 `ToUpper()`，该测试仍然通过**——RF-60 的回归防线实际形同虚设（这与本仓库 G 组"防 CI 假绿"的主题同类，属"看起来绿、实际没测到"）。注：RF-60 的代码修复本身（`ToUpperInvariant`）是可核查的、正确的，仅测试无效。
- **结论**：采纳——输入改为确含小写 `'i'` 的 codecs 串（如 `"mlpa"`/`"avci"`，或直接构造 `"eiac3"` 类合成值并在注释说明为回归专用输入），断言 `shortCodecs` 在 tr-TR 下仍为文化不变结果。
- **状态**：✅ 已修复（2026-09-18，第 16 轮消纳批）：`EntityTests` 输入改 `"avci"`（含小写 'i'），断言 `AVCI`；已验证回退到 `ToUpper()` 则该测试失败（原输入下仍通过——假绿消除）。

---

## RF-69：测试套件未隔离系统代理（本机代理在线时回环测试假红）

- **位置**：`BBDown.Tests/ServeApiHttpTests.cs:74`（`Client = new HttpClient { BaseAddress = new Uri(BaseUrl) };`——全测试套件唯一的 `HttpClient` 构造点，未设 `UseProxy = false`）；受影响用例 `:212`（`HostValidation_WithoutToken_RejectsNonLoopbackHost`）、`:233`（`HostValidation_WithoutToken_AcceptsLoopbackHosts`）。
- **发现**：.NET 的 `HttpClient` 默认 `UseProxy = true`，在 Windows 上读**系统代理设置**。当本机运行 Clash/V2Ray 类代理（本机实况：git 全局 `http.proxy = 127.0.0.1:7890`）时，回环 HTTP 测试的请求会经代理转发，代理按 Host 头策略返回 400/502，导致断言失配。**实测复现**（本地）：默认环境 `dotnet test --filter "FullyQualifiedName~HostValidation_WithoutToken"` → 2/2 失败（`localhost` 返回 400、POST 返回 400 而非 403）；加 `NO_PROXY=* HTTP_PROXY= HTTPS_PROXY=` 后同命令 → **2/2 通过**，且全量 `700/700` 全绿。结论：**代码无缺陷，失败纯属环境污染**（本机 `127.0.0.1:7890` 代理介入）。风险面双向：代理在线时假红（开发者白排查），代理恰好返回期望码时理论上可假绿。CI（ubuntu-latest，无系统代理）不受影响——这正是"本地红、CI 绿"的根因。
- **结论**：采纳——`RunningServer` 的客户端改为 `new HttpClient(new SocketsHttpHandler { UseProxy = false }) { BaseAddress = … }`（回环服务测试绝不应经代理）；可顺带在测试类注释记录该环境依赖。属 G 组"测试结构加固"的直接延伸。
- **状态**：✅ 已修复（2026-09-18，第 16 轮消纳批）：`ServeApiHttpTests.cs:74` 客户端改 `new HttpClient(new SocketsHttpHandler { UseProxy = false })`。

---

## RF-70：日志注入旁支——`WatchLater`/`Live` 命令的服务器可控字段未脱敏（RF-54 同族）

- **位置**：`BBDown/Commands/WatchLaterCommand.cs:81`（`Logger.Log($"--- 下载 av{aid} {title} ---")`，`title` 来自 `:143 item.GetValueAsStringSafe("title")` 服务器原文）；`BBDown/Commands/LiveCommand.cs:63`（`Logger.Log($"直播间: {info.Title} (UP: {info.Uname})…")`，二者均为 `LiveStreamUtil.ResolveAsync` 解析出的服务器字段）；可复用净化器 `BBDownApiServer.SanitizeLogString`（`BBDownApiServer.cs:492`，`internal static`，BBDown 项目内可达）。
- **发现**：RF-54 已把日志单行化下沉到 `UrlResolver.ResolveAsync` 返回前，并覆盖 `req.Url` 全部调用点，但**未覆盖命令层把服务器透传文本直接写日志**的路径。视频标题/直播间标题可含 `\r\n`，可将单条日志伪造为多条（日志污染/审计误导），与 RF-25/RF-54 的防护目标同族。触发面与 RF-25 同级（需服务器或中间人构造字段值），故判 Low 而非 Medium。
- **结论**：采纳——两处 `title`/`Uname` 经 `BBDownApiServer.SanitizeLogString(...)` 后写日志（与 RF-54 同构）；可顺带复查命令层是否还有其它服务器文本入日志的点。
- **状态**：✅ 已修复（2026-09-18，第 16 轮消纳批）：`WatchLaterCommand.cs:81-83`、`LiveCommand.cs:63-65` 的服务器字段过 `SanitizeLogString`。

---

## RF-71：`API.md` 时间戳字段"本机时区"措辞与实现不符（文档）

- **位置**：`API.md:125`（`TaskCreateTime` "Unix时间戳，精确到秒，**本机时区**"）、`:129`（`TaskFinishTime` 同）；实现 `BBDownApiServer.cs:1048`/`:1228`（`DateTimeOffset.Now.ToUnixTimeSeconds()`）。
- **发现**：`ToUnixTimeSeconds()` 的定义是"自 1970-01-01T00:00:00Z 的秒数"，**与时区无关**（UTC 纪元秒）。文档的"本机时区"措辞会让客户端按服务器本地时区解释绝对值，与真实语义偏差（仅同一字段的差值运算不受影响）。属 RF-33/39/42/61 文档漂移同族，为措辞级偏差。
- **结论**：采纳——改为"UTC 纪元秒（与时区无关）"；若确需"本机本地时间"语义，则应改实现（不推荐，会引入时区依赖）。建议在 API.md 的字段说明段落统一一次时间语义口径。
- **状态**：✅ 已修复（2026-09-18，第 16 轮消纳批）：`API.md:125/:129` 改为"UTC 纪元秒（`ToUnixTimeSeconds()`，与时区无关）"。

---

## 第 16 轮：全库续审（2026-09-18）

> 第 14 轮消纳批（v1.6.18）合入后的续审：四路深查（下载管线/命令层、Core 解析网络层、serve 服务层、测试/CI/文档）+ Medium 全量人工复核。新发现 **6 Medium + 11 Low**，登记 RF-72~RF-88。**仅登记评估未修复**，修复待消纳批。
>
> 基线：dotnet build -c Release 0 警告 0 错误；单测 700/700 全绿（PR gate 过滤器）；dotnet format --verify-no-changes 通过（exit 0）。RF-43~RF-61 修复抽查无回归（异常规范化的下一类 `InvalidDataException` 形成 RF-72；RF-18/RF-58 只净化叶子元数据形成 RF-73）。审查方法备注：对每个 Medium 做了"修复是否覆盖登记声称的全部引用面"的旁支复查——产出 RF-72/RF-73/RF-74 三条旁支与 RF-75/RF-76/RF-77 三条假绿门禁。

## RF-72：`InvalidDataException` 穿透下载两级过滤器（RF-28/RF-51 新增防线的逃逸面）

- **位置**：抛点 `BBDown.Core/Util/HTTPUtil.cs:69`（`EnsureBodySizeAllowed`）与 `:54`（`ReadContentBoundedAsync` 逐块累计）——RF-28/RF-51 新增的 64MB 响应体上限；`BBDown.Core/AppHelper.cs:395/:400/:403`（`ReadMessage` gRPC 帧过短/压缩标志非法/载荷长度非法）与 `:477`（`GzipDecompress` 48MB 上限）。两级过滤器 `BBDown/Application/Download.cs:98`（批级）与 `:1097`（页面级）白名单均**无** `InvalidDataException`。
- **发现**：`InvalidDataException` 继承 `SystemException` 而非 `IOException`（`IOException` 子句不匹配）。RF-28/RF-51 读取上限的注释自称"确定性失败，不命中 5xx/超时重试过滤器"，但该类型同样不在**页面失败隔离**过滤器内：`GetWebSourceCoreAsync` 的逐次 `try`（`HTTPUtil.cs:508-611`）只捕 `HttpRequestException`/`OperationCanceledException`；`Parser.ExtractTracksAsync` 对 `GetPlayJsonAsync` 无包裹。因此一次巨包/畸形 gRPC 帧即令 `DownloadPagesAsync` 的 `foreach` 冒泡退出——剩余分 P 全弃、`failedPages` 与 `NotifyCompletionAsync`（webhook）全丢，与 RF-14/43/47 同族逃逸面。触发源是项目明确采纳的对抗源（`--insecure` 中间人、`--host`/`--ep-host`/`--tv-host` 镜像站、`--use-app-api` 非 gRPC 帧响应）。
- **结论**：采纳——按既有先例在源头规范化（`HTTPUtil`/`AppHelper` 改抛 `InvalidOperationException`），或将 `InvalidDataException` 补入两级过滤器作纵深。
- **状态**：✅ 已修复（2026-09-18，第 16 轮消纳批）：`Download.cs` 两级过滤器 + 命令级过滤器（SubCommand ×2/WatchLater）补 `InvalidDataException`。

---

## RF-73：服务器可控 `aid`/`cid`/`epid` 未净化直拼路径（RF-18/RF-58 只净化了同表达式的叶子元数据）

- **位置**：`BBDown/Application/Download.cs:394-396`（`ResolveWorkPath($"{p.aid}/{p.aid}.P{p.index}.{p.cid}.mp4")` 等三处）、`:406-409`（`ResolveWorkPath(p.aid)` + `Directory.CreateDirectory`）、`:772/:1022/:1031`、`:1128`（`CleanNonResumableWorkArtifacts(p.aid)` 对其做 `Directory.Delete(dir, true)`）；`BBDown.Core/Parser.cs:529`；`BBDown.Core/Util/SubUtil.cs:19`（`BuildSubtitlePath` 只净化 `lan`）；`BBDown/Application/PathHelper.cs:57-58`（`<aid>`/`<cid>` 占位符返回原值）。录入点：`Fetcher/FavListFetcher.cs:125-126`、`MediaListFetcher.cs:95-96`、`SeriesListFetcher.cs:77-78`、`SpaceVideoFetcher.cs:241`、`BangumiInfoFetcher.cs:94-96`、`IntlBangumiInfoFetcher.cs:112-114`（均 `GetValueAsStringSafe("id")`，无数字校验）。
- **发现**：RF-18 明确只净化 `lan`/`audio_id`，RF-58 只净化 `dfn/res/fps/codecs/bandwidth`——同一条路径表达式里的**目录/文件名键** `aid`/`cid`/`epid` 从未被任何轮次净化。它们逐字来自响应体（`id` 字段），`ResolveWorkPath` 只 `Path.Combine`/`Path.GetFullPath`（`PathUtil.cs:92-97`），`Page` 构造逐字赋值（`Entity.cs:50-106`）。因此 `--insecure` 中间人或 `--host`/`--ep-host` 镜像站下发 `id` 为 `..\..\..\tmp\x` 的条目即可令产物写出 `--work-dir` 之外；`CleanNonResumableWorkArtifacts` 还会对该原值路径执行递归删除。需服务器可控 ID 源（镜像站/中间人），故定 Medium 而非 High。
- **结论**：采纳——在读取点对 `aid`/`cid`/`epid` 施加 `[0-9]+` 白名单或 `GetValidFileName`（单一收口点），并在 `FormatSavePath`/`BuildSubtitlePath` 对对应占位符兜底。
- **状态**：✅ 已修复（2026-09-18，第 16 轮消纳批）：`Page.aid/cid/epid` 改属性 setter 经 `PathUtil.SanitizePathSegment`（= `GetValidFileName(filterSlash:true)`）单一收口；纯数字/BV 为恒等变换，不影响 RF-48 bvid 回退。+1 回归测试。

---

## RF-74：serve 401 日志 sink 未脱敏且未限速（可灌盘/伪造日志行）

- **位置**：`BBDown/Infrastructure/BBDownApiServer.cs:190`（`Logger.LogWarn($"serve 认证失败（401）: {clientIp} {context.Request.Path}")`）与 `:194`（限速分支再记一行）；同文件 `:211` 的同族 sink 已过 `SanitizeLogString`，净化器在 `:492`；写入器 `BBDown.Core/Logger.cs:68-70`（`AutoFlush` 持久文件，**无大小上限/无轮转**），路径 `CWD/bbdown-api.log`（`Program.cs:228`，Docker 下 `/app`）。
- **发现**：**未认证**客户端（带错/缺 `X-Serve-Token`，非回环+token 部署即支持面）每次请求 `≥1` 行服务器可控文本（`Request.Path`，长度受 Kestrel `MaxRequestLineSize` 约束；`--trusted-proxy` 时 `X-Forwarded-For` 亦入行）同时写控制台与 `bbdown-api.log`。日志发生在 `IsAuthLockedOut`（`:191`）判定**之前**，限速分支又记一次——RF-9 的限速对日志量零节制，可持续刷盘填充磁盘；若 Kestrel 将 `%0D%0A` 解码进 `PathString`，更能伪造日志行/注入 ANSI（RF-25/RF-54 同族漏网 sink）。
- **结论**：采纳——两处插值过 `SanitizeLogString` 并截断（如 200 字符），并对同一 IP 的失败日志做独立节流。
- **状态**：✅ 已修复（2026-09-18，第 16 轮消纳批）：401 sink 改 `TruncateForLog`（`SanitizeLogString` + 截断 200 字符）；限速分支只记 IP、不回显攻击者可控路径；判断只求值一次。

---

## RF-75：假绿测试族——复刻实现的副本而非被测代码

- **位置**：`BBDown.Tests/ArchiveGranularityTests.cs:11-28`（私有 `ArchiveTracker` 复刻 `Download.cs:63`/`:84`/`:111-120` 的入档计数）；`BBDown.Tests/DownloadProgressAggregationTests.cs:12-24`（私有 `ProgressAggregator` 复刻 `BBDownDownloadUtil.cs:781-782`）。
- **发现**：两套测试只驱动本地副本，从不引用生产类型。变异实验：把生产入档判定改为"首个分 P 即入档"或删除 `failedAids`，或把 `downloaded - previous` 改为 `downloaded`——全部 6+4 个用例仍绿。即两处已修复缺陷（整 aid 全成功才入档、重试回退进度）**无真实回归网**，与 G 组"防 CI 假绿"主题同类。
- **结论**：采纳——把计数/聚合抽为生产 `internal` helper（`Archive.cs`/`BBDownDownloadUtil.cs`）并由测试直接驱动之。
- **状态**：✅ 已修复（2026-09-18，第 16 轮消纳批）：抽生产 `Program.ArchiveTracker`（Archive.cs）与 `BBDownDownloadUtil.ProgressAggregator`；两套测试改驱动生产类型；两个 helper 均经变异验证（改回缺陷实现则测试失败）。

---

## RF-76：AOT 绑定防线只覆盖 3/10 个 Settings 类

- **位置**：`BBDown.Tests/AotCliBindingTests.cs:28-31`（`SettingsTypes` = `MyOption`/`ServeSettings`/`LoginSettings`）；未覆盖 `LiveSettings`、`ArticleSettings`、`WatchLaterSettings`、`SubAddSettings`、`SubListSettings`、`SubRemoveSettings`、`SubCheckSettings`（实际 6 个命令 Settings，总 10 个类型，含 `Sub` 分支 4 个）。
- **发现**：该防线用意是"改动参数类型在此失败，而非等用户拿到 release 二进制"。变异实验：把 `WatchLaterSettings.Limit` 从 `int` 改为 `List<int>` 或自定义枚举，`EveryCommandOption_UsesAnAotSafeType` 仍绿（不枚举这些类型），而 Native AOT 产物在 `BBDown watchlater` 绑定时抛异常——正是防线要拦的场景。
- **结论**：采纳——`SettingsTypes` 补齐全部 10 个类型（一行理论数据）。
- **状态**：✅ 已修复（2026-09-18，第 16 轮消纳批）：`AotCliBindingTests.SettingsTypes` 补齐全部 10 个 Settings 类型。

---

## RF-77：`local-integration` 门禁可静默执行 0 个测试

- **位置**：`.github/workflows/pr.yml:75-94`（安装 ffmpeg 后直接 `dotnet test --filter Category=LocalIntegration`，无"ffmpeg 已就绪"断言）；相关测试 `BBDown.Tests/MuxerArgsTests.cs:337/:378/:504` 使用 `if (!TryLocateFfmpeg(out _)) return;`。
- **发现**：`pr.yml:73-74` 注释自称该 job "可以作为硬性门禁阻断 PR"，但测试在 ffmpeg 未定位到时**静默 return**（不产生断言）。若 apt 安装成功但 PATH 形态不被测试的探测命中（或镜像变化），job 报绿却零有效断言——又一个"看起来绿、实际没测到"的门禁。
- **结论**：采纳——CI 在 `dotnet test` 前置一步断言 `command -v ffmpeg`，或测试在 `CI=true` 时 `Assert.True(TryLocateFfmpeg(...))`。
- **状态**：✅ 已修复（2026-09-18，第 16 轮消纳批）：`pr.yml` 安装 ffmpeg 后插入 `Verify ffmpeg is on PATH`（`command -v ffmpeg`）步骤，封死静默空跑。

---

## RF-78：`WvdDevice.Load` 空文件抛 `IndexOutOfRangeException`（诊断退化）

- **位置**：`BBDown.Core/DRM/WvdDevice.cs:45`（`throw new InvalidDataException($"无法识别的 WVD 文件格式 (首字节: {allBytes[0]})")`）；三个格式探测 `:32`（`>= 4`）、`:38`（`>= 1`）、`:42`（`> 0`）对零长度文件全部不命中。
- **发现**：零字节 `device.wvd`（中断拷贝/空文件/`--wvd-path` 指向空文件）时 `allBytes[0]` 索引空数组，诊断退化为 `Index was outside the bounds of the array.`。不导致崩溃：`WidevineCdm.cs:29-37` 的 `catch (Exception)` 会接住 → `Decrypt.cs:98` 抛可操作的"密钥获取失败"。属诊断质量问题。
- **结论**：采纳——`Load` 补零长度分支（抛带文件格式提示的 `InvalidDataException`）。
- **状态**：✅ 已修复（2026-09-18，第 16 轮消纳批）：`WvdDevice.Load` 在三个格式探测前补零长度分支；+1 回归测试 `Load_EmptyWvd_ThrowsInvalidDataExceptionWithReadableMessage`。

---

## RF-79：有界响应体改造未覆盖 `WidevineCdm` 与 `BBDownLoginUtil` 的裸 `ReadAs*Async`

- **位置**：`BBDown.Core/DRM/WidevineCdm.cs:247`（`ReadAsStringAsync` 读许可证错误体）与 `:255`（`ReadAsByteArrayAsync` 读许可证响应）；旁支（应用层，同批登记）：`BBDown/Infrastructure/BBDownLoginUtil.cs:221` 与 `:255`（`ReadAsByteArrayAsync`）。
- **发现**：RF-28/RF-51 只枚举 `HTTPUtil` 调用点，这 4 处仍用无界 API。因 `LicenseUrl`/`CertUrl` 与 passport 主机均硬编码且 TLS 恒校验，仅端点被攻破或 `--insecure` 降级才可达，故定 Low（登记缺口而非可利用面）。
- **结论**：采纳——统一改走 `ReadContentBoundedAsync`。
- **状态**：✅ 已修复（2026-09-18，第 16 轮消纳批）：`HTTPUtil.ReadContentBoundedAsync` 提为 `public`；`WidevineCdm`（错误体 + 许可证响应）与 `BBDownLoginUtil`（TV auth + 轮询）4 处改经有界读取。

---

## RF-80：fetcher 服务器 `message` 未净化直拼异常消息落日志

- **位置**：`BBDown.Core/Fetcher/NormalInfoFetcher.cs:21`、`BangumiInfoFetcher.cs:24`、`IntlBangumiInfoFetcher.cs:28`、`CheeseInfoFetcher.cs:21`、`FavListFetcher.cs:34/57/152`、`MediaListFetcher.cs:43/72`、`SeriesListFetcher.cs:30/54`、`SpaceVideoFetcher.cs:307`（均 `GetValueAsStringSafe(root, "message")`）。
- **发现**：这些消息经 `Download.cs:106`（`Logger.LogError`）落日志，serve 侧经 `SanitizeErrorMessage`（`BBDownApiServer.cs:482-486`，仅做绝对路径→文件名替换，不剥控制字符）。`Parser.SanitizeServerText`（B3-L3）只覆盖 `Parser.cs:726/739`，fetcher 消息是其同族新实例（未登记）。
- **结论**：采纳——异常消息中的 `message` 过 `SanitizeServerText`（或 `SanitizeLogString`）。
- **状态**：✅ 已修复（2026-09-18，第 16 轮消纳批）：`JsonElementExtensions.SanitizeServerText`（新公开工具）在 8 个 fetcher 共 12 处 `message` 拼接前应用。

---

## RF-81：serve 下 `selectPage`/`danmakuFilter` 无接受上限

- **位置**：`BBDown/Configuration/MyOption.cs:189`（`SelectPage`）、`:148/:152`（`DanmakuFilter`/`DanmakuFilterUser`）均未被 `SanitizeUntrustedOptions`（`:771-858`）触及；`BBDown/Application/Pages.cs:90`（`MaxExpandedPages = 100_000`）在 `:128` **按段**检查，从不累计；消费点 `Download.cs:35`（`string.Join` 写日志行）、`:41`（`Where` + `Contains`）、`DanmakuUtil.cs:86`。
- **发现**：64KB 请求体可构造约 10^4 段 `selectPage`（展开约 10^6 串，≈30-40MB 内存）或约 3×10^4 个 `danmakuFilter` 关键词，产生每任务约 500-1000× 内存放大（× 接受队列深度）与 MB 级日志行、槽位内 CPU 抬升。受 64KB 体上限与接受队列限制，定 Low。
- **结论**：采纳——`ParsePageSelection` 增累计上限，`:35` 日志行截断，serve 下可忽略装饰性的 `DanmakuFilter*`。
- **状态**：✅ 已修复（2026-09-18，第 16 轮消纳批）：`ParsePageSelection` 增累计上限（+1 回归测试）；`Download.cs:35` 日志行截断为前 20 项 + 计数；`SanitizeUntrustedOptions` 清零 `DanmakuFilter*`。

---

## RF-82：隐藏废弃开关可绕过 serve "FilePattern 已清零"不变量

- **位置**：`BBDown/Infrastructure/BBDownApiServer.cs:807-808`（`SanitizeUntrustedOptions` 清空 `FilePattern`/`MultiFilePattern`）；`BBDown/Application/Options.cs:27-38`（`--add-dfn-subfix`，隐藏）与 `:60-68`（`--no-padding-page-num`）在两者为空时**重新填充**默认模板，全部隐藏开关是普通 `MyOption` 属性、可从 JSON 反序列化。安全注释在 `:804-806`。
- **发现**：`{"url":"BV...","addDfnSubfix":true}` 即可复活固定模板。当前仅恢复静态默认模板且值已过 `GetValidFileName`（RF-58），**无当下可达的穿越**；但安全不变量不密闭——以后改默认模板或新增写入 `FilePattern` 的废弃开关会重开路径注入面，且该开关会静默改变 API 客户端产物路径形态。
- **结论**：采纳——`SanitizeUntrustedOptions` 清零 `AddDfnSuffix`/`NoPaddingPageNum`/`BandwidthAscending`/`OnlyHevc`/`OnlyAvc`/`OnlyAv1`，或在 `HandleDeprecatedOptions` 之后重新断言两个模板。
- **状态**：✅ 已修复（2026-09-18，第 16 轮消纳批）：`SanitizeUntrustedOptions` 清零 `AddDfnSuffix`/`NoPaddingPageNum`/`BandwidthAscending`/`OnlyHevc`/`OnlyAvc`/`OnlyAv1`。

---

## RF-83：`/add-task` 队列满 429 缺 `Retry-After`

- **位置**：`BBDown/Infrastructure/BBDownApiServer.cs:329-335`（接受队列 429 无 `RetryAfter` 头）；对照 `:195`（认证 429）与 `:243`（查询 429）均带 `RetryAfter = "60"`，且 `:183` 注释称"429 的 Retry-After 在各自拒绝点单独附加"。
- **发现**：客户端队列满时拿到无退避提示的 429，无法统一应用退避（`API.md` 仅记录 `/get-tasks*` 的 Retry-After）；` :183` 注释失实。
- **结论**：采纳——接受队列分支补 `RetryAfter = "60"`（或修正注释与文档）。
- **状态**：✅ 已修复（2026-09-18，第 16 轮消纳批）：`/add-task` 队列满 429 补 `RetryAfter = "60"`（handler 注入 `HttpContext`）；+1 断言。

---

## RF-84：wiki 文档族 6 项

- **位置/发现**：
  ① `docs/wiki/API-Server-and-Docker.md:97-101` 响应样例含不存在的 `TotalPages`/`"Status":"Finished"`/`ErrorReason`（实际 DTO 无前二者，`Status` 取值 Queued/Running/Succeeded/Failed/Cancelled，失败字段为 `ErrorMessage`）；同页 `:85` 称缺 `Url` 返回 400，实测返回 202 + 稍后失败任务。
  ② `docs/wiki/Authentication.md:71` 称试看片段"返回退出码 2"——`Download.cs:505-509` 实为 return false（进程 0；批量经 failedPages 为 1），全仓库无 `return 2`/`Environment.Exit(2`。
  ③ `docs/wiki/Danmaku-and-Comments.md:16` 称格式"支持 `xml,protobuf`"——`BBDownEnums.cs:6-18` 枚举仅 Xml/Ass，RF-39 只修了 CLI-Reference。
  ④ `docs/wiki/Home.md:45` 仍称"18 种文件名占位符"（实为 19），`:51` 仍列"Protobuf 弹幕下载"。
  ⑤ `docs/wiki/Subcommands.md:18-23`/`:43-46`/`:61-67` 分别缺 `live`/`article` 的 `-w`、`watchlater` 的 `-c`/`--access-token`/`--use-intl-api`。
  ⑥ `docs/wiki/API-Server-and-Docker.md:84-87` 错误码清单缺 413（`BBDownApiServer.cs:306`）与 415（`ServeApiHttpTests.cs:393`）。
- **结论**：采纳——随下一文档批一并消纳。
- **状态**：✅ 已修复（2026-09-18，第 16 轮消纳批）：wiki 6 项——Authentication 退出码、Danmaku 格式、Home 占位符计数/弹幕格式、Subcommands 选项（live -w / article -w / watchlater -c/--access-token/--use-intl-api）、API-Server 错误码 413/415 + 样例字段。

---

## RF-85：Docker 配方挂载 BBDown 从不使用的路径

- **位置**：`docs/wiki/API-Server-and-Docker.md:158-160` 与 `:182-184`（compose 挂 `/app/downloads`、`/app/data`）；token 走 CLI 参数（`:168/:185`）。
- **发现**：镜像 `WORKDIR /app`（`Dockerfile:21/:46-47`），产物经 `PathUtil.ResolveWorkPath` → `Config.Current.WorkDir`（serve 下清零）→ 进程 CWD（`/app`）；凭据读自 `APP_DIR`（=`/app`）下 `BBDown.data` 等。因此 `/app/downloads`、`/app/data` 是死挂载——下载与 `bbdown-tasks.json`/`bbdown-api.log` 落容器可写层、`docker recreate` 即丢；放入被挂 config 目录的 `BBDown.data`/`device.wvd` 永不被找到（用户易报"刚登录却说尚未登录"）；token 出现在 `docker inspect`。
- **结论**：采纳——改挂 `/app`（或用显式 entrypoint 设工作目录），文档用 `-e BBDOWN_SERVE_TOKEN=`。
- **状态**：✅ 已修复（2026-09-18，第 16 轮消纳批）：Docker 配方改挂 `/app`（产物+凭据均在此），token 改 `BBDOWN_SERVE_TOKEN` 环境变量。

---

## RF-86：`DownloadTask.Snapshot()` 在锁外读 `Status`/`IsSuccessful`

- **位置**：`BBDown/Infrastructure/BBDownApiServer.cs:1510-1529`（仅 `SavePaths` 在 `_savePathLock` 下复制 `:1513`）；`SetStatus`/`SetAid`（`:1487-1503`）在锁内写并注释"与 SetStatus 共用 `_savePathLock`，避免 Snapshot 枚举期间读到半更新状态"（`:1496-1498`）。
- **发现**：`IsSuccessful`（`:1524`）与 `Status`（`:1527`）在锁外读。`GET /get-tasks*` 与任务完成（`:1229-1245`）赛跑时可短暂返回 `status:Succeeded` 而 `isSuccessful:false`（各字段原子，无损坏，仅短暂不一致），与自身契约注释不符。
- **结论**：采纳——整段复制入锁，或调整读取顺序并修正注释。
- **状态**：✅ 已修复（2026-09-18，第 16 轮消纳批）：`Snapshot()` 的 `Status`/`IsSuccessful`/`Aid` 读取移入 `_savePathLock`。

---

## RF-87：CI 卫生——PR CI 不构建 Docker 镜像；`build_latest.yml` 无 concurrency

- **位置**：`.github/workflows/pr.yml:14-155`（6 个 job，无 `docker build`；镜像契约仅在 master `build_latest.yml:197-243` 与 release `:265-291` 验证）；`build_latest.yml:1-13` 无 `concurrency` 组（对照 `pr.yml:6-8` 有）。
- **发现**：破坏 `Dockerfile` 或 serve 镜像 token 契约的 PR 在合入前始终绿；快速连续 master push 会排队并发跑完整 5 目标构建。
- **结论**：采纳——PR CI 加一个 docker-build-smoke（一次构建 + 两次 curl），`build_latest.yml` 加 `concurrency`。
- **状态**：✅ 已修复（2026-09-18，第 16 轮消纳批）：`pr.yml` 新增 `docker-build-smoke` job；`build_latest.yml` 加 `concurrency`（cancel-in-progress）。

---

## RF-88：测试假绿/名实不符（DRM 域）

- **位置**：`BBDown.Tests/WvdDeviceKeyTests.cs:69` 与 `:79`（`Assert.ThrowsAny<Exception>`）；`BBDown.Tests/WidevineCdmTests.cs:14-19`（方法名含 `Logs` 却只断言 `Assert.Null`）。
- **发现**：变异实验：让 `WvdDevice.ImportPrivateKey`/`Load` 抛 `NullReferenceException` 或 `ArgumentNullException` 而非预期的解析错误，`ThrowsAny<Exception>` 仍通过（"解析错误且释放 RSA 句柄"契约未被钉住）；删除 `WidevineCdm.cs:35` 的 `LogWarn` 也不影响 `GetKeysAsync_InvalidWvdPath_ReturnsNullAndLogs`。
- **结论**：采纳——改精确异常类型（如 `InvalidDataException`/`ArgumentException`，参照同文件 `:142` 的精确断言）；日志用例改名或补日志 sink 断言。
- **状态**：✅ 已修复（2026-09-18，第 16 轮消纳批）：`WvdDeviceKeyTests` 两处 `ThrowsAny<Exception>` 改精确类型（`ArgumentException`/`InvalidDataException`）；`WidevineCdmTests` 方法改名去掉不实的 `AndLogs`。

---

## RF-89：`sub check` 的 `-w` 绝对化抛出点未被捕获——单订阅输入错误升级为整批中止 + 误导性报错

- **位置**：`BBDown/Commands/SubCommand.cs`（PR #50 新增语句，位于 `CheckSubscriptionsAsync` 逐订阅循环之前、**任何 try 之外**）；对照 `BBDown/Application/Options.cs:282`（`ChangeWorkingDir` 在 CLI 非 serve 下写进程 CWD）与 `BBDown/Program.cs:170-198`（Spectre `SetExceptionHandler`）。
- **发现**：PR 为修复相对 `-w` 的 CWD 漂移嵌套而新增 `settings.WorkDir = Path.GetFullPath(Environment.ExpandEnvironmentVariables(settings.WorkDir))`。BCL 实测（Windows）：`GetFullPath(" ")`、`GetFullPath("a|b")` 抛 `ArgumentException`；`Directory.CreateDirectory` 对"已存在同名文件"抛 `IOException`。旧路径下同类异常被 per-aid 与整订阅的 catch 过滤器（列表**明确含 `ArgumentException`**）吞成"单个订阅失败并继续"，汇总为 `N 个订阅失败`；新路径下异常逃出 `CheckSubscriptionsAsync`——`ExecuteAsync` 只捕获 `OperationCanceledException`——落到命令级处理器，输出误导性的"请尝试升级到最新版本后重试!"并**静默放弃其余全部订阅**。同时与该方法的 XML 文档契约冲突（"用户取消与订阅数据损坏异常原样上抛"）。两次退出码均为 1，故脚本不误判成功，损失是优雅降级与诊断准确性。
- **结论**：采纳——把 `-w` 规范化移出循环，改为不抛异常的成功/错误双返回值，由命令入口报错并返回 1。
- **状态**：✅ 已修复（2026-09-26，第 17 轮消纳批）：新增 `Program.TryResolveWorkDir(workDir, out resolved, out error)`（纯函数，catch 白名单含 `ArgumentException`/`NotSupportedException`/`IOException`/`UnauthorizedAccessException`/`SecurityException`）；`SubCheckCommand.ExecuteAsync` 在进入循环前调用一次并报 `工作目录无效: …` + 退出码 1；`CheckSubscriptionsAsync` 内的解析语句删除。+3 回归测试（"已存在同名文件"跨平台用例 + Windows 专用 `ArgumentException` 分支用例 + 空 `-w` 原语义），均经**变异验证**（从 catch 白名单删去 `ArgumentException` 则用例失败）。

---

## RF-90：`watchlater` 存在与 RF-89 同源的相对 `-w` 嵌套缺陷（根因共享，修复只落在 sub check）

- **位置**：`BBDown/Commands/WatchLaterCommand.cs:79-87`（`foreach` → `BuildOption($"av{aid}", settings)` → `DoWorkAsync`）与 `:152-162`（`WorkDir = s.WorkDir` 原样透传）。
- **发现**：与 `sub check` 完全同形：逐任务透传 `WorkDir`，而 `ChangeWorkingDir` 在 CLI 下写进程 CWD，因此相对 `-w` 到第二个视频起同样产生 `<root>/<av1>/<av2>` 嵌套。PR #50 只在 `SubCommand` 内部打补丁，未打在根因上——根因是"多任务命令必须在循环前把 `-w` 解析一次"这一命令层共有约束。
- **结论**：采纳——抽出共享的 `TryResolveWorkDir` 并在两个多任务命令的入口各调用一次（而非在各命令内重复写绝对化）。
- **状态**：✅ 已修复（2026-09-26，第 17 轮消纳批）：`WatchLaterCommand.ExecuteAsync` 入口调用 `Program.TryResolveWorkDir`，非法 `-w` 报错 + 退出码 1；后续新增多任务命令可直接复用该入口。

---

## RF-91：`ResolveSubDirName` 的 target 回退是死代码，且回归测试假绿

- **位置**：`BBDown/Commands/SubCommand.cs`（`ResolveSubDirName`）；`BBDown.Tests/SubCheckDirNameTests.cs`（`EmptyName_FallsBackToTarget`、`WindowsIllegalChars_AreSanitized`、`NameWithPathSeparators_IsSanitized`、`SlotsAreConsumedEvenWithoutNewContent_SoSuffixesStayStable`）。
- **发现**：原实现 `if (name.Length == 0) name = SanitizePathSegment(sub.Target);` 判断的是**净化后**的值，而 `SanitizePathSegment`（=`GetValidFileName`）对空/纯空白/纯点输入兜底返回 `"_"`、**永不返回空串**（实测 `SanitizePathSegment("")`/`("   ")`/`("...")` 均为 `"_"`）——回退分支不可达，`"subscription"` 兜底同样不可达；PR 描述/CHANGELOG/wiki 声称的"缺省为 target"对空名不成立（实测 `ResolveSubDirName(name:"", target:"mid:163637592")` 返回 `"_"`）。原测试只断言 `NotEqual("", dir)` 与 `DoesNotContain(':', dir)`，`"_"` 同时满足两条 → **测试通过但未证明其名字声称的行为**；辅助方法 `Resolve` 传了 `target` 却从不校验。另有：`WindowsIllegalChars_AreSanitized` 的 `NotEqual(".", dir)`/`NotEqual("..", dir)` 对输入 `"mid:163637592"` 恒真（死断言）；`NameWithPathSeparators_IsSanitized` 只查"不含分隔符"，弱于真正的不变式（拼入 work-dir 后仍在 work-dir 之内）；占号用例的名字声称覆盖调用方侧行为，实际只覆盖方法自身。
- **结论**：采纳——回退判据改用**净化前**的原始值（与 `SubscriptionStore.AddAsync` 的 `IsNullOrWhiteSpace(name) ? target : name` 一致）；测试改断言真实的 `mid_163637592`，补"净化永不返回空"契约用例与 containment 断言，并给占号用例补真实断言。
- **状态**：✅ 已修复（2026-09-26，第 17 轮消纳批）：判据改 `string.IsNullOrWhiteSpace(sub.Name) ? sub.Target : sub.Name`；测试 +3（`EmptyishName_FallsBackToTarget` 三态、`DegenerateDotOnlyName_StaysSafeAndNonEmpty`、containment 断言），经**变异验证**（改回净化值判据则 4 个用例失败）。

---

## RF-92：PR 的 `-w` 绝对化改动零测试覆盖（影响面大于被测试的 `ResolveSubDirName`）

- **位置**：`BBDown.Tests/`（PR #50 新增 10 例全部针对 `ResolveSubDirName`）；被改行为位于 `SubCommand.CheckSubscriptionsAsync`。
- **发现**：`-w` 绝对化改变了**所有** `sub check -w <相对路径>` 调用的行为（即便不开 `--per-sub-dir`），却无任何测试；10 个新测试只覆盖目录名解析，回归保护与改动影响面倒挂。
- **结论**：采纳——把规范化抽成可直测的纯函数后补测试，锁"解析一次即绝对、CWD 漂移后仍指向同一目录"的不变式。
- **状态**：✅ 已修复（2026-09-26，第 17 轮消纳批）：`Program.TryResolveWorkDir` 提为 internal 纯函数并新增 4 例（相对路径绝对化 + CWD 漂移对照、不可用路径、Windows 无效字符、空 `-w` 原语义）；同时实测复现 PR 声称的嵌套缺陷（`first=[out] second=[out\out]`）。

---

## RF-93：未指定 `--name` 时 `--per-sub-dir` 的目录名不可辨认（UX）

- **位置**：`BBDown/Commands/SubCommand.cs`（`SubAddCommand`）、`docs/wiki/Subcommands.md`。
- **发现**：显示名缺省回退 target，净化后形如 `mid_163637592`；target 为 URL 时形如 `https___space_bilibili_com_163637592_video`——多订阅场景下目录名几乎无法辨认，削弱该特性的主要价值。文档已如实声明该行为，但无任何提示引导用户命名。
- **结论**：采纳——`sub add` 未带 `--name` 时提示一次（不改既有行为）；wiki 补命名建议。
- **状态**：✅ 已修复（2026-09-26，第 17 轮消纳批）：`SubAddCommand` 加 `LogWarn` 提示；`docs/wiki/Subcommands.md` 补"建议用 `--name`"说明。

---

## 处置规则说明

- ✅ 已修复：本轮已落地并有测试/验证。
- ⏳ 待排期 / 待议：技术债或改进提议，登记跟踪，由后续批次按优先级处理。
- ⭕ 维持现状：经评估不实施（含前提不成立的建议），保留理由以便后续不重复评估。
