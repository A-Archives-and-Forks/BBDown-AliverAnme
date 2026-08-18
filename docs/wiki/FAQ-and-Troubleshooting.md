# 常见问题与故障排查 (FAQ & Troubleshooting)

> 本文档汇总了使用 BBDown 过程中最常见的异常报错、成因分析、解决方案与排障技巧。

---

## 1. 混流与外部依赖问题

### Q1: 提示 `找不到可执行的ffmpeg文件` 或 `ffmpeg/mp4box 未找到`？
- **故障成因**：BBDown 采用音视频分离下载，需要外部混流程序（FFmpeg 或 MP4Box）合并为最终 MP4 容器。
- **解决方案**：
  1. 下载 [FFmpeg](https://ffmpeg.org/download.html)（Windows 推荐 Gyan Builds）。
  2. 将 `ffmpeg.exe` 放置在与 `BBDown.exe` 相同的目录下，或将其所在目录加入系统环境变量 `PATH`。
  3. 亦可在运行时显式传入 `--ffmpeg-path "/path/to/ffmpeg"`。

### Q2: 杜比视界 (Dolby Vision) 混流后画面发绿、偏色或播放黑屏？
- **故障成因**：旧版本 FFmpeg（< 5.0）对杜比视界 Profile 8 / Profile 5 元数据的容器封装支持不完整。
- **解决方案**：
  1. 升级 FFmpeg 至 **5.0 或更高版本**。
  2. 或者改用 MP4Box 混流：安装 MP4Box 并追加 `--use-mp4box` 参数。

---

## 2. 跨平台与运行时环境

### Q3: Linux / macOS 下执行 `login` 报错 `The type initializer for 'Gdip' threw an exception`？
- **故障成因**：二维码控制台渲染依赖底层 GDI+ 图像图形库，非 Windows 系统默认缺少 `libgdiplus`。
- **解决方案**：
  - **Debian / Ubuntu**: `sudo apt-get install -y libgdiplus`
  - **CentOS / RHEL**: `sudo yum install -y libgdiplus`
  - **macOS**: `brew install mono-libgdiplus`
  - *免安装替代方案*：在 Windows 本地登录生成 `BBDown.data` 后复制到服务器使用，或通过 `-c "SESSDATA=..."` 手动传参。

---

## 3. 画质、权限与账号问题

### Q4: 为什么下载的视频最高只有 480P / 720P，无法下载 1080P60 / 4K / 8K？
- **故障成因**：B 站对未登录游客限制了清晰度；部分 1080P+ 及高帧率画质仅对大会员开放。
- **解决方案**：
  - 执行 `BBDown login`（或 `BBDown logintv`）扫码登录拥有大会员权限的账号。
  - 确认登录成功后再次执行下载即可获取对应高画质。

### Q5: 提示 `[警告] 充电专属视频，接口只返回了试看片段` 并主动退出？
- **故障成因**：目标视频为 UP 主充电专属视频，但当前登录账号未为该 UP 充电。
- **解决方案**：
  - 登录已为该 UP 充电的账号后重新下载。
  - 若确实需要保存前几分钟试看内容，追加 `--allow-preview` 参数。

### Q6: 解析 UP 主空间全部投稿时非常缓慢？
- **故障成因**：B 站空间列表接口仅返回稿件元数据，BBDown 需为每个稿件单独请求详情以展开多分 P 结构。
- **优化建议**：
  - 配合 `-p 1-20` 先分批解析下载。
  - 配合 `--save-archives-to-file` 记录已完成历史，防止重复解析。

---

## 4. 下载速度与网络连接

### Q7: 下载速度慢、频繁断流或卡在 99%？
- **优化技巧**：
  1. **开启强制 HTTP**：追加 `--force-http`（部分运营商 CDN 对 HTTP 流限速策略更宽松）。
  2. **调整分片大小**：修改分片大小 `--thread-segment-size 10` 或 `--thread-segment-size 30`。
  3. **使用 TV 接口**：追加 `-t`（TV 接口采用不同的 CDN 调度策略）。
  4. **改用 aria2c**：安装 aria2 并追加 `--use-aria2c`。
  5. **更换 UPOS 调度服务器**：通过 `--upos-host` 指定更优的骨干 CDN 节点。

---

## 5. 如何获取详细调试日志以反馈 Issue？

遇到未知异常或解析错误时，可在命令末尾追加 `--debug` 参数：

```bash
BBDown --debug "<URL>"
```

控制台将输出包含完整 HTTP 请求头、响应载荷及异常调用栈的调试日志。在 [GitHub Issues](https://github.com/aliveranme/BBDown/issues) 提交反馈时附上该日志将极大提高问题定位效率。

---

### 🧭 快速跳转

| 上一篇 | 目录导航 | 下一篇 |
| :--- | :---: | ---: |
| ⬅️ [开发者指南与编译构建](Developer-Guide) | 📑 [返回目录](Home) | [知识库首页](Home) ➡️ |
