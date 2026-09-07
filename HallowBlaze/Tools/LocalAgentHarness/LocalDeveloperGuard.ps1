param(
    [string] $ArmSnapshot
)

$ErrorActionPreference = 'Stop'

$runtimeRoot = Join-Path $env:LOCALAPPDATA 'HallowBlaze\LocalAgentHarness'
$stateDirectory = if ($env:HALLOWBLAZE_GUARD_STATE_DIRECTORY) {
    $env:HALLOWBLAZE_GUARD_STATE_DIRECTORY
}
else {
    Join-Path $runtimeRoot 'state'
}
$statePath = Join-Path $stateDirectory 'active-session.json'
$policyPath = Join-Path $stateDirectory 'write-policy.json'
$repoRoot = if ($env:HALLOWBLAZE_GUARD_REPO_ROOT) {
    [IO.Path]::GetFullPath($env:HALLOWBLAZE_GUARD_REPO_ROOT)
}
else {
    [IO.Path]::GetFullPath((& git -C $PSScriptRoot rev-parse --show-toplevel).Trim())
}
$targetAgent = 'qwen-developer'
$maxToolCalls = 30
$maxNoProgressOutcomes = 3
$taskLimit = [TimeSpan]::FromMinutes(10)
$mutexName = 'Local\HallowBlazeLocalDeveloperGuard'

function Write-HookOutput([object] $value) {
    [Console]::Out.Write(($value | ConvertTo-Json -Compress -Depth 30))
}

function Get-ObjectProperty([object] $value, [string] $name) {
    if ($null -eq $value) { return $null }
    $property = $value.PSObject.Properties[$name]
    if ($null -eq $property) { return $null }
    return $property.Value
}

function Get-TextHash([string] $value) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $bytes = [Text.Encoding]::UTF8.GetBytes($value)
        return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

function Read-JsonFile([string] $path) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }
    return Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
}

function Write-JsonFile([string] $path, [object] $value) {
    New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force | Out-Null
    $temporaryPath = "$path.$PID.tmp"
    [IO.File]::WriteAllText(
        $temporaryPath,
        ($value | ConvertTo-Json -Depth 30),
        [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temporaryPath -Destination $path -Force
}

function Get-AllowlistFingerprint([object[]] $paths) {
    $parts = [Collections.Generic.List[string]]::new()
    $normalizedPaths = @(
        $paths |
            ForEach-Object { [string] $_ } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            Sort-Object
    )
    foreach ($path in $normalizedPaths) {
        [void] $parts.Add($path)
        if (Test-Path -LiteralPath $path -PathType Leaf) {
            [void] $parts.Add((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant())
        }
        else {
            [void] $parts.Add('missing')
        }
    }
    return Get-TextHash ([string]::Join("`n", $parts))
}

function Resolve-ToolPath([string] $candidatePath) {
    if ([string]::IsNullOrWhiteSpace($candidatePath) -or $candidatePath -match '[\*\?\[\]]') {
        return $null
    }
    try {
        if ($candidatePath -match '^(?i)file:') {
            return ([Uri] $candidatePath).LocalPath.TrimEnd('\', '/')
        }
        if ([IO.Path]::IsPathRooted($candidatePath)) {
            return [IO.Path]::GetFullPath($candidatePath).TrimEnd('\', '/')
        }
        return [IO.Path]::GetFullPath((Join-Path $repoRoot $candidatePath)).TrimEnd('\', '/')
    }
    catch {
        return $null
    }
}

function Get-ToolCandidatePaths([object] $value, [int] $depth = 0) {
    $result = [Collections.Generic.List[string]]::new()
    if ($null -eq $value -or $depth -gt 8 -or $value -is [string]) {
        return @($result)
    }
    foreach ($property in $value.PSObject.Properties) {
        if ($property.Name -match '(?i)^(file_?path|path|uri|target_?path|old_?path|new_?path)$' `
            -and $property.Value -is [string]) {
            [void] $result.Add([string] $property.Value)
        }
        elseif ($null -ne $property.Value -and -not ($property.Value -is [string])) {
            foreach ($nestedPath in @(Get-ToolCandidatePaths $property.Value ($depth + 1))) {
                [void] $result.Add([string] $nestedPath)
            }
        }
    }
    return @($result)
}

function Get-PatchOperations([object] $toolInput) {
    $patch = [string] (Get-ObjectProperty $toolInput 'input')
    if (-not $patch) { return @() }
    return @(
        [regex]::Matches($patch, '(?m)^\*\*\* (Add|Update|Delete) File: (.+)$') |
            ForEach-Object {
                [PSCustomObject]@{
                    Action = $_.Groups[1].Value
                    Path = $_.Groups[2].Value.Trim()
                }
            }
    )
}

function Test-FileMutationTool([string] $toolName) {
    return $toolName -match '(?i)(^|[_-])(create|edit|insert|replace|patch|write|delete|move|rename)([_-]|$)'
}

function Test-ExecutionTool([string] $toolName) {
    return $toolName -match '(?i)(terminal|execute|shell|powershell|bash|runcommand|run[_-]?task)'
}

function Test-DangerousTerminalCommand([string] $command) {
    if ([string]::IsNullOrWhiteSpace($command)) { return $false }
    $patterns = @(
        '(?i)(^|[;&|]\s*)(del|erase|rm|rmdir|rd|move|ren|rename|copy|cp|mv|touch|tee)\b',
        '(?i)\b(Remove-Item|Move-Item|Rename-Item|Copy-Item|Set-Content|Add-Content|Out-File|New-Item|Tee-Object)\b',
        '(?i)\[\s*(IO|System\.IO)\.File\s*\]::\s*(Write|Delete|Move|Replace|Copy)',
        '(?i)\bFile\s*\.\s*(Write|Delete|Move|Replace|Copy)',
        '>(?![>&])'
    )
    foreach ($pattern in $patterns) {
        if ($command -match $pattern) { return $true }
    }

    $readOnlyGitCommands = @('status', 'diff', 'show', 'log', 'blame', 'rev-parse', 'ls-files')
    $gitMatches = [regex]::Matches($command, '(?i)(?<![\w.-])git(?:\.exe)?(?=\s|$)')
    foreach ($gitMatch in $gitMatches) {
        $tokens = @(
            [regex]::Matches($command.Substring($gitMatch.Index), '(?:"[^"]*"|''[^'']*''|\S+)') |
                ForEach-Object { $_.Value }
        )
        $index = 1
        while ($index -lt $tokens.Count) {
            $token = [string] $tokens[$index]
            if ($token -ceq '-C' `
                -or $token -in @('--git-dir', '--work-tree', '--namespace', '--super-prefix', '--config-env')) {
                if ($index + 1 -ge $tokens.Count) { return $true }
                $index += 2
                continue
            }
            if ($token -match '(?i)^--(git-dir|work-tree|namespace|super-prefix|config-env)=.+$' `
                -or $token -in @('--no-pager', '--paginate', '--no-replace-objects', '--bare', '--literal-pathspecs', '--no-lazy-fetch', '--no-optional-locks')) {
                $index++
                continue
            }
            if ($token.StartsWith('-')) { return $true }
            break
        }
        if ($index -ge $tokens.Count) { return $true }
        $subcommand = ([string] $tokens[$index]).ToLowerInvariant()
        if ($readOnlyGitCommands -notcontains $subcommand `
            -or $command.Substring($gitMatch.Index) -match '(?i)\s--output(?:=|\s)') {
            return $true
        }
    }
    return $false
}

function Deny([string] $reason) {
    Write-HookOutput @{
        hookSpecificOutput = @{
            hookEventName = 'PreToolUse'
            permissionDecision = 'deny'
            permissionDecisionReason = $reason
        }
    }
}

function Allow {
    Write-HookOutput @{
        hookSpecificOutput = @{
            hookEventName = 'PreToolUse'
            permissionDecision = 'allow'
            permissionDecisionReason = 'Local developer guard checks passed.'
        }
    }
}

function Stop-Agent([string] $reason) {
    Write-HookOutput @{
        continue = $false
        stopReason = $reason
        systemMessage = $reason
    }
}

function Remove-BoundPolicy([object] $state) {
    if (-not $state.policyId -or -not (Test-Path -LiteralPath $policyPath -PathType Leaf)) { return }
    try {
        $policy = Read-JsonFile $policyPath
        if ([string] $policy.policyId -ceq [string] $state.policyId `
            -and [string] $policy.consumedSessionId -ceq [string] $state.sessionId) {
            Remove-Item -LiteralPath $policyPath -Force
        }
    }
    catch {
    }
}

function Remove-PolicyForSession([string] $sessionId) {
    if ([string]::IsNullOrWhiteSpace($sessionId) `
        -or -not (Test-Path -LiteralPath $policyPath -PathType Leaf)) { return }
    try {
        $policy = Read-JsonFile $policyPath
        if ([string] $policy.consumedSessionId -ceq $sessionId) {
            Remove-Item -LiteralPath $policyPath -Force
        }
    }
    catch {
    }
}

function Get-PolicyForSession([string] $sessionId) {
    if (-not (Test-Path -LiteralPath $policyPath -PathType Leaf)) { return $null }
    try {
        $policy = Read-JsonFile $policyPath
        $expiresUtc = [DateTimeOffset]::Parse([string] $policy.expiresUtc)
        $policyRoot = [IO.Path]::GetFullPath([string] $policy.gitRoot).TrimEnd('\', '/')
        if ($expiresUtc -le [DateTimeOffset]::UtcNow `
            -or -not $policyRoot.Equals($repoRoot.TrimEnd('\', '/'), [StringComparison]::OrdinalIgnoreCase)) {
            Remove-Item -LiteralPath $policyPath -Force
            return $null
        }

        $consumedSessionId = [string] (Get-ObjectProperty $policy 'consumedSessionId')
        if ($consumedSessionId -and $consumedSessionId -cne $sessionId) { return $null }
        if (-not $consumedSessionId) {
            $policy | Add-Member -NotePropertyName consumedSessionId -NotePropertyValue $sessionId -Force
            Write-JsonFile $policyPath $policy
        }
        return $policy
    }
    catch {
        return $null
    }
}

function Arm-WritePolicy([string] $snapshotPath) {
    $requiredArtifacts = @(
        'manifest.json',
        'status-short-branch.txt',
        'staged-name-status.txt',
        'unstaged-name-status.txt',
        'untracked-paths.txt',
        'staged.patch',
        'unstaged.patch',
        'allowlist-hashes.json'
    )
    foreach ($artifact in $requiredArtifacts) {
        if (-not (Test-Path -LiteralPath (Join-Path $snapshotPath $artifact) -PathType Leaf)) {
            throw "Snapshot artifact is missing: $artifact"
        }
    }

    $manifest = Read-JsonFile (Join-Path $snapshotPath 'manifest.json')
    $snapshotRoot = [IO.Path]::GetFullPath([string] $manifest.GitRoot).TrimEnd('\', '/')
    if (-not $snapshotRoot.Equals($repoRoot.TrimEnd('\', '/'), [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Snapshot Git root does not match this repository.'
    }
    if ([string] $manifest.Writer -cne $targetAgent) {
        throw "Snapshot writer must be $targetAgent."
    }
    if ([string] $manifest.Head -cne (& git -C $repoRoot rev-parse HEAD).Trim() `
        -or [string] $manifest.Branch -cne (& git -C $repoRoot branch --show-current).Trim()) {
        throw 'Snapshot HEAD or branch no longer matches the repository.'
    }

    $allowlist = @($manifest.Allowlist | ForEach-Object { [string] $_ })
    if ($allowlist.Count -eq 0) { throw 'Snapshot allowlist is empty.' }
    $fullPaths = [Collections.Generic.List[string]]::new()
    $repoPrefix = $repoRoot.TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    foreach ($relativePath in $allowlist) {
        if ([IO.Path]::IsPathRooted($relativePath) -or $relativePath -match '(^|[\\/])\.\.([\\/]|$)') {
            throw "Invalid allowlist path: $relativePath"
        }
        $fullPath = [IO.Path]::GetFullPath((Join-Path $repoRoot $relativePath))
        if (-not $fullPath.StartsWith($repoPrefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Allowlist path escapes the repository: $relativePath"
        }
        [void] $fullPaths.Add($fullPath.TrimEnd('\', '/'))
    }

    $policy = [PSCustomObject]@{
        policyId = [Guid]::NewGuid().ToString('N')
        snapshotId = [string] $manifest.SnapshotId
        gitRoot = $repoRoot
        expiresUtc = [DateTimeOffset]::UtcNow.Add($taskLimit).ToString('o')
        allowlistFullPaths = @($fullPaths)
        consumedSessionId = $null
    }
    Write-JsonFile $policyPath $policy
    Write-Output "Write policy armed for $($allowlist.Count) path(s); expires in 10 minutes."
}

if ($ArmSnapshot) {
    $mutex = [Threading.Mutex]::new($false, $mutexName)
    $lockTaken = $false
    try {
        $lockTaken = $mutex.WaitOne(5000)
        if (-not $lockTaken) { throw 'Could not acquire the developer guard lock.' }
        $activeState = Read-JsonFile $statePath
        if ($null -ne $activeState -and $activeState.active -eq $true) {
            throw 'A local developer session is already active.'
        }
        Arm-WritePolicy ([IO.Path]::GetFullPath($ArmSnapshot))
    }
    finally {
        if ($lockTaken) { $mutex.ReleaseMutex() }
        $mutex.Dispose()
    }
    exit 0
}

$rawInput = [Console]::In.ReadToEnd()
if ([string]::IsNullOrWhiteSpace($rawInput)) {
    Write-HookOutput @{}
    exit 0
}

try {
    $payload = $rawInput | ConvertFrom-Json
}
catch {
    Deny 'Local developer guard received invalid hook input.'
    exit 0
}

$eventName = [string] (Get-ObjectProperty $payload 'hook_event_name')
$sessionId = [string] (Get-ObjectProperty $payload 'session_id')
$agentType = [string] (Get-ObjectProperty $payload 'agent_type')
$toolName = [string] (Get-ObjectProperty $payload 'tool_name')
$toolInput = Get-ObjectProperty $payload 'tool_input'
$agentTypeMissing = [string]::IsNullOrWhiteSpace($agentType)
$explicitTargetAgent = -not $agentTypeMissing -and $agentType -ceq $targetAgent

if (-not $agentTypeMissing -and -not $explicitTargetAgent) {
    Write-HookOutput @{}
    exit 0
}

$isRiskyPreTool = $eventName -eq 'PreToolUse' `
    -and ((Test-FileMutationTool $toolName) -or (Test-ExecutionTool $toolName))
$mutex = [Threading.Mutex]::new($false, $mutexName)
$lockTaken = $false

try {
    $lockTaken = $mutex.WaitOne(5000)
    if (-not $lockTaken) {
        $busyStateMatches = $explicitTargetAgent
        if ($agentTypeMissing) {
            try {
                $busyState = Read-JsonFile $statePath
                $busyStateMatches = $null -ne $busyState `
                    -and $busyState.active -eq $true `
                    -and [string] $busyState.agentType -ceq $targetAgent `
                    -and [string] $busyState.sessionId -ceq $sessionId
            }
            catch {
                $busyStateMatches = $true
            }
        }
        if ($isRiskyPreTool -and $busyStateMatches) {
            Deny 'Local developer guard could not acquire its state lock.'
        }
        elseif ($eventName -eq 'SubagentStart' -and $explicitTargetAgent) {
            Stop-Agent 'Another local developer session holds the guard lock.'
        }
        else { Write-HookOutput @{} }
        exit 0
    }

    if ($eventName -eq 'SubagentStart') {
        if (-not $explicitTargetAgent) {
            Write-HookOutput @{}
            exit 0
        }

        try {
            $existingState = Read-JsonFile $statePath
        }
        catch {
            Stop-Agent 'Local developer state is invalid; refusing to overwrite it.'
            exit 0
        }

        if ($null -ne $existingState -and $existingState.active -eq $true) {
            $startedUtc = [DateTimeOffset]::Parse([string] $existingState.startedUtc)
            if ([DateTimeOffset]::UtcNow - $startedUtc -lt $taskLimit) {
                Stop-Agent 'Another qwen-developer session is already active.'
                exit 0
            }
            Remove-BoundPolicy $existingState
            Remove-Item -LiteralPath $statePath -Force
        }

        $policy = Get-PolicyForSession $sessionId
        $allowedPaths = if ($null -eq $policy) { @() } else { @($policy.allowlistFullPaths) }
        $state = [PSCustomObject]@{
            active = $true
            sessionId = $sessionId
            agentType = $targetAgent
            startedUtc = [DateTimeOffset]::UtcNow.ToString('o')
            toolCalls = 0
            noProgressOutcomes = 0
            allowlistFingerprint = Get-AllowlistFingerprint $allowedPaths
            callRecords = @()
            lastOutcomeHash = $null
            pendingAllowlistFingerprint = $null
            writePolicyArmed = $null -ne $policy
            policyId = if ($null -eq $policy) { $null } else { [string] $policy.policyId }
            allowedWritePaths = $allowedPaths
        }
        Write-JsonFile $statePath $state
        $writeContext = if ($state.writePolicyArmed) {
            'Writes are restricted to the snapshot allowlist. Use create_file for new files, native edit for existing files, and never write project files through the shell.'
        }
        else {
            'No write policy is armed. File mutations are blocked; read-only work is available.'
        }
        Write-HookOutput @{
            hookSpecificOutput = @{
                hookEventName = $eventName
                additionalContext = "Budgets: 30 tool calls, 3 no-progress outcomes, 10 minutes. $writeContext"
            }
        }
        exit 0
    }

    if ($eventName -eq 'SubagentStop') {
        $stopState = $null
        $stopStateInvalid = $false
        try {
            $stopState = Read-JsonFile $statePath
        }
        catch {
            $stopStateInvalid = $true
        }

        if ($stopStateInvalid) {
            Remove-PolicyForSession $sessionId
            Remove-Item -LiteralPath $statePath -Force -ErrorAction SilentlyContinue
        }
        elseif ($null -eq $stopState) {
            Remove-PolicyForSession $sessionId
        }
        elseif ($stopState.active -eq $true `
            -and [string] $stopState.agentType -ceq $targetAgent `
            -and [string] $stopState.sessionId -ceq $sessionId) {
            Remove-BoundPolicy $stopState
            Remove-Item -LiteralPath $statePath -Force -ErrorAction SilentlyContinue
        }
        Write-HookOutput @{}
        exit 0
    }

    try {
        $state = Read-JsonFile $statePath
    }
    catch {
        if ($isRiskyPreTool -and ($explicitTargetAgent -or $agentTypeMissing)) {
            Deny 'Local developer state is invalid.'
        }
        else { Write-HookOutput @{} }
        exit 0
    }

    $stateMatches = $null -ne $state `
        -and $state.active -eq $true `
        -and [string] $state.agentType -ceq $targetAgent `
        -and [string] $state.sessionId -ceq $sessionId
    if (-not $stateMatches) {
        if ($isRiskyPreTool -and $explicitTargetAgent) {
            Deny 'Local developer state is missing or belongs to another session.'
        }
        else { Write-HookOutput @{} }
        exit 0
    }

    if ($eventName -eq 'PreToolUse') {
        if ([DateTimeOffset]::UtcNow - [DateTimeOffset]::Parse([string] $state.startedUtc) -ge $taskLimit) {
            Deny 'Local developer task limit of 10 minutes was reached.'
            exit 0
        }
        if ([int] $state.noProgressOutcomes -ge $maxNoProgressOutcomes) {
            Deny 'Local developer reached 3 no-progress outcomes.'
            exit 0
        }

        $currentFingerprint = Get-AllowlistFingerprint @($state.allowedWritePaths)
        if ([string] $state.allowlistFingerprint -cne $currentFingerprint) {
            $state.allowlistFingerprint = $currentFingerprint
            $state.callRecords = @()
            $state.noProgressOutcomes = 0
            $state.lastOutcomeHash = $null
        }

        $state.toolCalls = [int] $state.toolCalls + 1
        if ([int] $state.toolCalls -gt $maxToolCalls) {
            Write-JsonFile $statePath $state
            Deny 'Local developer tool-call limit of 30 was reached.'
            exit 0
        }

        if (Test-FileMutationTool $toolName) {
            if ($toolName -match '(?i)(delete|move|rename)') {
                Write-JsonFile $statePath $state
                Deny 'Delete, move, and rename file tools are blocked.'
                exit 0
            }

            $patchOperations = @(Get-PatchOperations $toolInput)
            if ($patchOperations | Where-Object { $_.Action -ne 'Update' }) {
                Write-JsonFile $statePath $state
                Deny 'Patch tools may only update existing files; use create_file for new files.'
                exit 0
            }
            $candidatePaths = @(Get-ToolCandidatePaths $toolInput)
            if ($patchOperations.Count -gt 0) {
                $candidatePaths += @($patchOperations | ForEach-Object { $_.Path })
            }
            $candidatePaths = @($candidatePaths | Select-Object -Unique)
            if ($state.writePolicyArmed -ne $true -or $candidatePaths.Count -eq 0) {
                Write-JsonFile $statePath $state
                Deny 'File mutation has no armed allowlist or no resolvable target path.'
                exit 0
            }

            $resolvedPaths = @($candidatePaths | ForEach-Object { Resolve-ToolPath ([string] $_) })
            if ($resolvedPaths -contains $null) {
                Write-JsonFile $statePath $state
                Deny 'File mutation contains an invalid target path.'
                exit 0
            }
            foreach ($resolvedPath in $resolvedPaths) {
                $isAllowed = @($state.allowedWritePaths) | Where-Object {
                    ([string] $_).Equals([string] $resolvedPath, [StringComparison]::OrdinalIgnoreCase)
                } | Select-Object -First 1
                if ($null -eq $isAllowed) {
                    Write-JsonFile $statePath $state
                    Deny 'File mutation is outside the armed snapshot allowlist.'
                    exit 0
                }
            }

            $isCreate = $toolName -match '(?i)create[_-]?file'
            foreach ($resolvedPath in $resolvedPaths) {
                if ($isCreate -and (Test-Path -LiteralPath $resolvedPath)) {
                    Write-JsonFile $statePath $state
                    Deny 'create_file cannot overwrite an existing path; use native edit.'
                    exit 0
                }
                if (-not $isCreate -and -not (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) {
                    Write-JsonFile $statePath $state
                    Deny 'Native edit requires an existing allowlisted file.'
                    exit 0
                }
            }
        }

        if ((Test-ExecutionTool $toolName) `
            -and (Test-DangerousTerminalCommand ([string] (Get-ObjectProperty $toolInput 'command')))) {
            Write-JsonFile $statePath $state
            Deny 'Shell file creation, deletion, move, rename, or overwrite is blocked.'
            exit 0
        }

        $normalizedInput = if ($null -eq $toolInput) { 'null' } else {
            $toolInput | ConvertTo-Json -Compress -Depth 30
        }
        $callHash = Get-TextHash ($toolName + "`n" + $normalizedInput)
        $records = @($state.callRecords)
        $record = $records | Where-Object {
            [string] $_.hash -ceq $callHash `
                -and [string] $_.fingerprint -ceq $currentFingerprint
        } | Select-Object -First 1
        if ($null -ne $record) {
            if ([int] $record.count -ge 2) {
                $state.noProgressOutcomes = [int] $state.noProgressOutcomes + 1
                Write-JsonFile $statePath $state
                Deny 'Third identical tool call without allowlisted file progress was blocked.'
                exit 0
            }
            $record.count = [int] $record.count + 1
        }
        else {
            $state.callRecords = @($records + [PSCustomObject]@{
                hash = $callHash
                fingerprint = $currentFingerprint
                count = 1
            })
        }

        $state.pendingAllowlistFingerprint = $currentFingerprint
        Write-JsonFile $statePath $state
        Allow
        exit 0
    }

    if ($eventName -eq 'PostToolUse') {
        $response = Get-ObjectProperty $payload 'tool_response'
        $inputJson = if ($null -eq $toolInput) { 'null' } else {
            $toolInput | ConvertTo-Json -Compress -Depth 30
        }
        $responseJson = if ($null -eq $response) { 'null' } else {
            $response | ConvertTo-Json -Compress -Depth 30
        }
        $outcomeHash = Get-TextHash ($toolName + "`n" + $inputJson + "`n" + $responseJson)
        $currentFingerprint = Get-AllowlistFingerprint @($state.allowedWritePaths)

        if ([string] $state.pendingAllowlistFingerprint -cne $currentFingerprint) {
            $state.noProgressOutcomes = 0
            $state.callRecords = @()
            $state.lastOutcomeHash = $null
        }
        elseif ([string] $state.lastOutcomeHash -ceq $outcomeHash) {
            $state.noProgressOutcomes = [int] $state.noProgressOutcomes + 1
        }
        else {
            $state.noProgressOutcomes = 0
        }

        $state.allowlistFingerprint = $currentFingerprint
        $state.lastOutcomeHash = $outcomeHash
        $state.pendingAllowlistFingerprint = $null
        Write-JsonFile $statePath $state
        Write-HookOutput @{}
        exit 0
    }

    Write-HookOutput @{}
}
catch {
    if ($isRiskyPreTool) {
        Deny 'Local developer guard failed closed.'
    }
    elseif ($eventName -eq 'SubagentStart' -and $agentType -eq $targetAgent) {
        Stop-Agent 'Local developer guard could not initialize the session.'
    }
    else {
        Write-HookOutput @{}
    }
}
finally {
    if ($lockTaken) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}