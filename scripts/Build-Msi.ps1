param(
    [string]$Version = "6.0.0",
    [string]$RuntimeIdentifier = "win-x64"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "VramOp.csproj"
$publishDir = Join-Path $repoRoot "artifacts\publish\$RuntimeIdentifier-v$Version"
$wixIntermediateDir = Join-Path $repoRoot "artifacts\wix\$RuntimeIdentifier-v$Version"
$distDir = Join-Path $repoRoot "dist"
$msiPath = Join-Path $distDir "VRAMVue-Setup-v$Version-$RuntimeIdentifier.msi"
$wxsPath = Join-Path $repoRoot "installer\VRAMVue.wxs"

$wix = Get-Command wix -ErrorAction SilentlyContinue
if (-not $wix) {
    throw "WiX Toolset CLI was not found. Install it with: dotnet tool install --global wix"
}

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
    -arch x64 `
    -d ProductVersion=$Version `
    -d PublishDir=$publishDir `
    -d RepoRoot=$repoRoot `
    -intermediatefolder $wixIntermediateDir `
    -out $msiPath

Get-Item $msiPath
