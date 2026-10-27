[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+\.0$')]
    [string]$Version = '0.1.0.0',
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root 'artifacts'
$publish = Join-Path $output 'Resonant-win-x64'
$staging = Join-Path $output 'msix-staging'
$release = Join-Path $output "release-$Version"
$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin'
$sdk = Get-ChildItem -LiteralPath $sdkRoot -Directory |
    Where-Object { Test-Path (Join-Path $_.FullName 'x64/MakeAppx.exe') } |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (-not $sdk) { throw 'Install the Windows SDK with MakeAppx.exe before packaging.' }
if (([version]$Version).Major -gt 65535 -or ([version]$Version).Minor -gt 65535 -or ([version]$Version).Build -gt 65535) {
    throw 'MSIX version components must be between 0 and 65535.'
}

foreach ($directory in @($publish, $staging, $release)) {
    if (Test-Path -LiteralPath $directory) { Remove-Item -LiteralPath $directory -Recurse -Force }
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
}
$publishArguments = @('publish', (Join-Path $root 'src/Resonant.App/Resonant.App.csproj'),
    '--configuration', 'Release', '--runtime', 'win-x64', '--self-contained', 'true',
    "-p:Version=$Version", '--output', $publish)
if ($NoRestore) { $publishArguments += '--no-restore' }
& dotnet @publishArguments
if ($LASTEXITCODE -ne 0) { throw "Publish failed with exit code $LASTEXITCODE." }
foreach ($file in @('Resonant.exe', 'Resonant.Core.dll', 'Microsoft.WindowsAppRuntime.Bootstrap.dll')) {
    if (-not (Test-Path (Join-Path $publish $file))) { throw "Publish is missing $file." }
}
foreach ($language in @('nl', 'de', 'fr', 'es')) {
    if (-not (Test-Path (Join-Path $publish "$language/Resonant.Core.resources.dll"))) {
        throw "Publish is missing $language localization."
    }
}
foreach ($file in @('LICENSE', 'THIRD-PARTY-NOTICES', 'README.md')) {
    Copy-Item -LiteralPath (Join-Path $root $file) -Destination $publish
}
$zip = Join-Path $release "Resonant-$Version-win-x64.zip"
Compress-Archive -Path "$publish/*" -DestinationPath $zip
Copy-Item -Path "$publish/*" -Destination $staging -Recurse
[xml]$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Package.appxmanifest') -Raw
$manifest.Package.Identity.Version = $Version
$manifest.Save((Join-Path $staging 'AppxManifest.xml'))

Add-Type -AssemblyName System.Drawing
$source = [System.Drawing.Image]::FromFile((Join-Path $root 'src/Resonant.App/Assets/Resonant.png'))
try {
    foreach ($asset in @(
        @{ Name = 'StoreLogo.png'; Width = 50; Height = 50 },
        @{ Name = 'Square44x44Logo.png'; Width = 44; Height = 44 },
        @{ Name = 'Square150x150Logo.png'; Width = 150; Height = 150 },
        @{ Name = 'Wide310x150Logo.png'; Width = 310; Height = 150 }
    )) {
        $bitmap = [System.Drawing.Bitmap]::new($asset.Width, $asset.Height)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.Clear([System.Drawing.Color]::Transparent)
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $size = [Math]::Min($asset.Width, $asset.Height)
            $graphics.DrawImage($source, [int](($asset.Width - $size) / 2), 0, $size, $size)
            $bitmap.Save((Join-Path $staging "Assets/$($asset.Name)"), [System.Drawing.Imaging.ImageFormat]::Png)
        }
        finally { $graphics.Dispose(); $bitmap.Dispose() }
    }
}
finally { $source.Dispose() }
$msix = Join-Path $release "Resonant-$Version-win-x64.msix"
& (Join-Path $sdk.FullName 'x64/MakeAppx.exe') pack /d $staging /p $msix /o
if ($LASTEXITCODE -ne 0) { throw "MSIX packaging failed with exit code $LASTEXITCODE." }
@($zip, $msix) | ForEach-Object {
    $hash = Get-FileHash -LiteralPath $_ -Algorithm SHA256
    "$($hash.Hash.ToLowerInvariant())  $([System.IO.Path]::GetFileName($_))"
} | Set-Content -LiteralPath (Join-Path $release 'SHA256SUMS.txt') -Encoding ascii
Write-Output "Release artifacts: $release"
Write-Output 'MSIX is unsigned, intended for Partner Center ingestion, not sideload installation.'