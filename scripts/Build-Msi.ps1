param(
    [string]$Version = "6.0.6",
    [string]$RuntimeIdentifier = "win-x64"
)

$ErrorActionPreference = "Stop"
if ($PSVersionTable.PSVersion.Major -ge 7) {
    $PSNativeCommandUseErrorActionPreference = $true
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "VramOp.csproj"
$publishDir = Join-Path $repoRoot "artifacts\publish\$RuntimeIdentifier-v$Version"
$wixIntermediateDir = Join-Path $repoRoot "artifacts\wix\$RuntimeIdentifier-v$Version"
$distDir = Join-Path $repoRoot "dist"
$msiPath = Join-Path $distDir "VRAMVue-Setup-v$Version-$RuntimeIdentifier.msi"
$portableZipPath = Join-Path $distDir "VRAMVue-Portable-v$Version-$RuntimeIdentifier.zip"
$wxsPath = Join-Path $repoRoot "installer\VRAMVue.wxs"

$wix = Get-Command wix -ErrorAction SilentlyContinue
if (-not $wix) {
    throw "WiX Toolset CLI was not found. Install it with: dotnet tool install --global wix --version 6.0.2"
}

$wixUiExtension = "WixToolset.UI.wixext/6.0.2"
wix extension add --global $wixUiExtension | Out-Null

New-Item -ItemType Directory -Force -Path $publishDir, $wixIntermediateDir, $distDir | Out-Null

dotnet publish $projectPath `
    -c Release `
    -r $RuntimeIdentifier `
    --self-contained true `
    -o $publishDir `
    /p:Version=$Version `
    /p:AssemblyVersion=$Version.0 `
    /p:FileVersion=$Version.0 `
    /p:InformationalVersion=$Version `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:DebugType=none `
    /p:DebugSymbols=false

wix build $wxsPath `
    -ext WixToolset.UI.wixext `
    -arch x64 `
    -d ProductVersion=$Version `
    -d PublishDir=$publishDir `
    -d RepoRoot=$repoRoot `
    -intermediatefolder $wixIntermediateDir `
    -out $msiPath

if (Test-Path $portableZipPath) {
    Remove-Item -Path $portableZipPath -Force
}

Compress-Archive `
    -Path (Join-Path $publishDir "VramVue.exe") `
    -DestinationPath $portableZipPath `
    -CompressionLevel Optimal

Get-Item $msiPath, $portableZipPath
