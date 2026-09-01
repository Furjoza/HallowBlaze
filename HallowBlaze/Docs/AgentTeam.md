# Agent Team

## Architecture

| Role | Agent | Model | Writes project files |
|---|---|---|---|
| Tech Lead | `codex-lead` | Selected in VS Code Chat | No |
| Developer | `qwen-developer` | `qwen3-coder:30b` via Ollama | Yes |
| Reviewer | `qwen-reviewer` | `qwen3.6:27b` via Ollama | No |

`codex-lead` is the user-facing coordinator.

The two local Qwen agents are internal subagents and run sequentially.

## Workflow

```text
User
  │
  ▼
codex-lead
  │
  ├─ inspect context
  ├─ establish baseline + write allowlist
  ├─ create and verify recovery snapshot outside repo
  ├─ define acceptance criteria
  └─ plan
  │
  ▼
qwen-developer
  │
  ├─ implement
  ├─ self-validate
  └─ handoff
  │
  ▼
Lead completion + integrity gate
  │
  ├─ incomplete/invalid ───────► qwen-developer retry
  │
  ▼
qwen-reviewer
  │
  ├─ CHANGES_REQUIRED ────────► Lead arbitration
  │                              │
  │                              ▼
  │                         qwen-developer
  │                              │
  │                              └────────► review again
  │
  ├─ BLOCKED ─────────────────► Lead/user decision
  │
  └─ PASS ────────────────────► Lead accepts and reports
```

## Responsibility boundaries

### Tech Lead

Owns:

- task interpretation;
- scope;
- acceptance criteria;
- architectural decisions;
- preflight snapshot and integrity checks;
- delegation;
- review arbitration;
- final acceptance.

Does not routinely implement production changes.

### Developer

Owns:

- implementation;
- writes within the approved allowlist;
- implementation-level validation;
- accurate handoff.

It is the only writer during an implementation iteration.

### Reviewer

Owns:

- independent verification;
- adversarial review;
- acceptance-criteria checks;
- safe read-only tests and diagnostics.

It never repairs the implementation.

## Snapshot before every writer

Before each implementation, configuration, or correction iteration, `codex-lead` creates a new local recovery snapshot outside the Git worktree. The snapshot records the Git baseline, staged and unstaged binary patches, untracked paths, the closed allowlist, SHA-256 hashes, and exact copies of existing allowlisted files.

The Lead verifies the artifacts and passes the snapshot ID and path to `qwen-developer`. The Developer must return `BASELINE_SNAPSHOT_REQUIRED` without editing when the snapshot is absent, incomplete, or inconsistent with the handoff.

Every transfer of write ownership requires a fresh snapshot. After the writer returns, the Lead compares repository state with that snapshot before starting review. Unexpected changes stop the workflow; restoration is never automatic.

## Unity integration

The primary Editor automation path is:

```text
VS Code agent
    │
    ▼
terminal / execute
    │
    ▼
Unity CLI
    │
    ▼
Unity Pipeline
    │
    ▼
Unity Editor
```

Useful discovery commands:

```powershell
unity pipeline list
unity command
```

Focused Editor inspection uses:

```powershell
unity command eval "<valid C# statements>;"
```

The legacy Unity AI Assistant MCP Server is not part of the primary workflow.

## Configuration sources

Project-wide rules:

`AGENTS.md`

Role-specific behavior:

`.github/agents/*.agent.md`

Game-design contract:

`Docs/GameDesignContract.md`

Technical roadmap:

`Docs/TechnicalRoadmap.md`

Architecture decisions:

`Docs/Decisions/`