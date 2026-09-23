$ErrorActionPreference = 'Continue'
# 目录按「脚本自己所在的位置」算, 不再写死盘符和用户名 —— 整个项目搬到哪都能直接构建
$D = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
$S = $D + '\apk-src'
$SDK = $D + '\sdk'
$BT = $SDK + '\build-tools\35.0.0'
$AJAR = $SDK + '\platforms\android-34\android.jar'
$OUT = $D + '\apk-out'

# ---- 找 JDK：优先环境变量, 再扫常见安装位置（不写死某台机器的路径）----
function Find-Jdk {
    # 1) 显式指定: $env:SICAU_JDK 或 JdkPath.txt
    if ($env:SICAU_JDK -and (Test-Path (Join-Path $env:SICAU_JDK 'bin\javac.exe'))) { return $env:SICAU_JDK }
    $cfg = Join-Path $D 'JdkPath.txt'
    if (Test-Path $cfg) {
        $p = (Get-Content $cfg -First 1).Trim()
        if ($p -and (Test-Path (Join-Path $p 'bin\javac.exe'))) { return $p }
    }
    # 2) JAVA_HOME
    if ($env:JAVA_HOME -and (Test-Path (Join-Path $env:JAVA_HOME 'bin\javac.exe'))) { return $env:JAVA_HOME }
    # 3) 常见安装目录里挑版本号最大的
    foreach ($base in @("$env:ProgramFiles\Java", "$env:ProgramFiles\Eclipse Adoptium",
                        "$env:ProgramFiles\Microsoft", "$env:ProgramFiles\Android\Android Studio\jbr",
                        "${env:ProgramFiles(x86)}\Java")) {
        if (-not (Test-Path $base)) { continue }
        if (Test-Path (Join-Path $base 'bin\javac.exe')) { return $base }   # Android Studio 的 jbr
        $cand = Get-ChildItem $base -Directory -ErrorAction SilentlyContinue |
                Where-Object { Test-Path (Join-Path $_.FullName 'bin\javac.exe') } |
                Sort-Object Name -Descending | Select-Object -First 1
        if ($cand) { return $cand.FullName }
    }
    # 4) PATH 里的 javac
    $jc = Get-Command javac.exe -ErrorAction SilentlyContinue
    if ($jc) { return (Split-Path (Split-Path $jc.Source -Parent) -Parent) }
    return $null
}

$JDKP = Find-Jdk
if (-not $JDKP) {
    Write-Host 'FAILED 找不到 JDK。三种解决办法(任选一种):'
    Write-Host '  1) 装一个 JDK 8 或更高版本 (https://adoptium.net)'
    Write-Host ('  2) 设环境变量 SICAU_JDK 指向 JDK 目录, 例如: $env:SICAU_JDK = ''C:\Program Files\Java\jdk-21''')
    Write-Host ('  3) 在本脚本旁边建一个 JdkPath.txt, 第一行写 JDK 目录')
    exit 1
}
$JDK = $JDKP + '\bin'
$env:JAVA_HOME = $JDKP
Write-Host ('JDK: ' + $JDKP)

# ---- 检查 Android SDK ----
if (-not (Test-Path ($BT + '\aapt2.exe'))) {
    Write-Host ('FAILED 找不到 Android SDK (构建工具): ' + $BT)
    Write-Host '  需要 build-tools 35.0.0 和 platforms/android-34, 放到 .build\sdk\ 下面。'
    Write-Host '  用 Android Studio 的 SDK Manager 装完, 把 sdk 目录整个复制过来就行。'
    Write-Host '  注意: 必须用 build-tools 35.0.0 —— 34.0.0 自带的 d8 在这个工程上会内部报错(R8 的 bug)。'
    exit 1
}

# 版本号从 AndroidManifest.xml 里读, 一个地方说了算, 免得脚本和清单对不上
$MANIFEST = $S + '\AndroidManifest.xml'
try {
  [xml]$mx = Get-Content $MANIFEST -Encoding UTF8
  $VNAME = $mx.manifest.versionName
  $VCODE = $mx.manifest.versionCode
} catch {
  Write-Host ('FAILED 读 AndroidManifest.xml 失败: ' + $_.Exception.Message); exit 1
}
if (-not $VNAME) { Write-Host 'FAILED 清单里没有 versionName'; exit 1 }
$APKDIR = [System.IO.Path]::GetFullPath(($D + '\..\android'))
$FINAL  = $APKDIR + '\SICAU-android-' + $VNAME + '.apk'
Write-Host ('版本: versionName=' + $VNAME + ' versionCode=' + $VCODE)
Write-Host ('产物: ' + $FINAL)

Remove-Item $OUT -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force -Path ($OUT + '\classes'), ($OUT + '\gen'), ($OUT + '\dex') | Out-Null

Write-Host '=== 1) aapt2 compile ==='
& ($BT + '\aapt2.exe') compile --dir ($S + '\res') -o ($OUT + '\res.zip') 2>&1 | Out-String
if ($LASTEXITCODE -ne 0) { Write-Host 'FAILED compile'; exit 1 }

Write-Host '=== 2) aapt2 link ==='
& ($BT + '\aapt2.exe') link -o ($OUT + '\base.apk') -I $AJAR --manifest $MANIFEST --auto-add-overlay ($OUT + '\res.zip') --java ($OUT + '\gen') --min-sdk-version 21 --target-sdk-version 34 --version-code $VCODE --version-name $VNAME 2>&1 | Out-String
if (-not (Test-Path ($OUT + '\base.apk'))) { Write-Host 'FAILED link'; exit 1 }
Write-Host ('base.apk = ' + (Get-Item ($OUT + '\base.apk')).Length)

Write-Host '=== 3) javac ==='
$all = New-Object System.Collections.Generic.List[string]
foreach ($f in @('Http.java','SicauAuth.java','AppLog.java','Prefs.java','Scheduler.java','AuthJobService.java','BootReceiver.java','KeepAliveService.java','MainActivity.java','WifiPlan.java','WifiSwitch.java','WifiScan.java','AppCtx.java')) {
  $all.Add($S + '\' + $f)
}
foreach ($g in @(Get-ChildItem ($OUT + '\gen') -Recurse -Filter *.java)) { $all.Add($g.FullName) }
Write-Host ('source files = ' + $all.Count)

$carg = New-Object System.Collections.Generic.List[string]
foreach ($o in @('-encoding','UTF-8','-nowarn','-source','8','-target','8','-bootclasspath',$AJAR,'-cp',$AJAR,'-d',($OUT + '\classes'))) { $carg.Add($o) }
foreach ($f in $all) { $carg.Add($f) }
$jout = (& ($JDK + '\javac.exe') @carg 2>&1 | Out-String)
$jrc = $LASTEXITCODE
if ($jrc -ne 0) { Write-Host $jout; Write-Host ('FAILED javac (exit=' + $jrc + ')'); exit 1 }

$ncls = @(Get-ChildItem ($OUT + '\classes') -Recurse -Filter *.class -ErrorAction SilentlyContinue).Count
Write-Host ('class files = ' + $ncls)
# 期望的类: 9 个源文件 + 若干匿名内部类, 少于 12 个肯定有问题
if ($ncls -lt 12) { Write-Host 'FAILED javac: class files too few'; exit 1 }

Write-Host '=== 4) d8 ==='
$darg = New-Object System.Collections.Generic.List[string]
foreach ($o in @('--lib',$AJAR,'--min-api','21','--output',($OUT + '\dex'))) { $darg.Add($o) }
foreach ($c in @(Get-ChildItem ($OUT + '\classes') -Recurse -Filter *.class)) { $darg.Add($c.FullName) }
& ($BT + '\d8.bat') @darg 2>&1 | Out-String
if (-not (Test-Path ($OUT + '\dex\classes.dex'))) { Write-Host 'FAILED d8'; exit 1 }
Write-Host ('classes.dex = ' + (Get-Item ($OUT + '\dex\classes.dex')).Length)

Write-Host '=== 5) 打包 dex ==='
Add-Type -AssemblyName System.IO.Compression.FileSystem
Copy-Item ($OUT + '\base.apk') ($OUT + '\app-unsigned.apk') -Force
$zip = [System.IO.Compression.ZipFile]::Open(($OUT + '\app-unsigned.apk'), 'Update')
[void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, ($OUT + '\dex\classes.dex'), 'classes.dex', [System.IO.Compression.CompressionLevel]::Optimal)
$zip.Dispose()
Write-Host ('unsigned = ' + (Get-Item ($OUT + '\app-unsigned.apk')).Length)

Write-Host '=== 6) zipalign ==='
& ($BT + '\zipalign.exe') -p -f 4 ($OUT + '\app-unsigned.apk') ($OUT + '\app-aligned.apk') 2>&1 | Out-String
if (-not (Test-Path ($OUT + '\app-aligned.apk'))) { Write-Host 'FAILED zipalign'; exit 1 }

Write-Host '=== 7) keystore ==='
if (-not (Test-Path ($D + '\sicau.keystore'))) {
  & ($JDK + '\keytool.exe') -genkeypair -v -keystore ($D + '\sicau.keystore') -alias sicau -keyalg RSA -keysize 2048 -validity 10000 -storepass sicau123456 -keypass sicau123456 -dname 'CN=SICAU AutoLogin,O=SICAU,L=Yaan,ST=Sichuan,C=CN' 2>&1 | Out-String
}

Write-Host '=== 8) sign ==='
New-Item -ItemType Directory -Force -Path $APKDIR | Out-Null
Remove-Item $FINAL -Force -ErrorAction SilentlyContinue
& ($BT + '\apksigner.bat') sign --ks ($D + '\sicau.keystore') --ks-pass pass:sicau123456 --key-pass pass:sicau123456 --v1-signing-enabled true --v2-signing-enabled true --out $FINAL ($OUT + '\app-aligned.apk') 2>&1 | Out-String
if (-not (Test-Path $FINAL)) { Write-Host 'FAILED sign'; exit 1 }

# 顺手清掉旧名字/旧版本的包, 免得装错
Write-Host '=== 8b) 清理旧包 ==='
$staleApk = @(Get-ChildItem $APKDIR -Filter '*.apk' -File -ErrorAction SilentlyContinue |
              Where-Object { $_.FullName -ne $FINAL })
if ($staleApk.Count -eq 0) {
  Write-Host '  没有旧包'
} else {
  foreach ($f in $staleApk) {
    try { Remove-Item $f.FullName -Force; Write-Host ('  已删掉旧包 ' + $f.Name) }
    catch { Write-Host ('  旧包删不掉: ' + $f.Name) }
  }
}
# 签名校验文件(.idsig)也跟着清, 别让旧名字的残留下来
$keepIdsig = (Split-Path $FINAL -Leaf) + '.idsig'
$staleIdsig = @(Get-ChildItem $APKDIR -Filter '*.idsig' -File -ErrorAction SilentlyContinue |
                Where-Object { $_.Name -ne $keepIdsig })
foreach ($f in $staleIdsig) {
  try { Remove-Item $f.FullName -Force; Write-Host ('  已删掉旧校验文件 ' + $f.Name) }
  catch { Write-Host ('  旧校验文件删不掉: ' + $f.Name) }
}

Write-Host '=== 9) verify ==='
& ($BT + '\apksigner.bat') verify --verbose $FINAL 2>&1 | Out-String
& ($BT + '\aapt2.exe') dump badging $FINAL 2>&1 | Select-Object -First 5 | Out-String
Write-Host ('APK OK = ' + [math]::Round((Get-Item $FINAL).Length / 1KB, 1) + ' KB   ' + (Split-Path $FINAL -Leaf))
