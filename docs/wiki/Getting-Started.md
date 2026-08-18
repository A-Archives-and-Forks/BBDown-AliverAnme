# 快速上手 (Getting Started)

> 本文档指导你如何在各操作系统中安装 BBDown、准备必要的音视频混流外部依赖，并掌握最基础的音视频下载操作。

---

## 1. 程序获取与安装

### 1.1 下载预编译发布版（推荐）
BBDown 为 Windows、Linux 与 macOS 提供了开箱即用的原生单文件可执行程序：
- **正式发布版**：[GitHub Releases · aliveranme/BBDown](https://github.com/aliveranme/BBDown/releases)
- **CI 最新构建版**：[GitHub Actions 构建产物](https://github.com/aliveranme/BBDown/actions)

下载对应系统架构的压缩包并解压，即可直接在终端运行 `BBDown`（Windows 下为 `BBDown.exe`）。

### 1.2 从源码编译构建
若需本地定制或二次开发，需要安装 [.NET 10.0 SDK](https://dotnet.microsoft.com/download)：

```bash
# 1. 克隆代码仓库
git clone https://github.com/aliveranme/BBDown.git
cd BBDown

# 2. 编译发布 Release 版本
dotnet build -c Release

# 3. 运行验证
./BBDown/bin/Release/net10.0/BBDown --help
```

---

## 2. 外部依赖工具准备

BBDown 采用高保真音视频分离下载架构（分别拉取最高码率的独立视频轨与音频轨），下载完成后会自动调用外部混流工具合并为标准 `.mp4` 容器。

### 2.1 混流工具（必备）

| 工具名称 | 适用场景 | 获取途径 |
| :--- | :--- | :--- |
| **FFmpeg** *(强烈推荐)* | 普通视频、HDR、8K、杜比视界 (需 5.0+)、杜比全景声混流 | [FFmpeg 官网](https://ffmpeg.org/download.html) 或 [Gyan Builds (Windows)](https://www.gyan.dev/ffmpeg/builds/) |
| **MP4Box** | 早期杜比视界混流替代方案 | [GPAC 官方下载](https://gpac.wp.imt.fr/downloads/) |

> [!TIP]
> **配置推荐**：
> 1. 将 `ffmpeg.exe`（或 `MP4Box.exe`）所在目录添加到系统的环境变量 `PATH` 中。
> 2. 或者直接将 `ffmpeg.exe` 放置在与 `BBDown.exe` 相同的目录下。
> 3. 若存放在自定义路径，可在运行时通过 `--ffmpeg-path <path>` 或 `--mp4box-path <path>` 显式指定。

### 2.2 下载加速工具（可选）

- **aria2c**：如需使用 aria2 替代内置的多线程分片下载引擎，请下载 [aria2 二进制文件](https://github.com/aria2/aria2/releases) 并加入 `PATH`，运行时追加 `--use-aria2c` 参数即可。

---

## 3. 基础下载实战

### 3.1 下载单个普通视频
直接传入视频播放页链接、BV 号或 AV 号：
```bash
# 完整 URL
BBDown "https://www.bilibili.com/video/BV1qt4y1X7TW"

# 简写 BV 号
BBDown BV1qt4y1X7TW
```

### 3.2 使用 TV 端接口解析（无水印）
TV 端接口不仅通常没有视频水印，且具备独立的 CDN 调度：
```bash
BBDown -t "https://www.bilibili.com/video/BV1qt4y1X7TW"
```

### 3.3 仅查看流信息（不下载）
使用 `-I`（`--only-show-info`）快速探测该视频包含的全部清晰度、帧率、编码及分 P 列表：
```bash
BBDown -I "https://www.bilibili.com/video/BV1qt4y1X7TW"
```

### 3.4 交互式选择画质与编码
使用 `-i`（`--interactive`）在终端中弹出可视化的多流选择菜单：
```bash
BBDown -i "https://www.bilibili.com/video/BV1qt4y1X7TW"
```

### 3.5 批量下载番剧 / 电影全集
配合 `-p ALL` 下载当前番剧或合集的所有剧集：
```bash
BBDown -p ALL "https://www.bilibili.com/bangumi/play/ss33073"
```

---

## 4. 输出产物与临时文件解析

下载完成后，BBDown 会在当前工作目录下产出以下文件：

```
工作目录/
├── <视频标题>.mp4             # 最终混流生成的完整音视频文件
├── <视频标题>.srt / .ass       # 外挂字幕文本文件（若视频包含字幕）
├── <视频标题>.xml              # 弹幕数据文件（需添加 -d 参数）
├── <视频标题>.comments.json    # 评论区数据导出（需添加 --comments 参数）
└── .segs/                     # 直播录制分段临时目录（仅 live 子命令产生）
```

> [!NOTE]
> 下载过程中产生的 `.vclip`（视频分片）与 `.aclip`（音频分片）临时文件会在混流完成后被程序自动清理。若下载被意外中断，下次在同一目录下运行相同命令将自动进行断点续传。

---

### 🧭 快速跳转

| 上一篇 | 目录导航 | 下一篇 |
| :--- | :---: | ---: |
| ⬅️ [知识库首页](Home) | 📑 [返回目录](Home) | [全命令行参数详解](CLI-Reference) ➡️ |
