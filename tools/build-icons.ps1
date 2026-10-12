$ErrorActionPreference='Stop'
$projectRoot=Split-Path $PSScriptRoot -Parent
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$builder=Join-Path $projectRoot 'dist\IconBuilder.exe'
New-Item -ItemType Directory -Path (Split-Path $builder) -Force | Out-Null
& $compiler /nologo /target:exe /platform:x64 /optimize+ "/out:$builder" /reference:System.Drawing.dll (Join-Path $PSScriptRoot 'IconBuilder.cs')
if($LASTEXITCODE -ne 0){throw 'Icon compiler failed.'}
& $builder (Join-Path $projectRoot 'assets')
if($LASTEXITCODE -ne 0){throw 'Icon generation failed.'}
