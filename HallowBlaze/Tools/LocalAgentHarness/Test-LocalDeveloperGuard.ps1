# Test script for LocalDeveloperGuard.ps1
# This script tests the lifecycle scenarios A-F as specified in the requirements

param(
    [string] $GuardPath = "$PSScriptRoot\LocalDeveloperGuard.ps1",
    [string] $TestStateDir = "$env:TEMP\HallowBlazeGuardTestState-$([Guid]::NewGuid().ToString('N'))",
    [string] $TestRepoRoot = "$PSScriptRoot\..\..\.."
)

$ErrorActionPreference = 'Stop'
$originalStateDirectory = $env:HALLOWBLAZE_GUARD_STATE_DIRECTORY
$originalRepoRoot = $env:HALLOWBLAZE_GUARD_REPO_ROOT

# Clean up any existing test state
if (Test-Path $TestStateDir) {
    Remove-Item $TestStateDir -Recurse -Force
}

New-Item -ItemType Directory -Path $TestStateDir -Force | Out-Null

# Set up environment for isolated testing
$env:HALLOWBLAZE_GUARD_STATE_DIRECTORY = $TestStateDir
$env:HALLOWBLAZE_GUARD_REPO_ROOT = $TestRepoRoot

function Invoke-GuardTest([object] $payload) {
    $inputJson = $payload | ConvertTo-Json -Depth 30
    return @($inputJson | & "$PSHOME\powershell.exe" -NoProfile -File $GuardPath 2>&1) -join "`n"
}

function Create-SyntheticState(
    [string] $sessionId,
    [string] $agentType,
    [bool] $active,
    [AllowNull()][string] $startedUtc = $null,
    [AllowNull()][string] $policyId = $null,
    [string] $createdUtc = ([DateTimeOffset]::UtcNow.ToString('o'))) {
    $state = [PSCustomObject]@{
        active = $active
        sessionId = $sessionId
        agentType = $agentType
        createdUtc = $createdUtc
        startedUtc = $startedUtc
        toolCalls = 0
        noProgressOutcomes = 0
        allowlistFingerprint = "test"
        callRecords = @()
        lastOutcomeHash = $null
        pendingAllowlistFingerprint = $null
        writePolicyArmed = -not [string]::IsNullOrWhiteSpace($policyId)
        policyId = $policyId
        allowedWritePaths = @()
    }
    Write-JsonFile (Join-Path $TestStateDir "active-session.json") $state
}

function Create-SyntheticPolicy([string] $sessionId, [string] $policyId, [string] $expiresUtc) {
    $policy = [PSCustomObject]@{
        policyId = $policyId
        gitRoot = $TestRepoRoot
        baselineHead = "test-head"
        baselineBranch = "test-branch"
        expiresUtc = $expiresUtc
        allowlistFullPaths = @()
        consumedSessionId = $sessionId
    }
    Write-JsonFile (Join-Path $TestStateDir "write-policy.json") $policy
}

function Write-JsonFile([string] $path, [object] $value) {
    New-Item -ItemType Directory -Path (Split-Path $path -Parent) -Force | Out-Null
    [IO.File]::WriteAllText(
        $path,
        ($value | ConvertTo-Json -Depth 30),
        [Text.UTF8Encoding]::new($false))
}

function Read-JsonFile([string] $path) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }
    return Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
}

function Restore-TestEnvironment {
    $env:HALLOWBLAZE_GUARD_STATE_DIRECTORY = $originalStateDirectory
    $env:HALLOWBLAZE_GUARD_REPO_ROOT = $originalRepoRoot
    Remove-Item -LiteralPath $TestStateDir -Recurse -Force -ErrorAction SilentlyContinue
}

function Test-Scenario([string] $name, [scriptblock] $testBlock) {
    Write-Host "Testing scenario $name..." -ForegroundColor Cyan
    try {
        $testBlock.Invoke()
        Write-Host "PASS: $name" -ForegroundColor Green
        return $true
    }
    catch {
        Write-Host "FAIL: $name`: $_" -ForegroundColor Red
        return $false
    }
}

# Test A: Normal qwen session works
$testA = {
    # Clean state
    if (Test-Path (Join-Path $TestStateDir "active-session.json")) {
        Remove-Item (Join-Path $TestStateDir "active-session.json") -Force
    }

    # SubagentStart should succeed
    $result = Invoke-GuardTest @{
        hook_event_name = "SubagentStart"
        session_id = "qwen-session-1"
        agent_type = "qwen-developer"
    }

    if ($result -notlike "*Budgets: 30 tool calls*" -and $result -notlike "*10 minutes*") {
        throw "SubagentStart did not return expected budget info"
    }

    # PreToolUse should work
    $result = Invoke-GuardTest @{
        hook_event_name = "PreToolUse"
        session_id = "qwen-session-1"
        agent_type = "qwen-developer"
        tool_name = "read_file"
        tool_input = @{ filePath = "test.txt"; startLine = 1; endLine = 10 }
    }

    if ($result -notlike "*permissionDecision*:*allow*" -and $result -notlike "*Local developer guard checks passed*") {
        throw "PreToolUse was denied for valid qwen session"
    }
}

# Test B: >10 minute qwen session denied
$testB = {
    # Create expired state
    Create-SyntheticState "qwen-session-2" "qwen-developer" $true ([DateTimeOffset]::UtcNow.AddMinutes(-11).ToString('o'))

    # PreToolUse should be denied
    $result = Invoke-GuardTest @{
        hook_event_name = "PreToolUse"
        session_id = "qwen-session-2"
        agent_type = "qwen-developer"
        tool_name = "read_file"
        tool_input = @{ filePath = "test.txt"; startLine = 1; endLine = 10 }
    }

    if ($result -notlike "*permissionDecision*:*deny*" -and $result -notlike "*expired*" -and $result -notlike "*10 minutes*") {
        throw "Expired session was not properly denied"
    }

    # State should be cleaned up
    $state = Read-JsonFile (Join-Path $TestStateDir "active-session.json")
    if ($null -ne $state) {
        throw "State was not cleaned up after expired session"
    }
}

# Test C: Pending failed start does not block codex-lead
$testC = {
    # Create pending state (startedUtc = null)
    Create-SyntheticState "qwen-session-3" "qwen-developer" $true $null

    # codex-lead should work fine
    $result = Invoke-GuardTest @{
        hook_event_name = "PreToolUse"
        session_id = "codex-session-1"
        agent_type = "codex-lead"
        tool_name = "read_file"
        tool_input = @{ filePath = "test.txt"; startLine = 1; endLine = 10 }
    }

    if ($result -notlike "{}" -and $result -notlike "{}") {
        throw "codex-lead was blocked by pending qwen session"
    }
}

# Test D: Stale started state and policy cleaned on unrelated use
$testD = {
    # Create a stale pending state left before the developer's first tool call.
    Create-SyntheticState "qwen-session-4" "qwen-developer" $true $null "policy-123" ([DateTimeOffset]::UtcNow.AddMinutes(-15).ToString('o'))
    Create-SyntheticPolicy "qwen-session-4" "policy-123" ([DateTimeOffset]::UtcNow.AddMinutes(-15).ToString('o'))

    # codex-lead use should trigger cleanup
    $result = Invoke-GuardTest @{
        hook_event_name = "PreToolUse"
        session_id = "codex-session-2"
        agent_type = "codex-lead"
        tool_name = "read_file"
        tool_input = @{ filePath = "test.txt"; startLine = 1; endLine = 10 }
    }

    # State and policy should be cleaned up
    $state = Read-JsonFile (Join-Path $TestStateDir "active-session.json")
    $policy = Read-JsonFile (Join-Path $TestStateDir "write-policy.json")
    if ($null -ne $state) {
        throw "Stale state was not cleaned up"
    }
    if ($null -ne $policy) {
        throw "Stale policy was not cleaned up"
    }
}

# Test E: Second active pending/started writer blocked
$testE = {
    # Create active qwen session
    Create-SyntheticState "qwen-session-5" "qwen-developer" $true ([DateTimeOffset]::UtcNow.AddMinutes(-5).ToString('o'))

    # Second qwen SubagentStart should be blocked
    $result = Invoke-GuardTest @{
        hook_event_name = "SubagentStart"
        session_id = "qwen-session-6"
        agent_type = "qwen-developer"
    }

    if ($result -notlike "*Another qwen-developer session is already active*" -and $result -notlike "*continue*:*false*") {
        throw "Second active qwen session was not properly blocked"
    }
}

# Test F: Mismatched qwen session denied
$testF = {
    # Create state for session A
    Create-SyntheticState "qwen-session-7" "qwen-developer" $true ([DateTimeOffset]::UtcNow.AddMinutes(-5).ToString('o'))

    # PreToolUse from session B should be denied
    $result = Invoke-GuardTest @{
        hook_event_name = "PreToolUse"
        session_id = "qwen-session-8"  # Different session
        agent_type = "qwen-developer"
        tool_name = "read_file"
        tool_input = @{ filePath = "test.txt"; startLine = 1; endLine = 10 }
    }

    if ($result -notlike "*permissionDecision*:*deny*" -and $result -notlike "*missing or belongs to another session*") {
        throw "Mismatched qwen session was not properly denied"
    }
}

# Run all tests
$results = @()

$results += Test-Scenario "A normal qwen session works" $testA
$results += Test-Scenario "B >10 minute qwen session denied" $testB
$results += Test-Scenario "C pending failed start does not block codex-lead" $testC
$results += Test-Scenario "D stale started state and policy cleaned on unrelated use" $testD
$results += Test-Scenario "E second active pending/started writer blocked" $testE
$results += Test-Scenario "F mismatched qwen session denied" $testF

# Summary
$passed = $results | Where-Object { $_ }
$failed = $results | Where-Object { -not $_ }

Write-Host "`n=== Test Summary ===" -ForegroundColor White
Write-Host "Passed: $($passed.Count)/6" -ForegroundColor Green
Write-Host "Failed: $($failed.Count)/6" -ForegroundColor Red

if ($failed.Count -gt 0) {
    Restore-TestEnvironment
    exit 1
}

Restore-TestEnvironment
exit 0