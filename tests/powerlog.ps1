$ErrorActionPreference = 'SilentlyContinue'
$ev = Get-WinEvent -FilterHashtable @{LogName = 'System'; ProviderName = 'Microsoft-Windows-Kernel-Power'} -MaxEvents 30
foreach ($e in $ev) {
    $m = $e.Message
    if ($m.Length -gt 150) { $m = $m.Substring(0, 150) }
    $m = $m -replace "`r`n", ' '
    '{0}  id={1}  {2}' -f $e.TimeCreated.ToString('MM-dd HH:mm:ss'), $e.Id, $m
}
