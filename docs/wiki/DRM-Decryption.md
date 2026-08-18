# Widevine DRM 原生解密指南 (DRM Decryption)

> 本文档介绍如何在 BBDown 中使用内置的原生 Widevine CDM 解密并下载哔哩哔哩受 DRM 保护的内容（如付费课程 Cheese、特定版权番剧等）。

---

## 1. 核心技术优势

传统工具通常需要额外安装 Python 运行时及第三方库（如 `pywidevine`、`protobuf`、`cryptography` 等）才能处理 Widevine DRM 协议。

BBDown 内置了**纯原生 C# 实现的 Widevine Content Decryption Module (CDM)**：
- **零外部环境依赖**：无需安装 Python、无需配置 pip 虚拟环境。
- **全自动化流程**：自动请求 B 站许可证服务器获取内容解密密钥，自动完成流分片解密，并无缝调用 FFmpeg 混流。
- **支持手动注入**：对于特殊离线场景，也支持手动通过 `--key` 和 `--kid` 传入已知密钥。

---

## 2. 准备工作：配置 `device.wvd`

解密 Widevine DRM 内容必须提供一个有效的 Widevine L3 设备凭据文件（`device.wvd`）。

### 2.1 放置位置与检索优先级
BBDown 在启动时会按以下优先级自动在系统中检索 `device.wvd`：
1. **当前程序目录**：直接将 `device.wvd` 文件放置在与 `BBDown.exe` 相同的目录下。
2. **环境变量 `PATH` 目录**：放置在任意已加入系统 `PATH` 的路径中。
3. **操作系统标准路径**：
   - **Windows**：程序所在目录
   - **macOS**：`/opt/homebrew/bin` 或 `/usr/local/bin`
   - **Linux**：`/usr/local/bin`
4. **显式命令行指定**：通过 `--wvd-path` 手动指定：
   ```bash
   BBDown --decrypt-drm --wvd-path "/path/to/device.wvd" <URL>
   ```

---

## 3. 自动解密下载实战

### 3.1 下载 DRM 付费课程（Cheese 课堂）
确保已登录拥有该课程购买权限的账号（执行过 `BBDown login`），然后追加 `--decrypt-drm`：

```bash
BBDown --decrypt-drm "https://www.bilibili.com/cheese/play/ep1243104"
```

**内部执行流程**：
1. BBDown 检测到 DRM 流（`drm_tech_type=2`）。
2. 从本地加载 `device.wvd` 构建 CDM 客户端。
3. 向 B 站许可证服务器发送 Challenge 请求并获取解密密钥（Key & KID）。
4. 对下载的加密分片完成原生解密，并混流为标准 `.mp4` 文件。

---

## 4. 手动指定密钥模式

如果已通过其他方式获取了该视频的解密 Key 和 KID（Hex 字符串），可以直接手动传入：

```bash
BBDown --key "0123456789abcdef0123456789abcdef" --kid "fedcba9876543210fedcba9876543210" <URL>
```

---

## 5. 常见问题与排障

- **报错 `Cannot find device.wvd`**：
  未检测到设备凭据文件。请确认文件名确为 `device.wvd` 并放置在程序所在目录。
- **获取许可证返回 403 / 失败**：
  请检查当前登录账号是否已购买该课程/番剧。DRM 解密无法绕过账号的购买鉴权，必须拥有合法的播放权限。
- **部分超高清画质未下发密钥**：
  B 站部分最高画质仅向硬件 L1 设备下发密钥，L3 凭据通常可解密 1080P 及以下画质，此时程序会自动选择 L3 支持的最高画质。

---

### 🧭 快速跳转

| 上一篇 | 目录导航 | 下一篇 |
| :--- | :---: | ---: |
| ⬅️ [批量下载与自动化脚本](Batch-and-Automation) | 📑 [返回目录](Home) | [API 服务器与 Docker 部署](API-Server-and-Docker) ➡️ |
