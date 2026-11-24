# RayScreen 一键安装脚本（由 install.cmd 以管理员身份调用）
[CmdletBinding()]
param(
    [string]$Dest = (Join-Path $env:ProgramFiles 'RayScreen'),
    [switch]$Startup,
    [switch]$NoShortcut
)

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path

function Step($t) { Write-Host ''; Write-Host "== $t" -ForegroundColor Cyan }
function Ok($t)   { Write-Host "   [OK] $t" -ForegroundColor Green }
function Warn($t) { Write-Host "   [!]  $t" -ForegroundColor Yellow }

# ---------- 0. 权限检查 ----------
$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host '需要管理员权限：请右键以管理员身份运行 install.cmd' -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host '  RayScreen 安装程序' -ForegroundColor White
Write-Host '  跑任务时只关屏幕、不休眠' -ForegroundColor DarkGray

# ---------- 1. 复制程序 ----------
Step '复制程序文件'
if (-not (Test-Path $Dest)) { New-Item -ItemType Directory -Path $Dest -Force | Out-Null }
foreach ($item in @('bin', 'src', 'vdd', 'README.md', 'LICENSE', 'screenshot.png', 'ray.cmd', 'ray.ps1', 'install.cmd', 'install.ps1', 'uninstall.cmd', 'uninstall.ps1')) {
    $src = Join-Path $here $item
    if (Test-Path $src) {
        Copy-Item $src -Destination $Dest -Recurse -Force
        Ok "$item -> $Dest"
    }
}

# ---------- 2. 安装虚拟显示驱动 ----------
Step '安装虚拟显示器驱动（Virtual Display Driver，MIT）'

$devId = 'ROOT\MTTVDD\0000'
$existing = Get-PnpDevice -InstanceId $devId -ErrorAction SilentlyContinue

if ($existing -and $existing.Status -eq 'OK') {
    Ok '虚拟显示器已安装，跳过'
    if (-not (Test-Path 'C:\ProgramData\RayScreen\vdd-package.txt')) {
        $dump = (& pnputil.exe /enum-drivers 2>&1) -join "`n"
        $m = [regex]::Match($dump, 'oem\d+\.inf(?s).{0,400}?MttVDD')
        if ($m.Success) {
            New-Item -ItemType Directory -Path 'C:\ProgramData\RayScreen' -Force | Out-Null
            Set-Content -Path 'C:\ProgramData\RayScreen\vdd-package.txt' -Value $m.Value.Substring(0, 12) -Encoding ASCII
            Ok '已补齐驱动包记录'
        }
    }
} else {
    $inf = Join-Path $here 'vdd\VirtualDisplayDriver\MttVDD.inf'
    if (-not (Test-Path $inf)) { throw "找不到驱动文件：$inf" }

    $out = & pnputil.exe /add-driver "$inf" /install 2>&1
    if ($LASTEXITCODE -ne 0) { throw "pnputil 失败：`n$out" }
    Ok '驱动包已加入驱动库'

    # 记下驱动包发布名（形如 oem42.inf），卸载时只删这一条
    $pub = [regex]::Match(($out -join "`n"), 'oem\d+\.inf').Value
    if ($pub) {
        New-Item -ItemType Directory -Path 'C:\ProgramData\RayScreen' -Force | Out-Null
        Set-Content -Path 'C:\ProgramData\RayScreen\vdd-package.txt' -Value $pub -Encoding ASCII
        Ok "驱动包发布名 $pub 已记录"
    }

    $cs = @'
using System;
using System.Runtime.InteropServices;
using System.Text;

public static class VddSetup
{
    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVINFO_DATA
    {
        public uint cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiGetINFClassW(string infName, ref Guid classGuid, StringBuilder className, uint classNameSize, out uint requiredSize);

    [DllImport("setupapi.dll", SetLastError = true)]
    static extern IntPtr SetupDiCreateDeviceInfoList(ref Guid classGuid, IntPtr hwndParent);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiCreateDeviceInfoW(IntPtr set, string deviceName, ref Guid classGuid, string description, IntPtr hwndParent, uint creationFlags, ref SP_DEVINFO_DATA data);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiSetDeviceRegistryPropertyW(IntPtr set, ref SP_DEVINFO_DATA data, uint property, byte[] buffer, uint size);

    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiCallClassInstaller(uint function, IntPtr set, ref SP_DEVINFO_DATA data);

    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("newdev.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool UpdateDriverForPlugAndPlayDevicesW(IntPtr hwnd, string hardwareId, string infPath, uint flags, out bool reboot);

    const uint DIF_REGISTERDEVICE = 0x19;
    const uint SPDRP_HARDWAREID = 0x1;
    const uint DICD_GENERATE_ID = 0x1;
    const uint INSTALLFLAG_FORCE = 0x1;

    public static int Install(string inf, string hwid, out string message)
    {
        message = null;
        Guid classGuid = Guid.Empty;
        StringBuilder className = new StringBuilder(64);
        uint required;
        if (!SetupDiGetINFClassW(inf, ref classGuid, className, 64, out required))
        { message = "SetupDiGetINFClass " + Marshal.GetLastWin32Error(); return 3; }

        IntPtr set = SetupDiCreateDeviceInfoList(ref classGuid, IntPtr.Zero);
        if (set == IntPtr.Zero || set == new IntPtr(-1))
        { message = "SetupDiCreateDeviceInfoList " + Marshal.GetLastWin32Error(); return 4; }

        SP_DEVINFO_DATA did = new SP_DEVINFO_DATA();
        did.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA));

        if (!SetupDiCreateDeviceInfoW(set, "MttVDD", ref classGuid, "Virtual Display Driver", IntPtr.Zero, DICD_GENERATE_ID, ref did))
        { message = "SetupDiCreateDeviceInfo " + Marshal.GetLastWin32Error(); SetupDiDestroyDeviceInfoList(set); return 5; }

        byte[] hwidBytes = Encoding.Unicode.GetBytes(hwid + "\0\0");
        if (!SetupDiSetDeviceRegistryPropertyW(set, ref did, SPDRP_HARDWAREID, hwidBytes, (uint)hwidBytes.Length))
        { message = "SetupDiSetDeviceRegistryProperty " + Marshal.GetLastWin32Error(); SetupDiDestroyDeviceInfoList(set); return 6; }

        if (!SetupDiCallClassInstaller(DIF_REGISTERDEVICE, set, ref did))
        { message = "DIF_REGISTERDEVICE " + Marshal.GetLastWin32Error(); SetupDiDestroyDeviceInfoList(set); return 7; }

        SetupDiDestroyDeviceInfoList(set);

        bool reboot;
        if (!UpdateDriverForPlugAndPlayDevicesW(IntPtr.Zero, hwid, inf, INSTALLFLAG_FORCE, out reboot))
        { message = "UpdateDriverForPlugAndPlayDevices " + Marshal.GetLastWin32Error(); return 8; }

        message = "reboot required = " + reboot;
        return 0;
    }
}
'@
    if (-not ('VddSetup' -as [type])) { Add-Type -TypeDefinition $cs -Language CSharp | Out-Null }

    $msg = ''
    $rc = [VddSetup]::Install($inf, 'Root\MttVDD', [ref]$msg)
    if ($rc -ne 0) { throw "创建设备失败：$msg" }
    Ok "虚拟显示器设备已创建（$msg）"
}

# 驱动从 C:\VirtualDisplayDriver\ 读配置
$cfgDir = 'C:\VirtualDisplayDriver'
if (-not (Test-Path $cfgDir)) { New-Item -ItemType Directory -Path $cfgDir -Force | Out-Null }
$cfgSrc = Join-Path $here 'vdd\VirtualDisplayDriver\vdd_settings.xml'
if ((Test-Path $cfgSrc) -and -not (Test-Path (Join-Path $cfgDir 'vdd_settings.xml'))) {
    Copy-Item $cfgSrc -Destination $cfgDir -Force
    Ok "配置文件 -> $cfgDir\vdd_settings.xml"
}

# 让驱动重新加载，确保显示器出现
try {
    $dev = Get-PnpDevice -InstanceId $devId -ErrorAction SilentlyContinue
    if ($dev -and $dev.Status -ne 'OK') {
        Disable-PnpDevice -InstanceId $devId -Confirm:$false -ErrorAction SilentlyContinue
        Enable-PnpDevice  -InstanceId $devId -Confirm:$false -ErrorAction SilentlyContinue
        Ok '已重新加载虚拟显示设备'
    }
} catch { Warn "重新加载设备失败：$($_.Exception.Message)" }

# ---------- 3. 计划任务（管理员身份静默启动，之后不再弹 UAC） ----------
Step '创建计划任务'
$exe = Join-Path $Dest 'bin\RayScreen.exe'
$cli = Join-Path $Dest 'bin\RayScreen-cli.exe'

function Register-RayTask {
    param([string]$TaskName, [string]$ExePath, [string]$ArgsLine, [bool]$AtLogon)
    if ($ArgsLine) {
        $action = New-ScheduledTaskAction -Execute $ExePath -Argument $ArgsLine -WorkingDirectory (Split-Path $ExePath)
    } else {
        $action = New-ScheduledTaskAction -Execute $ExePath -WorkingDirectory (Split-Path $ExePath)
    }
    $principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel Highest
    $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances Parallel -ExecutionTimeLimit ([TimeSpan]::Zero)
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
    if ($AtLogon) {
        $trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
        Register-ScheduledTask -TaskName $TaskName -Action $action -Principal $principal -Settings $settings -Trigger $trigger -Force | Out-Null
    } else {
        Register-ScheduledTask -TaskName $TaskName -Action $action -Principal $principal -Settings $settings -Force | Out-Null
    }
    Ok "计划任务 $TaskName"
}

Register-RayTask -TaskName 'RayScreen' -ExePath $exe -ArgsLine '' -AtLogon ([bool]$Startup)
Register-RayTask -TaskName 'RayScreenCli' -ExePath $cli -ArgsLine '--task-host' -AtLogon $false

# 清理旧版本留下的自启项
$legacyRun = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Remove-ItemProperty -Path $legacyRun -Name 'RayScreen' -ErrorAction SilentlyContinue

# ---------- 4. 快捷方式 / 卸载入口 ----------
if (-not $NoShortcut) {
    Step '创建快捷方式与卸载入口'
    $schtasks = Join-Path $env:SystemRoot 'System32\schtasks.exe'
    $shell = New-Object -ComObject WScript.Shell

    function New-AppShortcut([string]$path, [string]$desc) {
        $lnk = $shell.CreateShortcut($path)
        $lnk.TargetPath = $schtasks
        $lnk.Arguments = '/run /tn "RayScreen"'
        $lnk.WindowStyle = 7
        $lnk.WorkingDirectory = (Join-Path $Dest 'bin')
        $lnk.IconLocation = "$exe,0"
        $lnk.Description = $desc
        $lnk.Save()
        Ok $path
    }

    # 桌面
    $desktop = [Environment]::GetFolderPath('Desktop')
    if (Test-Path $desktop) {
        New-AppShortcut (Join-Path $desktop 'RayScreen 关屏不休眠.lnk') '跑任务时只关屏幕、不休眠'
    }

    # 开始菜单文件夹：主程序 + 卸载
    $startMenu = Join-Path $env:ProgramData 'Microsoft\Windows\Start Menu\Programs'
    $folder = Join-Path $startMenu 'RayScreen'
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    New-AppShortcut (Join-Path $folder 'RayScreen 关屏不休眠.lnk') '跑任务时只关屏幕、不休眠'

    $oldTop = Join-Path $startMenu 'RayScreen 关屏不休眠.lnk'
    if (Test-Path $oldTop) { Remove-Item $oldTop -Force -ErrorAction SilentlyContinue }

    $uninstallCmd = Join-Path $Dest 'uninstall.cmd'
    if (Test-Path $uninstallCmd) {
        $u = $shell.CreateShortcut((Join-Path $folder '卸载 RayScreen.lnk'))
        $u.TargetPath = $uninstallCmd
        $u.WorkingDirectory = $Dest
        $u.IconLocation = (Join-Path $env:SystemRoot 'System32\Shell32.dll') + ',131'
        $u.Description = '卸载 RayScreen（程序、驱动、计划任务全部清理）'
        $u.Save()
        Ok (Join-Path $folder '卸载 RayScreen.lnk')
    }

    # 应用与功能里的卸载入口
    $uninstKey = 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\RayScreen'
    New-Item -Path $uninstKey -Force | Out-Null
    Set-ItemProperty -Path $uninstKey -Name 'DisplayName'     -Value 'RayScreen 关屏不休眠'
    Set-ItemProperty -Path $uninstKey -Name 'DisplayVersion'  -Value '1.0.0'
    Set-ItemProperty -Path $uninstKey -Name 'Publisher'       -Value 'shiwushuifeng'
    Set-ItemProperty -Path $uninstKey -Name 'InstallLocation' -Value $Dest
    Set-ItemProperty -Path $uninstKey -Name 'DisplayIcon'     -Value $exe
    Set-ItemProperty -Path $uninstKey -Name 'UninstallString' -Value ('"' + $uninstallCmd + '"')
    Set-ItemProperty -Path $uninstKey -Name 'NoModify'        -Value 1 -Type DWord
    Set-ItemProperty -Path $uninstKey -Name 'NoRepair'        -Value 1 -Type DWord
    Ok '应用与功能中的卸载项'

    $rayCmd = Join-Path $Dest 'ray.cmd'
    if (Test-Path $rayCmd) { Ok "命令行入口：$rayCmd" }
}

# ---------- 5. 自检 ----------
Step '自检'
$cli = Join-Path $Dest 'bin\RayScreen-cli.exe'
if (Test-Path $cli) {
    & $cli --selftest
    $rc = $LASTEXITCODE
    if ($rc -eq 0) { Ok '自检通过' } else { Warn "自检返回 $rc，请查看上面的输出" }
} else {
    Warn '没找到 RayScreen-cli.exe'
}

Write-Host ''
Write-Host '  安装完成！' -ForegroundColor Green
Write-Host "  程序位置：$Dest\bin\RayScreen.exe"
Write-Host '  用法：双击快捷方式 -> 勾选「开始后立即变黑屏」-> 开始守护'
Write-Host '        按任意键立即点亮内屏；停止或关闭窗口自动恢复'
Write-Host ''
