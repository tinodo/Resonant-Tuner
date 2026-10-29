[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+\.0$')]
    [string]$Version = '1.0.0.0',
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root 'artifacts/nuget'
$project = Join-Path $root 'src/Resonant.Core/Resonant.Core.csproj'
$packageId = 'Resonant.Core'
$repositoryUrl = 'https://github.com/tinodo/Resonant-Tuner'
$parsed = [version]$Version
if ($parsed.Major -gt 65535 -or $parsed.Minor -gt 65535 -or $parsed.Build -gt 65535) {
    throw 'Version components must be between 0 and 65535.'
}
# NuGet drops the trailing .0, so release 1.0.2.0 is package 1.0.2.
$packageVersion = '{0}.{1}.{2}' -f $parsed.Major, $parsed.Minor, $parsed.Build

if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
New-Item -ItemType Directory -Path $output -Force | Out-Null

$packArguments = @('pack', $project, '--configuration', 'Release', '--output', $output,
    "-p:Version=$Version", "-p:PackageVersion=$packageVersion")
if ($env:GITHUB_ACTIONS -eq 'true') { $packArguments += '-p:ContinuousIntegrationBuild=true' }
if ($NoRestore) { $packArguments += '--no-restore' }
& dotnet @packArguments
if ($LASTEXITCODE -ne 0) { throw "Pack failed with exit code $LASTEXITCODE." }

$files = @(Get-ChildItem -LiteralPath $output -File)
$expectedName = "$packageId.$packageVersion.nupkg"
if ($files.Count -ne 1 -or $files[0].Name -ne $expectedName) {
    throw "Expected only $expectedName in $output but found: $(($files | ForEach-Object Name) -join ', ')."
}
$package = $files[0]

$head = $null
if (Get-Command git -ErrorAction SilentlyContinue) {
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $head = (& git -C $root rev-parse HEAD 2>$null) } finally { $ErrorActionPreference = $previousPreference }
    if ($LASTEXITCODE -ne 0) { $head = $null }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$expectedEntries = @("$packageId.nuspec", 'README.md', 'lib/net10.0/Resonant.Core.dll') +
    @('de', 'es', 'fr', 'nl' | ForEach-Object { "lib/net10.0/$_/Resonant.Core.resources.dll" })
$structuralEntries = '^(\[Content_Types\]\.xml|_rels/\.rels|package/services/metadata/core-properties/[^/]+\.psmdcp)$'
$entries = @()
$nuspecText = $null
$fileVersion = $null
$assemblyPath = [System.IO.Path]::GetTempFileName()
$archive = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
try {
    $entries = @($archive.Entries | ForEach-Object { $_.FullName })
    $nuspecEntry = $archive.GetEntry("$packageId.nuspec")
    $assemblyEntry = $archive.GetEntry('lib/net10.0/Resonant.Core.dll')
    if ($nuspecEntry) {
        $reader = New-Object System.IO.StreamReader($nuspecEntry.Open())
        try { $nuspecText = $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    if ($assemblyEntry) {
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($assemblyEntry, $assemblyPath, $true)
        $fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($assemblyPath).FileVersion
    }
}
finally {
    $archive.Dispose()
    Remove-Item -LiteralPath $assemblyPath -Force -ErrorAction SilentlyContinue
}

$missing = @($expectedEntries | Where-Object { $entries -notcontains $_ })
if ($missing.Count -gt 0) { throw "The package is missing: $($missing -join ', ')." }
$unexpected = @($entries | Where-Object { $expectedEntries -notcontains $_ -and $_ -notmatch $structuralEntries })
if ($unexpected.Count -gt 0) { throw "The package has unexpected files: $($unexpected -join ', ')." }

[xml]$nuspec = $nuspecText
$metadata = $nuspec.package.metadata
if ($metadata.id -ne $packageId -or $metadata.version -ne $packageVersion) {
    throw "The package is $($metadata.id) $($metadata.version), expected $packageId $packageVersion."
}
if ($metadata.license.type -ne 'expression' -or $metadata.license.InnerText -ne 'MIT') {
    throw 'The package must declare the MIT license expression.'
}
if ($metadata.repository.type -ne 'git' -or $metadata.repository.url -ne $repositoryUrl) {
    throw "The package repository must be git $repositoryUrl; GitHub Packages links the package to the repository from it."
}
if ($head -and $metadata.repository.commit -ne $head) {
    throw "The package commit is '$($metadata.repository.commit)', expected $head."
}
$groups = @($metadata.dependencies.group)
if ($groups.Count -ne 1 -or $groups[0].targetFramework -ne 'net10.0') {
    throw 'The package must have exactly one dependency group, for net10.0.'
}
$dependencies = @($groups[0].dependency)
if ($dependencies.Count -ne 1 -or $dependencies[0].id -ne 'MathNet.Numerics') {
    throw 'The package must depend only on MathNet.Numerics.'
}
if ($fileVersion -ne $Version) { throw "Resonant.Core.dll has file version $fileVersion, expected $Version." }

Write-Output "NuGet package: $($package.FullName)"
Write-Output "$($metadata.id) $($metadata.version), assembly $fileVersion, repository $($metadata.repository.url) at $($metadata.repository.commit)"
