# 账号登录与鉴权指南 (Authentication)

> 本文档介绍如何在 BBDown 中配置与维护 B 站账号登录凭据，以解锁 1080P 高码率、4K、8K、杜比视界/全景声、付费课程、UP 主全量投稿及稍后再看等受保护内容。

---

## 1. 登录权限与收益对照

| 账号状态 | 可获最高画质 | 音频规格 | 特殊功能与权限 |
| :--- | :--- | :--- | :--- |
| **未登录 (游客)** | 480P / 普通 720P | 普通 128kbps AAC | 仅能下载普通公开单视频 |
| **普通登录账号** | 1080P 高清 (部分源) | 320kbps AAC | 可下载个人公开列表、无水印 TV 源 |
| **大会员账号** | 1080P60 / 4K / 8K / HDR / 杜比视界 | 杜比全景声 / 无损 FLAC / Hi-Res | 番剧全集、UP 主全量稿件、稍后再看 |
| **付费/已充电账号** | 购买课程最高画质 / 完整充电内容 | 完整音频规格 | 配合 `--decrypt-drm` 解密付费课程 |

---

## 2. 扫码登录方式（推荐）

### 2.1 网页端扫码登录 (WEB)
在终端中执行以下命令：
```bash
BBDown login
```
1. 终端将自动渲染出 ASCII 字符二维码。
2. 打开手机哔哩哔哩 APP 扫码并点击「确认登录」。
3. 登录成功后，凭据将安全保存在程序同级目录的 **`BBDown.data`** 文件中。
4. 后续执行任何下载命令均会自动静默加载该凭据，无需每次手动传参。

### 2.2 云视听小电视扫码登录 (TV)
如果需要经常使用 `-t`（TV 端接口）下载无水印源或专享视频：
```bash
BBDown logintv
```
- 授权成功后，凭据将保存在同级目录的 **`BBDownTV.data`** 文件中。

> [!TIP]
> **APP 端凭据复用小技巧**：
> TV 端生成的 `access_token` 可以直接用于 APP 端接口。只需将生成的 `BBDownTV.data` 复制一份并重命名为 **`BBDownApp.data`**，即可直接享受 `BBDown -a <URL>` APP 接口解析。

---

## 3. 手动凭据注入 (CI/CD 与无头环境)

在服务器无头环境、自动化脚本或容器中运行时，可以通过参数直接注入凭据：

### 3.1 注入 Cookie 字符串 (`-c`)
```bash
BBDown -c "SESSDATA=xxxxxx; bili_jct=xxxxxx; DedeUserID=xxxxxx" "https://www.bilibili.com/video/BV1qt4y1X7TW"
```
> [!NOTE]
> 在浏览器登录 B 站，按 `F12` 打开控制台，在 Application -> Cookies 中复制 `SESSDATA` 键值即可（通常只需 `SESSDATA` 即可通过大会员身份鉴权）。

### 3.2 注入 Access Token (`--access-token`)
```bash
BBDown -a --access-token "5227************1" "https://www.bilibili.com/video/BV1qt4y1X7TW"
```

---

## 4. 充电专属视频防御机制

UP 主设置的充电专属稿件在当前账号未充电时，B 站接口不会报错，而是照常下发前几分钟的试看片段。

```
[警告] 充电专属视频
当前账号没有该UP主的充电权限，接口只返回了 00:06:29 的试看片段（完整视频 02:23:48）
已跳过。如需下载试看片段，请加 --allow-preview
```

- **默认安全防御**：BBDown 会自动比对声明时长与实际媒体流时长，发现试看片段时立即**主动终止并返回退出码 2**，防止错误生成残损文件。
- **允许试看片段**：若确实需要保留前几分钟试看内容，可显式追加 `--allow-preview`，产出文件名将包含 `[试看]` 标识。

---

## 5. 凭据文件管理速查

| 凭据文件 | 对应接口模式 | 对应命令 | 包含核心内容 |
| :--- | :--- | :--- | :--- |
| `BBDown.data` | WEB 网页端 | `BBDown login` | SESSDATA、bili_jct、DedeUserID、有效期 |
| `BBDownTV.data` | TV 小电视端 | `BBDown logintv` | TV Access Token、Refresh Token、Mid |
| `BBDownApp.data` | APP 移动端 | 手动复制或抓包 | APP Access Token |

---

### 🧭 快速跳转

| 上一篇 | 目录导航 | 下一篇 |
| :--- | :---: | ---: |
| ⬅️ [全命令行参数详解](CLI-Reference) | 📑 [返回目录](Home) | [配置文件与命名规则](Configuration-and-Templates) ➡️ |
