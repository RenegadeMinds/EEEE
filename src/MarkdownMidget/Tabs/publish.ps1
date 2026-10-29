<#
  publish.ps1 - make an MMEEEE release locally.

    .\src\MarkdownMidget\Tabs\publish.ps1 [-Version 1.1.0]

  Writes to dist\ at the repository root:
    MMEEEE-v<version>-win-x64.exe   self-contained single file (no .NET needed to run it);
                                    the asset name the app's updater looks for
    MMEEEE-v<version>-win-x64.zip   that exe, README.md (Tabs\RELEASE-README.md) and LICENSE
  .github\workflows\release.yml does the same on a tag push.
#>
param([string]$Version)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$proj = Join-Path $repo 'src\MarkdownMidget\MarkdownMidget.csproj'
if (-not $Version) { $Version = ([xml](Get-Content $proj)).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1 }
$tag = "v$Version"

dotnet publish $proj -p:PublishProfile=win-x64 -p:Version=$Version -p:InformationalVersion=$Version -p:UseLocalBuildNumber=false --nologo
if ($LASTEXITCODE) { throw "dotnet publish failed ($LASTEXITCODE)" }

$exe = Join-Path $repo 'src\MarkdownMidget\bin\Release\publish\self-contained\MMEEEE.exe'
$dist = Join-Path $repo 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null
$assetExe = Join-Path $dist "MMEEEE-$tag-win-x64.exe"
Copy-Item $exe $assetExe -Force

$stage = Join-Path $dist "MMEEEE-$tag"
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $stage | Out-Null
Copy-Item $exe (Join-Path $stage 'MMEEEE.exe')
Copy-Item (Join-Path $PSScriptRoot 'RELEASE-README.md') (Join-Path $stage 'README.md')
Copy-Item (Join-Path $repo 'LICENSE') (Join-Path $stage 'LICENSE')
$zip = Join-Path $dist "MMEEEE-$tag-win-x64.zip"
Remove-Item $zip -Force -ErrorAction SilentlyContinue
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip
Remove-Item $stage -Recurse -Force

"Made:"
Get-Item $assetExe, $zip | ForEach-Object { "  {0}  ({1:N1} MB)" -f $_.FullName, ($_.Length / 1MB) }
