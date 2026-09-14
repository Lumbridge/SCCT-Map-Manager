param([switch]$Test, [switch]$LiveTests)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    if ($Test -or $LiveTests) {
        $testArguments = @('run', '--project', 'tests/MapManager.Tests', '-c', 'Release')
        if ($LiveTests) {
            $null = New-Item -ItemType Directory -Path 'artifacts' -Force
            $catalogPath = Join-Path $PSScriptRoot 'artifacts/catalog-tree.json'
            Invoke-WebRequest -Uri 'https://api.github.com/repos/Lumbridge/SCCT-Maps/git/trees/main?recursive=1' -Headers @{'User-Agent'='SCCT-Map-Manager-Build'} -OutFile $catalogPath
            $testArguments += @('--', '--tree', $catalogPath, '--live')
        }
        & dotnet @testArguments
        if ($LASTEXITCODE -ne 0) { throw 'Map manager tests failed.' }
    }
    & dotnet publish src/MapManager.App/MapManager.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o artifacts/publish --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Map manager build failed.' }
    $exe = Join-Path $PSScriptRoot 'artifacts/publish/SCCT Map Manager.exe'
    $hash = (Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash.ToLowerInvariant()
    [IO.File]::WriteAllText((Join-Path $PSScriptRoot 'artifacts/publish/SHA256SUMS.txt'), "$hash  SCCT Map Manager.exe`n")
    Write-Output "Built $exe"
} finally { Pop-Location }
