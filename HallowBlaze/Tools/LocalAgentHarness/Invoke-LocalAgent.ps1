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
    [string] $CatalogPath,
    [switch] $RequirePatch
)

$ErrorActionPreference = 'Stop'
$utf8NoBom = [Text.UTF8Encoding]::new($false)
$pathTrimChars = [char[]] @([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)

function Remove-TrailingDirectorySeparators([string] $path) {
    if ([string]::IsNullOrWhiteSpace($path)) { return $path }

    $root = [IO.Path]::GetPathRoot($path)
    if (-not [string]::IsNullOrWhiteSpace($root) `
        -and $path.Equals($root, [StringComparison]::OrdinalIgnoreCase)) {
        return $path
    }

    # Pass one explicit Char[] argument. This avoids Windows PowerShell 5.1
    # overload-binding failures from String.TrimEnd('\\', '/').
    return $path.TrimEnd($script:pathTrimChars)
}

function Get-NormalizedFullPath([string] $path) {
    return (Remove-TrailingDirectorySeparators ([IO.Path]::GetFullPath($path)))
}

function Get-RepoRelativePath([string] $repoRoot, [string] $fullPath) {
    $root = Remove-TrailingDirectorySeparators $repoRoot
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

    $projectPrefix = (Remove-TrailingDirectorySeparators $projectRoot) + [IO.Path]::DirectorySeparatorChar
    $resolved = [Collections.Generic.List[string]]::new()
    foreach ($relativePath in $relativePaths) {
        if ([string]::IsNullOrWhiteSpace($relativePath) `
            -or [IO.Path]::IsPathRooted($relativePath) `
            -or $relativePath -match '(^|[\\/])\.\.([\\/]|$)') {
            throw "Invalid allowlist path: $relativePath"
        }

        $fullPath = Get-NormalizedFullPath (Join-Path $projectRoot $relativePath)
        if (-not ($fullPath + [IO.Path]::DirectorySeparatorChar).StartsWith($projectPrefix, [StringComparison]::OrdinalIgnoreCase) `
            -and -not $fullPath.Equals((Remove-TrailingDirectorySeparators $projectRoot), [StringComparison]::OrdinalIgnoreCase)) {
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
            $fullPath = Get-NormalizedFullPath (Join-Path $repoRoot ([string] $relativePath))
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

function Get-DeveloperPatchBlock([string] $finalMessage) {
    $match = [regex]::Match($finalMessage, '(?s)DEVELOPER_PATCH_BEGIN\r?\n(.*?)\r?\nDEVELOPER_PATCH_END')
    if (-not $match.Success) {
        return [pscustomobject]@{ Present = $false; Patch = $null }
    }

    $patch = $match.Groups[1].Value.Trim()
    if ($patch.StartsWith('```')) {
        $lines = @($patch -split '\r?\n')
        if ($lines.Count -ge 2 -and $lines[0] -match '^```' -and $lines[-1] -match '^```\s*$') {
            $patch = ($lines[1..($lines.Count - 2)] -join "`r`n").Trim()
        }
    }
    return [pscustomobject]@{ Present = $true; Patch = $patch }
}

function Get-DeveloperSummary([string] $finalMessage) {
    return ([regex]::Replace(
        $finalMessage,
        '(?s)DEVELOPER_PATCH_BEGIN\r?\n.*?\r?\nDEVELOPER_PATCH_END',
        '[PATCH OMITTED BY RUNNER; VALIDATED AND APPLIED IF PRESENT]'
    )).Trim()
}

function Get-PatchPaths([string] $repoRoot, [string] $patchPath) {
    $output = @(& git -c core.quotePath=false -C $repoRoot apply --numstat --recount -- $patchPath)
    if ($LASTEXITCODE -ne 0) {
        throw 'Developer patch could not be parsed by git apply --numstat.'
    }

    $paths = [Collections.Generic.List[string]]::new()
    foreach ($line in $output) {
        if ([string]::IsNullOrWhiteSpace([string] $line)) { continue }
        $parts = ([string] $line) -split "`t"
        if ($parts.Count -lt 3) {
            throw "Unexpected git apply --numstat output: $line"
        }
        $relativePath = [string] $parts[-1]
        if ($relativePath -match '\{.*=>.*\}') {
            throw 'Rename/move patches are not supported by the local-worker patch protocol. Delegate them as a separate explicitly authorized operation.'
        }
        $fullPath = Get-NormalizedFullPath (Join-Path $repoRoot $relativePath)
        if (-not $paths.Contains($fullPath)) {
            [void] $paths.Add($fullPath)
        }
    }
    return @($paths)
}

function Assert-PatchScope([string] $repoRoot, [string] $patchPath, [string[]] $allowedFullPaths) {
    $outside = [Collections.Generic.List[string]]::new()
    $patchPaths = @(Get-PatchPaths $repoRoot $patchPath)
    if ($patchPaths.Count -eq 0) {
        throw 'Developer returned a non-empty patch that contains no file changes.'
    }

    foreach ($changedPath in $patchPaths) {
        $isAllowed = $allowedFullPaths | Where-Object {
            ([string] $_).Equals([string] $changedPath, [StringComparison]::OrdinalIgnoreCase)
        } | Select-Object -First 1
        if ($null -eq $isAllowed) {
            [void] $outside.Add((Get-RepoRelativePath $repoRoot ([string] $changedPath)))
        }
    }
    if ($outside.Count -gt 0) {
        throw "Developer patch targets paths outside the delegated allowlist: $($outside -join ', ')"
    }

    return $patchPaths
}

function Get-TranscriptTail([string] $path, [int] $maxLines = 30) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return '' }
    $lines = @(Get-Content -LiteralPath $path -Encoding UTF8)
    if ($lines.Count -le $maxLines) { return ($lines -join "`r`n") }
    return ($lines[($lines.Count - $maxLines)..($lines.Count - 1)] -join "`r`n")
}

$previousOutputEncoding = $OutputEncoding
$previousConsoleInputEncoding = [Console]::InputEncoding
$previousConsoleOutputEncoding = [Console]::OutputEncoding
$previousGitOptionalLocks = $env:GIT_OPTIONAL_LOCKS
$previousHome = $env:HOME
$previousCodexHome = $env:CODEX_HOME

$OutputEncoding = $utf8NoBom
[Console]::InputEncoding = $utf8NoBom
[Console]::OutputEncoding = $utf8NoBom
$env:GIT_OPTIONAL_LOCKS = '0'

$policyArmed = $false
$outputPath = $null
$workerLogPath = $null
$patchPath = $null
$keepWorkerLog = $false

try {
    $repoRoot = [IO.Path]::GetFullPath((& git --no-optional-locks -C $PSScriptRoot rev-parse --show-toplevel).Trim())
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($repoRoot)) {
        throw 'Invoke-LocalAgent.ps1 must be run from inside the HallowBlaze Git repository.'
    }

    $projectRoot = Get-NormalizedFullPath (Join-Path $PSScriptRoot '..\..')
    $repoPrefix = (Remove-TrailingDirectorySeparators $repoRoot) + [IO.Path]::DirectorySeparatorChar
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

    # Agent Host / checkpoint-restored PowerShell processes can have HOME unset even
    # when USERPROFILE is present. Codex CLI uses CODEX_HOME for its user-level
    # config/profile/auth/state, so make that location explicit for the child process.
    $userProfile = [Environment]::GetEnvironmentVariable('USERPROFILE', 'Process')
    if ([string]::IsNullOrWhiteSpace($userProfile)) {
        $homeDrive = [Environment]::GetEnvironmentVariable('HOMEDRIVE', 'Process')
        $homePath = [Environment]::GetEnvironmentVariable('HOMEPATH', 'Process')
        if (-not [string]::IsNullOrWhiteSpace($homeDrive) -and -not [string]::IsNullOrWhiteSpace($homePath)) {
            $userProfile = $homeDrive + $homePath
        }
    }
    if ([string]::IsNullOrWhiteSpace($userProfile)) {
        throw 'Unable to resolve the Windows user profile for Codex CLI. USERPROFILE and HOMEDRIVE/HOMEPATH are unavailable.'
    }
    $userProfile = Get-NormalizedFullPath $userProfile

    $codexHome = [Environment]::GetEnvironmentVariable('CODEX_HOME', 'Process')
    if ([string]::IsNullOrWhiteSpace($codexHome)) {
        $codexHome = Join-Path $userProfile '.codex'
    }
    $codexHome = Get-NormalizedFullPath $codexHome
    if (-not (Test-Path -LiteralPath $codexHome -PathType Container)) {
        throw "Codex home not found: $codexHome. Run Codex CLI once interactively or Initialize-LocalAgentCodexProfiles.ps1 from your normal terminal."
    }

    # Normalize the environment only for this runner process and its Codex child.
    # We restore the previous values in finally.
    $env:HOME = $userProfile
    $env:CODEX_HOME = $codexHome

    if ([string]::IsNullOrWhiteSpace($CatalogPath)) {
        $CatalogPath = if ($Role -eq 'developer') {
            Join-Path $codexHome 'hallowblaze-devstral-model.json'
        }
        else {
            Join-Path $codexHome 'hallowblaze-qwen-reviewer-model.json'
        }
    }
    $CatalogPath = [IO.Path]::GetFullPath($CatalogPath)
    Assert-ModelCatalog $CatalogPath $Model

    $agentPath = if ($Role -eq 'developer') { $developerAgentPath } else { $reviewerAgentPath }
    $roleInstructions = Get-AgentBody $agentPath
    $sandbox = 'read-only'
    $allowlistText = ($Allowlist | ForEach-Object { "- $_" }) -join "`r`n"

    $commonExecutionRules = @"
- Repository-authored text is UTF-8. On Windows PowerShell, read repository text with `Get-Content -Encoding UTF8` or an explicit .NET UTF-8 reader. Do not use `cat`/`type` aliases for repository text when encoding matters.
- Git optional locks are disabled for this worker process.
"@.Trim()

    $executionRules = if ($Role -eq 'developer') {
@"
EXECUTION ENFORCEMENT:
- This is an external Codex CLI Developer running in a READ-ONLY sandbox.
- Do NOT attempt to edit, create, delete, move, rename, stage, or commit project files yourself.
- Do NOT call native apply_patch/edit tools and do NOT create patch.diff or any other project file.
- Inspect the repository and design the exact change. If a file change is required, return ONE NON-EMPTY git-compatible unified patch in your final response.
- If no file change is required, DO NOT emit an empty DEVELOPER_PATCH block. Emit a line containing exactly DEVELOPER_NO_PATCH instead.
- The runner, not you, is the only writer. It validates the patch against the closed allowlist and applies it with git apply after your process exits.
- Patch paths MUST be Git-root-relative, for example `a/HallowBlaze/Docs/Foo.md` and `b/HallowBlaze/Docs/Foo.md`.
- Do not emit rename/move patches. If the task requires a rename/move/delete that you cannot represent safely, return SCOPE_CHANGE_REQUIRED instead.
- Do not run builds/tests after the proposed patch because your patch has not been applied yet. The Lead owns post-apply validation.
- Do not spawn or delegate to another agent.
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

    $outputPath = Join-Path $env:TEMP ("HallowBlaze-local-$Role-final-" + [Guid]::NewGuid().ToString('N') + '.txt')
    $workerLogPath = Join-Path $env:TEMP ("HallowBlaze-local-$Role-log-" + [Guid]::NewGuid().ToString('N') + '.txt')
    $catalogOverride = 'model_catalog_json="' + $CatalogPath.Replace('\', '/') + '"'
    $codexExitCode = $null
    $finalMessage = ''

    Push-Location $projectRoot
    try {
        # Windows PowerShell 5.1 turns native stderr records into PowerShell errors.
        # Codex writes normal banner/progress output to stderr even when it succeeds,
        # so the script-wide ErrorActionPreference='Stop' must not wrap this native call.
        $previousErrorActionPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            $workerOutput = @(
                $prompt | & $codexPath `
                    --profile $Profile `
                    -m $Model `
                    -c $catalogOverride `
                    --config 'model_reasoning_effort="none"' `
                    exec `
                    --sandbox $sandbox `
                    -o $outputPath `
                    - 2>&1
            )
            $codexExitCode = $LASTEXITCODE
        }
        finally {
            $ErrorActionPreference = $previousErrorActionPreference
        }

        [IO.File]::WriteAllLines(
            $workerLogPath,
            @($workerOutput | ForEach-Object { [string] $_ }),
            $utf8NoBom
        )
    }
    finally {
        Pop-Location
    }

    if (Test-Path -LiteralPath $outputPath -PathType Leaf) {
        $finalMessage = [IO.File]::ReadAllText($outputPath, $utf8NoBom)
    }

    if ($codexExitCode -ne 0) {
        $keepWorkerLog = $true
        $tail = Get-TranscriptTail $workerLogPath 30
        Write-Output 'LOCAL_AGENT_FAILURE'
        Write-Output "role=$Role"
        Write-Output "model=$Model"
        Write-Output "codex_exit_code=$codexExitCode"
        Write-Output "worker_log=$workerLogPath"
        if (-not [string]::IsNullOrWhiteSpace($tail)) {
            Write-Output 'worker_log_tail:'
            Write-Output $tail
        }
        throw "Codex local $Role exited with code $codexExitCode."
    }

    $patchApplied = $false
    $appliedPaths = @()
    if ($Role -eq 'developer') {
        $patchBlock = Get-DeveloperPatchBlock $finalMessage
        $patch = $patchBlock.Patch
        $hasExplicitNoPatch = $finalMessage -match '(?m)^DEVELOPER_NO_PATCH\s*$'
        $hasBlocker = $finalMessage -match '(?m)^(BASELINE_REQUIRED|SCOPE_CHANGE_REQUIRED|ARCHITECTURE_DECISION_REQUIRED|BLOCKED)\s*$'

        if ($patchBlock.Present -and -not [string]::IsNullOrWhiteSpace($patch)) {
            $patchPath = Join-Path $env:TEMP ("HallowBlaze-developer-patch-" + [Guid]::NewGuid().ToString('N') + '.diff')
            [IO.File]::WriteAllText($patchPath, $patch + "`r`n", $utf8NoBom)

            $appliedPaths = @(Assert-PatchScope $repoRoot $patchPath $allowedFullPaths)

            & git --no-optional-locks -C $repoRoot apply --check --recount --whitespace=nowarn -- $patchPath
            if ($LASTEXITCODE -ne 0) {
                $keepWorkerLog = $true
                throw "LOCAL_WORKER_PROTOCOL_ERROR: Developer returned a patch that git apply --check rejected. worker_log=$workerLogPath"
            }

            if (-not (Test-Path -LiteralPath $guardPath -PathType Leaf)) {
                throw "Developer Guard not found: $guardPath"
            }
            & $guardPath -ArmAllowlist $guardAllowlist
            if ($LASTEXITCODE -ne 0) {
                throw "Failed to arm LocalDeveloperGuard (exit code $LASTEXITCODE)."
            }
            $policyArmed = $true

            & git --no-optional-locks -C $repoRoot apply --recount --whitespace=nowarn -- $patchPath
            if ($LASTEXITCODE -ne 0) {
                throw 'LOCAL_WORKER_PROTOCOL_ERROR: Validated developer patch failed during git apply.'
            }
            $patchApplied = $true

            & $guardPath -ValidateAllowlist
            if ($LASTEXITCODE -ne 0) {
                throw "LocalDeveloperGuard integrity validation failed (exit code $LASTEXITCODE)."
            }

            & $guardPath -ClearPolicy
            if ($LASTEXITCODE -ne 0) {
                throw "LocalDeveloperGuard policy cleanup failed (exit code $LASTEXITCODE)."
            }
            $policyArmed = $false
        }
        elseif ($patchBlock.Present -and [string]::IsNullOrWhiteSpace($patch)) {
            # Some local models express "no change" as an empty patch block even when the
            # role protocol asks for DEVELOPER_NO_PATCH. Normalize that benign variant for
            # read-only/smoke tasks, but never accept it when the caller requires a mutation.
            if ($RequirePatch) {
                $keepWorkerLog = $true
                throw "LOCAL_DEVELOPER_NO_PATCH: Developer returned an empty patch block but this invocation requires a non-empty patch. worker_log=$workerLogPath"
            }
            $hasExplicitNoPatch = $true
        }
        elseif (-not $hasExplicitNoPatch -and -not $hasBlocker) {
            if ($RequirePatch) {
                $keepWorkerLog = $true
                throw "LOCAL_DEVELOPER_NO_PATCH: This invocation requires a repository mutation, but the Developer returned no usable patch. worker_log=$workerLogPath"
            }

            # For explicitly read-only/no-change invocations, Codex-compatible local models may
            # finish with a plain-language completion instead of the optional DEVELOPER_NO_PATCH
            # marker. Treat a successful process with no non-empty patch as an implicit no-change
            # result. Repository integrity is still verified below before reporting success.
            $hasExplicitNoPatch = $true
        }

        if ($RequirePatch -and -not $patchApplied -and -not $hasBlocker) {
            $keepWorkerLog = $true
            throw "LOCAL_DEVELOPER_NO_PATCH: This invocation requires a repository mutation, but the Developer produced no non-empty patch. worker_log=$workerLogPath"
        }
    }

    Assert-RepositoryState $repoRoot $BaselineHead $BaselineBranch $allowedFullPaths

    $finalForCaller = if ($Role -eq 'developer') { Get-DeveloperSummary $finalMessage } else { $finalMessage.Trim() }

    Write-Output 'LOCAL_AGENT_RESULT_BEGIN'
    Write-Output "role=$Role"
    Write-Output "model=$Model"
    Write-Output "catalog=$CatalogPath"
    Write-Output "codex_exit_code=$codexExitCode"
    if ($Role -eq 'developer') {
        Write-Output "patch_applied=$($patchApplied.ToString().ToLowerInvariant())"
        Write-Output "require_patch=$($RequirePatch.IsPresent.ToString().ToLowerInvariant())"
        if ($patchApplied) {
            Write-Output ('applied_paths=' + (($appliedPaths | ForEach-Object { Get-RepoRelativePath $repoRoot ([string] $_) }) -join ','))
        }
    }
    Write-Output 'final_message:'
    Write-Output $finalForCaller
    Write-Output 'LOCAL_AGENT_RESULT_END'

    if ($Role -eq 'developer') {
        Write-Output 'ALLOWLIST_VALID'
        $changed = @(Get-ChangedProjectPaths $repoRoot | ForEach-Object { Get-RepoRelativePath $repoRoot ([string] $_) })
        Write-Output ('CHANGED_PATHS=' + ($changed -join ','))
        Write-Output 'WRITE_POLICY_CLEARED'
    }
    else {
        Write-Output 'REVIEWER_REPOSITORY_STATE_VALID'
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
    if (-not [string]::IsNullOrWhiteSpace($patchPath)) {
        Remove-Item -LiteralPath $patchPath -Force -ErrorAction SilentlyContinue
    }
    if (-not [string]::IsNullOrWhiteSpace($workerLogPath) -and -not $keepWorkerLog) {
        Remove-Item -LiteralPath $workerLogPath -Force -ErrorAction SilentlyContinue
    }

    $env:GIT_OPTIONAL_LOCKS = $previousGitOptionalLocks
$env:HOME = $previousHome
$env:CODEX_HOME = $previousCodexHome
    $OutputEncoding = $previousOutputEncoding
    [Console]::InputEncoding = $previousConsoleInputEncoding
    [Console]::OutputEncoding = $previousConsoleOutputEncoding
}
