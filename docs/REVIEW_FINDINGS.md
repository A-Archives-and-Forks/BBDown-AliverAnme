# 审查发现与建议跟踪（REVIEW_FINDINGS）

> **用途**：记录代码审查轮次中**经评估后需要决策 / 持续跟踪**的发现与建议（含已判定"不实施"的条目，避免后续重复评估）。
> **与 REVIEW_PLAN.md 互补**：`docs/REVIEW_PLAN.md` 跟踪"剩余未处理修复项"的排期；本文件跟踪"已分析、留有处置结论"的条目（采纳 / 技术债 / 维持现状 / 待议）。
> **创建时间**：2026-08-19（自 v1.6.14 起的审查轮次结项时建立）。

## 状态总览

| 编号 | 主题 | 评估级别 | 判定 | 状态 |
|------|------|---------|------|------|
| RF-1 | 分片扩展名判定一致性（`IsVideoClipPath`） | Low | 采纳（1 行修复） | ✅ 已修复（本轮） |
| RF-2 | CLI 命令层 Async-over-Sync 迁移 `AsyncCommand` | Medium（对应 REVIEW_PLAN I8） | 技术债，一次性批量重构 | ⏳ 待排期 |
| RF-3 | serve 已完成任务历史"无界堆积" | —（建议前提不成立） | 不实施（现有防护已覆盖） | ⭕ 维持现状 |
| RF-4 | Widevine 许可证请求跟随重定向 | Low（一致性） | 改进提议 | ⏳ 待议 |

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
- **状态**：⏳ 待排期（技术债，REVIEW_PLAN I8）。

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
- **状态**：⏳ 待议（Low，一致性改进，不影响当前发版）。

---

## 处置规则说明

- ✅ 已修复：本轮已落地并有测试/验证。
- ⏳ 待排期 / 待议：技术债或改进提议，登记跟踪，由后续批次按优先级处理。
- ⭕ 维持现状：经评估不实施（含前提不成立的建议），保留理由以便后续不重复评估。
