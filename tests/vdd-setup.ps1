$ErrorActionPreference = 'Continue'
$src = 'C:\DSH NEXT\develop\RayScreen\vdd\VirtualDisplayDriver\vdd_settings.xml'
$dstDir = 'C:\VirtualDisplayDriver'
New-Item -ItemType Directory -Force -Path $dstDir | Out-Null
Copy-Item -Path $src -Destination (Join-Path $dstDir 'vdd_settings.xml') -Force
$log = 'C:\DSH NEXT\develop\RayScreen\tests\vdd-restart.log'
"settings copied to $dstDir" | Out-File -FilePath $log -Encoding utf8
Get-ChildItem $dstDir | Select-Object Name, Length | Out-String | Add-Content -Path $log -Encoding utf8

$dev = Get-PnpDevice -Class Display | Where-Object { $_.FriendlyName -eq 'Virtual Display Driver' }
if (-not $dev) { "device not found" | Add-Content -Path $log; exit 1 }
"instance: " + $dev.InstanceId | Add-Content -Path $log -Encoding utf8

Disable-PnpDevice -InstanceId $dev.InstanceId -Confirm:$false -ErrorAction Continue
Start-Sleep -Seconds 2
Enable-PnpDevice -InstanceId $dev.InstanceId -Confirm:$false -ErrorAction Continue
Start-Sleep -Seconds 5

Get-PnpDevice -Class Display | Select-Object Status, FriendlyName | Out-String | Add-Content -Path $log -Encoding utf8
Add-Type -AssemblyName System.Windows.Forms
([System.Windows.Forms.Screen]::AllScreens | ForEach-Object { $_.DeviceName + ' ' + $_.Bounds.ToString() + ' primary=' + $_.Primary }) | Add-Content -Path $log -Encoding utf8
"done" | Add-Content -Path $log -Encoding utf8
