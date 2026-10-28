# Agent Team

## Architecture

| Role | Agent | Model | Writes project files |
|---|---|---|---|
| Lead and implementation developer | `codex-lead` | `GPT 6.1 Sol (openai-codex)` | Scoped implementation, tests, documentation, and authorized configuration |
| Reviewer | `reviewer` | No pinned model; workspace Auto preference applies when supported | No |

`codex-lead` handles the task end to end: interpretation, implementation, validation, corrections, and acceptance. `reviewer` is the only configured subagent and provides independent read-only review after the Lead finishes implementation and required checks.

The Lead uses `read`, `search`, `edit`, `execute`, and `agent`. The Reviewer uses only `read` and `search`, with no child agents. There is no separate implementation worker or local-model handoff.

GitHub Copilot is the intended review service. When the Reviewer is unavailable, including exhausted allowance, the Lead continues with explicitly disclosed self-review. This does not waive required tests or unresolved material findings.

## Model Routing

[Workspace settings](../.vscode/settings.json) enable `chat.subagents.defaultToAuto`. [Reviewer configuration](../.github/agents/reviewer.agent.md) intentionally omits `model`. Invoke `reviewer` without a runtime model override; do not add a local-model fallback automatically.

An omitted `model` is not a guarantee of provider routing. Native VS Code uses the agent configuration and available workspace routing; an adapter may behave differently. Do not claim the actual provider unless it is known, require account probes, or inspect session logs solely to establish routing. See the [VS Code subagent documentation](https://code.visualstudio.com/docs/copilot/agents/subagents).

If an invocation fails, report its actual cause rather than assuming quota exhaustion. Do not repeatedly retry, change providers, or introduce more agents. Continue under the self-review policy below. A stale agent picker/catalog may require reloading VS Code or starting a new session; do not invoke a removed role from a stale catalog.

## Workflow

```text
Lead scopes and implements
  -> focused validation and corrections
  -> final scope and integrity checks
  -> Reviewer if available, otherwise explicit Lead self-review
  -> Lead corrections, affected validation, and review recheck as needed
  -> Lead acceptance
```

The Lead is the only project-file writer. Independent review begins only after implementation and required validation finish. All operations target the active workspace or an explicitly assigned worktree, not another project copy.

## Responsibility Boundaries

The Lead owns task interpretation, scope, acceptance criteria, architectural/product decisions within the accepted contract, baseline protection, implementation, tests, validation, review arbitration, and final acceptance. It fixes in-scope failures directly; no worker handoff, attempt budget, or rescue protocol is needed. `Docs/TechnicalRoadmap.md` remains Lead-owned.

The Reviewer independently checks the actual files, task-scoped diff, acceptance criteria, and supplied validation evidence. It returns `PASS`, `CHANGES_REQUIRED`, or `BLOCKED`, never edits files, and does not claim terminal checks it cannot execute.

Material review findings require Lead corrections and affected validation, followed by a focused recheck when the Reviewer remains available. A substantive `BLOCKED` result is not service unavailability; missing required evidence or decisions must be resolved.

## Git Baseline

Before a normal ticket, the Lead records the real Git root, branch, and `HEAD` and verifies the clean worktree required by [AGENTS.md](../AGENTS.md), or records the owner's explicit authorization to continue with identified existing changes. Unexpected pre-existing changes stop the workflow for a user decision; they are never automatically stashed, cleaned, or reverted.

The Lead defines a closed repo-relative write scope before editing. It checks actual disk changes, Git status/diff, preserved baseline content, required checks, and acceptance criteria. Normal corrections continue from the current ticket state without a new baseline or snapshot.

Unexpected scope changes require a user decision, not automatic recovery. Disposable worktrees and benchmarks are not part of normal task orchestration; use them only for an explicitly scoped task or isolation need.

## Review Evidence

Send one concise review request in Polish with the task, acceptance criteria, absolute project path, changed files, baseline `HEAD`, Git status/diff, and actual validation results. The Lead supplies command/filter, expected test identity, and counts when tests apply, because the Reviewer has no terminal tool.

For documentation/configuration-only work, check structure, links, source-of-truth consistency, and relevant editor diagnostics. Do not run Unity solely for Markdown changes. Detailed standalone reports are needed only for failures/blockers or an explicit request.

## Validation And Fallback

Use [unity-validate](../.github/skills/unity-validate/SKILL.md) for C# and Unity changes. Discover the live Pipeline/Editor commands first; use existing CLI batchmode compilation and focused tests when appropriate. PowerShell 5.1 does not support `&&`; pass the absolute assigned project path to `unity run` and `unity test`.

Unity test evidence requires a fresh XML report with the expected fixture, nonzero executed tests, and actual passing results. Keep reports outside project `Temp`, which Unity clears. CLI success text alone is not PASS.

When the Reviewer cannot be invoked, the Lead completes an adversarial self-review of the final diff against all acceptance criteria, relevant regressions, boundary cases, lifecycle, and missing tests. If a previous review raised findings, verify those corrections explicitly too. The owner may also explicitly request solo work.

Disclose `Independent review: Not run` with the concrete cause and the actual `Lead self-review` result. Do not label self-review as independent `PASS`. This fallback allows completion without another approval round only when required checks pass and no material finding or unresolved decision remains. Historical validation and review attributions remain unchanged.

## Configuration Sources

- [Project rules](../AGENTS.md)
- [Lead](../.github/agents/codex-lead.agent.md)
- [Reviewer](../.github/agents/reviewer.agent.md)
- [Unity validation](../.github/skills/unity-validate/SKILL.md)
- [Game-design contract](GameDesignContract.md)
- [Technical roadmap](TechnicalRoadmap.md)