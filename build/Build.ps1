param(
    [Parameter(Mandatory)][string]$GameRoot,
    [string]$Dotnet = 'dotnet'
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $GameRoot).Path
$core = Join-Path $root 'BepInEx/core'
$interop = Join-Path $root 'BepInEx/interop'
$repo = Split-Path -Parent $PSScriptRoot

if (!(Test-Path -LiteralPath (Join-Path $root 'Dokisaba.exe') -PathType Leaf)) {
    throw 'GameRoot must contain Dokisaba.exe.'
}

foreach ($file in @(
    'BepInEx.Core.dll', 'BepInEx.Unity.IL2CPP.dll',
    '0Harmony.dll', 'Il2CppInterop.Runtime.dll'
)) {
    if (!(Test-Path -LiteralPath (Join-Path $core $file) -PathType Leaf)) {
        throw "BepInEx core reference missing: $file"
    }
}

foreach ($file in @('App.Game.dll', 'Message.dll')) {
    if (!(Test-Path -LiteralPath (Join-Path $interop $file) -PathType Leaf)) {
        throw "Game-specific BepInEx interop reference missing: $file"
    }
}

foreach ($module in 'P0', 'P1', 'P2') {
    $project = Join-Path $repo "src/$module/Dokisaba.$module.csproj"
    & $Dotnet build $project -c Release --nologo -p:UseSharedCompilation=false "-p:FrameworkCore=$core" "-p:GameInterop=$interop"
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed: $module"
    }
}
