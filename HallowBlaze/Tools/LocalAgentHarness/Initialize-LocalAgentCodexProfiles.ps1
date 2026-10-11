param(
    [string] $DeveloperModel = 'devstral-small-2:24b',
    [string] $ReviewerModel = 'qwen3.6:27b'
)

$ErrorActionPreference = 'Stop'
$utf8NoBom = [Text.UTF8Encoding]::new($false)

$ollamaCommand = Get-Command ollama -ErrorAction Stop
$ollamaPath = if ($ollamaCommand.Source) { $ollamaCommand.Source } else { $ollamaCommand.Path }
if ([string]::IsNullOrWhiteSpace($ollamaPath)) {
    throw 'Ollama CLI was found but its executable path could not be resolved.'
}

$codexCommand = Get-Command codex -ErrorAction Stop
if ($null -eq $codexCommand) {
    throw 'Codex CLI is required before initializing local-agent profiles.'
}

$codexDir = Join-Path $HOME '.codex'
New-Item -ItemType Directory -Path $codexDir -Force | Out-Null

$generatedCatalog = Join-Path $codexDir 'model.json'
$generatedProfile = Join-Path $codexDir 'ollama-launch.config.toml'
$developerCatalog = Join-Path $codexDir 'hallowblaze-devstral-model.json'
$reviewerCatalog = Join-Path $codexDir 'hallowblaze-reviewer-model.json'
$profileBackup = Join-Path $codexDir 'ollama-launch.config.toml.pre-hallowblaze.bak'
$catalogBackup = Join-Path $codexDir 'model.json.pre-hallowblaze.bak'

if ((Test-Path -LiteralPath $generatedProfile -PathType Leaf) -and -not (Test-Path -LiteralPath $profileBackup -PathType Leaf)) {
    Copy-Item -LiteralPath $generatedProfile -Destination $profileBackup
}
if ((Test-Path -LiteralPath $generatedCatalog -PathType Leaf) -and -not (Test-Path -LiteralPath $catalogBackup -PathType Leaf)) {
    Copy-Item -LiteralPath $generatedCatalog -Destination $catalogBackup
}

function Configure-And-SnapshotCatalog([string] $model, [string] $destination) {
    Write-Output "Configuring Ollama Codex catalog for $model ..."
    & $ollamaPath launch codex --model $model --config --yes
    if ($LASTEXITCODE -ne 0) {
        throw "ollama launch codex failed for '$model' with exit code $LASTEXITCODE."
    }

    if (-not (Test-Path -LiteralPath $generatedCatalog -PathType Leaf)) {
        throw "Ollama did not generate the expected Codex model catalog: $generatedCatalog"
    }

    $catalog = [IO.File]::ReadAllText($generatedCatalog, $utf8NoBom) | ConvertFrom-Json
    $slugs = @($catalog.models | ForEach-Object { [string] $_.slug })
    if ($slugs -notcontains $model) {
        throw "Generated Codex model catalog does not contain '$model'. Found: $($slugs -join ', ')"
    }

    Copy-Item -LiteralPath $generatedCatalog -Destination $destination -Force
    Write-Output "Saved role-specific catalog: $destination"
}

# Generate deterministic role-specific catalogs. The final call restores the default
# ollama-launch profile to the Developer model so ad-hoc Codex use remains consistent.
Configure-And-SnapshotCatalog $DeveloperModel $developerCatalog
try {
    Configure-And-SnapshotCatalog $ReviewerModel $reviewerCatalog
}
finally {
    Write-Output "Restoring default ollama-launch profile to $DeveloperModel ..."
    & $ollamaPath launch codex --model $DeveloperModel --config --yes
    if ($LASTEXITCODE -ne 0) {
        Write-Warning "Could not restore the default ollama-launch profile to '$DeveloperModel'. Run: ollama launch codex --model $DeveloperModel --config --yes"
    }
}

Write-Output ''
Write-Output 'LOCAL_AGENT_CODEX_PROFILES_READY'
Write-Output "developer_model=$DeveloperModel"
Write-Output "developer_catalog=$developerCatalog"
Write-Output "reviewer_model=$ReviewerModel"
Write-Output "reviewer_catalog=$reviewerCatalog"
Write-Output "default_profile=$generatedProfile"
