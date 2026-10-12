param([switch]$KeepDisplayMode)
$ErrorActionPreference = 'Stop'
$appRoot = Join-Path $env:LOCALAPPDATA 'ExternalDisplayGuard'
$appExe = Join-Path $appRoot 'ExternalDisplayGuard.exe'
if (Test-Path -LiteralPath $appExe) {
    $existing = @(Get-Process -Name ExternalDisplayGuard -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $appExe })
    Start-Process -FilePath $appExe -ArgumentList '--stop' -WindowStyle Hidden -Wait
    foreach ($process in $existing) { if (-not $process.WaitForExit(10000)) { throw 'The guard did not exit. Close it and retry.' } }
}
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name ExternalDisplayGuard -ErrorAction SilentlyContinue
$shortcutRoot = Join-Path ([Environment]::GetFolderPath('Programs')) 'ExternalDisplayGuard'
# Only remove known files, and keep configuration/logs for diagnosis. No recursive directory deletion.
foreach ($name in @('外接屏守护.lnk','仅外接屏.lnk','双屏扩展.lnk','停用守护并恢复笔记本屏.lnk','屏幕守护.lnk','仅所选主屏.lnk','全部屏幕扩展.lnk','停用屏幕守护.lnk')) { $file = Join-Path $shortcutRoot $name; if (Test-Path -LiteralPath $file) { Remove-Item -LiteralPath $file } }
if (Test-Path -LiteralPath $appExe) { Remove-Item -LiteralPath $appExe }
Write-Output "Uninstalled. Configuration and logs retained at $appRoot"
