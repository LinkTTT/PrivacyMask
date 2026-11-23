param([string]$OutputPath)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if (-not $PSBoundParameters.ContainsKey('OutputPath')) { $OutputPath = Join-Path $PSScriptRoot 'PrivacyMask.exe' }

$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler -PathType Leaf)) {
    throw 'The Windows .NET Framework C# compiler was not found.'
}

$sources = 'PrivacyMask.cs', 'MaskControl.cs', 'MaskPanel.cs', 'MaskAppearance.cs', 'MaskPassword.cs', 'MaskSettings.cs', 'MaskLanguage.cs' | ForEach-Object { Join-Path $PSScriptRoot $_ }
$output = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
& $compiler /nologo /target:winexe /platform:anycpu /optimize+ /warnaserror+ /codepage:65001 /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll "/out:$output" $sources
if ($LASTEXITCODE -ne 0) { throw "C# compilation failed ($LASTEXITCODE)." }
Get-FileHash -LiteralPath $output -Algorithm SHA256
