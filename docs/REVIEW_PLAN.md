# 代码审查修复排期（REVIEW_PLAN）

> 审查来源：R1/R2/R3/R4 四轮审查（安全/可读性/可靠性/韧性），2026-08 批次。
> 本文件跟踪**剩余未处理项**；已修复项见 git log（13766b0..2ab54d1 五连提交）与各代码注释。

## 状态总览

| 组 | 总数 | 已完成 | 剩余 |
|----|------|--------|------|
| A 安全 Infra | 7 | 7 | 0 |
| B 安全 Core | 3 | 2 | **1**（B3） |
| C 功能缺陷 | 3 | 3 | 0 |
| D 韧性 Infra | 10 | 9 | **1**（D8） |
| E 韧性 Core | 6 | 6 | 0 |
| F 测试 Infra | 12 | 6 | **6** |
| G 测试结构 | 10 | 4 | **6** |
| H 可读性 Infra | 13 | 1 | **12**（H11 同 C3 已修） |
| I 可读性 App/Core | 22 | 3 | **19**（I4 同 C1 已修、I21/I22 验证通过） |
| J CI/发布 | 4 | 2 | **2**（J1/J2 跟踪项） |
| **合计** | **90** | **43** | **47** |

---

## 第 1 轮：测试套件结构加固（防 CI 假绿/假失败，低风险高价值）

| 项 | 级别 | 位置 | 处理 |
|----|------|------|------|
| G1 | Critical | .github/workflows/pr.yml | ✅ 已落地（替代方案）：审查原建议 `xunit.runner.json 加 forbidOnly` 是 Playwright（JS `test.only`）概念，**xunit v2/v3 均无 forbidOnly/`[Only]`**（已查源码 ConfigReader_Json 与官方配置文档确认）。xunit 中“测试子集全绿”的等价残留是 `[Fact(Skip=...)]` 跳过不跑。已用 `xunit.runner.json` 的 `failSkips: true`（v2.5+，项目 v2.9.3 可用）把任何 Skip 当作硬失败；项目当前 0 个 Skip 零副作用，且已实测验证（注入临时 Skip 被报 FAIL） |
| G5 | Medium | DownloadPipelineTests.cs:848 | ✅ 墙钟断言改区间重叠断言（a 区间 ∩ b 区间必须重叠）——CI 调度抖动只影响总耗时不再误报 |
| G6 | Medium | RedirectHopValidationTests.cs:25,147 | ✅ 两处固定端口段（24000-26000/25000段）改 TestPort.Allocate() 动态端口 |
| G8 | Medium | HttpUtilRetryTests.cs 7 处 finally | ✅ 9 个测试全部改为捕获前值恢复（`var original = Config.Current` + finally 恢复）；补连接被拒用例（StatusCode=null 命中重试谓词，退避耗时下限证明重试发生） |
| G9 | Low | RedirectHopValidationTests.cs:120 | ✅ LocalRedirectServer 加 RequestCount 计数；断言请求数 ≤ maxHops+1（强证据：仅断言终值无法区分“截断返回”与“侥幸返回”） |
| G10 | Low | ServeApiHttpTests.cs:21,415 | ✅ BaseUrl 改动态端口（TestPort.Allocate 静态字段）；WaitForFinishedCountAsync 轮询耗尽抛带上下文 TimeoutException；Cancel 持久化断言带任务文件名/存在性上下文 |
| F9 | Medium | ServeApiSecurityTests | ✅ 新增 SanitizeUntrustedOptions_ClampsNumerics：上界（3/5000/120/30/64）+ 下界（RetryCount→1、MuxerTimeout→1、ThreadSegmentSize→1）共 10 断言 |
| F11 | Suggestion | ServeApiHttpTests.cs:1-40 | ✅ 补 [CollectionDefinition("ServeApiCollection")]；更新类注释（_taskFile 已实例字段注入，不再静态污染） |

## 第 2 轮：功能/韧性测试补齐

| 项 | 级别 | 位置 | 处理 |
|----|------|------|------|
| F6 | Medium | ExternalProcessRunnerTests.cs:50,68 | ✅ “KillsProcessTree” 两个测试升级为进程树哨兵验证：根进程派生持续写哨兵文件的子进程，超时/取消后验证哨兵文件停止增长（整棵进程树被杀）——替换此前只断言异常类型、杀根不杀子也通过的零证据断言；Unix 用 sh 后台子 shell / Windows 用 cmd+ping 重定向 |
| F7 | Medium | ExternalProcessRunnerTests.cs:160-201 | ✅ MergeFLV 假 runner 测试从“try/catch 吞异常”（抛/不抛都通过）改为确定性断言：Assert.ThrowsAsync<InvalidOperationException> + 消息含“保留源分段” + 假 runner 确实被调 + 源分段保留 |
| G7 | Medium | DownloadPipelineTests.cs:133-161 | ✅ 新增 3-clips SHA-256 用例：2.5MB 载荷/1MB 分片 → 服务端 Record RangeHeaders（3 段互补不重叠覆盖 [0,size)）+ 产物逐字节哈希一致 + 分片清理 + 锁释放 |
| F10 | Suggestion | LiveStreamUtil.cs | ✅ 补 2 个可稳定分支：非数字 roomId→ArgumentException（ResolveAsync 不发起网络请求）；零字节 EOF→删除空 seg + 退避重连续录（新 StreamMode.ZeroByte）。⚠️ LiveStreamWriteException 分支需要磁盘故障/只读文件系统，跨平台测试不可靠触发，保留人工验证 |
| F12 | Suggestion | 多处 | ✅ IsBlockedAddress CGNAT/ULA 经 IsSafeCallbackUrl 域名 DNS 分支直测（6 断言）；ProgressBar Dispose 结算新增 Test 文件（2 测试）；SubscriptionStore 幂等新增 5 测试（重复 Add/不存在 Remove/同 aid 去重/最近优先）。⚠️ LocalIntegration 缺 ffmpeg return 改 Skip 在 **xunit v2 无法实现**：`Assert.Skip` 动态跳过仅 v3 支持；静态 `[Fact(Skip)]` 编译期写死不能表达运行时缺 ffmpeg，且与 G1 的 `failSkips:true` 冲突（Skip 会变失败）——保留 return，待 v3 迁移时改为 Assert.Skip |

## 第 3 轮：B3 独立安全审查（上次 risk-core 超时未完成）

- 范围：Parser 签名验证、HTTP 层安全视角、AppHelper
- 产出：发现项清单 → 按优先级并入后续轮次修复
- 注意：验证 AES 的自测 C 程序有 bug 属子代理工具问题，非项目代码缺陷

## 第 4 轮：D8 HTTP 并发请求数上限

- serve /get-tasks 等端点无限并发放大 CPU/GC（每请求深拷贝 ~1000 条任务）
- 处理：可选信号量（本地工具定位，低优先，可接受搁置）

## 第 5 轮：H 组可读性重构（Infrastructure）

| 项 | 级别 | 位置 | 处理 |
|----|------|------|------|
| H1 | High | BBDownApiServer.cs 全文件 | God 类拆 ServeSecurityMiddleware / TaskRouteMapper / TaskFileStore / CallbackGuard；SetUpServer ~200 行 lambda 内联无法单测 |
| H2 | High | BBDownMuxer.cs:64,174 | MuxAV 20 参 / MuxByMp4box 15 参改 MuxRequest 参数对象 |
| H3 | High | BBDownDownloadUtil.cs:28 | RangeDownloadToTmpAsync 10 参 → RangeDownloadRequest + 拆两段 |
| H4 | High | BBDownDownloadUtil.cs:227,611 | Core 170/200 行嵌套 6-7 层：预检决策方法 + DownloadClipWithRetryAsync；6 个"检查 .tmp/.aria2"块收敛 |
| H5 | High | 多处 | 重复簇抽 6 个辅助方法（任务收尾四元组 ×4、IsLoopback 判定、SSRF 字面 IP ×2、DNS+逐地址校验 ×3、头块 ×3、权威大小复核 ×3、clip 路径推导 ×4） |
| H6 | Medium | LiveStreamUtil.cs:75,222,286 | 异常消息文本契约改 LiveRoomClosedException 专用异常 |
| H7 | Medium | 多处 | 死代码逐条删除（未用 using ×4、空 WriteLine ×2、孤立 doc、死参数 GetAllClips、CommandLineSplitter 同行为分支） |
| H8 | Medium | 多处 | 误导性命名：ReadLinesThrottled、_savePathLock、MyOptionBindingResult<T>、QualityName 档位映射顺序、nowId |
| H9 | Medium | 多处 | 魔法数字集中常量（关停 30s/回调 2min/1048576/复核 15s/分片并发 8/退避 3000*2^n/完整性 0.8/FLV 常量 13 个） |
| H10 | Medium | SubscriptionStore.cs:110-149,205 | 同一历史文件两套异常语义：抽 ReadHistoryLocked() 单入口 |
| H12 | Low | 多处 | bool & bool、拼写 recevied ×2、ProgressBar 命名、空 XML doc、SetUpServer 命名 |
| H13 | Low | LiveStreamUtil.cs:57,235 | ResolveAsync 5 元组改 LiveStreamInfo record |

## 第 6 轮：I 组可读性重构（应用层 + Core 结构）

| 项 | 级别 | 位置 | 处理 |
|----|------|------|------|
| I1 | High | Download.cs:326-843 | DownloadPageAsync ~520 行 god 方法拆 4 helper + dash/flv 子方法；弹幕块 55 行重复、CoverOnly 重复、"已存在跳过"×3、"空 aid 目录删除"×5 收敛 |
| I2 | High | Parser.cs:106-540 | ExtractTracksAsync ~430 行：PickDataRoot/PickTrackBaseUrl 纯函数 + ApiMode 枚举；数据节点定位 3 份漂移变体收敛 |
| I3 | High | BBDownUtil.cs:167 vs Parser.cs:680 | GetSign MD5 盐 ×2、appkey ×2、GetTimeStamp(bool bflag) ×2 集中 BiliApiKeys 常量 + 单份实现 |
| I5 | Medium | Workflow.cs:15-16 起 | SetUpWork 10 元组 → DownloadContext record；4 层透传参数收敛 |
| I6 | Medium | BBDownLoginUtil.cs:69-316 | LoginWEB/LoginTV 复制收敛 2 helper + QrPollCode 常量组（86038/86101/86090/86039） |
| I7 | Medium | 6+ 处 | 异常过滤器 or-链逐字重复抽 IsRetryableDownloadException(Exception) |
| I8 | Medium | 4 个命令 | Task.Run(...).GetAwaiter().GetResult() async-over-sync 改 AsyncCommand + ExitCodeFor |
| I9 | Medium | SubCommand.cs:49-81 + WatchLaterCommand.cs:13-45 | 两个 Settings 类复制 8 个下载选项 + 两份 BuildOption 抽公共基类 |
| I10 | Medium | BBDownUtil.cs 全文件 | god 工具类按职责拆分（更新检查/文件/签名/TV 指纹/章节/WBI/SESSDATA） |
| I11 | Medium | Config.cs:61-84 | 门面双命名体系统一 PascalCase |
| I12 | Medium | UrlResolver.cs:15-180 | ResolveAsync 200 行 13 分支拆 ResolveHttpUrl/ResolveBareId + 改名 target |
| I13 | Medium | Entity.cs:37-93 | Page 阶梯构造器（8/9/10/12 参）改无参构造 + 初始化器 + 属性 |
| I14 | Medium | AppHelper.cs:448 vs Entity.cs:203 | 同名 AudioMaterial 冲突：DTO 改名 AppRoleAudioDto |
| I15 | Medium | Display.cs | XML 文档挂错方法归位；.Replace("[] ", "") hack ×4；带宽估算公式 ×6 抽 EstimatedBytes；bool video 参数 |
| I16 | Medium | BBDownConfigParser.cs:83-209 | MergeWithConfig 130 行 4 次手工扫参收敛 SkipOptionValue + 静态缓存 BuildAliasMap |
| I17 | Low | 5 处 | 死代码删除（BBDownLoginUtil 注释 Log、BBDownUtil.GetFiles、UrlResolver.MdRegex、GetAvIdAsync 无 token 重载、Pages.cs:170-174 悬空 XML 注释） |
| I18 | Low | 多处 | 魔法数具名（debug 保留 20、WebJson 摘要 1024、Task.Delay(200)、DrmTechType==2、QR GetGraphic(7) ×2、86400.0） |
| I19 | Low | 3 处 | 注释残余："与 B3 的 ClassifyCancellation 一致"、Parser 硬编码行号、Download.cs:23-24 连续 ThrowIfCancellationRequested ×2 |
| I20 | Low | 多处 | 命名/文档歧义：aidOri、--bandwith-ascending 拼写、--skip-ai 指代、Page.bvid getter fallback |

## 第 7 轮：CI 跟踪项

| 项 | 级别 | 位置 | 处理 |
|----|------|------|------|
| J1 | Observation | release.yml | ubuntu:18.04 已 EOL（glibc 2.27 兼容是刻意选择），跟踪 apt 源长期可用性；必要时迁移容器基镜像 |
| J2 | Observation | 所有 workflow | Actions 固定 major 版本（@v4/@v5/@v6）非完整 SHA；有 Dependabot 周更兜底可接受，严格供应链可升级 SHA 固定 |

---

## 已完成批次（git log 13766b0..2ab54d1，2026-08）

1. **13766b0** Core 韧性：字幕 TimeoutException 降级（E1）、Widevine 许可证有界重试+超时分类（B2/E2）、重定向 GET 重试（E3）、fetcher code 诊断（E4/E5）、Logger.LogStack（E6）
2. **2cff36c** serve 安全：ForceHttp 清零（A1）、trusted-proxy XFF（A2）、数值 Clamp 慢速 DoS（A3）、RetryCount [1,3]（A4/D5）、日志单行化（A7）、DnsSafeHost（C2）、token 环境变量（A6）、持久化/加载/webhook 日志升级与重启提示/关停枚举 JobId（D2/D3/D4/D7）
3. **df4eba9** 下载/直播/订阅韧性：VOD 读停滞看门狗（D1）、分片扩展名大小写统一（C3/H11）、直播短段退避（D6）、管道成功路径超时（D9）、订阅历史有界（D10）、itags CRLF 转义（A5）
4. **e59402c** 应用层：LATEST 全词匹配（C1/I4）、Download.cs 字幕 TimeoutException、Download.cs 662 处缩进对齐（format 门禁修复）
5. **2ab54d1** 测试：CSRF/认证限速/cancel 端点测试（F1/F2/F3/G2）、413 契约对齐（F8）、看门狗 per-call 注入（F5）、程序集级串行（G3/G4）、ExpandPageAliases 测试（C1）

另：B1 WidevineCrypto 亲验无问题；I21/I22 亲验无问题；J3 设计合理；J4 全部 CI/依赖验证通过。
