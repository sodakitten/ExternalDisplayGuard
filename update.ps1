$ErrorActionPreference = 'Stop'
$appRoot = Join-Path $env:LOCALAPPDATA 'ExternalDisplayGuard'
$appExe = Join-Path $appRoot 'ExternalDisplayGuard.exe'
$newExe = Join-Path $PSScriptRoot 'dist\ExternalDisplayGuard.exe'
if (-not (Test-Path -LiteralPath $newExe)) { $newExe = Join-Path $PSScriptRoot 'ExternalDisplayGuard.exe' }
if (-not (Test-Path -LiteralPath $newExe)) { throw 'The new executable was not found.' }
if (-not (Test-Path -LiteralPath $appExe)) { throw 'Use install.ps1 for the first installation.' }
$stamp = Get-Date -Format 'yyyyMMdd-HHmmssfff'
$backupRoot = Join-Path $appRoot ('backups\' + $stamp)
New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
Copy-Item -LiteralPath $appExe -Destination $backupRoot
$configPath = Join-Path $appRoot 'config.json'
if (Test-Path -LiteralPath $configPath) { Copy-Item -LiteralPath $configPath -Destination $backupRoot }
$runKey = Get-Item -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$startupBefore = @{}
foreach ($name in $runKey.GetValueNames()) { $startupBefore[$name] = $runKey.GetValue($name) }
$existing = @(Get-Process -Name ExternalDisplayGuard -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $appExe })
Start-Process -FilePath $appExe -ArgumentList '--stop' -WindowStyle Hidden -Wait
foreach ($process in $existing) { if (-not $process.WaitForExit(10000)) { throw 'The guard did not exit. Retry after closing it from the tray.' } }
try {
    Copy-Item -LiteralPath $newExe -Destination $appExe -Force
    [Reflection.Assembly]::LoadFrom($appExe) | Out-Null
    $config = [ExternalDisplayGuard.Storage]::Load()
    [ExternalDisplayGuard.Storage]::Save($config)
    foreach ($supportName in @('README.md','uninstall.ps1')) {
        $supportPath = Join-Path $PSScriptRoot $supportName
        if ([IO.Path]::GetFullPath($supportPath) -ne [IO.Path]::GetFullPath((Join-Path $appRoot $supportName))) {
            Copy-Item -LiteralPath $supportPath -Destination $appRoot -Force
        }
    }
} catch {
    Copy-Item -LiteralPath (Join-Path $backupRoot 'ExternalDisplayGuard.exe') -Destination $appExe -Force
    if (Test-Path -LiteralPath (Join-Path $backupRoot 'config.json')) { Copy-Item -LiteralPath (Join-Path $backupRoot 'config.json') -Destination $configPath -Force }
    Start-Process -FilePath $appExe -WindowStyle Hidden
    throw
}
Start-Process -FilePath $appExe -WindowStyle Hidden
$shortcutRoot = Join-Path ([Environment]::GetFolderPath('Programs')) 'ExternalDisplayGuard'
$shell = New-Object -ComObject WScript.Shell
foreach ($name in @('屏幕守护','仅所选主屏','全部屏幕扩展','停用屏幕守护')) {
    $path = Join-Path $shortcutRoot ($name + '.lnk')
    if (Test-Path -LiteralPath $path) { $shortcut = $shell.CreateShortcut($path); $shortcut.IconLocation = $appExe + ',0'; $shortcut.Save() }
}
$shortcuts=@(@{Name='屏幕守护';Args='--settings'},@{Name='仅所选主屏';Args='--single'},@{Name='全部屏幕扩展';Args='--all'},@{Name='停用屏幕守护';Args='--disable'})
New-Item -ItemType Directory -Path $shortcutRoot -Force | Out-Null
foreach($item in $shortcuts){$path=Join-Path $shortcutRoot ($item.Name+'.lnk');$shortcut=$shell.CreateShortcut($path);$shortcut.TargetPath=$appExe;$shortcut.Arguments=$item.Args;$shortcut.WorkingDirectory=$appRoot;$shortcut.IconLocation=$appExe+',0';$shortcut.Save()}
foreach($oldName in @('外接屏守护','仅外接屏','双屏扩展','停用守护并恢复笔记本屏')){$oldPath=Join-Path $shortcutRoot ($oldName+'.lnk');if(Test-Path -LiteralPath $oldPath){Remove-Item -LiteralPath $oldPath}}
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class DisplayGuardShellRefresh {
 [DllImport("shell32.dll", CharSet=CharSet.Unicode)] public static extern void SHChangeNotify(uint change,int flags,string item,IntPtr unused);
}
'@
[DisplayGuardShellRefresh]::SHChangeNotify(0x2000,5,$appExe,[IntPtr]::Zero)
$runKey = Get-Item -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$changed = @($startupBefore.Keys | Where-Object { $runKey.GetValue($_) -ne $startupBefore[$_] })
if ($changed.Count -gt 0 -or $runKey.GetValueNames().Count -ne $startupBefore.Count) { throw 'Startup settings unexpectedly changed.' }
[pscustomobject]@{Updated=$appExe;Backup=$backupRoot;StartupValuesPreserved=$startupBefore.Count;SchemaVersion=$config.SchemaVersion;Profile=$config.Current.Name} | ConvertTo-Json
