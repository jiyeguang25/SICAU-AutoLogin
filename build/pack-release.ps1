# =====================================================================
#  发版打包：把 exe (+ apk) + 下载说明.txt 打成一个 zip
#  用法: powershell -NoProfile -ExecutionPolicy Bypass -File pack-release.ps1
#        powershell ... -File pack-release.ps1 -NoAndroid      # 只发 Windows 版
#  产物: <项目根>\release\SICAU-AutoLogin-v<版本>.zip
#
#  为什么发 zip 不发裸 exe：浏览器下载的 exe 会带「来自 Internet」标记，
#  双击会弹 SmartScreen「发布者: 发布者未知」；从 zip 里解压出来的不带。
#  详见开发笔记 §10。
#
#  版本号只有一个来源: build\Core.cs 的 Core.AppVersion。
#  以前「包版本 = 程序版本 + 0.1」的错位规矩已废弃（2026-10-25 起统一），
#  所以这里直接用程序版本当包版本，不再 +0.1。
#
#  -NoAndroid：安卓端自 v2.1 起暂停更新（2026-10-28 用户决定）。
#  加了这个开关就不要求 apk 存在，zip 里只有 exe，下载说明里也写明
#  「本次只发 Windows 版、安卓版停在 v2.1」—— 免得拿到包的人以为漏了文件。
# =====================================================================

param([switch]$NoAndroid)

$ErrorActionPreference = 'Stop'

$D    = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$ROOT = [System.IO.Path]::GetFullPath((Join-Path $D '..'))

# ---- 版本号：从 Core.cs 读，别在这里再写一份 ---------------------------
$coreSrc = Get-Content (Join-Path $D 'Core.cs') -Raw -Encoding UTF8
$mv = [regex]::Match($coreSrc, 'AppVersion\s*=\s*"([0-9.]+)"')
if (-not $mv.Success) { Write-Host 'FAILED 从 Core.cs 读不出版本号'; exit 1 }
$VER = $mv.Groups[1].Value

$EXE = Join-Path $D ('SICAU-win-' + $VER + '.exe')
$APK = Join-Path (Join-Path $ROOT 'android') ('SICAU-android-' + $VER + '.apk')
$OUTDIR = Join-Path $ROOT 'release'
$ZIP = Join-Path $OUTDIR ('SICAU-AutoLogin-v' + $VER + '.zip')

Write-Host ('=== 打包 v' + $VER + ' ===')

# ---- 产物检查：exe 必须有；apk 只在没加 -NoAndroid 时要求 ---------------
$need = if ($NoAndroid) { @($EXE) } else { @($EXE, $APK) }
$bad = 0
foreach ($f in $need) {
    if (-not (Test-Path $f)) {
        Write-Host ('  ✗ 缺产物: ' + $f)
        $bad++
    }
}
if ($bad -gt 0) {
    if ($NoAndroid) { Write-Host '  先构建: build-win.ps1' }
    else { Write-Host '  先把两个平台都构建一遍: build-win.ps1 / build-apk.ps1（安卓暂不发可加 -NoAndroid）' }
    exit 1
}

# ---- 中间目录（工作区里会被打 Low 完整性，aapt2/压缩都不受影响）--------
$stage = Join-Path $env:TEMP ('sicau-pack-' + $VER)
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path $stage | Out-Null
New-Item -ItemType Directory -Force -Path $OUTDIR | Out-Null

Copy-Item $EXE (Join-Path $stage (Split-Path $EXE -Leaf)) -Force
if (-not $NoAndroid) { Copy-Item $APK (Join-Path $stage (Split-Path $APK -Leaf)) -Force }

$h1 = (Get-FileHash $EXE -Algorithm SHA256).Hash.ToLower()
Write-Host ('  exe ' + [math]::Round((Get-Item $EXE).Length/1KB,1) + ' KB  ' + $h1)
if ($NoAndroid) {
    Write-Host '  apk 本次不发（安卓端自 v2.1 起暂停更新）'
} else {
    $h2 = (Get-FileHash $APK -Algorithm SHA256).Hash.ToLower()
    Write-Host ('  apk ' + [math]::Round((Get-Item $APK).Length/1KB,1) + ' KB  ' + $h2)
}

# ---- 下载说明.txt ------------------------------------------------------
$exeName = Split-Path $EXE -Leaf
$apkName = if ($NoAndroid) { '' } else { Split-Path $APK -Leaf }
$pad = 34
if ($NoAndroid) {
    $apkNote = @"
【关于安卓版】
这个压缩包里只有 Windows 程序，没有安卓版。
安卓端从 v2.1 起暂停更新（没有新功能，也不再修 bug），
最新可用版本仍是 v2.1：https://github.com/jiyeguang25/SICAU-AutoLogin/releases/tag/v2.1

"@
    $hashBlock = "  $($exeName.PadRight($pad))$h1"
} else {
    $apkNote = ''
    $hashBlock = "  $($exeName.PadRight($pad))$h1`r`n  $($apkName.PadRight($pad))$h2"
}
$txt = @"
下载说明 / README
========================================
版本: v$VER
打包时间: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')

【为什么给你一个 zip 而不是 exe】
浏览器下载 exe 会给文件打上"来自 Internet"标记，Windows 的 SmartScreen 会弹
「Windows 已保护你的电脑 —— 发布者: 发布者未知」，要手动点"仍要运行"。
从这个 zip 里解压出来的 exe 不带那个标记，双击直接就能跑。

【怎么用】
1. 右键这个 zip -> 全部解压缩（不要直接在压缩包里双击运行）
2. 进入解压出来的文件夹，双击里面的程序

$apkNote【校验文件没被掉包】
PowerShell 里执行下面命令，和下面的哈希对比：
  Get-FileHash .\<文件名> -Algorithm SHA256

SHA256:
$hashBlock
"@
# 老规矩：给 .txt 带 BOM，记事本/手机都能正确识别中文
[IO.File]::WriteAllText((Join-Path $stage '下载说明.txt'), ($txt -replace "`r?`n", "`r`n"), (New-Object Text.UTF8Encoding($true)))

# ---- 压缩 --------------------------------------------------------------
Add-Type -AssemblyName System.IO.Compression.FileSystem
Remove-Item $ZIP -Force -ErrorAction SilentlyContinue
[IO.Compression.ZipFile]::CreateFromDirectory($stage, $ZIP, [IO.Compression.CompressionLevel]::Optimal, $false)

$zsz = [math]::Round((Get-Item $ZIP).Length / 1KB, 1)
$zh  = (Get-FileHash $ZIP -Algorithm SHA256).Hash.ToLower()
Write-Host ('  zip ' + $zsz + ' KB  ' + $ZIP)
Write-Host ('      SHA256=' + $zh)
Write-Host ''
Write-Host '  接下来: 打成 tag 并传 GitHub Release'
Write-Host ('    git tag -a v' + $VER + ' -m "v' + $VER + '"')
Write-Host ('    上传 ' + $ZIP)
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
