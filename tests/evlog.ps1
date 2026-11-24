Get-WinEvent -FilterHashtable @{LogName='System'; ProviderName='Microsoft-Windows-Kernel-Power'; ID=506,507} -MaxEvents 6 -ErrorAction SilentlyContinue |
  ForEach-Object {
    $first = ($_.Message -split "`r?`n")[0]
    "{0:HH:mm:ss} id={1}  {2}" -f $_.TimeCreated, $_.Id, $first
  }
"--- now: " + (Get-Date -Format 'HH:mm:ss')
