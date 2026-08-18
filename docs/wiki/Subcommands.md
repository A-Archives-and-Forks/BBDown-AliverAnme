# 子命令使用指南 (Subcommands)

> 本文档详细介绍 BBDown 除了基础下载功能之外的高级子命令生态，涵盖直播录制、专栏保存、稍后再看同步以及 UP 主订阅管理。

---

## 1. 直播录制 (`live`)

BBDown 支持对哔哩哔哩直播间进行无损分段实时录制与自动合成。

### 1.1 命令语法
```bash
BBDown live <room_id> [选项]
```

### 1.2 参数列表

| 短选项 | 长选项 | 类型/默认值 | 说明 |
| :--- | :--- | :--- | :--- |
| *(参数0)*| `<room_id>` | `string` | 直播间短号或长号房间 ID（如 `6` 或 `12345`） |
| `-o` | `--output` | `string?` | 最终输出文件路径（默认：`直播间标题_直播录制_时间.flv`） |
| `-c` | `--cookie` | `string ("")` | 手动指定 Cookie（若省略则自动加载 `BBDown.data`） |
| | `--access-token` | `string ("")` | 手动指定 Access Token |

### 1.3 核心技术特性
- **画质权限自动提升**：未登录录制仅返回 720P 游客画质；BBDown 会自动读取本地 `BBDown.data` 凭据，解析获取原画、4K、杜比视界等账号可用最高规格。
- **断流重连与分段暂存**：直播卡顿或主播重启推流时，程序会自动进行重试，将已接收数据保存在 `.segs/session-*` 目录中。
- **FFmpeg Concat 安全合成**：录制结束时（按 `Ctrl+C` 或主播下播），程序自动调用 FFmpeg 将所有分段安全拼接为最终单个视频。

---

## 2. 专栏文章下载 (`article`)

将 B 站专栏 / 动态图文（Opus）下载并完整转换为 Markdown 格式文件。

### 2.1 命令语法
```bash
BBDown article <cv_id或链接> [选项]
```

### 2.2 参数列表

| 参数/选项 | 类型/默认值 | 说明 |
| :--- | :--- | :--- |
| `<cv_id>` | `string` | 专栏 ID（如 `cv12345`）或完整文章链接 |
| `-o, --output` | `string?` | 输出 Markdown 路径（默认：`<专栏标题>.md`） |

---

## 3. 稍后再看批量下载 (`watchlater`)

一键批量同步并下载当前登录账号「稍后再看」列表中的所有视频。

### 3.1 命令语法
```bash
BBDown watchlater [选项]
```

### 3.2 参数列表

| 选项 | 类型/默认值 | 说明 |
| :--- | :--- | :--- |
| `--limit <N>` | `int (0)` | 最大下载视频数量（默认 0 代表下载全部） |
| `-w, --work-dir` | `string ("")` | 设置下载输出目录 |
| `-q, --dfn-priority` | `string?` | 画质优先级 |
| `-e, --encoding-priority` | `string?` | 编码优先级 |
| `-t` / `-a` | `bool` | 使用 TV 端或 APP 端解析模式 |

---

## 4. 订阅增量管理 (`sub`)

BBDown 支持对指定的 UP 主、番剧或合集进行订阅追踪，通过增量检查一键下载最新更新。

### 4.1 常用子操作

| 命令 | 功能说明 | 示例 |
| :--- | :--- | :--- |
| `BBDown sub add <target> [--name <name>]` | 添加新订阅源 | `BBDown sub add "mid:163637592" --name "何同学"` |
| `BBDown sub list` | 查看当前所有订阅列表 | `BBDown sub list` |
| `BBDown sub remove <target>` | 删除指定订阅源 | `BBDown sub remove "mid:163637592"` |
| `BBDown sub check [选项]` | 增量拉取所有订阅的新视频 | `BBDown sub check -q "1080P 高码率"` |

### 4.2 支持的订阅源标识格式
- **UP 主 UID**：`mid:163637592`
- **个人空间链接**：`https://space.bilibili.com/163637592`
- **合集 / 系列链接**：`https://space.bilibili.com/163637592/channel/seriesdetail?sid=12345`
- **番剧季度**：`ss:33073`

---

### 🧭 快速跳转

| 上一篇 | 目录导航 | 下一篇 |
| :--- | :---: | ---: |
| ⬅️ [配置文件与命名规则](Configuration-and-Templates) | 📑 [返回目录](Home) | [弹幕与评论区抓取](Danmaku-and-Comments) ➡️ |
