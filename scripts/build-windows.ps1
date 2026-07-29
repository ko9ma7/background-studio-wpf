$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$releaseRoot = Join-Path $projectRoot "release"
$packageName = "BackgroundStudio-WPF-v1.2.0-win-x64"
$packageFolder = Join-Path $releaseRoot $packageName
$zipPath = Join-Path $releaseRoot "$packageName.zip"
$resolvedReleaseRoot = [IO.Path]::GetFullPath($releaseRoot) + [IO.Path]::DirectorySeparatorChar
$resolvedPackageFolder = [IO.Path]::GetFullPath($packageFolder)

if (-not $resolvedPackageFolder.StartsWith($resolvedReleaseRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to build outside the project release directory."
}

if (Test-Path -LiteralPath $packageFolder) {
    Remove-Item -LiteralPath $packageFolder -Recurse -Force
}

dotnet publish (Join-Path $projectRoot "BackgroundStudio.csproj") `
    --configuration Release `
    --runtime win-x64 `
    --self-contained true `
    --output $packageFolder `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

Copy-Item -LiteralPath (Join-Path $projectRoot "README.md") -Destination $packageFolder
Copy-Item -LiteralPath (Join-Path $projectRoot "LICENSE") -Destination $packageFolder
Copy-Item -LiteralPath (Join-Path $projectRoot "THIRD_PARTY_NOTICES.md") -Destination $packageFolder

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}
Compress-Archive -LiteralPath $packageFolder -DestinationPath $zipPath -CompressionLevel Optimal
$hash = Get-FileHash -LiteralPath $zipPath -Algorithm SHA256
"$($hash.Hash.ToLowerInvariant())  $packageName.zip" |
    Set-Content -LiteralPath "$zipPath.sha256" -Encoding ascii

Write-Host "Created $zipPath"
Write-Host "SHA256 $($hash.Hash)"
