# Packages the Signet application for win-x64 as a zip.
#
#   scripts/release/package-windows.ps1 -Version <version> -Variant SingleFile|Aot [-Output <folder>]
#
# SingleFile: Signet-<version>-win-x64.zip - Signet.exe (self-contained single file, ReadyToRun) and the native
#             libraries beside it (Skia, HarfBuzz, ANGLE, PCRE, WebView2 loader).
# Aot:        Signet-<version>-win-x64-aot.zip - Signet.exe compiled with NativeAOT (trimmed). Experimental: XSLT
#             (EPUBCheck's Schematron) and reflection bindings are known not to be AOT-safe; build warnings are allowed.
#
# Runs on Windows (NativeAOT cannot cross-compile); needs the .NET SDK, and for Aot the C++ build tools of Visual Studio.
# The default output folder is artifacts/release.
param(
    [Parameter(Mandatory = $true)][string] $Version,
    [Parameter(Mandatory = $true)][ValidateSet('SingleFile', 'Aot')][string] $Variant,
    [string] $Output
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if (-not $Output) { $Output = Join-Path $root 'artifacts\release' }
New-Item -ItemType Directory -Force -Path $Output | Out-Null
$Output = (Resolve-Path $Output).Path

$folder = Join-Path $root "artifacts\windows-$Variant\Signet"
if (Test-Path $folder) { Remove-Item -Recurse -Force $folder }

$common = @(
    'publish', (Join-Path $root 'src\Signet.App\Signet.App.csproj'),
    '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
    "-p:SignetReleaseVersion=$Version", '-p:DebugType=none', '-p:PublishDocumentationFiles=false',
    '-o', $folder, '-nologo'
)
if ($Variant -eq 'SingleFile') {
    $variantArgs = @('-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=false')
    $archive = "Signet-$Version-win-x64.zip"
} else {
    $variantArgs = @('-p:PublishAot=true', '-p:TreatWarningsAsErrors=false')
    $archive = "Signet-$Version-win-x64-aot.zip"
}

Write-Host "== Publishing Signet $Version (win-x64, $Variant)"
& dotnet @common @variantArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

Get-ChildItem $folder -Include *.pdb, *.xml -Recurse | Remove-Item -Force
Move-Item (Join-Path $folder 'Signet.App.exe') (Join-Path $folder 'Signet.exe')
Copy-Item (Join-Path $root 'LICENSE') (Join-Path $folder 'LICENSE.txt')

$zip = Join-Path $Output $archive
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path $folder -DestinationPath $zip
Write-Host "== $zip"
Get-ChildItem $folder | Format-Table Name, Length
