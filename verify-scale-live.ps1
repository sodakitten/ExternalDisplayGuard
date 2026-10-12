$ErrorActionPreference = 'Stop'
New-Item -ItemType Directory -Path (Join-Path $PSScriptRoot 'evidence') -Force | Out-Null
$appExe = Join-Path $env:LOCALAPPDATA 'ExternalDisplayGuard\ExternalDisplayGuard.exe'
[Reflection.Assembly]::LoadFrom($appExe) | Out-Null
$report = [ordered]@{}
function Send-Command([string]$command) { Start-Process -FilePath $appExe -ArgumentList $command -WindowStyle Hidden -Wait }
function Current-Target($snapshot) { [ExternalDisplayGuard.NativeDisplay]::FindTarget($snapshot,([ExternalDisplayGuard.Storage]::Load()).TargetPath) }
function Wait-State([string]$mode,[int]$scale) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    do {
        $snapshot = [ExternalDisplayGuard.NativeDisplay]::Query($false)
        $target = Current-Target $snapshot
        if ($null -ne $target -and [ExternalDisplayGuard.NativeDisplay]::Matches($snapshot,$target.Key,$mode) -and ($scale -eq 0 -or $target.ScalePercent -eq $scale)) { return [math]::Round($watch.Elapsed.TotalSeconds,2) }
        Start-Sleep -Milliseconds 500
    } while ($watch.Elapsed.TotalSeconds -lt 25)
    throw "The guard did not reach $mode with $scale percent scaling."
}
function Pause-Guard {
    Send-Command '--pause'
    $watch = [Diagnostics.Stopwatch]::StartNew()
    do {
        $status = Get-Content -LiteralPath (Join-Path $env:LOCALAPPDATA 'ExternalDisplayGuard\status.json') -Raw | ConvertFrom-Json
        if ($null -ne $status.PauseUntil) { return }
        Start-Sleep -Milliseconds 200
    } while ($watch.Elapsed.TotalSeconds -lt 5)
    throw 'Pause command was not acknowledged.'
}
try {
    $report.InitialRecoverySeconds = Wait-State 'external' 200
    $report.InitialTarget = Current-Target ([ExternalDisplayGuard.NativeDisplay]::Query($false))
    Pause-Guard
    $snapshot = [ExternalDisplayGuard.NativeDisplay]::Query($false)
    $target = Current-Target $snapshot
    [ExternalDisplayGuard.NativeDisplay]::SetScale($snapshot,$target,175) | Out-Null
    Start-Sleep -Seconds 6
    $report.PauseKeeps175 = (Current-Target ([ExternalDisplayGuard.NativeDisplay]::Query($false))).ScalePercent -eq 175
    if (-not $report.PauseKeeps175) { throw 'Scaling was changed while paused.' }
    Send-Command '--enable'
    $report.ScaleOnlyRecoverySeconds = Wait-State 'external' 200
    Pause-Guard
    $all = [ExternalDisplayGuard.NativeDisplay]::Query($true)
    $target = Current-Target $all
    [ExternalDisplayGuard.NativeDisplay]::Apply($all,$target,'extend')
    $snapshot = [ExternalDisplayGuard.NativeDisplay]::Query($false)
    $target = Current-Target $snapshot
    [ExternalDisplayGuard.NativeDisplay]::SetScale($snapshot,$target,175) | Out-Null
    $injected = [ExternalDisplayGuard.NativeDisplay]::Query($false)
    $report.Extended175Injected = [ExternalDisplayGuard.NativeDisplay]::Matches($injected,$target.Key,'extend') -and (Current-Target $injected).ScalePercent -eq 175
    if (-not $report.Extended175Injected) { throw 'Combined topology and scaling injection failed.' }
    $report.InnerBefore = @($injected.Monitors | Where-Object Internal | Select-Object Name,ScalePercent)
    Send-Command '--enable'
    $report.TopologyAndScaleRecoverySeconds = Wait-State 'external' 200
    Send-Command '--extend'
    $report.ExtendSwitchSeconds = Wait-State 'extend' 0
    $snapshot = [ExternalDisplayGuard.NativeDisplay]::Query($false)
    $target = Current-Target $snapshot
    [ExternalDisplayGuard.NativeDisplay]::SetScale($snapshot,$target,175) | Out-Null
    Start-Sleep -Seconds 6
    $extended = [ExternalDisplayGuard.NativeDisplay]::Query($false)
    $report.ExtendedModeKeeps175 = (Current-Target $extended).ScalePercent -eq 175
    $report.InnerAfter = @($extended.Monitors | Where-Object Internal | Select-Object Name,ScalePercent)
    if (-not $report.ExtendedModeKeeps175) { throw 'Extended-mode scaling was unexpectedly enforced.' }
    if (($report.InnerBefore | ConvertTo-Json -Compress) -ne ($report.InnerAfter | ConvertTo-Json -Compress)) { throw 'The laptop panel scaling changed.' }
    Send-Command '--external'
    $report.FinalRecoverySeconds = Wait-State 'external' 200
    $report.FinalActive = @([ExternalDisplayGuard.NativeDisplay]::Query($false).Monitors)
    $report.Result = 'PASS'
} finally {
    Send-Command '--external'
    $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $PSScriptRoot 'evidence\scale-live-tests.json') -Encoding UTF8
}
$report | ConvertTo-Json -Depth 6
