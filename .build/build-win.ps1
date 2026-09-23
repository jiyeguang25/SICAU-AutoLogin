# =====================================================================
#  校园网自动认证 - Windows 版构建脚本
#  用法: powershell -NoProfile -ExecutionPolicy Bypass -File build-win.ps1
#  产物: <项目根>\SICAU-win-<版本>.exe
#        (程序实际就在项目根目录跑; 桌面那份是用户自己删的, 不往桌面放)
#  发版: 改下面的 $VER, 然后重新跑这个脚本
#  注意: 构建前请先关掉正在运行的 EXE, 否则复制会失败
# =====================================================================

# ---- 版本号: 改这里就行 -------------------------------------------------
$VER = '1.7'
# ------------------------------------------------------------------------

$ErrorActionPreference = 'Stop'

# 目录按「脚本自己所在的位置」算, 不再写死盘符和用户名 —— 整个项目搬到哪都能直接构建
$D    = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$CSC  = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$TMP  = Join-Path $D ('SICAU-win-' + $VER + '.exe')          # 编译产物
$APP  = [System.IO.Path]::GetFullPath((Join-Path $D ('..\SICAU-win-' + $VER + '.exe')))   # 程序运行的位置
$ROOT = Split-Path $APP -Parent

Write-Host ('=== 1) 编译  SICAU-win-' + $VER + '.exe ===')
Remove-Item $TMP -Force -ErrorAction SilentlyContinue

& $CSC /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 ('/out:' + $TMP) ('/win32icon:' + $D + '\app.ico') ('/win32manifest:' + $D + '\app.manifest') ('/resource:' + $D + '\HotspotScript.ps1,HotspotScript') ('/resource:' + $D + '\avatar.png,avatar') /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ($D + '\Ui.cs') ($D + '\Core.cs') ($D + '\App.cs') 2>&1 | Select-String -NotMatch 'warning CS' | Select-Object -First 15 | Out-String

if (-not (Test-Path $TMP)) { Write-Host 'FAILED 编译失败'; exit 1 }

$sz  = [math]::Round((Get-Item $TMP).Length / 1KB, 1)
$md5 = (Get-FileHash $TMP -Algorithm MD5).Hash.Substring(0,12)
Write-Host ('  EXE OK: ' + $sz + ' KB   MD5=' + $md5)

Write-Host '=== 2) 放到程序运行的位置(项目根目录) ==='
try {
    Copy-Item $TMP $APP -Force
    $h2 = (Get-FileHash $APP -Algorithm MD5).Hash.Substring(0,12)
    Write-Host ('  已部署到 ' + $APP)
    Write-Host ('  与编译产物一致=' + ($md5 -eq $h2))
} catch {
    Write-Host ('  复制失败(程序可能正开着): ' + $_.Exception.Message.Split([char]10)[0])
}

# 顺手把项目根目录下"别的版本"清掉, 免得堆一堆分不清哪个是正在用的
Write-Host '=== 2b) 清理旧版本 ==='
$old = @(Get-ChildItem $ROOT -Filter 'SICAU-win-*.exe' -File -ErrorAction SilentlyContinue |
         Where-Object { $_.FullName -ne $APP })
if ($old.Count -eq 0) {
    Write-Host '  没有旧版本'
} else {
    foreach ($f in $old) {
        try { Remove-Item $f.FullName -Force; Write-Host ('  已删掉旧版本 ' + $f.Name) }
        catch { Write-Host ('  旧版本删不掉(可能正开着): ' + $f.Name) }
    }
}
# 以前叫 SICAU-AutoLogin.exe, 改名后别再留着, 免得双击到旧的
$legacy = Join-Path $ROOT 'SICAU-AutoLogin.exe'
if (Test-Path $legacy) {
    try { Remove-Item $legacy -Force; Write-Host '  已删掉旧名字 SICAU-AutoLogin.exe' }
    catch { Write-Host '  旧名字删不掉(可能正开着): SICAU-AutoLogin.exe' }
}

Write-Host '=== 3) 开机自启 ==='
# 2026-09-21 起不用计划任务了(被 Defender 判成 Behavior:Win32/Persistence.A!ml 删过),
# 改成「启动」文件夹里的快捷方式
$lnk = Join-Path ([Environment]::GetFolderPath('Startup')) '校园网自动认证.lnk'
if (Test-Path $lnk) {
    $sh = New-Object -ComObject WScript.Shell
    $s = $sh.CreateShortcut($lnk)
    if ($s.TargetPath -ne $APP) {
        # 程序改名/换位置之后, 快捷方式得跟着走, 否则开机自启就跑的是老文件
        $was = $s.TargetPath
        try {
            $s.TargetPath = $APP
            $s.WorkingDirectory = $ROOT
            $s.Save()
            Write-Host ('  启动了旧指向: ' + $was)
            Write-Host ('  ↳ 已改指向: ' + $APP)
        } catch {
            Write-Host ('  启动文件夹: ' + $was)
            Write-Host ('  ↳ 改指向失败: ' + $_.Exception.Message.Split([char]10)[0])
        }
    } else {
        Write-Host ('  启动文件夹: ' + $s.TargetPath + ' ' + $s.Arguments + '  (指向正确, 不用改)')
    }
} else {
    Write-Host '  启动文件夹里没有快捷方式 (程序里勾「开机自动认证」并保存后会创建)'
}
