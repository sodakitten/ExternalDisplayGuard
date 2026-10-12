$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Windows .NET Framework 4.x compiler not found.' }
$output = Join-Path $projectRoot 'dist\ExternalDisplayGuard.exe'
New-Item -ItemType Directory -Path (Split-Path $output) -Force | Out-Null
& (Join-Path $projectRoot 'tools\build-icons.ps1')
$sources = @(Get-ChildItem -LiteralPath (Join-Path $projectRoot 'src') -Filter '*.cs' | ForEach-Object FullName)
$sources += Join-Path $projectRoot 'tests\OfflineChecks.cs'
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /warn:4 /warnaserror+ "/out:$output" "/win32manifest:$projectRoot\src\app.manifest" "/win32icon:$projectRoot\assets\app-icon.ico" "/resource:$projectRoot\assets\app-icon.ico,ExternalDisplayGuard.App.ico" /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll /reference:System.Windows.Forms.dll /reference:System.Web.Extensions.dll /reference:System.Management.dll $sources
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Copy-Item -LiteralPath (Join-Path $projectRoot 'install.ps1'),(Join-Path $projectRoot 'uninstall.ps1'),(Join-Path $projectRoot 'update.ps1'),(Join-Path $projectRoot 'README.md'),(Join-Path $projectRoot 'VALIDATION.md') -Destination (Join-Path $projectRoot 'dist') -Force
Get-Item -LiteralPath $output | Select-Object FullName,Length,LastWriteTime
