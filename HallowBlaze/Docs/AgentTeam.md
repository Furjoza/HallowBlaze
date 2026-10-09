# Agent Team

## Architecture

| Role | Agent | Model | Writes project files |
|---|---|---|---|
| Tech Lead | `codex-lead` | `GPT 6.1 Sol (openai-codex)` | Authorized configuration and roadmap; not routine implementation |
| Developer | `developer` | `Devstral Small 2 24B - Ollama Custom (customendpoint)` | Direct native filesystem edits |
| Reviewer | `reviewer` | No pinned model; workspace Auto preference applies when supported | No |

`codex-lead` is the user-facing coordinator. `developer` and `reviewer` are internal native subagent roles and run sequentially. Their names identify responsibilities, not model vendors.

The Developer uses `read`, `search`, `edit`, and `execute`, and retains its configured local model. The Reviewer uses only `read` and `search`. The intended default reviewer provider is GitHub Copilot; the owner explicitly selects a local reviewer when the Copilot allowance is exhausted. A routing failure is not evidence of exhausted allowance and must not silently change the provider.

The roles were renamed from `qwen-developer` and `qwen-reviewer` on 2026-10-08. Historical roadmap handoffs and validation reports retain their original agent names and model attribution; the rename does not turn past local reviews into Copilot reviews. If the agent picker or subagent catalog still exposes the old identifiers, reload VS Code or start a fresh session rather than invoking obsolete names.

## Model Routing

[Workspace settings](../.vscode/settings.json) enable `chat.subagents.defaultToAuto`. [Reviewer configuration](../.github/agents/reviewer.agent.md) intentionally omits `model`; [Developer configuration](../.github/agents/developer.agent.md) keeps its explicit local selection.

For native VS Code subagents, model selection prioritizes an explicit invocation override, then the selected agent's `model`, then Auto when the workspace preference and availability conditions permit it, and finally the main conversation model. The Lead must not supply a model override unless the user explicitly requests one.

Auto selection for subagents is experimental. It does not override an agent's configured model. If Auto is unavailable, the main model is used; subagents of a BYOK model continue using that model unless another is explicitly selected. An SDK adapter may implement different routing. This preference is therefore not a guarantee of Copilot execution when the Lead uses another provider. Verify actual routing when the harness exposes it, and report a blocker instead of attributing an unverified fallback to Copilot. See the [VS Code subagent model-selection documentation](https://code.visualstudio.com/docs/copilot/agents/subagents).

Use the qualified model names offered by VS Code completion in agent frontmatter. Runtime SDK identifiers are a different layer and must be resolved to the correct provider, not substituted blindly into the agent files.

The optional [local profile initializer](../Tools/LocalAgentHarness/Initialize-LocalAgentCodexProfiles.ps1) writes the local reviewer catalog as `hallowblaze-reviewer-model.json`. Existing user-level catalogs are not migrated or deleted by this repository rename; the initializer is not run automatically.

## Workflow

```text
Codex Lead
  -> native runSubagent (developer)
  -> direct filesystem edits
  -> local compile/test/fix iterations
  -> Lead completion and integrity gates
  -> native runSubagent (reviewer)
  -> Lead arbitration and acceptance
```

Exactly one writer runs at a time. Independent review begins only after the writer and required validation finish. All operations target the active workspace or a worktree explicitly assigned to the task, not another project copy.

## Responsibility Boundaries

The Lead owns task interpretation, scope, acceptance criteria, architectural/product decisions, baseline and worktree preparation, delegation, integrity gates, review arbitration, and final acceptance. `Docs/TechnicalRoadmap.md` remains Lead-owned.

The Developer owns implementation within a closed write scope, focused validation, and an accurate handoff. It follows the existing code and tests and iterates on its own failures until PASS or a concrete technical blocker. It does not administer the Git baseline or perform independent review.

The Reviewer independently checks actual files, the task-scoped diff, acceptance criteria, and supplied validation evidence. It returns `PASS`, `CHANGES_REQUIRED`, or `BLOCKED`, never edits files, and does not claim terminal checks it cannot execute.

## Git Baseline

Before a normal ticket, the Lead records the real Git root, branch, and `HEAD` and verifies the clean worktree required by [AGENTS.md](../AGENTS.md). Unexpected pre-existing changes stop the workflow for a user decision; they are never automatically stashed, cleaned, or reverted.

The Lead defines a closed repo-relative write scope before delegation. After each writer iteration it checks actual disk changes, Git status/diff, preserved baseline content, required checks, and acceptance criteria. Normal corrections continue from the current ticket state.

Tool-completion metadata, generated tool-call text, chat previews, and agent reports alone do not prove that a file changed. Verify the intended content at the exact project path. Unexpected scope changes require a user decision, not automatic recovery.

## Task Packets

Send compact handoffs in Polish. The Developer receives the absolute project/worktree path, typically 1-3 scoped files, a concrete behavioral goal and invariants, implementation/test references, acceptance criteria, and the focused validation command or filter. Prefer references over long procedural prompts.

The Reviewer receives the task, acceptance criteria, changed-file scope, baseline `HEAD`, Git status and diff, the Developer report, and actual validation evidence with report paths. The Lead supplies Git and test evidence because the Reviewer has no terminal tool.

## Validation And Review

Use [unity-validate](../.github/skills/unity-validate/SKILL.md) for C# and Unity changes. Discover the live Pipeline/Editor commands first; use existing CLI batchmode compilation and focused tests when appropriate. PowerShell 5.1 does not support `&&`; pass the absolute assigned project path to `unity run` and `unity test`.

Unity test evidence requires a fresh XML report with the expected fixture, nonzero executed tests, and actual passing results. Keep reports outside project `Temp`, which Unity clears. CLI success text alone is not PASS.

For normal tickets, the Lead sends concrete validation failures or material review findings back to the Developer in a bounded correction task. It reruns the relevant gates after corrections and does not automatically take over implementation. A missing required check or `BLOCKED` review prevents acceptance.

## Controlled Experiments

Benchmarks run only when explicitly requested. Use disposable Git worktrees, record intentional fixture preparation separately from worker changes, and follow the agreed trial count and gates.

A returned compile failure, test failure, scope violation, or materially incomplete implementation is FAIL. The Lead records evidence and rejects the result without repairing it or running rescue handoffs. Worker self-repair before returning is allowed. The Lead performs the experiment's brief final acceptance review.

Worktrees isolate repository content, not arbitrary terminal effects. An invocation that never reaches the local model is an environment failure, not a competency result.

## Configuration Sources

- [Project rules](../AGENTS.md)
- [Lead](../.github/agents/codex-lead.agent.md)
- [Developer](../.github/agents/developer.agent.md)
- [Reviewer](../.github/agents/reviewer.agent.md)
- [Unity validation](../.github/skills/unity-validate/SKILL.md)
- [Game-design contract](GameDesignContract.md)
- [Technical roadmap](TechnicalRoadmap.md)