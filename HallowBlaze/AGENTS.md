# AGENTS.md — HallowBlaze

## 1. Scope

This file applies to all agents working in the HallowBlaze Unity project and its subdirectories.

Repository layout:

```text
repo root/
  .git/
  .gitignore
  HallowBlaze/          ← Unity project / workspace root
    AGENTS.md
    .github/
      agents/
    Assets/
    Docs/
    Packages/
    ProjectSettings/
```

The Git repository root is one level above the Unity project root.

Unity project commands should normally be run from the Unity project root.

Project Unity version is currently `6000.3.21f1`. When version-specific behavior matters, verify `ProjectSettings/ProjectVersion.txt` instead of relying on this document.

## 2. Communication

User-facing communication and agent handoffs should be written in Polish.

Preserve the existing language and conventions of source code, identifiers, comments, and project documentation unless the task explicitly requires changing them.

## 3. Sources of truth

When instructions conflict, use this order:

1. system/developer instructions and the user's explicit current request;
2. this `AGENTS.md` for repository-wide working rules;
3. `Docs/GameDesignContract.md` for intended game behavior and accepted design decisions;
4. accepted ADRs in `Docs/Decisions/`;
5. the active `HB-xxx` card in `Docs/TechnicalRoadmap.md`;
6. existing implementation and established project patterns, unless documented as legacy or incorrect.

Existing prototype behavior is not automatically intended design.

If a task depends on a decision marked `Open` in `GameDesignContract.md`, do not invent a behavior-changing answer.

Escalate decisions that materially affect:

- player-facing behavior,
- reset semantics,
- save compatibility or format,
- major architectural boundaries.

## 4. Shared working rules

Work on one scoped task/ticket at a time.

Prefer:

- minimal, local changes;
- existing project patterns;
- explicit behavior;
- small diffs;
- focused validation.

Do not:

- implement future tickets opportunistically;
- perform unrelated cleanup or refactoring;
- overwrite unrelated user work;
- treat a dirty worktree as something that must be cleaned;
- modify protected agent configuration unless the user explicitly requested agent-configuration changes.

Protected configuration:

- `.github/agents/**`
- `AGENTS.md`
- `Docs/AgentTeam.md`

Exactly one agent may write project files during an implementation iteration.

Read-only analysis may run separately, but review begins only after the implementation writer has finished.

Before a normal ticket begins, the coordinator must verify that the worktree is clean and record the Git root, branch, and `HEAD`. The user commits and pushes any manual changes before starting agent work. If unexpected pre-existing changes are present, stop and ask the user how to proceed.

A writer must not begin without a closed write allowlist enforced by the Guard.

## 5. Architecture invariants

Implementations must preserve these rules unless an explicitly approved design change says otherwise:

- `ProfileState`, `RunState`, and `BoardState` have separate responsibilities.
- Starting a new run does not erase profile knowledge or the atlas.
- Grid state, not physics or `Transform`, is the target gameplay source of truth.
- Input produces a command; a central resolver produces results/events; presentation reproduces them.
- A rejected command does not consume a turn, food, or a tool.
- Intent shown to the player is the intent that will execute or be explicitly blocked.
- Domain code does not store `GameObject`, `MonoBehaviour`, `Transform`, prefabs, colliders, or Unity `InstanceID`.
- Gameplay RNG is deterministic and separated from cosmetic randomness.
- Saves use stable textual IDs and versioned DTOs rather than direct Unity-object serialization.
- Discovering a fact documents an existing rule; it does not unlock that rule.
- A randomly found tool cannot be required for a mandatory exit.

When existing legacy code violates these rules, migrate only within the scope of the active task. Do not perform a big-bang rewrite.

## 6. Git and worktree safety

Treat pre-existing tracked and untracked changes as user-owned baseline.

### Git baseline before a writer

For a new ticket, record the real Git root, branch, and `HEAD`, then require `git status --short --branch --untracked-files=normal` to show no staged, unstaged, or untracked project changes. The clean commit is the recovery baseline; do not create a separate snapshot.

Define a closed repo-relative write allowlist and arm it through the Guard before delegating. After the writer returns, compare the repository changes with the recorded baseline and allowlist before review.

Validation or review corrections continue from the current ticket state without creating a new baseline or snapshot. If the second validation fails, the Lead takes over implementation as defined by the completion gate.

If a path outside the allowlist changes or baseline content disappears, stop the normal workflow. Preserve the evidence and do not restore automatically; recovery requires an explicit user decision.

Never automatically use destructive operations such as:

- `git reset --hard`;
- `git clean`;
- `git checkout --` on unrelated work;
- `git restore` on unrelated work;
- force push;
- destructive rebase;
- automatic stash of user changes.

Do not create commits, branches, tags, or pull requests unless explicitly requested.

Do not modify global Git configuration without explicit approval.

Before operating on the parent repository directory, confirm the real Git root and exact intended scope.

Do not recursively inspect, stage, or manipulate generated Unity/IDE directories such as:

- `Library/`
- `Temp/`
- `obj/`
- `Logs/`
- `Build/`
- `Builds/`
- `UserSettings/`

A task may explicitly authorize changes to repository-root files such as `../.gitignore` or `../.gitattributes`. Such authorization does not extend to other parent files.

History rewriting is always a separate, explicitly authorized operation.

## 7. Unity asset safety

Do not manually invent or edit Unity GUIDs.

Do not regenerate the `.meta` file of an existing asset.

Do not delete, move, or rename an asset independently of its `.meta`.

Prefer Unity-aware operations for asset database moves and renames.

Treat scenes, prefabs, serialized assets, `ProjectSettings`, and package configuration as sensitive project state.

Do not change:

- Unity version;
- `Packages/manifest.json`;
- `Packages/packages-lock.json`;
- `ProjectSettings`;

unless the active task requires it.

Do not mix mass reserialization with unrelated gameplay changes.

Text/YAML serialization does not make blind merge conflict resolution safe.

A scene or prefab has one writer per iteration.

After scene/prefab changes, verify relevant references and check for missing scripts where practical.

Run only one Unity Editor instance against this project directory.

## 8. Unity CLI and Pipeline

Unity CLI with the Unity Pipeline package is the primary automation interface to the running Unity Editor.

The legacy Unity AI Assistant MCP Server is not part of the primary workflow.

For this project environment, use:

```powershell
unity pipeline list
unity command
```

Use `unity command` to discover commands exposed by the connected Editor.

For focused C# inspection or small controlled Editor operations:

```powershell
unity command eval "<valid C# statements>;"
```

Example:

```powershell
unity command eval "UnityEngine.Debug.Log(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);"
```

In the currently installed CLI/Pipeline combination, use `unity command eval`, not top-level `unity eval`.

Code passed to `eval` must be valid compilable C# statements and should normally terminate statements with `;`.

Prefer `eval` for inspection, diagnostics, and narrowly scoped Editor interaction.

Do not use large opaque `eval` scripts as a substitute for maintainable source-controlled code or dedicated Editor/Pipeline tooling.

If an Editor operation changes persistent project state:

- make the mutation intentional;
- save the affected state explicitly;
- inspect resulting repository changes;
- verify the result.

## 9. Validation

Use the smallest validation level that gives meaningful evidence.

| Change type | Minimum expected validation |
| --- | --- |
| Documentation only | structure, links, source-of-truth consistency |
| Pure domain logic | relevant EditMode tests |
| `MonoBehaviour`, scene, or UI integration | relevant EditMode/PlayMode validation and smoke check |
| Generator | determinism test, validator, representative seeds |
| Save/profile/run state | round-trip, reset semantics, corrupt-data handling, isolated temp location |
| Prefab/scene | open/inspect in Unity, relevant references, smoke check |
| Input/turn logic | accepted-command turn behavior and rejected-command behavior |

Unity Test Framework `1.6.0` is already installed. Do not introduce another test framework without an explicit requirement.

Tests must not write to the player's real profile directory.

Deterministic failures should report enough information to reproduce them, including seed/node identifiers where relevant.

Gameplay and cosmetic randomness must not share the same RNG stream.

Do not claim that a build or test passed unless it was actually run and observed.

If validation cannot run because of the environment, report it as `Not run` with the concrete reason.

## 10. Definition of done

A task is complete only when:

- its acceptance criteria have evidence;
- required behavior is implemented;
- relevant compilation/tests/validation passed, or an explicit limitation was accepted;
- no unintended user changes were overwritten;
- scope did not silently expand;
- save changes include compatible versioning/migration or an explicitly approved reset;
- documentation/configuration was updated when the task changed a public contract;
- implementation received the required independent review.

Partial implementation, token limits, or plausible-looking code are not evidence of completion.

## 11. Security and external data

Do not add or expose secrets, credentials, private codes, or tokens in repository content, logs, prompts, or reports.

Do not repeat values of credentials already present in legacy code.

Do not send project code, telemetry, results, or player data to external services without explicit authorization.

Tests involving external services should use fakes, isolated test infrastructure, or explicitly approved endpoints.

Security cleanup, credential rotation, and Git-history rewriting are separate tasks that require explicit scope.