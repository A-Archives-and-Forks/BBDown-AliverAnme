# 弹幕与评论区抓取指南 (Danmaku & Comments)

> 本文档介绍如何在 BBDown 中下载和过滤弹幕文件、将其转换为 ASS 格式字幕，以及如何导出视频评论区数据。

---

## 1. 弹幕下载与格式支持

通过 `-d`（`--download-danmaku`）选项可以开启弹幕下载。

### 1.1 弹幕相关选项速查

| 参数 | 默认值 | 功能说明 |
| :--- | :--- | :--- |
| `-d, --download-danmaku` | `false` | 开启弹幕下载 |
| `--download-danmaku-formats` | `xml` | 指定下载的弹幕格式（支持 `xml,protobuf`） |
| `--danmaku-only` | `false` | 仅下载弹幕，跳过音视频下载与混流 |
| `--danmaku-filter` | *(无)* | 弹幕关键词黑名单过滤（逗号分隔） |
| `--danmaku-filter-user` | *(无)* | 弹幕发送者 `midHash` 黑名单过滤（逗号分隔） |

---

## 2. 弹幕黑名单过滤

为净化观看体验，BBDown 提供了双维度的弹幕过滤功能：

### 2.1 文本关键词过滤 (`--danmaku-filter`)
若弹幕文本中包含指定的任一关键词（不区分大小写），则该条弹幕会被自动剔除：
```bash
BBDown -d --danmaku-filter "前方高能,打卡,剧透,第一" <URL>
```

### 2.2 发送者过滤 (`--danmaku-filter-user`)
B 站弹幕 XML 包含发送者的哈希标识（`midHash`）。可通过此参数过滤指定频繁刷屏的用户：
```bash
BBDown -d --danmaku-filter-user "a1b2c3d4,e5f67890" <URL>
```

---

## 3. 弹幕转 ASS 字幕渲染标准

BBDown 内置高性能 XML 弹幕解析器与 ASS 字幕转换引擎：
- **画布基准**：`1920 × 1080` 分辨率。
- **字号与样式**：默认 `40px`，具备高对比度黑边描边与投影。
- **存活与滚动时长**：
  - **滚动弹幕**：全屏滑动持续时间为 `8.0 秒`。
  - **顶部 / 底部固定弹幕**：屏幕居中悬停持续时间为 `4.0 秒`。
- **屏幕遮挡保护**：屏幕占用高度上限默认限制为 `50%`，防止弹幕过密遮挡核心视觉区域。

---

## 4. 视频评论区数据抓取 (`--comments`)

通过 `--comments` 参数，BBDown 可提取当前视频的评论区数据（包含用户信息、点赞数、发布时间及内容正文），并保存为结构化 JSON 文件。

### 4.1 使用示例
```bash
BBDown --comments "https://www.bilibili.com/video/BV1qt4y1X7TW"
```
下载完成后，将在视频同目录下生成 `<视频标题>.comments.json`。

### 4.2 导出 JSON 数据结构规范

```json
[
  {
    "user": "UP主昵称",
    "time": "1715000000",
    "likes": 12580,
    "content": "感谢大家的支持！本期视频所用测试素材已开源在 GitHub..."
  },
  {
    "user": "热心观众",
    "time": "1715001234",
    "likes": 648,
    "content": "这个讲解太透彻了，支持一波！"
  }
]
```

---

### 🧭 快速跳转

| 上一篇 | 目录导航 | 下一篇 |
| :--- | :---: | ---: |
| ⬅️ [子命令使用指南](Subcommands) | 📑 [返回目录](Home) | [批量下载与自动化脚本](Batch-and-Automation) ➡️ |
