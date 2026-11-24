$ErrorActionPreference = 'Stop'
$log = 'C:\DSH NEXT\develop\RayScreen\tests\tick3.log'
Remove-Item $log -ErrorAction SilentlyContinue

powercfg /setacvalueindex SCHEME_CURRENT SUB_VIDEO VIDEOIDLE 60 | Out-Null
powercfg /setactive SCHEME_CURRENT | Out-Null
'VIDEOIDLE set to 60s (AC)'

$job = Start-Job -ScriptBlock {
    param($log)
    $i = 0
    while ($i -le 400) {
        Add-Content -Path $log -Value ("{0} {1}" -f $i, (Get-Date).ToString('HH:mm:ss.fff'))
        $i++
        Start-Sleep -Milliseconds 1000
    }
} -ArgumentList $log

Start-Sleep -Seconds 200

powercfg /setacvalueindex SCHEME_CURRENT SUB_VIDEO VIDEOIDLE 0 | Out-Null
powercfg /setactive SCHEME_CURRENT | Out-Null
'VIDEOIDLE restored to 0 (never)'

Stop-Job $job -ErrorAction SilentlyContinue
Remove-Job $job -Force -ErrorAction SilentlyContinue

$lines = @(Get-Content $log)
'ticks: ' + $lines.Count
if ($lines.Count -gt 0) {
    'first: ' + $lines[0]
    'last : ' + $lines[$lines.Count - 1]
}
$prev = $null
$gaps = 0
foreach ($l in $lines) {
    $parts = $l -split ' '
    $t = [datetime]::ParseExact($parts[1], 'HH:mm:ss.fff', $null)
    if ($prev -ne $null) {
        $d = ($t - $prev).TotalSeconds
        if ($d -gt 3) { 'GAP {0:N1}s at {1}' -f $d, $parts[1]; $gaps++ }
    }
    $prev = $t
}
'gaps: ' + $gaps
'--- recent 506/507 ---'
Get-WinEvent -FilterHashtable @{LogName='System'; StartTime=(Get-Date).AddMinutes(-20)} -ErrorAction SilentlyContinue |
    Where-Object { $_.Id -eq 506 -or $_.Id -eq 507 } |
    Select-Object -First 6 TimeCreated, Id | Format-Table -AutoSize | Out-String | Write-Output
