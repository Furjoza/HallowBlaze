---
name: unity-validate
description: Validate HallowBlaze C# changes with scoped compilation and focused Unity EditMode or PlayMode tests. Use after editing code and when diagnosing compiler or test failures.
---

# Unity Validation

Run from the Unity project directory, containing `Assets`, `Packages`, and `ProjectSettings`. Use the delegated worktree, not another open copy of the project. Never run two Editors against the same directory.

## Windows Invocation

The terminal uses Windows PowerShell 5.1. Run commands separately or separate statements with `;`, never `&&`: that operator fails parsing before Unity starts. Pass the absolute project directory to `unity test` and `unity run`, as shown below; changing directory does not replace that argument.

`dotnet test` on Unity-generated projects is not a substitute for the Unity Test Framework or evidence that Unity tests executed. Use the focused `unity test` command and inspect its fresh XML.

If an edit seems invisible, inspect the source on disk at the exact project path being tested. A chat preview or the state of a Keep button is not evidence of what Unity compiled.

## Discover

```powershell
unity pipeline list
unity command
```

These discover the live Editor connection. If Pipeline is unreachable, the same Unity CLI can run batchmode validation below. Do not use legacy MCP or install/upgrade Unity or packages to bypass a failure. The project version is recorded in `ProjectSettings/ProjectVersion.txt` (currently `6000.3.21f1`).

## Compile

For an existing, current generated project, the quick domain-assembly check is:

```powershell
dotnet build .\HallowBlaze.Core.State.csproj --no-restore --nologo -v:minimal
$LASTEXITCODE
```

Replace the assembly with the one owning the changed files. Generated projects may be absent or stale in a fresh worktree; do not edit them or treat this check as a substitute for Unity tests.

Prepare durable reports outside the project, because Unity clears `Temp` between processes:

```powershell
$project = (Get-Location).Path
$results = Join-Path $env:TEMP ('HB-validation-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $results | Out-Null
```

For a fresh worktree or Unity integration, import and compile through Unity CLI:

```powershell
unity run $project -- -nographics -logFile "$results/compile.log"
$LASTEXITCODE
```

The CLI manages `-batchmode`, `-projectPath`, and `-quit`; do not forward these reserved flags. Exit code zero and a log without compiler errors are required. A focused Unity test also imports and compiles the project before execution.

## Focused Tests

Use the fully qualified test or fixture owning the changed behavior. These concrete smoke filters verify the two project test assemblies; replace them with the delegated behavior's filter, not the entire suite.

```powershell
unity test $project --mode EditMode --filter HallowBlaze.Tests.EditMode.EditModeInfrastructureTests.EditModeAssemblyLoads --output "$results/editmode.xml" -- -nographics -logFile "$results/editmode.log"
$LASTEXITCODE
```

```powershell
unity test $project --mode PlayMode --filter HallowBlaze.Tests.PlayMode.PlayModeInfrastructureTests.PlayModeAssemblyLoads --output "$results/playmode.xml" -- -nographics -logFile "$results/playmode.log"
$LASTEXITCODE
```

Use EditMode for pure domain behavior. Use PlayMode for the relevant `MonoBehaviour`, scene, UI, or lifecycle integration. Run platforms sequentially. Persistence tests must use their isolated temporary-root fixtures, never the player's real save directory.

After each command, inspect the corresponding XML before starting another process:

```powershell
[xml]$report = Get-Content -LiteralPath "$results/editmode.xml" -Raw
$report.'test-run' | Select-Object result, total, passed, failed, inconclusive, skipped
$report.SelectNodes('//test-case[@result="Failed"]/failure') | ForEach-Object { $_.InnerText }
```

Require an existing, fresh report, `result="Passed"`, nonzero executed tests, and zero failures. Check the expected fixture/test actually ran. A missing report, zero matching tests, or a success-looking CLI message is not PASS. Use the PlayMode XML for PlayMode.

After a nonzero exit, inspect fresh XML first. The CLI can print licensing warnings alongside an actual assertion failure; a report with executed tests and that failure proves the test ran. If XML is absent, use the complete log to diagnose startup, licensing, or other environment blockers. Do not infer a blocker from warnings alone.

## Failure Loop

`change -> validate -> read complete failure -> fix -> validate again`

Read the complete failed assertion, stack trace, and relevant compiler diagnostics. If XML is absent or does not explain the failure, read the command's complete log with `Get-Content -LiteralPath <log> -Raw`. Fix the implementation and rerun the same focused check. Do not weaken assertions or filters to manufacture PASS.

Do not finish while required compilation or tests fail. If validation cannot run, report `Not run`, the exact command, and the concrete technical cause (for example, a project lock, missing installed Editor, license failure, or package restore failure). Return the observed exit codes, test counts, and report paths; do not infer success from intended actions.