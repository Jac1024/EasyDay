param(
    [string]$Version = "0.1.0",
    [switch]$SkipSelfContained,
    [switch]$NoZip
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root 'dist'
$project = Join-Path $root 'src\EasyDay.App\EasyDay.App.csproj'

New-Item -ItemType Directory -Force -Path $dist | Out-Null

function Publish-Variant {
    param(
        [string]$Name,
        [bool]$SelfContained
    )

    $out = Join-Path $dist $Name
    if (Test-Path $out) {
        Remove-Item $out -Recurse -Force
    }

    Write-Host "==> $Name (self-contained=$SelfContained)"
    dotnet publish $project `
        -c Release `
        -r win-x64 `
        --self-contained $SelfContained `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:DebugType=none `
        -p:DebugSymbols=false `
        -p:Version=$Version `
        -o $out

    if ($LASTEXITCODE -ne 0) {
        throw "publish failed: $Name"
    }

    Copy-Item (Join-Path $root 'README.md') $out
    Copy-Item (Join-Path $root 'LICENSE') $out

    if ($NoZip) {
        return
    }

    $zip = Join-Path $dist "$Name.zip"
    if (Test-Path $zip) {
        Remove-Item $zip -Force
    }

    Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip
    Write-Host ("    -> " + $zip + " (" + [math]::Round((Get-Item $zip).Length / 1MB, 1) + " MB)")
}

Publish-Variant -Name "EasyDay-$Version-win-x64" -SelfContained $false

if (-not $SkipSelfContained) {
    Publish-Variant -Name "EasyDay-$Version-win-x64-self-contained" -SelfContained $true
}

Write-Host "done: $dist"
