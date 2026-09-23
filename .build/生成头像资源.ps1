# =====================================================================
#  生成「极夜光」的个人标识资源
#
#  从一张正方形头像生成两套资源:
#    · Windows : .build\avatar.png          (128px 圆头像, 编译时嵌进 EXE 的「关于」对话框)
#    · Android : apk-src\res\drawable-*\ic_avatar.png   (各密度一份, 主界面标题区显示)
#
#  用法: powershell -NoProfile -ExecutionPolicy Bypass -File 生成头像资源.ps1 -Source <一张正方形图片>
#  换头像: 直接再跑一次这个脚本, 指定新的图片就行。
# =====================================================================
param(
    # 不写死某台机器的路径: 默认用项目里的 avatar-source.jpg(如果你放进来了),
    # 否则必须用 -Source 指定。
    [string]$Source = ''
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$D = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$RES = Join-Path $D 'apk-src\res'

if (-not $Source) {
    $def = Join-Path $D 'avatar-source.jpg'
    if (Test-Path $def) { $Source = $def }
}
if (-not $Source -or -not (Test-Path $Source)) {
    Write-Host '没有可用的头像源图。用法:'
    Write-Host '  powershell -File .build\生成头像资源.ps1 -Source "D:\我的头像.jpg"'
    Write-Host '或者把源图命名为 avatar-source.jpg 放到 .build\ 目录下再直接运行本脚本。'
    exit 1
}
Write-Host ('源图: ' + $Source)

# ---- 裁成正方形 + 缩放到 size + 圆形遮罩(圆外用 alpha=0 抠掉) ----
function New-CircleAvatar([string]$srcPath, [int]$size, [string]$outPath) {
    $srcBmp = New-Object System.Drawing.Bitmap($srcPath)

    # 中心裁正方形(输入不是正方形也不会拉变形)
    $side = [Math]::Min($srcBmp.Width, $srcBmp.Height)
    $offX = [int](($srcBmp.Width  - $side) / 2)
    $offY = [int](($srcBmp.Height - $side) / 2)

    $dst = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $cx = ($size - 1) / 2.0
    $cy = ($size - 1) / 2.0
    $r  = ($size / 2.0) - 1.0
    $rr = $r * $r

    for ($y = 0; $y -lt $size; $y++) {
        for ($x = 0; $x -lt $size; $x++) {
            $dx = $x - $cx
            $dy = $y - $cy
            if (($dx * $dx + $dy * $dy) -le $rr) {
                $sx = $offX + [int]([Math]::Round($x * ($side - 1) / [double]($size - 1)))
                $sy = $offY + [int]([Math]::Round($y * ($side - 1) / [double]($size - 1)))
                $dst.SetPixel($x, $y, $srcBmp.GetPixel($sx, $sy))
            }
            # 圆外保持透明(默认就是 0,0,0,0)
        }
    }

    $dir = Split-Path $outPath -Parent
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $dst.Save($outPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $dst.Dispose()
    $srcBmp.Dispose()
    $kb = [Math]::Round((Get-Item $outPath).Length / 1KB, 1)
    Write-Host ('  ' + $outPath.Replace($D, '.') + '  ' + $size + 'px  ' + $kb + ' KB')
}

# ---- 缩成正方形 JPEG（安卓用; 圆角在代码里裁, 所以不用透明）----
function New-SquareJpeg([string]$srcPath, [int]$size, [int]$quality, [string]$outPath) {
    $srcBmp = New-Object System.Drawing.Bitmap($srcPath)
    $side = [Math]::Min($srcBmp.Width, $srcBmp.Height)
    $offX = [int](($srcBmp.Width  - $side) / 2)
    $offY = [int](($srcBmp.Height - $side) / 2)

    $dst = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    for ($y = 0; $y -lt $size; $y++) {
        for ($x = 0; $x -lt $size; $x++) {
            $sx = $offX + [int]([Math]::Round($x * ($side - 1) / [double]($size - 1)))
            $sy = $offY + [int]([Math]::Round($y * ($side - 1) / [double]($size - 1)))
            $dst.SetPixel($x, $y, $srcBmp.GetPixel($sx, $sy))
        }
    }

    $dir = Split-Path $outPath -Parent
    if ($dir -and -not (Test-Path $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $codec = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() |
             Where-Object { $_.MimeType -eq 'image/jpeg' }
    $ps = New-Object System.Drawing.Imaging.EncoderParameters(1)
    $ps.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter(
        [System.Drawing.Imaging.Encoder]::Quality, [long]$quality)
    $dst.Save($outPath, $codec, $ps)
    $dst.Dispose()
    $srcBmp.Dispose()
    $kb = [Math]::Round((Get-Item $outPath).Length / 1KB, 1)
    Write-Host ('  ' + $outPath.Replace($D, '.') + '  ' + $size + 'px q' + $quality + '  ' + $kb + ' KB')
}

Write-Host '=== 1) Windows 用的头像(嵌进 EXE) ==='
New-CircleAvatar $Source 128 (Join-Path $D 'avatar.png')

Write-Host '=== 2) 安卓头像(方形 JPEG, 界面上运行时裁成圆的) ==='
# 为什么用 JPEG 不用 PNG 圆形: 这张图 PNG 带透明压不下去 —— 160px 要 47 KB,
# 同一张图 JPEG 只要 7.7 KB(小 6 倍)。安卓那边在代码里裁圆(RoundedBitmapDrawable),
# 所以资源本身不需要透明。
# 只做 3 个密度: 主界面显示 40dp, 大多数手机落在 xxhdpi 上。
New-SquareJpeg $Source 40  82 (Join-Path $RES 'drawable-mdpi\ic_avatar.jpg')
New-SquareJpeg $Source 80  85 (Join-Path $RES 'drawable-xhdpi\ic_avatar.jpg')
New-SquareJpeg $Source 120 85 (Join-Path $RES 'drawable-xxhdpi\ic_avatar.jpg')

Write-Host '=== 3) 校验 ==='
foreach ($p in @(
    (Join-Path $D 'avatar.png'),
    (Join-Path $RES 'drawable-mdpi\ic_avatar.jpg'),
    (Join-Path $RES 'drawable-xxhdpi\ic_avatar.jpg')
)) {
    $b = New-Object System.Drawing.Bitmap($p)
    Write-Host ('  ' + (Split-Path $p -Leaf) + '  ' + $b.Width + 'x' + $b.Height +
                '  ' + [Math]::Round((Get-Item $p).Length / 1KB, 1) + ' KB  像素格式=' + $b.PixelFormat)
    $b.Dispose()
}
Write-Host '完成。'
