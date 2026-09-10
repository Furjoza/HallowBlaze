# Agent Team

## Architecture

| Role | Agent | Model | Writes project files |
|---|---|---|---|
| Tech Lead | `codex-lead` | Selected in VS Code Chat | No |
| Developer | `qwen-developer` | `devstral-small-2:24b` via gateway and Ollama | Yes |
| Reviewer | `qwen-reviewer` | Auto (Copilot) | No |

`codex-lead` is the user-facing coordinator.

The local Developer and Reviewer are internal subagents and run sequentially.

## Workflow

```text
User
  │
  ▼
codex-lead
  │
  ├─ inspect context
  ├─ verify clean Git baseline
  ├─ establish write allowlist
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
Lead validation + integrity gate
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
- clean Git baseline and integrity checks;
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

## Git baseline and write allowlist

Before a normal ticket, `codex-lead` records the Git root, branch, and `HEAD`, then requires a clean worktree. The user commits and pushes manual changes before agent work. An unexpected dirty worktree stops the workflow for a user decision; it is not automatically stashed, cleaned, or snapshotted.

The Lead defines a closed repo-relative allowlist and arms it in the Guard before delegating. The Developer must return `BASELINE_REQUIRED` without editing when the Git baseline or allowlist is missing.

Validation and review corrections continue from the current ticket state without creating a new baseline. After the writer returns, the Lead compares repository changes with the original Git baseline and allowlist before starting review. Unexpected changes stop the workflow; restoration is never automatic.

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

Versioned local Developer harness:

`Tools/LocalAgentHarness/`

Game-design contract:

`Docs/GameDesignContract.md`

Technical roadmap:

`Docs/TechnicalRoadmap.md`

Architecture decisions:

`Docs/Decisions/`