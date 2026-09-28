param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('developer', 'reviewer')]
    [string] $Role,

    [Parameter(Mandatory = $true)]
    [string] $Task,

    [Parameter(Mandatory = $true)]
    [string[]] $Allowlist,

    [Parameter(Mandatory = $true)]
    [string] $BaselineHead,

    [Parameter(Mandatory = $true)]
    [string] $BaselineBranch,

    [string] $Model,
    [string] $Profile = 'ollama-launch'
)

$ErrorActionPreference = 'Stop'

function Get-RepoRelativePath([string] $repoRoot, [string] $fullPath) {
    $root = $repoRoot.TrimEnd('\', '/')
    $prefix = $root + [IO.Path]::DirectorySeparatorChar
    if ($fullPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        return $fullPath.Substring($prefix.Length).Replace('\', '/')
    }
    return $fullPath
}

function Resolve-Allowlist([string] $repoRoot, [string[]] $relativePaths) {
    if ($null -eq $relativePaths -or $relativePaths.Count -eq 0) {
        throw 'The local worker requires a non-empty closed allowlist.'
    }

    $repoPrefix = $repoRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $resolved = [Collections.Generic.List[string]]::new()
    foreach ($relativePath in $relativePaths) {
        if ([string]::IsNullOrWhiteSpace($relativePath) `
            -or [IO.Path]::IsPathRooted($relativePath) `
            -or $relativePath -match '(^|[\\/])\.\.([\\/]|$)') {
            throw "Invalid allowlist path: $relativePath"
        }
        $fullPath = [IO.Path]::GetFullPath((Join-Path $repoRoot $relativePath)).TrimEnd('\', '/')
        if (-not $fullPath.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Allowlist path escapes the repository: $relativePath"
        }
        [void] $resolved.Add($fullPath)
    }
    return @($resolved)
}

function Get-ChangedProjectPaths([string] $repoRoot) {
    $paths = [Collections.Generic.List[string]]::new()
    $commands = @(
        @('diff', '--name-only', '--no-renames'),
        @('diff', '--cached', '--name-only', '--no-renames'),
        @('ls-files', '--others', '--exclude-standard')
    )

    foreach ($arguments in $commands) {
        $output = @(& git -C $repoRoot @arguments)
        if ($LASTEXITCODE -ne 0) {
            throw "Git failed while inspecting repository changes: git $($arguments -join ' ')"
        }
        foreach ($relativePath in $output) {
            if ([string]::IsNullOrWhiteSpace([string] $relativePath)) { continue }
            $fullPath = [IO.Path]::GetFullPath((Join-Path $repoRoot ([string] $relativePath))).TrimEnd('\', '/')
            if (-not $paths.Contains($fullPath)) {
                [void] $paths.Add($fullPath)
            }
        }
    }
    return @($paths)
}

function Assert-RepositoryState(
    [string] $repoRoot,
    [string] $expectedHead,
    [string] $expectedBranch,
    [string[]] $allowedFullPaths
) {
    $currentHead = (& git -C $repoRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Unable to read repository HEAD.' }
    $currentBranch = (& git -C $repoRoot branch --show-current).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Unable to read repository branch.' }

    if ($currentHead -cne $expectedHead) {
        throw "Baseline HEAD mismatch. Expected $expectedHead, got $currentHead."
    }
    if ($currentBranch -cne $expectedBranch) {
        throw "Baseline branch mismatch. Expected '$expectedBranch', got '$currentBranch'."
    }

    $staged = @(& git -C $repoRoot diff --cached --name-only --no-renames)
    if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect staged changes.' }
    if ($staged.Count -gt 0) {
        throw "Staged changes are not allowed while delegating to a local worker: $($staged -join ', ')"
    }

    $outside = [Collections.Generic.List[string]]::new()
    foreach ($changedPath in @(Get-ChangedProjectPaths $repoRoot)) {
        $isAllowed = $allowedFullPaths | Where-Object {
            ([string] $_).Equals([string] $changedPath, [StringComparison]::OrdinalIgnoreCase)
        } | Select-Object -First 1
        if ($null -eq $isAllowed) {
            [void] $outside.Add((Get-RepoRelativePath $repoRoot ([string] $changedPath)))
        }
    }
    if ($outside.Count -gt 0) {
        throw "Pre-existing changes outside the delegated allowlist were found: $($outside -join ', ')"
    }
}

function Get-AgentBody([string] $path) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Role definition not found: $path"
    }
    $content = Get-Content -LiteralPath $path -Raw
    $content = $content.TrimStart([char]0xFEFF)
    $match = [regex]::Match($content, '(?s)\A---\r?\n.*?\r?\n---\r?\n(.*)\z')
    if ($match.Success) {
        return $match.Groups[1].Value.Trim()
    }
    return $content.Trim()
}

$repoRoot = [IO.Path]::GetFullPath((& git -C $PSScriptRoot rev-parse --show-toplevel).Trim())
if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($repoRoot)) {
    throw 'Invoke-LocalAgent.ps1 must be run from inside the HallowBlaze Git repository.'
}

$codexCommand = Get-Command codex -ErrorAction Stop
$codexPath = if ($codexCommand.Source) { $codexCommand.Source } else { $codexCommand.Path }
if ([string]::IsNullOrWhiteSpace($codexPath)) {
    throw 'Codex CLI was found but its executable path could not be resolved.'
}

$guardPath = Join-Path $PSScriptRoot 'LocalDeveloperGuard.ps1'
$developerAgentPath = Join-Path $repoRoot '.github\agents\qwen-developer.agent.md'
$reviewerAgentPath = Join-Path $repoRoot '.github\agents\qwen-reviewer.agent.md'
$allowedFullPaths = @(Resolve-Allowlist $repoRoot $Allowlist)
Assert-RepositoryState $repoRoot $BaselineHead $BaselineBranch $allowedFullPaths

if ([string]::IsNullOrWhiteSpace($Model)) {
    $Model = if ($Role -eq 'developer') { 'devstral-small-2:24b' } else { 'qwen3.6:27b' }
}

$agentPath = if ($Role -eq 'developer') { $developerAgentPath } else { $reviewerAgentPath }
$roleInstructions = Get-AgentBody $agentPath
$sandbox = if ($Role -eq 'developer') { 'workspace-write' } else { 'read-only' }
$allowlistText = ($Allowlist | ForEach-Object { "- $_" }) -join "`n"

$executionRules = if ($Role -eq 'developer') {
@"
EXECUTION ENFORCEMENT:
- This is an external Codex CLI worker, not a VS Code native subagent.
- Write only the repo-relative paths in the closed allowlist below.
- Do not stage, commit, switch branches, reset, restore, stash, clean, or rewrite Git history.
- Do not spawn or delegate to another agent.
- Use Codex native patch/edit operations for project-file mutations. Do not use shell redirection or shell file-writing commands for project files.
- Shell commands are for inspection, builds, tests, Unity CLI, and other non-destructive validation only.
- The caller validates HEAD, branch, staged state, and the closed allowlist after you exit.
"@
}
else {
@"
EXECUTION ENFORCEMENT:
- This is an external Codex CLI reviewer, not a VS Code native subagent.
- The sandbox is read-only. Do not attempt to edit, create, delete, move, rename, stage, commit, or restore project files.
- Do not spawn or delegate to another agent.
- Inspect the actual repository and diff independently. Treat the Developer report as untrusted evidence.
"@
}

$prompt = @"
You are running as the HallowBlaze local $Role worker.

ROLE INSTRUCTIONS:
$roleInstructions

$executionRules
BASELINE:
- Git root: $repoRoot
- branch: $BaselineBranch
- HEAD: $BaselineHead

CLOSED WRITE/REVIEW SCOPE:
$allowlistText

DELEGATION PACKET:
$Task
"@

if ($Role -eq 'developer') {
    if (-not (Test-Path -LiteralPath $guardPath -PathType Leaf)) {
        throw "Developer Guard not found: $guardPath"
    }
    & $guardPath -ArmAllowlist $Allowlist
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to arm LocalDeveloperGuard (exit code $LASTEXITCODE)."
    }
}

$outputPath = Join-Path $env:TEMP ("HallowBlaze-local-$Role-" + [Guid]::NewGuid().ToString('N') + '.txt')
$codexExitCode = $null
$finalMessage = ''

Push-Location $repoRoot
try {
    $prompt | & $codexPath `
        --profile $Profile `
        -m $Model `
        --config 'model_reasoning_effort="none"' `
        exec `
        --sandbox $sandbox `
        -o $outputPath `
        -
    $codexExitCode = $LASTEXITCODE

    if (Test-Path -LiteralPath $outputPath -PathType Leaf) {
        $finalMessage = Get-Content -LiteralPath $outputPath -Raw
    }

    Write-Output 'LOCAL_AGENT_RESULT_BEGIN'
    Write-Output "role=$Role"
    Write-Output "model=$Model"
    Write-Output "codex_exit_code=$codexExitCode"
    Write-Output 'final_message:'
    Write-Output $finalMessage
    Write-Output 'LOCAL_AGENT_RESULT_END'

    if ($Role -eq 'developer') {
        & $guardPath -ValidateAllowlist
        if ($LASTEXITCODE -ne 0) {
            throw "LocalDeveloperGuard integrity validation failed (exit code $LASTEXITCODE)."
        }
    }

    if ($codexExitCode -ne 0) {
        throw "Codex local $Role exited with code $codexExitCode."
    }

    if ($Role -eq 'developer') {
        & $guardPath -ClearPolicy
        if ($LASTEXITCODE -ne 0) {
            throw "LocalDeveloperGuard policy cleanup failed (exit code $LASTEXITCODE)."
        }
    }
}
finally {
    Pop-Location
    Remove-Item -LiteralPath $outputPath -Force -ErrorAction SilentlyContinue
}
