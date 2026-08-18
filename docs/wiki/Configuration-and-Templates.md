# 配置文件与命名规则 (Configuration & Templates)

> 本文档介绍 BBDown 本地配置文件 `BBDown.config` 的书写规范与加载逻辑，以及如何利用内置模板变量自定义输出文件名称与目录归档层级。

---

## 1. 配置文件 `BBDown.config`

为了避免每次在终端输入长串参数，BBDown 支持读取本地配置文件。

### 1.1 加载规则与优先级
- **默认加载路径**：程序会优先读取可执行文件同目录下的 `BBDown.config`。
- **显式指定路径**：可在运行时通过 `--config-file <path>` 指定任意位置的配置文件。
- **覆盖原则**：命令行显式传入的参数拥有最高优先级，会覆盖配置文件中的同名选项。
- **子命令隔离**：子命令（如 `serve`、`live`、`article`、`sub`）拥有独立的参数体系，不会继承 `BBDown.config` 中的主下载参数。

### 1.2 配置文件编写语法
1. 每一行代表一个配置项。
2. 以 `#` 开头的行为注释，会被程序自动忽略。
3. 带有参数的选项（如 `--file-pattern`），其参数值**必须独占换行写在下一行**。
4. 无参数的布尔开关（如 `--download-danmaku`）直接单独写一行。

### 1.3 典型配置文件示例

```config
# ========================================================
# BBDown 全局常用配置文件
# ========================================================

# 1. 默认画质优先级 (优先 8K/高码率/HDR)
--dfn-priority
8K 超高清, 1080P 高码率, HDR 真彩, 杜比视界, 1080P 高清

# 2. 默认编码优先级 (优先 HEVC 与无损音频)
--encoding-priority
hevc,av1,avc,flac,eac3,m4a

# 3. 单 P 视频命名格式
--file-pattern
[<publishDate>] <videoTitle> [<dfn>]

# 4. 多 P 视频目录归档格式
--multi-file-pattern
<videoTitle>/[P<pageNumberWithZero>] <pageTitle> [<dfn>]

# 5. 多分 P 请求退避间隔 (秒)
--delay-per-page
2

# 6. 默认开启弹幕下载
--download-danmaku

# 7. 增强网络重试策略
--retry-count
5

--retry-delay
2000

# 8. 多线程分片大小 (MB)
--thread-segment-size
20
```

---

## 2. 输出文件名变量模板

通过 `-F`（`--file-pattern`）和 `-M`（`--multi-file-pattern`），你可以使用预置的 18 种变量占位符自定义文件名和多层级子目录。

### 2.1 内置占位符对照表

| 占位符代码 | 说明 | 示例解析结果 |
| :--- | :--- | :--- |
| `<videoTitle>` | 视频主标题 / 番剧主标题 | `2024 年度数码盘点` |
| `<pageTitle>` | 当前分 P 标题（单 P 视频与主标题相同） | `Part 1 手机篇` |
| `<pageNumber>` | 当前分 P 自然序号（无补零） | `1`、`12` |
| `<pageNumberWithZero>` | 当前分 P 序号（按总集数自动补零对齐） | `01`、`002` |
| `<bvid>` | 视频 BV 唯一编号 | `BV1qt4y1X7TW` |
| `<aid>` | 视频 AV 纯数字编号 | `170001` |
| `<cid>` | 当前分 P 的 CID 标识 | `22334455` |
| `<dfn>` | 实际下载的清晰度描述 | `1080P 高码率`、`4K 超清` |
| `<res>` | 实际下载视频的分辨率 | `1920x1080`、`3840x2160` |
| `<fps>` | 实际下载视频的帧率 | `60`、`30` |
| `<videoCodecs>` | 实际选中的视频编码 | `hevc`、`avc`、`av01` |
| `<videoBandwidth>` | 视频码率 | `6250kbps` |
| `<audioCodecs>` | 实际选中的音频编码 | `flac`、`mp4a.40.2` |
| `<audioBandwidth>` | 音频码率 | `320kbps` |
| `<ownerName>` | 视频 UP 主昵称（番剧/电影模式下为空） | `老师好我叫何同学` |
| `<ownerMid>` | 视频 UP 主 UID（番剧/电影模式下为空） | `163637592` |
| `<publishDate>` | 视频发布时间戳（`yyyy-MM-dd_HH-mm-ss`） | `2024-05-01_12-00-00` |
| `<apiType>` | 本次解析调用的接口类型 | `WEB`、`TV`、`APP`、`INTL` |

---

## 3. 经典生产级目录归档方案

### 方案 A：按 UP 主归档多 P 投稿
```bash
BBDown -M "<ownerName>/<videoTitle>/[P<pageNumberWithZero>] <pageTitle> [<dfn>]" <URL>
```
**生成路径效果**：
```
何同学/
  数码新品开箱合集/
    [P01] 手机深度测评 [1080P 高码率].mp4
    [P02] 平板使用体验 [1080P 高码率].mp4
```

### 方案 B：媒体库（Emby / Plex / Jellyfin）番剧规范
```bash
BBDown -M "<videoTitle>/Season 1/[S01E<pageNumberWithZero>] <pageTitle> [<res>][<videoCodecs>]" -p ALL <URL>
```
**生成路径效果**：
```
间谍过家家/
  Season 1/
    [S01E01] 任务目标 [1920x1080][hevc].mp4
    [S01E02] 寻找妻子 [1920x1080][hevc].mp4
```

---

### 🧭 快速跳转

| 上一篇 | 目录导航 | 下一篇 |
| :--- | :---: | ---: |
| ⬅️ [账号登录与鉴权](Authentication) | 📑 [返回目录](Home) | [子命令使用指南](Subcommands) ➡️ |
