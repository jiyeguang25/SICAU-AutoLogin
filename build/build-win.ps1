# =====================================================================
#  校园网自动认证 - Windows 版构建脚本
#  用法: powershell -NoProfile -ExecutionPolicy Bypass -File build-win.ps1
#  产物: <build>\SICAU-win-<版本>.exe                              编译+签名产物(发版用)
#        %LOCALAPPDATA%\SICAU-AutoLogin\SICAU-win-<版本>.exe     实际运行的那份, 启动文件夹指向它
#  为什么不在项目根目录跑(2026-10-08 改):
#    本项目的源码放在 DSH 工作区里。DSH 自带的 Windows ACL 沙箱会给工作区打上
#    「Low 完整性」标签(Mandatory Label\Low:(OI)(CI)(NW), 见 @deepseek-ai/dsh-sandbox-windows-acl
#    的 "Build the Low mandatory label applied with every write grant"), 而
#    **从带这个标签的文件启动的进程会被降成 Low 完整性**(子进程继承), 后果:
#      · WinRT 网络 API(GetConnectionProfiles / GetInternetConnectionProfile)一律 Access denied
#        → 开机自动开热点永远失败
#      · 写 %APPDATA% / %TEMP% 被拒 → 主日志写不进去、热点脚本只能落到临时目录
#    所以「开机要跑的那份 exe」必须放在工作区之外的用户目录里, 项目根里不再留一份。
#  发版: 改下面的 $VER, 然后重新跑这个脚本
#  注意: 构建前请先关掉正在运行的 EXE, 否则复制会失败
# =====================================================================

# ---- 版本号: 改这里就行 -------------------------------------------------
$VER = '1.9'
# ------------------------------------------------------------------------

$ErrorActionPreference = 'Stop'

# 目录按「脚本自己所在的位置」算, 不再写死盘符和用户名 —— 整个项目搬到哪都能直接构建
$D    = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$CSC  = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$TMP  = Join-Path $D ('SICAU-win-' + $VER + '.exe')            # 编译产物(留在 build\ 里, 发版就用它)
$ROOT = [System.IO.Path]::GetFullPath((Join-Path $D '..'))     # 项目根
$RUNA = Join-Path $env:LOCALAPPDATA 'SICAU-AutoLogin'          # 实际运行目录(在工作区之外)
$APP  = Join-Path $RUNA ('SICAU-win-' + $VER + '.exe')         # 启动文件夹指向的那份

Write-Host ('=== 1) 编译  SICAU-win-' + $VER + '.exe ===')
Remove-Item $TMP -Force -ErrorAction SilentlyContinue

& $CSC /nologo /target:winexe /platform:anycpu /optimize+ /codepage:65001 ('/out:' + $TMP) ('/win32icon:' + $D + '\app.ico') ('/win32manifest:' + $D + '\app.manifest') ('/resource:' + $D + '\HotspotScript.ps1,HotspotScript') ('/resource:' + $D + '\avatar.png,avatar') /r:System.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll ($D + '\Ui.cs') ($D + '\Core.cs') ($D + '\App.cs') 2>&1 | Select-String -NotMatch 'warning CS' | Select-Object -First 15 | Out-String

if (-not (Test-Path $TMP)) { Write-Host 'FAILED 编译失败'; exit 1 }

$sz  = [math]::Round((Get-Item $TMP).Length / 1KB, 1)
$md5 = (Get-FileHash $TMP -Algorithm MD5).Hash.Substring(0,12)
Write-Host ('  EXE OK: ' + $sz + ' KB   MD5=' + $md5)

Write-Host '=== 2) 部署到工作区之外的运行目录 ==='
# 放之前先签名：未签名 + 无信誉的 exe，Windows 会弹
# 「已保护你的电脑 / 发布者: 发布者未知」（要点"仍要运行"）。
# 签名工具在 DSH\tools\ 下，两个项目共用；证书不存在时只提示，不算构建失败。
$signer = Join-Path (Split-Path (Split-Path $D -Parent) -Parent) 'tools\Sign-DshApp.ps1'
if (Test-Path -LiteralPath $signer) {
    Write-Host '  签名中...'
    # 真证书（SSL.com 学生包那张）到手后不用改这里：把主题关键字写进环境变量
    #   setx DSH_CODESIGN_FILTER "SSL.com"
    # 签名工具会自动优先用它；没设就走本机自签名兜底。
    $signArgs = @('-Path', $TMP)
    if ($env:DSH_CODESIGN_FILTER) {
        $signArgs += @('-CertSubjectFilter', $env:DSH_CODESIGN_FILTER)
        Write-Host ('  使用真证书, 主题关键字: ' + $env:DSH_CODESIGN_FILTER)
    }
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $signer @signArgs
    if ($LASTEXITCODE -ne 0) { Write-Host '  签名失败(EXE 仍可用, 只是会继续弹 SmartScreen)' }
} else {
    Write-Host ('  签名工具不在, 跳过: ' + $signer)
}

try {
    New-Item -ItemType Directory -Path $RUNA -Force | Out-Null
    Copy-Item $TMP $APP -Force
    $h2 = (Get-FileHash $APP -Algorithm MD5).Hash.Substring(0,12)
    Write-Host ('  已部署到 ' + $APP)
    # 注意：$md5 是"签名之前"算的，签完名 exe 会变，所以这里不一致是正常的，
    # 不代表复制出错。真正要一致的是"build 目录那份和运行目录那份"（都签过之后）。
    Write-Host ('  与签名前的编译产物一致=' + ($md5 -eq $h2) + ' (签名会改文件, 不一致属正常)')
    # 运行目录里那份必须不是 Low 完整性标签(否则开机跑起来还是 Low, 见文件头说明)
    $lbl = (icacls $APP 2>&1 | Select-String 'Mandatory Label')
    if ($lbl -match 'Low') {
        Write-Host '  ⚠ 运行目录那份带 Low 完整性标签, 正在清掉...'
        icacls $APP /setintegritylevel M 2>&1 | Out-Null
    }
} catch {
    Write-Host ('  复制失败(程序可能正开着): ' + $_.Exception.Message.Split([char]10)[0])
}

# 顺手把旧版本清掉, 免得堆一堆分不清哪个是正在用的
Write-Host '=== 2b) 清理旧版本 ==='
$old = @(Get-ChildItem $RUNA -Filter 'SICAU-win-*.exe' -File -ErrorAction SilentlyContinue |
         Where-Object { $_.FullName -ne $APP })
# 项目根目录里以前放的那份也必须删: 从工作区里跑 = Low 完整性 = 开机开热点必失败
$old += @(Get-ChildItem $ROOT -Filter 'SICAU-win-*.exe' -File -ErrorAction SilentlyContinue)
$old = @($old | Sort-Object FullName -Unique)
if ($old.Count -eq 0) {
    Write-Host '  没有旧版本'
} else {
    foreach ($f in $old) {
        try { Remove-Item $f.FullName -Force; Write-Host ('  已删掉旧版本 ' + $f.FullName) }
        catch { Write-Host ('  旧版本删不掉(可能正开着): ' + $f.FullName) }
    }
}
# 以前叫 SICAU-AutoLogin.exe, 改名后别再留着, 免得双击到旧的
foreach ($legacy in @((Join-Path $ROOT 'SICAU-AutoLogin.exe'), (Join-Path $RUNA 'SICAU-AutoLogin.exe'))) {
    if (Test-Path $legacy) {
        try { Remove-Item $legacy -Force; Write-Host ('  已删掉旧名字 ' + $legacy) }
        catch { Write-Host ('  旧名字删不掉(可能正开着): ' + $legacy) }
    }
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
            $s.WorkingDirectory = $RUNA
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
