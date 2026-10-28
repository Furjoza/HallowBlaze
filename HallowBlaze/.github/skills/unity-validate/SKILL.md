---
name: unity-validate
description: Validate HallowBlaze C# changes with scoped compilation and focused Unity EditMode or PlayMode tests. The Lead implements, runs the checks, and supplies actual results for review or explicit self-review.
---

# Unity Validation

Run against the exact Unity project path assigned to the current task: the directory containing `Assets`, `Packages`, and `ProjectSettings`.

Normally this is the active project workspace. If the Lead explicitly assigns a separate worktree or other isolated copy, use that exact path instead. Never substitute another open copy of the project and never run two Unity Editors against the same project directory.

# Ownership

The Lead owns implementation and acceptance validation:

- Run focused checks while implementing and after relevant corrections.
- Validate the final filesystem state against the acceptance criteria.
- Supply actual results to the read-only Reviewer when available, otherwise use them for explicitly disclosed Lead self-review.
- Reviewer unavailability does not waive required compilation, tests, or other checks.

The XML produced by `unity test --output` is simply the Unity Test Framework's machine-readable test report. It records which tests ran, counts, failures, and result. It is validation evidence, not a source artifact or implementation deliverable.

# Windows invocation

The terminal uses Windows PowerShell 5.1. Run commands separately or separate statements with `;`, never `&&`.

Always pass the exact absolute Unity project directory to `unity test` or `unity run`. Changing the current directory does not replace that argument.

`dotnet test` on Unity-generated projects is not a substitute for the Unity Test Framework.

Follow `AGENTS.md`: first run `unity pipeline list`, then `unity command` to discover the connected Editor's commands. Prefer an appropriate live command when available. If the live connection is unavailable, diagnose it with the CLI and use the existing batchmode `unity test` or `unity run` when appropriate; do not fall back automatically to legacy Unity MCP.

If an edit seems invisible, inspect the source on disk at the exact project path being tested. A chat preview or tool-completion message is not evidence of what Unity compiled.

# Validation output

Write validation output outside the Unity project.

A convenient temporary directory is:

```powershell
$project = '<absolute Unity project path>'
$results = Join-Path $env:TEMP ('HB-validation-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $results | Out-Null
```

Do not place validation XML or logs under `Assets`, `Docs`, `Packages`, `ProjectSettings`, or another project directory.

The exact external filename is not part of PASS unless the task explicitly says otherwise. Always know which report belongs to the current invocation.

# Compile

For an existing current generated project, a quick domain-assembly check can be useful:

```powershell
Set-Location -LiteralPath $project
dotnet build .\HallowBlaze.Core.State.csproj --no-restore --nologo -v:minimal
$LASTEXITCODE
```

Replace the assembly with the one owning the changed files. Generated projects may be absent or stale; do not edit generated project files or treat this check as a substitute for Unity validation.

When a Unity import/compile check is needed:

```powershell
unity run $project -- -nographics -logFile "$results/compile.log"
$LASTEXITCODE
```

The CLI manages reserved Unity batch/project arguments. Do not add `-batchmode`, `-projectPath`, or `-quit` manually.

# Focused tests

Use the fully qualified test or fixture that owns the changed behavior.

EditMode example:

```powershell
unity test $project --mode EditMode --filter HallowBlaze.Tests.EditMode.EditModeInfrastructureTests.EditModeAssemblyLoads --output "$results/editmode.xml" -- -nographics -logFile "$results/editmode.log"
$LASTEXITCODE
```

PlayMode example:

```powershell
unity test $project --mode PlayMode --filter HallowBlaze.Tests.PlayMode.PlayModeInfrastructureTests.PlayModeAssemblyLoads --output "$results/playmode.xml" -- -nographics -logFile "$results/playmode.log"
$LASTEXITCODE
```

Use EditMode for pure domain behavior. Use PlayMode for relevant `MonoBehaviour`, scene, UI, or lifecycle integration. Persistence tests must use their isolated temporary-root fixtures, never the player's real save directory.

# Read the Unity test report

After `unity test`, inspect the fresh XML report:

```powershell
[xml]$report = Get-Content -LiteralPath "$results/editmode.xml" -Raw
$report.'test-run' | Select-Object result, total, passed, failed, inconclusive, skipped
$report.SelectNodes('//test-case[@result="Failed"]/failure') | ForEach-Object { $_.InnerText }
```

Require:

- a report created by the current invocation;
- the expected test or fixture identity;
- a nonzero executed test count;
- `result="Passed"` and zero failures for PASS.

A success-looking CLI message alone is not enough. A nonzero CLI exit does not by itself prove Unity failed to run the test: if a fresh XML contains the expected executed test and assertion failure, the test did run and failed.

If XML is absent, read the complete log to diagnose startup, licensing, project lock, package, compilation, or other environment failures.

# Focused correction loop

`implement -> focused validation -> inspect failure -> scoped correction -> rerun affected checks`

The Lead corrects its implementation directly. Do not weaken assertions or filters to manufacture PASS. Stop for a concrete blocker or missing product decision, not because a separate worker's attempt budget was exhausted.

# Lead acceptance gate

Validate the final filesystem state; previous results do not cover later behavior-changing edits automatically.

The Lead should record or report:

- exact validation command/filter;
- expected test identity;
- total/passed/failed counts;
- compilation/result status;
- concrete failure text when relevant.

The Reviewer needs this validation summary plus the actual diff and acceptance criteria. If independent review is unavailable, the Lead uses the same evidence for self-review and discloses that limitation. The raw XML does not need to live in the repository.
