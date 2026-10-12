$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot 'evidence') -Force | Out-Null
$appExe = Join-Path $env:LOCALAPPDATA 'ExternalDisplayGuard\ExternalDisplayGuard.exe'
[Reflection.Assembly]::LoadFrom($appExe) | Out-Null
$report = [ordered]@{}
function Send-GuardCommand([string]$command) { Start-Process -FilePath $appExe -ArgumentList $command -WindowStyle Hidden -Wait }
function Wait-External {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    do {
        $snapshot = [ExternalDisplayGuard.NativeDisplay]::Query($false)
        $config = [ExternalDisplayGuard.Storage]::Load()
        $target = [ExternalDisplayGuard.NativeDisplay]::FindTarget($snapshot,$config.TargetPath)
        if ($null -ne $target -and [ExternalDisplayGuard.NativeDisplay]::Matches($snapshot,$target.Key,'external')) { return [math]::Round($watch.Elapsed.TotalSeconds,2) }
        Start-Sleep -Milliseconds 500
    } while ($watch.Elapsed.TotalSeconds -lt 20)
    throw 'External-only mode did not recover within 20 seconds.'
}
try {
    Send-GuardCommand '--external'
    $report.InitialExternalSeconds = Wait-External
    $all = [ExternalDisplayGuard.NativeDisplay]::Query($true)
    $target = [ExternalDisplayGuard.NativeDisplay]::FindTarget($all,([ExternalDisplayGuard.Storage]::Load()).TargetPath)
    [ExternalDisplayGuard.NativeDisplay]::Apply($all,$target,'extend')
    $report.WrongModeInjected = [ExternalDisplayGuard.NativeDisplay]::Matches([ExternalDisplayGuard.NativeDisplay]::Query($false),$target.Key,'extend')
    if (-not $report.WrongModeInjected) { throw 'The extended-mode test injection failed.' }
    $report.AutoRecoverySeconds = Wait-External
    Send-GuardCommand '--pause'
    Start-Sleep -Milliseconds 800
    $all = [ExternalDisplayGuard.NativeDisplay]::Query($true)
    $target = [ExternalDisplayGuard.NativeDisplay]::FindTarget($all,([ExternalDisplayGuard.Storage]::Load()).TargetPath)
    [ExternalDisplayGuard.NativeDisplay]::Apply($all,$target,'extend')
    Start-Sleep -Seconds 7
    $report.PauseRespected = [ExternalDisplayGuard.NativeDisplay]::Matches([ExternalDisplayGuard.NativeDisplay]::Query($false),$target.Key,'extend')
    if (-not $report.PauseRespected) { throw 'The guard changed topology while paused.' }
    Send-GuardCommand '--enable'
    $report.ResumeRecoverySeconds = Wait-External
    $final = [ExternalDisplayGuard.NativeDisplay]::Query($false)
    $report.FinalActive = @($final.Monitors)
    $report.Result = 'PASS'
} finally {
    Send-GuardCommand '--external'
    [ExternalDisplayGuard.Storage]::Write((Join-Path $PSScriptRoot 'evidence\live-tests.json'),$report)
}
$report | ConvertTo-Json -Depth 6
