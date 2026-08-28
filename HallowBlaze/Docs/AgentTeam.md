# Agent Team

## Architecture

| Role | Agent | Model |
|---|---|---|
| Tech Lead | `codex-lead` | Selected in VS Code Chat |
| Developer | `qwen-developer` | `qwen3-coder:30b` via Ollama |
| Reviewer | `qwen-reviewer` | `qwen3.6:27b` via Ollama |

The Lead runs in the VS Code Chat agent runtime and delegates work through subagents.

## Workflow

```text
User
  ↓
codex-lead
  ↓
qwen-developer
  ↓
Lead completion gate
  ├─ incomplete → Developer retry
  ↓
qwen-reviewer
  ├─ CHANGES_REQUIRED → qwen-developer → review again
  └─ PASS → Lead completes the task