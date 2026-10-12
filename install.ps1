param([switch]$NoStart)
$ErrorActionPreference = 'Stop'
$appRoot = Join-Path $env:LOCALAPPDATA 'ExternalDisplayGuard'
$sourceExe = Join-Path $PSScriptRoot 'ExternalDisplayGuard.exe'
if (-not (Test-Path -LiteralPath $sourceExe)) { $sourceExe = Join-Path $PSScriptRoot 'dist\ExternalDisplayGuard.exe' }
if (-not (Test-Path -LiteralPath $sourceExe)) { throw 'Build ExternalDisplayGuard.exe first.' }
$destinationExe = Join-Path $appRoot 'ExternalDisplayGuard.exe'
# Stop only this product's executable before replacing it; never stop unrelated processes by name.
$existing = @(Get-Process -Name ExternalDisplayGuard -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $destinationExe })
if ($existing.Count -gt 0) {
    Start-Process -FilePath $destinationExe -ArgumentList '--stop' -WindowStyle Hidden -Wait
    foreach ($process in $existing) { if (-not $process.WaitForExit(10000)) { throw 'The guard did not exit. Close it from its tray menu and retry.' } }
}
New-Item -ItemType Directory -Path $appRoot -Force | Out-Null
if ([IO.Path]::GetFullPath($sourceExe) -ne [IO.Path]::GetFullPath($destinationExe)) { Copy-Item -LiteralPath $sourceExe -Destination $destinationExe -Force }
foreach ($name in @('uninstall.ps1','README.md')) { $sourceFile = Join-Path $PSScriptRoot $name; if (Test-Path -LiteralPath $sourceFile) { if ([IO.Path]::GetFullPath($sourceFile) -ne [IO.Path]::GetFullPath((Join-Path $appRoot $name))) { Copy-Item -LiteralPath $sourceFile -Destination $appRoot -Force } } }
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if (Test-Path -LiteralPath $runKey) {
    $backupFile = Join-Path $appRoot ('startup-before-install-' + (Get-Date -Format 'yyyyMMdd-HHmmssfff') + '.reg')
    & reg.exe export 'HKCU\Software\Microsoft\Windows\CurrentVersion\Run' $backupFile /y | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Could not back up login startup settings. Installation stopped.' }
}
if (-not (Test-Path -LiteralPath $runKey)) { New-Item -Path $runKey | Out-Null }
New-ItemProperty -Path $runKey -Name ExternalDisplayGuard -Value ('"' + $destinationExe + '"') -PropertyType String -Force | Out-Null
$shell = New-Object -ComObject WScript.Shell
$shortcutRoot = Join-Path ([Environment]::GetFolderPath('Programs')) 'ExternalDisplayGuard'
New-Item -ItemType Directory -Path $shortcutRoot -Force | Out-Null
foreach ($item in @(@{Name='屏幕守护';Args='--settings'},@{Name='仅所选主屏';Args='--single'},@{Name='全部屏幕扩展';Args='--all'},@{Name='停用屏幕守护';Args='--disable'})) {
    $shortcut = $shell.CreateShortcut((Join-Path $shortcutRoot ($item.Name + '.lnk')))
    $shortcut.TargetPath = $destinationExe
    $shortcut.Arguments = $item.Args
    $shortcut.WorkingDirectory = $appRoot
    $shortcut.IconLocation = $destinationExe + ',0'
    $shortcut.Save()
}
if (-not $NoStart) { Start-Process -FilePath $destinationExe -WindowStyle Hidden }
[pscustomobject]@{Installed=$destinationExe;AutoStart=$true;Shortcuts=$shortcutRoot;Started=(-not $NoStart)} | ConvertTo-Json
