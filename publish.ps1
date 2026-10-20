# Run on Windows 10/11 with .NET 10 SDK installed.
$ErrorActionPreference = 'Stop'
$project = Join-Path $PSScriptRoot 'src\SynologyShareManager.csproj'
$out = Join-Path $PSScriptRoot 'dist'
dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=none -o $out
if ($LASTEXITCODE -ne 0) { throw 'dotnet publish failed.' }
Write-Host "EXE ready: $out\DKSynologyShareManager.exe"
