param(
    [Parameter(Position = 0)][ValidateSet('on','off','toggle','status','set','band','repair','info')][string]$Action = 'status',
    [string]$Ssid,
    [string]$Pass,
    [string]$Band
)
$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false) } catch { }

try {
    Add-Type -AssemblyName System.Runtime.WindowsRuntime -ErrorAction Stop
    $null = [Windows.Networking.Connectivity.NetworkInformation, Windows.Networking.Connectivity, ContentType = WindowsRuntime]
    $null = [Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager, Windows.Networking.NetworkOperators, ContentType = WindowsRuntime]
    $null = [Windows.Networking.NetworkOperators.NetworkOperatorTetheringClient, Windows.Networking.NetworkOperators, ContentType = WindowsRuntime]
} catch {
    Write-Host ('无法加载热点接口(可能没有无线网卡或系统版本过低): ' + $_.Exception.Message)
    exit 1
}
# 频段接口在旧版 Windows 上不存在, 单独加载, 失败就降级
$script:HasBand = $true
try { $null = [Windows.Networking.NetworkOperators.TetheringWiFiBand, Windows.Networking.NetworkOperators, ContentType = WindowsRuntime] }
catch { $script:HasBand = $false }

function Parse-Band {
    param([string]$b)
    if (-not $script:HasBand) { return $null }
    if (-not $b) { return [Windows.Networking.NetworkOperators.TetheringWiFiBand]::Auto }
    switch ($b.ToLower()) {
        '2.4'  { return [Windows.Networking.NetworkOperators.TetheringWiFiBand]::TwoPointFourGigahertz }
        '2.4g' { return [Windows.Networking.NetworkOperators.TetheringWiFiBand]::TwoPointFourGigahertz }
        '24'   { return [Windows.Networking.NetworkOperators.TetheringWiFiBand]::TwoPointFourGigahertz }
        '5'    { return [Windows.Networking.NetworkOperators.TetheringWiFiBand]::FiveGigahertz }
        '5g'   { return [Windows.Networking.NetworkOperators.TetheringWiFiBand]::FiveGigahertz }
        '6'    { return [Windows.Networking.NetworkOperators.TetheringWiFiBand]::SixGigahertz }
        '6g'   { return [Windows.Networking.NetworkOperators.TetheringWiFiBand]::SixGigahertz }
        default { return [Windows.Networking.NetworkOperators.TetheringWiFiBand]::Auto }
    }
}

function Band-Text {
    param($b)
    if (-not $script:HasBand) { return '不支持' }
    $s = [string]$b
    if ($s -eq 'TwoPointFourGigahertz') { return '2.4 GHz' }
    if ($s -eq 'FiveGigahertz') { return '5 GHz' }
    if ($s -eq 'SixGigahertz') { return '6 GHz' }
    return '自动'
}

# 拿一个"可以共享出去"的网络连接。
#
# ⚠️ 踩过的坑: 以前直接用 GetInternetConnectionProfile() —— 那个是**默认连接**，
# 而热点开着的时候它可能已经变成热点自己那条虚拟连接, 于是 CreateFromConnectionProfile
# 拿到的是热点自己 → 后续读取/配置就失败。现象是「在设置里改过热点信息之后工具就读不到了」。
# 现在改成: 先挑有互联网的、且**不是**热点虚拟网卡(本地连接* / Wi-Fi Direct)的连接;
# 挑不到再退回默认连接。
function Get-Mgr {
    $profiles = $null
    try { $profiles = [Windows.Networking.Connectivity.NetworkInformation]::GetConnectionProfiles() } catch { }

    $fallback = $null
    foreach ($p in $profiles) {
        if (-not $p) { continue }
        $name = ''
        try { $name = [string]$p.ProfileName } catch { }
        # 热点自己的虚拟网卡通常叫"本地连接* N", 排除掉
        $isHotspotNic = ($name -like '本地连接*') -or ($name -like 'Local Area Connection*')
        try {
            if ($p.IsWlanConnectionProfile -or $p.IsWwanConnectionProfile) { }
        } catch { }
        if ($isHotspotNic) { continue }
        if (-not $fallback) { $fallback = $p }
        try {
            if ($p.GetNetworkConnectivityLevel() -eq [Windows.Networking.Connectivity.NetworkConnectivityLevel]::InternetAccess) {
                return [Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager]::CreateFromConnectionProfile($p)
            }
        } catch { }
    }
    if ($fallback) {
        return [Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager]::CreateFromConnectionProfile($fallback)
    }
    $def = [Windows.Networking.Connectivity.NetworkInformation]::GetInternetConnectionProfile()
    if ($def) {
        return [Windows.Networking.NetworkOperators.NetworkOperatorTetheringManager]::CreateFromConnectionProfile($def)
    }
    throw '当前没有可共享的网络连接, 请先认证上网'
}

# 读热点配置, 失败自动重试。
# 刚才在"设置"里改过热点的, 系统切换状态需要一两秒, 这时候读会失败一次 —— 重试就能过去。
function Get-Retry {
    param([int]$Times = 5, [int]$SleepMs = 800)
    $last = $null
    for ($i = 0; $i -lt $Times; $i++) {
        try {
            $mgr = Get-Mgr
            $ap = $mgr.GetCurrentAccessPointConfiguration()
            if ($null -ne $ap -and $null -ne $ap.Ssid) { return @{ mgr = $mgr; ap = $ap } }
            $last = '取到的配置为空'
        } catch { $last = $_.Exception.Message }
        if ($i -lt $Times - 1) { Start-Sleep -Milliseconds $SleepMs }
    }
    throw ('读取热点配置失败: ' + $last)
}

# WinRT 拿不到已连设备的名字, 用 ARP 表把 IP/MAC 列出来
function Show-Clients {
    param($mgr)
    $n = 0
    try { $n = $mgr.ClientCount } catch { }
    Write-Host ('已连接   : ' + $n + ' 台')
    try {
        $arp = (arp.exe -a) 2>$null | Out-String
        $found = 0
        foreach ($line in ($arp -split [char]10)) {
            if ($line -match '^\s*(\d+\.\d+\.\d+\.\d+)\s+([0-9a-fA-F-]{17})') {
                $ip = $Matches[1]
                $mac = $Matches[2]
                if ($ip -notlike '192.168.137.*') { continue }
                if ($ip -like '*.255') { continue }
                $ml = $mac.ToLower()
                if ($ml -eq 'ff-ff-ff-ff-ff-ff') { continue }
                if ($ml.StartsWith('01-00-5e')) { continue }
                Write-Host ('   - ' + $ip + '   ' + $mac)
                $found++
            }
        }
        if ($found -eq 0 -and $n -gt 0) { Write-Host '   (ARP 表暂时没有记录, 等几秒重试)' }
    } catch { }
}

function Show-Status {
    $r = Get-Retry
    $mgr = $r.mgr
    $ap = $r.ap
    $state = [string]$mgr.TetheringOperationalState
    if ($state -eq 'On') { Write-Host '热点状态 : 已开启' } else { Write-Host '热点状态 : 已关闭' }
    Write-Host ('热点名称 : ' + $ap.Ssid)
    Write-Host ('热点密码 : ' + $ap.Passphrase)
    try { Write-Host ('热点频段 : ' + (Band-Text $ap.Band)) } catch { Write-Host '热点频段 : 不支持' }
    Write-Host ('最大设备 : ' + $mgr.MaxClientCount)
    if ($state -eq 'On') { Show-Clients $mgr }
}

switch ($Action) {
    # 输出成 key=value, 方便程序解析(不依赖系统语言)
    # 读失败时也要给出 error= 一行, 而不是一声不响 —— 否则程序那边只看到空输出，没法告诉用户为什么
    'info' {
        try {
            $r = Get-Retry
            $mgr = $r.mgr
            $ap = $r.ap
            Write-Host ('ssid=' + $ap.Ssid)
            Write-Host ('pass=' + $ap.Passphrase)
            try { Write-Host ('band=' + $ap.Band) } catch { Write-Host 'band=' }
            Write-Host ('state=' + $mgr.TetheringOperationalState)
            Write-Host ('clients=' + $mgr.ClientCount)
            Write-Host ('max=' + $mgr.MaxClientCount)
        } catch {
            Write-Host ('error=' + $_.Exception.Message)
            exit 2
        }
    }

    'status' {
        try { Show-Status } catch { Write-Host ('读不到热点信息: ' + $_.Exception.Message); exit 2 }
    }

    'on' {
        try {
            $r = Get-Retry
            $mgr = $r.mgr
            if ([string]$mgr.TetheringOperationalState -eq 'On') {
                Write-Host '热点已经是开启状态'
            } else {
                $null = $mgr.StartTetheringAsync()
                Start-Sleep -Seconds 3
                Write-Host '已开启热点'
            }
            Show-Status
        } catch { Write-Host ('开启热点失败: ' + $_.Exception.Message); exit 2 }
    }

    'off' {
        try {
            $r = Get-Retry
            $mgr = $r.mgr
            if ([string]$mgr.TetheringOperationalState -eq 'Off') {
                Write-Host '热点已经是关闭状态'
            } else {
                $null = $mgr.StopTetheringAsync()
                Start-Sleep -Seconds 3
                Write-Host '已关闭热点'
            }
        } catch { Write-Host ('关闭热点失败: ' + $_.Exception.Message); exit 2 }
    }

    'toggle' {
        try {
            $r = Get-Retry
            $mgr = $r.mgr
            if ([string]$mgr.TetheringOperationalState -eq 'On') {
                $null = $mgr.StopTetheringAsync()
                Start-Sleep -Seconds 3
                Write-Host '热点已关闭'
            } else {
                $null = $mgr.StartTetheringAsync()
                Start-Sleep -Seconds 3
                Write-Host '热点已开启'
                Show-Status
            }
        } catch { Write-Host ('切换热点失败: ' + $_.Exception.Message); exit 2 }
    }

    'set' {
        $r = Get-Retry
        $mgr = $r.mgr
        $ap = $r.ap
        $changed = $false
        if ($Ssid) { $ap.Ssid = $Ssid; $changed = $true }
        if ($Pass) {
            if ($Pass.Length -lt 8) { Write-Host 'WiFi 密码至少 8 位'; exit 1 }
            $ap.Passphrase = $Pass; $changed = $true
        }
        if ($Band) { $ap.Band = Parse-Band $Band; $changed = $true }
        if (-not $changed) { Write-Host '没有指定要修改的内容'; exit 1 }
        Write-Host ('正在应用: SSID=' + $ap.Ssid + '  频段=' + (Band-Text $ap.Band))
        $null = $mgr.ConfigureAccessPointAsync($ap)
        Start-Sleep -Seconds 3
        Show-Status
    }

    'band' {
        if (-not $Band) { Write-Host '用法: hotspot band -Band 2.4|5|auto'; exit 1 }
        $r = Get-Retry
        $mgr = $r.mgr
        $ap = $r.ap
        $ap.Band = Parse-Band $Band
        Write-Host ('正在切换到频段: ' + (Band-Text $ap.Band))
        $null = $mgr.ConfigureAccessPointAsync($ap)
        Start-Sleep -Seconds 3
        Show-Status
    }

    'repair' {
        Write-Host '=== 修复移动热点 ==='
        $r = Get-Retry
        $mgr = $r.mgr
        if ([string]$mgr.TetheringOperationalState -eq 'On') {
            $null = $mgr.StopTetheringAsync()
            Start-Sleep -Seconds 3
            Write-Host '已关闭热点'
        }
        if ($Band) {
            $r2 = Get-Retry
            $ap = $r2.ap
            $ap.Band = Parse-Band $Band
            $null = $r2.mgr.ConfigureAccessPointAsync($ap)
            Start-Sleep -Seconds 2
            Write-Host ('频段已设为: ' + (Band-Text $ap.Band))
        }
        try {
            Restart-Service SharedAccess -Force -ErrorAction Stop
            Write-Host '已重启 Internet 连接共享服务'
        } catch {
            Write-Host '跳过重启共享服务(需要管理员权限, 不影响使用)'
        }
        Start-Sleep -Seconds 2
        $r3 = Get-Retry
        $null = $r3.mgr.StartTetheringAsync()
        Start-Sleep -Seconds 4
        Write-Host '已重新开启热点'
        Show-Status
    }
}
