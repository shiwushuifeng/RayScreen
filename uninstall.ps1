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

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host '需要管理员权限：请右键以管理员身份运行 uninstall.cmd' -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host '  RayScreen 卸载' -ForegroundColor White

Step '关闭正在运行的程序'
Get-Process RayScreen, RayScreen-cli -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
Ok '完成'

Step '删除快捷方式与自启动'
foreach ($t in @([Environment]::GetFolderPath('Desktop'),
                 (Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs'))) {
    $lnk = Join-Path $t 'RayScreen 关屏不休眠.lnk'
    if (Test-Path $lnk) { Remove-Item $lnk -Force; Ok $lnk }
}
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'RayScreen' -ErrorAction SilentlyContinue
foreach ($tn in @('RayScreen', 'RayScreenCli')) {
    Unregister-ScheduledTask -TaskName $tn -Confirm:$false -ErrorAction SilentlyContinue
}
Ok '自启动项与计划任务已清理'

if (-not $KeepDriver) {
    Step '移除虚拟显示驱动'
    $devId = 'ROOT\MTTVDD\0000'
    $dev = Get-PnpDevice -InstanceId $devId -ErrorAction SilentlyContinue
    if ($dev) {
        & pnputil.exe /remove-device "$devId" 2>&1 | Out-Null
        Ok '已移除虚拟显示设备'
    }

    # 只删除安装时记录下来的那一个驱动包，避免误删别的 oem*.inf
    $marker = 'C:\ProgramData\RayScreen\vdd-package.txt'
    if (Test-Path $marker) {
        $pkg = (Get-Content $marker -Raw).Trim()
        if ($pkg -match '^oem\d+\.inf$') {
            & pnputil.exe /delete-driver $pkg /uninstall /force 2>&1 | Out-Null
            Ok "驱动包 $pkg 已删除"
        }
        Remove-Item $marker -Force -ErrorAction SilentlyContinue
    } else {
        Warn '没有找到驱动包记录，跳过（可在设备管理器里手动卸载「Virtual Display Driver」）'
    }

    if (Test-Path 'C:\VirtualDisplayDriver') {
        Remove-Item 'C:\VirtualDisplayDriver' -Recurse -Force -ErrorAction SilentlyContinue
        Ok 'C:\VirtualDisplayDriver 已删除'
    }
}

Step '删除程序文件'
if (Test-Path $Dest) { Remove-Item $Dest -Recurse -Force -ErrorAction SilentlyContinue; Ok $Dest }

Write-Host ''
Write-Host '  卸载完成。' -ForegroundColor Green
Write-Host ''
