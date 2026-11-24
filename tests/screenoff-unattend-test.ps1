$ErrorActionPreference = 'Stop'
Add-Type @"
using System;
using System.Runtime.InteropServices;
public class M {
  [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr h, int m, IntPtr w, IntPtr l);
}
"@
$log = 'C:\DSH NEXT\develop\RayScreen\tests\tick2.log'
Remove-Item $log -ErrorAction SilentlyContinue
$ticker = Start-Job -ScriptBlock {
  param($p)
  for ($i = 0; $i -lt 330; $i++) {
    Add-Content -Path $p -Value ("$i " + (Get-Date -Format 'HH:mm:ss.fff'))
    Start-Sleep 1
  }
} -ArgumentList $log
Start-Sleep 3
"off_at=" + (Get-Date -Format 'HH:mm:ss.fff')
[M]::SendMessage([IntPtr]0xFFFF, 0x0112, [IntPtr]0xF170, [IntPtr]2) | Out-Null
Start-Sleep 240
"on_at=" + (Get-Date -Format 'HH:mm:ss.fff')
[M]::SendMessage([IntPtr]0xFFFF, 0x0112, [IntPtr]0xF170, [IntPtr](-1)) | Out-Null
Start-Sleep 2
Receive-Job $ticker -Wait -ErrorAction SilentlyContinue | Out-Null
$lines = Get-Content $log
"lines=" + $lines.Count
$lines | Select-Object -First 2
$lines | Select-Object -Last 2
