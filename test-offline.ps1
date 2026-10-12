$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$output=Join-Path $root 'dist\OfflineRunner.exe'
New-Item -ItemType Directory -Path (Split-Path $output) -Force | Out-Null
$sources=@('src\NativeDisplay.cs','src\HardwareIdentity.cs','src\Profiles.cs','src\DisplayEngine.cs','tests\OfflineChecks.cs','tests\OfflineRunner.cs') | ForEach-Object {Join-Path $root $_}
& $compiler /nologo /target:exe /platform:x64 /optimize+ /warn:4 /warnaserror+ "/out:$output" /reference:System.dll /reference:System.Core.dll /reference:System.Web.Extensions.dll /reference:System.Management.dll $sources
if($LASTEXITCODE -ne 0){throw 'Offline runner compilation failed.'}
$report=Join-Path $root 'evidence\offline-results.json'
& $output $report
if($LASTEXITCODE -ne 0){throw ('Offline checks failed: '+$report)}
$result=Get-Content -LiteralPath $report -Raw | ConvertFrom-Json
[pscustomobject]@{Result=$result.Result;Count=$result.Count;Backend=$result.Backend;NativeDisplayCalls=$result.NativeDisplayCalls;Report=$report} | ConvertTo-Json
