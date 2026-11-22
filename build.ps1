$ErrorActionPreference = 'Stop'

$compiler = Join-Path ([Runtime.InteropServices.RuntimeEnvironment]::GetRuntimeDirectory()) 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) {
    $compiler = Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
}
if (-not (Test-Path -LiteralPath $compiler)) {
    throw 'The Windows .NET Framework C# compiler was not found. Enable .NET Framework 4.8 and try again.'
}

$output = Join-Path $PSScriptRoot 'bin'
$null = New-Item -ItemType Directory -Path $output -Force
$manifest = Join-Path $PSScriptRoot 'app.manifest'
$icon = Join-Path $PSScriptRoot 'assets/deskrewind.ico'
$sources = Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'src') -Filter '*.cs' -File
$executable = Join-Path $output 'DeskRewind.exe'

& $compiler /nologo /warnaserror /target:winexe /platform:anycpu /optimize+ /codepage:65001 `
    /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Runtime.Serialization.dll `
    "/win32manifest:$manifest" "/win32icon:$icon" "/out:$executable" $sources.FullName
if ($LASTEXITCODE -ne 0) {
    throw "DeskRewind build failed with exit code $LASTEXITCODE."
}
