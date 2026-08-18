# ==============================================================================
# BBDown Wiki 一键同步脚本 (PowerShell)
# 功能: 将 docs/wiki/ 目录下的所有 Markdown 文档同步推送至 GitHub Wiki 仓库
# ==============================================================================

$ErrorActionPreference = "Stop"

$repoOwner = "aliveranme"
$repoName = "BBDown"
$wikiGitUrl = "https://github.com/$repoOwner/$repoName.wiki.git"
$wikiSourceDir = Join-Path $PSScriptRoot "..\docs\wiki"
$tempWikiDir = Join-Path $env:TEMP "BBDown_Wiki_Sync"

Write-Host ">>> 正在准备同步 Wiki 文档..." -ForegroundColor Cyan

# 1. 确保源目录存在
if (-not (Test-Path $wikiSourceDir)) {
    Write-Error "错误: 找不到 Wiki 源文档目录: $wikiSourceDir"
    exit 1
}

# 2. 清理临时目录
if (Test-Path $tempWikiDir) {
    Remove-Item -Recurse -Force $tempWikiDir
}

# 3. 尝试克隆 Wiki 仓库
Write-Host ">>> 正在连接 GitHub Wiki 仓库: $wikiGitUrl ..." -ForegroundColor Cyan
try {
    git clone $wikiGitUrl $tempWikiDir 2>$null
} catch {
    # 忽略错误
}

if (-not (Test-Path "$tempWikiDir\.git")) {
    Write-Host ">>> Wiki 仓库尚未在 GitHub 上初始化，尝试创建本地仓库并推送..." -ForegroundColor Yellow
    New-Item -ItemType Directory -Force -Path $tempWikiDir | Out-Null
    Set-Location $tempWikiDir
    git init -b master
    git remote add origin $wikiGitUrl
} else {
    Set-Location $tempWikiDir
}

# 4. 拷贝最新文档
Write-Host ">>> 复制 docs/wiki/ 到暂存区..." -ForegroundColor Cyan
Copy-Item -Path "$wikiSourceDir\*" -Destination $tempWikiDir -Force

# 5. 提交并推送
git add .
$status = git status --porcelain
if ([string]::IsNullOrWhiteSpace($status)) {
    Write-Host ">>> Wiki 文档内容无变更，无需推送。" -ForegroundColor Green
} else {
    git commit -m "docs(wiki): sync wiki documentation from repository"
    Write-Host ">>> 正在推送到 GitHub Wiki..." -ForegroundColor Cyan
    git push -u origin master
    if ($LASTEXITCODE -eq 0) {
        Write-Host ">>> ✅ Wiki 同步成功！访问地址: https://github.com/$repoOwner/$repoName/wiki" -ForegroundColor Green
    } else {
        Write-Host ">>> ⚠️ 推送失败。如果是首次创建 Wiki，请先前往浏览器访问 https://github.com/$repoOwner/$repoName/wiki 点击一次 'Create the first page'，然后再运行本脚本。" -ForegroundColor Yellow
    }
}

# 清理并切回
Set-Location $PSScriptRoot
