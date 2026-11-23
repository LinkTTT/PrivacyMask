param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('Start', 'Status', 'Stop')][string]$Action,
    [ValidateRange(1, 3600)][int]$Seconds = 600,
    [switch]$UntilUnlocked,
    [string]$Exe
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $PSBoundParameters.ContainsKey('Exe')) {
    $previewExe = Join-Path $PSScriptRoot 'preview\PrivacyMask.exe'
    $Exe = if (Test-Path -LiteralPath $previewExe -PathType Leaf) { $previewExe } else { Join-Path $PSScriptRoot 'PrivacyMask.exe' }
}
$actionName = @{ start = 'Start'; status = 'Status'; stop = 'Stop' }[$Action]
$hasSeconds = $PSBoundParameters.ContainsKey('Seconds')
$invalidOptions = $UntilUnlocked -and ($Action -ne 'Start' -or $hasSeconds)
if ($Action -eq 'Start' -and -not $hasSeconds -and -not $UntilUnlocked) { $actionName = 'Default' }
$durationArgument = if ($UntilUnlocked) { 0 } else { $Seconds }
$process = $null
$failure = 'control-failure'

try {
    if ($invalidOptions) {
        $failure = 'invalid-arguments'
        throw '-UntilUnlocked applies only to Start and cannot be combined with -Seconds.'
    }
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = (Resolve-Path -LiteralPath $Exe).ProviderPath
    $info.Arguments = "--control $actionName $durationArgument"
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $process = [Diagnostics.Process]::Start($info)

    $line = $process.StandardOutput.ReadLineAsync()
    if (-not $line.Wait(15000) -or -not $process.WaitForExit(1000)) {
        $failure = 'controller-timeout'
        if (-not $process.HasExited) { $process.Kill() }
        throw 'Controller did not exit within the expected time.'
    }

    $result = ConvertFrom-Json -InputObject $line.Result
    if ($result.state -notin 'active', 'inactive', 'unavailable') { throw 'Invalid controller response.' }
    $line.Result
    exit $process.ExitCode
}
catch {
    @{ state = 'unavailable'; expiresAt = $null; reason = $failure } | ConvertTo-Json -Compress
    exit 1
}
finally {
    if ($null -ne $process) { $process.Dispose() }
}
