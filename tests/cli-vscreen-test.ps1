$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms

$dir = 'C:\DSH NEXT\develop\RayScreen'
$log = Join-Path $dir 'tests\cli-run.log'
if (Test-Path $log) { Remove-Item $log -Force }

$inner = 'cmd.exe /c ""' + (Join-Path $dir 'bin\RayScreen-cli.exe') + '" --screen-off -- cmd /c "ping -n 6 127.0.0.1 >nul" > "' + $log + '" 2>&1"'
Start-Process -Verb RunAs -FilePath 'cmd.exe' -ArgumentList '/c', $inner

for ($i = 0; $i -lt 14; $i++) {
    Start-Sleep -Milliseconds 1000
    $s = ([System.Windows.Forms.Screen]::AllScreens | ForEach-Object { $_.DeviceName + ' ' + $_.Bounds.Width + 'x' + $_.Bounds.Height }) -join ' | '
    "{0,2}s  {1}" -f $i, $s
}

Start-Sleep -Milliseconds 1500
'--- log ---'
if (Test-Path $log) { Get-Content $log -Encoding UTF8 } else { 'no log' }
'--- screens after ---'
[System.Windows.Forms.Screen]::AllScreens | ForEach-Object { $_.DeviceName + ' ' + $_.Bounds.ToString() + ' primary=' + $_.Primary }
