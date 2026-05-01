# InfinityFree 文件打包脚本
# 将所有需要的文件复制到临时文件夹用于上传

$ErrorActionPreference = "Stop"

Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "InfinityFree 上传文件打包工具" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

# 获取当前目录
$currentDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$sourceDir = $currentDir
$uploadDir = Join-Path $currentDir "upload_package"

# 如果已存在，删除旧目录
if (Test-Path $uploadDir) {
    Write-Host "删除旧的上传目录..." -ForegroundColor Yellow
    Remove-Item -Recurse -Force $uploadDir
}

# 创建新目录
Write-Host "创建上传目录..." -ForegroundColor Green
New-Item -ItemType Directory -Path $uploadDir | Out-Null

# 需要上传的文件列表
$filesToUpload = @(
    "config.php",
    "database.sql",
    "receive.php",
    "check_status.php",
    "get_pending_messages.php",
    "submit_response.php",
    "index.html",
    "check_config.php",
    "README.md",
    "QUICKSTART.md",
    "PROJECT_INFO.md",
    "上传说明.txt"
)

# 复制文件
Write-Host "`n开始复制文件..." -ForegroundColor Green
$copiedCount = 0

foreach ($file in $filesToUpload) {
    $sourcePath = Join-Path $sourceDir $file
    $destPath = Join-Path $uploadDir $file
    
    if (Test-Path $sourcePath) {
        Copy-Item -Path $sourcePath -Destination $destPath -Force
        Write-Host "  ✓ $file" -ForegroundColor Gray
        $copiedCount++
    } else {
        Write-Host "  ⚠ 警告：$file 不存在" -ForegroundColor Yellow
    }
}

Write-Host "`n=====================================" -ForegroundColor Cyan
Write-Host "打包完成！" -ForegroundColor Green
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "✓ 成功复制 $copiedCount 个文件" -ForegroundColor Green
Write-Host ""
Write-Host "📁 上传目录：" -NoNewline -ForegroundColor Cyan
Write-Host "$uploadDir" -ForegroundColor White
Write-Host ""
Write-Host "下一步操作：" -ForegroundColor Yellow
Write-Host "1. 使用 FTP 客户端（如 FileZilla）连接到 InfinityFree" -ForegroundColor White
Write-Host "2. 进入 /htdocs 目录" -ForegroundColor White
Write-Host "3. 将 upload_package 文件夹中的所有文件上传到 /htdocs" -ForegroundColor White
Write-Host "   （注意：不要上传 upload_package 文件夹本身，只上传里面的文件）" -ForegroundColor White
Write-Host "4. 访问 https://your-domain.infinityfreeapp.com/check_config.php 验证" -ForegroundColor White
Write-Host ""
Write-Host "按任意键打开上传目录..." -ForegroundColor Yellow
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")

# 打开上传目录
Invoke-Item $uploadDir
