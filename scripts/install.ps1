param(
    [switch]$PetOnly,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$codexHome = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $env:USERPROFILE '.codex' }
$dataRoot = Join-Path $env:LOCALAPPDATA 'WisadelCompanion'
$petId = 'wisadel-rest'
$petSource = Join-Path $repo "pets/$petId"
$petTarget = Join-Path $codexHome "pets/$petId"
$requiredPetFiles = @('pet.json', 'spritesheet.png', 'spritesheet.webp')
foreach ($name in $requiredPetFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $petSource $name))) { throw "Missing pet file: $name" }
}
$manifest = Get-Content -LiteralPath (Join-Path $petSource 'pet.json') -Raw | ConvertFrom-Json
if ($manifest.id -ne $petId -or $manifest.spritesheetPath -ne 'spritesheet.png') { throw 'Invalid pet manifest' }
if ($manifest.spriteVersionNumber -ne 2) { throw 'Pet must use spriteVersionNumber 2' }

Write-Host "Pet: $petSource -> $petTarget"
Write-Host "Data: $dataRoot"
if ($DryRun) { Write-Host 'Dry run: no files or Codex settings changed.'; return }

$stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
if (Test-Path -LiteralPath $petTarget) {
    $petBackup = Join-Path $dataRoot "setup-backups/$stamp/$petId"
    New-Item -ItemType Directory -Force -Path (Split-Path $petBackup -Parent) | Out-Null
    Copy-Item -LiteralPath $petTarget -Destination $petBackup -Recurse -Force
    Write-Host "Previous pet backed up: $petBackup"
}
New-Item -ItemType Directory -Force -Path $petTarget | Out-Null
foreach ($name in $requiredPetFiles) {
    Copy-Item -LiteralPath (Join-Path $petSource $name) -Destination (Join-Path $petTarget $name) -Force
}
Write-Host "Installed Codex pet: $petId"
if ($PetOnly) { Write-Host 'Pet only. Choose it in Codex; switch pets or restart to refresh the cache.'; return }

$codex = Get-Command codex -ErrorAction SilentlyContinue
if (-not $codex) { throw 'Codex CLI was not found. The pet is installed; install the companion after Codex CLI is available.' }
$pythonCandidates = @()
if ($env:CODEX_PYTHON) { $pythonCandidates += $env:CODEX_PYTHON }
$foundPython = Get-Command python.exe -ErrorAction SilentlyContinue
if ($foundPython) { $pythonCandidates += $foundPython.Source }
$pythonCandidates += Join-Path $env:USERPROFILE '.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe'
$python = $null
foreach ($candidate in ($pythonCandidates | Select-Object -Unique)) {
    if (-not (Test-Path -LiteralPath $candidate)) { continue }
    & $candidate -c 'import sys; sys.exit(0)' 2>$null
    if ($LASTEXITCODE -eq 0) { $python = (Resolve-Path -LiteralPath $candidate).Path; break }
}
if (-not $python) { throw 'Python 3 was not found. The pet is installed; set CODEX_PYTHON and rerun for the companion.' }

$installed = (& $codex.Source plugin list --json | ConvertFrom-Json).installed
if ($installed | Where-Object { $_.name -eq 'wisadel-companion' }) {
    Write-Warning 'A wisadel-companion plugin is already installed. Skipping companion installation to avoid duplicate counters.'
    return
}

$marketRoot = Join-Path $dataRoot 'marketplace'
$pluginSource = Join-Path $repo 'plugins/wisadel-companion'
$pluginTarget = Join-Path $marketRoot 'plugins/wisadel-companion'
if (Test-Path -LiteralPath $pluginTarget) {
    $pluginBackup = Join-Path $dataRoot "setup-backups/$stamp/plugin"
    New-Item -ItemType Directory -Force -Path (Split-Path $pluginBackup -Parent) | Out-Null
    Copy-Item -LiteralPath $pluginTarget -Destination $pluginBackup -Recurse -Force
    Write-Host "Previous plugin source backed up: $pluginBackup"
}
New-Item -ItemType Directory -Force -Path $pluginTarget | Out-Null
Get-ChildItem -LiteralPath $pluginSource -Force | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination $pluginTarget -Recurse -Force
}
New-Item -ItemType Directory -Force -Path (Join-Path $marketRoot '.agents/plugins') | Out-Null
Copy-Item -LiteralPath (Join-Path $repo '.agents/plugins/marketplace.json') -Destination (Join-Path $marketRoot '.agents/plugins/marketplace.json') -Force

$server = (Join-Path $pluginTarget 'scripts/server.py').Replace('\','/')
$hook = (Join-Path $pluginTarget 'scripts/hook.py').Replace('\','/')
$stats = (Join-Path $dataRoot 'stats.json').Replace('\','/')
$pythonJson = $python.Replace('\','/')
$mcp = @{ mcpServers = @{ wisadel = @{ command = $pythonJson; args = @($server); env = @{ WISADEL_STATS_PATH = $stats } } } }
$mcp | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $pluginTarget '.mcp.json') -Encoding UTF8
$hookCommand = '"' + $pythonJson + '" "' + $hook + '"'
$hookEntry = @{ hooks = @(@{ type = 'command'; command = $hookCommand; timeout = 5 }) }
$hooks = @{ hooks = @{ SessionStart = @($hookEntry); UserPromptSubmit = @($hookEntry); Stop = @($hookEntry); Interrupt = @($hookEntry) } }
$hooks | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $pluginTarget 'hooks/hooks.json') -Encoding UTF8

& $codex.Source plugin marketplace add $marketRoot
if ($LASTEXITCODE -ne 0) { throw 'Codex could not add the local marketplace. Pet installation is complete.' }
& $codex.Source plugin add 'wisadel-companion@wisadel-community'
if ($LASTEXITCODE -ne 0) { throw 'Codex could not install the companion plugin. Pet installation is complete.' }
Write-Host 'Companion plugin installed. Review the hook trust prompt in Codex, then restart Codex.'
