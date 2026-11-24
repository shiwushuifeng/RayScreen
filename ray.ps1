# RayScreen 命令行包装：通过计划任务以管理员身份运行，不弹 UAC

$dir = Join-Path $env:LOCALAPPDATA 'RayScreen'
New-Item -ItemType Directory -Path $dir -Force | Out-Null
$argFile  = Join-Path $dir 'args.txt'
$outFile  = Join-Path $dir 'out.txt'
$doneFile = Join-Path $dir 'done.txt'

$quoted = foreach ($a in $args) {
    if ($a -match '[\s"]') { '"' + ($a -replace '"', '\"') + '"' } else { $a }
}
[IO.File]::WriteAllText($argFile, ($quoted -join ' '), (New-Object System.Text.UTF8Encoding($false)))

Remove-Item $doneFile -ErrorAction SilentlyContinue
Remove-Item $outFile  -ErrorAction SilentlyContinue

& schtasks.exe /run /tn 'RayScreenCli' | Out-Null

function Read-Shared([string]$path) {
    try {
        $fs = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
        $sr = New-Object IO.StreamReader($fs, [Text.Encoding]::UTF8)
        $t = $sr.ReadToEnd()
        $sr.Dispose(); $fs.Dispose()
        return $t
    } catch { return '' }
}

$pos = 0
while (-not (Test-Path $doneFile)) {
    Start-Sleep -Milliseconds 150
    if (Test-Path $outFile) {
        $text = Read-Shared $outFile
        if ($text.Length -gt $pos) {
            Write-Host $text.Substring($pos) -NoNewline
            $pos = $text.Length
        }
    }
}
if (Test-Path $outFile) {
    $text = Read-Shared $outFile
    if ($text.Length -gt $pos) { Write-Host $text.Substring($pos) -NoNewline }
}

$code = 0
[void][int]::TryParse(((Get-Content $doneFile -Raw) -replace '\s', ''), [ref]$code)
exit $code
