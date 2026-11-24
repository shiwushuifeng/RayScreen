$ErrorActionPreference = 'Continue'
$start = Get-Date
"t0 = " + $start.ToString('HH:mm:ss')

Add-Type -AssemblyName System.Windows.Forms
$before = [System.Windows.Forms.Screen]::AllScreens
"切换前屏幕数: " + $before.Count
foreach ($s in $before) { "  " + $s.DeviceName + "  " + $s.Bounds }

C:\Windows\System32\DisplaySwitch.exe /external
Start-Sleep -Seconds 9

Add-Type -AssemblyName System.Windows.Forms
$after = [System.Windows.Forms.Screen]::AllScreens
"切换后屏幕数: " + $after.Count
foreach ($s in $after) { "  " + $s.DeviceName + "  " + $s.Bounds }

$ev = Get-WinEvent -FilterHashtable @{LogName='System'; ProviderName='Microsoft-Windows-Kernel-Power'; StartTime=$start} -ErrorAction SilentlyContinue
$cs = @($ev | Where-Object { $_.Id -eq 506 })
"这段时间内进入连接待机次数: " + $cs.Count

C:\Windows\System32\DisplaySwitch.exe /internal
Start-Sleep -Seconds 3
"已切回内置屏幕: " + [System.Windows.Forms.Screen]::AllScreens.Count
