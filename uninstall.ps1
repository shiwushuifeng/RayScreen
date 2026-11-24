# RayScreen 卸载脚本（由 uninstall.cmd 以管理员身份调用）
[CmdletBinding()]
param(
    [string]$Dest = (Join-Path $env:ProgramFiles 'RayScreen'),
    [switch]$KeepDriver
)

$ErrorActionPreference = 'Continue'

function Step($t) { Write-Host ''; Write-Host "== $t" -ForegroundColor Cyan }
function Ok($t)   { Write-Host "   [OK] $t" -ForegroundColor Green }
function Warn($t) { Write-Host "   [!]  $t" -ForegroundColor Yellow }
function Skip($t) { Write-Host "   [-]  $t" -ForegroundColor DarkGray }

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host '需要管理员权限：请右键以管理员身份运行 uninstall.cmd' -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host '  RayScreen 卸载' -ForegroundColor White

# ---------- 1. 结束进程 ----------
Step '结束正在运行的程序'
& schtasks.exe /end /tn 'RayScreen'    2>&1 | Out-Null
& schtasks.exe /end /tn 'RayScreenCli' 2>&1 | Out-Null
foreach ($name in @('RayScreen', 'RayScreen-cli')) {
    $p = Get-Process $name -ErrorAction SilentlyContinue
    if ($p) { $p | Stop-Process -Force -ErrorAction SilentlyContinue; Ok "已结束 $name" }
}
Ok '完成'

# ---------- 2. 快捷方式 / 开始菜单 / 卸载入口 ----------
Step '删除快捷方式与卸载入口'
$startMenu = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs'
$lnkNames = @('RayScreen 关屏不休眠.lnk', '卸载 RayScreen.lnk')
$lnkDirs = @([Environment]::GetFolderPath('Desktop'), $startMenu, (Join-Path $startMenu 'RayScreen'))
foreach ($d in $lnkDirs) {
    foreach ($n in $lnkNames) {
        $p = Join-Path $d $n
        if (Test-Path $p) { Remove-Item $p -Force -ErrorAction SilentlyContinue; Ok $p }
    }
}
$startFolder = Join-Path $startMenu 'RayScreen'
if ((Test-Path $startFolder) -and -not (Get-ChildItem $startFolder -Force -ErrorAction SilentlyContinue)) {
    Remove-Item $startFolder -Force -ErrorAction SilentlyContinue
    Ok $startFolder
}

Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'RayScreen' -ErrorAction SilentlyContinue
$uninstKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\RayScreen'
if (Test-Path $uninstKey) { Remove-Item $uninstKey -Recurse -Force -ErrorAction SilentlyContinue; Ok '应用列表中的卸载入口' }

foreach ($tn in @('RayScreen', 'RayScreenCli')) {
    $task = Get-ScheduledTask -TaskName $tn -ErrorAction SilentlyContinue
    if ($task) { Unregister-ScheduledTask -TaskName $tn -Confirm:$false -ErrorAction SilentlyContinue; Ok "计划任务 $tn" }
}
Ok '自启动项与计划任务已清理'

# ---------- 3. 虚拟显示驱动 ----------
if (-not $KeepDriver) {
    Step '移除虚拟显示驱动'

    # 3.1 设备与它带出来的显示器
    $devIds = @()
    $main = Get-PnpDevice -InstanceId 'ROOT\MTTVDD\0000' -ErrorAction SilentlyContinue
    if ($main) { $devIds += 'ROOT\MTTVDD\0000' }
    $extra = Get-PnpDevice -ErrorAction SilentlyContinue | Where-Object {
        $_.InstanceId -like 'ROOT\MTTVDD\*' -or
        $_.InstanceId -like 'DISPLAY\MTT1337*' -or
        $_.FriendlyName -eq 'Virtual Display Driver' -or
        $_.FriendlyName -like 'Generic Monitor (VDD by MTT)*'
    }
    foreach ($d in $extra) { if ($devIds -notcontains $d.InstanceId) { $devIds += $d.InstanceId } }
    foreach ($id in $devIds) {
        & pnputil.exe /remove-device "$id" 2>&1 | Out-Null
        Ok "移除设备 $id"
    }
    if ($devIds.Count -eq 0) { Skip '没有残留的虚拟显示设备' }

    # 3.2 驱动包：优先用安装时记录的发布名，否则按原始 INF 名扫描
    $pkgs = @()
    $marker = 'C:\ProgramData\RayScreen\vdd-package.txt'
    if (Test-Path $marker) {
        $v = (Get-Content $marker -Raw -ErrorAction SilentlyContinue)
        if ($v) {
            $m = [regex]::Match($v, 'oem\d+\.inf')
            if ($m.Success) { $pkgs += $m.Value }
        }
    }
    if ($pkgs.Count -eq 0 -or -not $KeepDriver) {
        $pub = $null
        foreach ($line in (& pnputil.exe /enum-drivers 2>&1)) {
            if ($line -match '(?:已发布名称|Published Name)[:：]\s*(\S+\.inf)') { $pub = $Matches[1] }
            elseif ($line -match '(?:原始名称|Original Name)[:：]\s*(\S+)' -and $Matches[1] -match 'mttvdd') {
                if ($pub -and $pkgs -notcontains $pub) { $pkgs += $pub }
            }
        }
    }
    foreach ($pkg in $pkgs) {
        & pnputil.exe /delete-driver $pkg /uninstall /force 2>&1 | Out-Null
        Ok "删除驱动包 $pkg"
    }
    if ($pkgs.Count -eq 0) { Skip '驱动库里没有找到 mttvdd 驱动包' }

    foreach ($dir in @('C:\VirtualDisplayDriver', 'C:\ProgramData\RayScreen')) {
        if (Test-Path $dir) {
            Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
            Ok "删除 $dir"
        }
    }
} else {
    Skip '按 -KeepDriver 保留虚拟显示驱动'
}

# ---------- 4. 程序文件与用户数据 ----------
Step '删除程序文件与用户数据'
foreach ($dir in @($Dest, (Join-Path $env:LOCALAPPDATA 'RayScreen'))) {
    if (Test-Path $dir) {
        Remove-Item $dir -Recurse -Force -ErrorAction SilentlyContinue
        if (Test-Path $dir) { Warn "$dir 删除失败（可能有文件被占用）" } else { Ok $dir }
    }
}

# ---------- 5. 残留检查 ----------
Step '残留检查'
$left = @()
if (Test-Path $Dest) { $left += "目录仍在：$Dest" }
if (Test-Path (Join-Path $env:LOCALAPPDATA 'RayScreen')) { $left += '目录仍在：%LOCALAPPDATA%\RayScreen' }
if (Test-Path 'C:\VirtualDisplayDriver') { $left += '目录仍在：C:\VirtualDisplayDriver' }
if (Get-ScheduledTask -TaskName 'RayScreen' -ErrorAction SilentlyContinue) { $left += '计划任务仍在：RayScreen' }
if (Get-ScheduledTask -TaskName 'RayScreenCli' -ErrorAction SilentlyContinue) { $left += '计划任务仍在：RayScreenCli' }
if (Test-Path $uninstKey) { $left += '卸载注册表项仍在' }
if (-not $KeepDriver) {
    if (Get-PnpDevice -InstanceId 'ROOT\MTTVDD\0000' -ErrorAction SilentlyContinue) { $left += '设备仍在：ROOT\MTTVDD\0000' }
    $vddMon = Get-PnpDevice -Class Monitor -ErrorAction SilentlyContinue | Where-Object { $_.FriendlyName -like '*VDD*' }
    if ($vddMon) { $left += '显示器设备仍在：' + ($vddMon | ForEach-Object { $_.InstanceId }) -join ', ' }
    if ((& pnputil.exe /enum-drivers 2>&1) -match 'mttvdd') { $left += '驱动库仍有 mttvdd 驱动包' }
}
if ($left.Count -eq 0) {
    Ok '没有残留'
} else {
    foreach ($l in $left) { Warn $l }
}

Write-Host ''
if ($left.Count -eq 0) {
    Write-Host '  卸载完成，已清理干净。' -ForegroundColor Green
} else {
    Write-Host '  卸载完成，但上面有残留项需要人工处理。' -ForegroundColor Yellow
}
Write-Host ''
if ($left.Count -eq 0) { exit 0 } else { exit 2 }
