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
    [string] $Profile = 'ollama-launch',
    [string] $CatalogPath
)

$ErrorActionPreference = 'Stop'
$utf8NoBom = [Text.UTF8Encoding]::new($false)

function Get-RepoRelativePath([string] $repoRoot, [string] $fullPath) {
    $root = $repoRoot.TrimEnd('\', '/')
    $prefix = $root + [IO.Path]::DirectorySeparatorChar
    if ($fullPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        return $fullPath.Substring($prefix.Length).Replace('\', '/')
    }
    return $fullPath
}

function Resolve-Allowlist([string] $projectRoot, [string[]] $relativePaths) {
    if ($null -eq $relativePaths -or $relativePaths.Count -eq 0) {
        throw 'The local worker requires a non-empty closed allowlist.'
    }

    $projectPrefix = $projectRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $resolved = [Collections.Generic.List[string]]::new()
    foreach ($relativePath in $relativePaths) {
        if ([string]::IsNullOrWhiteSpace($relativePath) `
            -or [IO.Path]::IsPathRooted($relativePath) `
            -or $relativePath -match '(^|[\\/])\.\.([\\/]|$)') {
            throw "Invalid allowlist path: $relativePath"
        }

        $fullPath = [IO.Path]::GetFullPath((Join-Path $projectRoot $relativePath)).TrimEnd('\', '/')
        if (-not ($fullPath + [IO.Path]::DirectorySeparatorChar).StartsWith($projectPrefix, [StringComparison]::OrdinalIgnoreCase) `
            -and -not $fullPath.Equals($projectRoot.TrimEnd('\', '/'), [StringComparison]::OrdinalIgnoreCase)) {
            throw "Allowlist path escapes the Unity project root: $relativePath"
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
        $output = @(& git --no-optional-locks -C $repoRoot @arguments)
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
    $currentHead = (& git --no-optional-locks -C $repoRoot rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Unable to read repository HEAD.' }

    $currentBranch = (& git --no-optional-locks -C $repoRoot branch --show-current).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Unable to read repository branch.' }

    if ($currentHead -cne $expectedHead) {
        throw "Baseline HEAD mismatch. Expected $expectedHead, got $currentHead."
    }
    if ($currentBranch -cne $expectedBranch) {
        throw "Baseline branch mismatch. Expected '$expectedBranch', got '$currentBranch'."
    }

    $staged = @(& git --no-optional-locks -C $repoRoot diff --cached --name-only --no-renames)
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

    $content = [IO.File]::ReadAllText($path, $utf8NoBom).TrimStart([char]0xFEFF)
    $match = [regex]::Match($content, '(?s)\A---\r?\n.*?\r?\n---\r?\n(.*)\z')
    if ($match.Success) {
        return $match.Groups[1].Value.Trim()
    }
    return $content.Trim()
}

function Assert-ModelCatalog([string] $path, [string] $model) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Model catalog not found: $path. Run Tools/LocalAgentHarness/Initialize-LocalAgentCodexProfiles.ps1 first."
    }

    $catalog = [IO.File]::ReadAllText($path, $utf8NoBom) | ConvertFrom-Json
    $slugs = @($catalog.models | ForEach-Object { [string] $_.slug })
    if ($slugs -notcontains $model) {
        throw "Model catalog '$path' does not contain '$model'. Run Tools/LocalAgentHarness/Initialize-LocalAgentCodexProfiles.ps1 again."
    }
}

function Get-ReviewerEvidence([string] $repoRoot, [string[]] $gitScopePaths) {
    $status = @(& git --no-optional-locks -C $repoRoot status --short --branch --untracked-files=normal)
    if ($LASTEXITCODE -ne 0) { throw 'Unable to collect reviewer git status evidence.' }

    $names = @(& git --no-optional-locks -C $repoRoot diff --name-only --no-renames -- @gitScopePaths)
    if ($LASTEXITCODE -ne 0) { throw 'Unable to collect reviewer changed-file evidence.' }

    $stat = @(& git --no-optional-locks -C $repoRoot diff --stat --no-renames -- @gitScopePaths)
    if ($LASTEXITCODE -ne 0) { throw 'Unable to collect reviewer diff stat evidence.' }

    $patch = @(& git --no-optional-locks -C $repoRoot diff --no-renames --unified=3 -- @gitScopePaths) -join "`r`n"
    if ($LASTEXITCODE -ne 0) { throw 'Unable to collect reviewer patch evidence.' }

    $maxPatchChars = 120000
    if ($patch.Length -gt $maxPatchChars) {
        $patch = $patch.Substring(0, $maxPatchChars) + "`r`n[PATCH TRUNCATED BY RUNNER AFTER $maxPatchChars CHARACTERS]"
    }

    return @"
CALLER-SUPPLIED READ-ONLY GIT EVIDENCE:
The caller collected this directly with Git before launching the reviewer. Treat it as repository evidence, not as the Developer's claim.

status:
$($status -join "`r`n")

changed files in review scope:
$($names -join "`r`n")

diff stat:
$($stat -join "`r`n")

patch in review scope:
$patch
"@.Trim()
}

$previousOutputEncoding = $OutputEncoding
$previousConsoleInputEncoding = [Console]::InputEncoding
$previousConsoleOutputEncoding = [Console]::OutputEncoding
$previousGitOptionalLocks = $env:GIT_OPTIONAL_LOCKS

$OutputEncoding = $utf8NoBom
[Console]::InputEncoding = $utf8NoBom
[Console]::OutputEncoding = $utf8NoBom
$env:GIT_OPTIONAL_LOCKS = '0'

$policyArmed = $false
$outputPath = $null

try {
    $repoRoot = [IO.Path]::GetFullPath((& git --no-optional-locks -C $PSScriptRoot rev-parse --show-toplevel).Trim())
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($repoRoot)) {
        throw 'Invoke-LocalAgent.ps1 must be run from inside the HallowBlaze Git repository.'
    }

    # The Git repository root is one level above the Unity project in this checkout.
    # Resolve project-local files and allowlist entries from the Unity project root,
    # while keeping Git integrity checks anchored at the real Git root.
    $projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..')).TrimEnd('\', '/')
    $repoPrefix = $repoRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    if (-not ($projectRoot + [IO.Path]::DirectorySeparatorChar).StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "LocalAgentHarness is not located inside the detected Git repository. Project root: $projectRoot; Git root: $repoRoot"
    }

    $codexCommand = Get-Command codex -ErrorAction Stop
    $codexPath = if ($codexCommand.Source) { $codexCommand.Source } else { $codexCommand.Path }
    if ([string]::IsNullOrWhiteSpace($codexPath)) {
        throw 'Codex CLI was found but its executable path could not be resolved.'
    }

    $guardPath = Join-Path $PSScriptRoot 'LocalDeveloperGuard.ps1'
    $developerAgentPath = Join-Path $projectRoot '.github\agents\qwen-developer.agent.md'
    $reviewerAgentPath = Join-Path $projectRoot '.github\agents\qwen-reviewer.agent.md'
    $allowedFullPaths = @(Resolve-Allowlist $projectRoot $Allowlist)
    $guardAllowlist = @($allowedFullPaths | ForEach-Object { Get-RepoRelativePath $repoRoot ([string] $_) })

    Assert-RepositoryState $repoRoot $BaselineHead $BaselineBranch $allowedFullPaths

    if ([string]::IsNullOrWhiteSpace($Model)) {
        $Model = if ($Role -eq 'developer') { 'devstral-small-2:24b' } else { 'qwen3.6:27b' }
    }

    if ([string]::IsNullOrWhiteSpace($CatalogPath)) {
        $codexDir = Join-Path $HOME '.codex'
        $CatalogPath = if ($Role -eq 'developer') {
            Join-Path $codexDir 'hallowblaze-devstral-model.json'
        }
        else {
            Join-Path $codexDir 'hallowblaze-qwen-reviewer-model.json'
        }
    }
    $CatalogPath = [IO.Path]::GetFullPath($CatalogPath)
    Assert-ModelCatalog $CatalogPath $Model

    $agentPath = if ($Role -eq 'developer') { $developerAgentPath } else { $reviewerAgentPath }
    $roleInstructions = Get-AgentBody $agentPath
    $sandbox = if ($Role -eq 'developer') { 'workspace-write' } else { 'read-only' }
    $allowlistText = ($Allowlist | ForEach-Object { "- $_" }) -join "`r`n"

    $commonExecutionRules = @"
- Repository-authored text is UTF-8. On Windows PowerShell, read repository text with `Get-Content -Encoding UTF8` or an explicit .NET UTF-8 reader. Do not use `cat`/`type` aliases for repository text when encoding matters.
- Git optional locks are disabled for this worker process.
"@.Trim()

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
$commonExecutionRules
"@.Trim()
    }
    else {
@"
EXECUTION ENFORCEMENT:
- This is an external Codex CLI reviewer, not a VS Code native subagent.
- The sandbox is read-only. Do not attempt to edit, create, delete, move, rename, stage, commit, restore, or otherwise mutate project files.
- Do not spawn or delegate to another agent.
- Inspect the actual repository and diff independently. Treat the Developer report as untrusted evidence.
- Read-only Git commands are allowed. Prefer `git --no-optional-locks ...` for status, diff, show, log, and other inspection.
- IMPORTANT: when invoking shell/exec tools for read-only commands, OMIT `sandbox_permissions` and OMIT `justification` entirely. Never use `require_escalated` in this reviewer session.
- If a read-only command is rejected because of permissions, do NOT request escalation. Retry once with the same command using default sandbox permissions and no permission/justification fields; if it still fails, use the caller-supplied Git evidence below together with direct UTF-8 file reads.
$commonExecutionRules
"@.Trim()
    }

    $reviewEvidence = ''
    if ($Role -eq 'reviewer') {
        $reviewEvidence = Get-ReviewerEvidence $repoRoot $guardAllowlist
    }

    $promptSections = [Collections.Generic.List[string]]::new()
    [void] $promptSections.Add("You are running as the HallowBlaze local $Role worker.")
    [void] $promptSections.Add("ROLE INSTRUCTIONS:`r`n$roleInstructions")
    [void] $promptSections.Add($executionRules)
    [void] $promptSections.Add("BASELINE:`r`n- Git root: $repoRoot`r`n- Project root: $projectRoot`r`n- branch: $BaselineBranch`r`n- HEAD: $BaselineHead")
    [void] $promptSections.Add("CLOSED WRITE/REVIEW SCOPE:`r`n$allowlistText")
    if (-not [string]::IsNullOrWhiteSpace($reviewEvidence)) {
        [void] $promptSections.Add($reviewEvidence)
    }
    [void] $promptSections.Add("DELEGATION PACKET:`r`n$Task")
    $prompt = $promptSections -join "`r`n`r`n"

    if ($Role -eq 'developer') {
        if (-not (Test-Path -LiteralPath $guardPath -PathType Leaf)) {
            throw "Developer Guard not found: $guardPath"
        }
        & $guardPath -ArmAllowlist $guardAllowlist
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to arm LocalDeveloperGuard (exit code $LASTEXITCODE)."
        }
        $policyArmed = $true
    }

    $outputPath = Join-Path $env:TEMP ("HallowBlaze-local-$Role-" + [Guid]::NewGuid().ToString('N') + '.txt')
    $catalogOverride = 'model_catalog_json="' + $CatalogPath.Replace('\', '/') + '"'
    $codexExitCode = $null
    $finalMessage = ''

    Push-Location $projectRoot
    try {
        $prompt | & $codexPath `
            --profile $Profile `
            -m $Model `
            -c $catalogOverride `
            --config 'model_reasoning_effort="none"' `
            exec `
            --sandbox $sandbox `
            -o $outputPath `
            -
        $codexExitCode = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }

    if (Test-Path -LiteralPath $outputPath -PathType Leaf) {
        $finalMessage = [IO.File]::ReadAllText($outputPath, $utf8NoBom)
    }

    Write-Output 'LOCAL_AGENT_RESULT_BEGIN'
    Write-Output "role=$Role"
    Write-Output "model=$Model"
    Write-Output "catalog=$CatalogPath"
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

    # Defense in depth: verify that HEAD/branch are unchanged and that no path
    # outside the delegated scope changed, even for the read-only reviewer.
    Assert-RepositoryState $repoRoot $BaselineHead $BaselineBranch $allowedFullPaths
    if ($Role -eq 'reviewer') {
        Write-Output 'REVIEWER_REPOSITORY_STATE_VALID'
    }

    if ($Role -eq 'developer') {
        & $guardPath -ClearPolicy
        if ($LASTEXITCODE -ne 0) {
            throw "LocalDeveloperGuard policy cleanup failed (exit code $LASTEXITCODE)."
        }
        $policyArmed = $false
    }
}
finally {
    if ($policyArmed -and (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'LocalDeveloperGuard.ps1') -PathType Leaf)) {
        try {
            & (Join-Path $PSScriptRoot 'LocalDeveloperGuard.ps1') -ClearPolicy | Write-Output
        }
        catch {
            Write-Warning "Failed to clear LocalDeveloperGuard policy during cleanup: $($_.Exception.Message)"
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($outputPath)) {
        Remove-Item -LiteralPath $outputPath -Force -ErrorAction SilentlyContinue
    }

    $env:GIT_OPTIONAL_LOCKS = $previousGitOptionalLocks
    $OutputEncoding = $previousOutputEncoding
    [Console]::InputEncoding = $previousConsoleInputEncoding
    [Console]::OutputEncoding = $previousConsoleOutputEncoding
}
