// 程序集级测试并行化开关：xunit 默认并行执行各测试类，而本套件多个测试类
// 读写进程全局状态——Config._settings（读-改-写非原子的全局 AppSettings，如
// DownloadPipelineTests 22 处 Config.Apply）、BBDownDownloadUtil 的静态路径锁字典
// （ActivePathLockCount 断言）等。并行下这些读-改-写竞争产生 flaky：
// 一个测试恢复配置时覆盖另一个测试刚写入的配置。
// 整套测试仅 ~21s，串行化代价可忽略，一次性消除这类竞态。
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]
