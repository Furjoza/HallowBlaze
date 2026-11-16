# HallowBlaze Testing

## M7.24 Executed Attack Miss Audio

The production-prefab regression first failed because `Enemy.PresentAttack` only created
an audio source for hits. Both existing enemy prefabs now reference the existing authored
`scavengers_enemy1.aif` for misses, separately from the unchanged LightInjury hit clips.
Only replay of a resolved attack requests the outcome-specific cue; no damage, mechanical
noise, turn, RNG, save, animation-off audio policy or audio import settings changed.

The extended `ProductionEnemyAttackReplaysAuthoredAnimation` checks both prefabs and both
animation modes, separate hit/miss request counts, distinct clips with non-silent samples,
mute before/during playback and re-enabling sound, cancellation before/during replay,
synchronization, deactivation and disposal. The complete owning presentation fixture passed
30/30. Batchmode full PlayMode executed 252 tests: 250 passed; the two unchanged
`MenuOpeningExitsShowTheActualAtlas` variants cannot yield `WaitForEndOfFrame` in batchmode.
The final full live graphics PlayMode passed 252/252, with zero failures/skips/inconclusive,
including both actual-atlas variants and the production hit/miss/rest tests. The existing
live report exporter recorded 2026-11-11 20:13:55Z-20:16:11Z. Reports remain outside the
project under `%TEMP%/HB-M724-96b249d0e84b400098af8eaf61f09d06`; the final result is
`live-playmode.xml`. Independent technical review passed on 2026-11-11.

Actual owner listening is Not run. Automated sample/playback assertions are not a listening
verdict and do not accept M7.8, M7.17 or the unresolved M7.26 incident.

## M7.26 Missing Opening Atlas Investigation

The owner reported no atlas after the third opening board on 2026-11-11. PlayMode had ended
before inspection, so the failed live state and its cause were not captured. This incident
is not claimed fixed, and M7.17 remains Revise.

The previous `EveryOpeningBoundaryContinuesWithoutRerollRefillOrEarlyChoice` test asserted
route-choice state without checking the actual atlas. It now also requires a persistent
production atlas, visibility only at the third boundary, an active/interactable canvas and
two legal route buttons. The focused probe passed.

`MenuOpeningExitsShowTheActualAtlas` starts through the production menu using keyboard
activation, traverses all three generated boards through player commands and event replay,
and verifies the actual atlas, two legal options, focus and no implicit new leg/day. A
framebuffer check requires visible selected-route pixels and saves external screenshots as
`%TEMP%/HB-atlas-opening-<False|True>.png`. The same test exits to the menu and resumes the
arrived checkpoint through Continue, requiring the atlas and choices again. Both animation
variants passed with fresh Unity XML at `%TEMP%/HB-atlas-opening-check-final2.xml`
(2026-11-11 18:28:14Z-18:28:27Z, 2/2). Earlier extensions of the probe failed because its
caller omitted the scene load after `ExitToMenu`; that test setup was corrected without
weakening the visibility/Continue requirements.

Read-only checkpoint inspection confirmed completed opening progress at `forest.start`,
one completed day and no active leg. A smoke run using only an external temporary copy of
the two save files also displayed the atlas, four known places, three roads, two choices
and focus on the first route. No road was committed and the original save files were not
used as test persistence. This supports Continue as a recovery probe, not a confirmed fix
for the original missing view.

The final owning-fixture run passed 42/42 with no failures, skips or inconclusive results;
fresh XML at `%TEMP%/HB-atlas-opening-lifecycle.xml` records 2026-11-11
18:30:58Z-18:32:21Z and all three atlas probes. Original profile/run SHA-256 fingerprints
were unchanged after the isolated smoke. No production implementation changed. The known
Unity-generated `SENTIS_ANALYTICS_ENABLED` token is handled under the repository exception;
temporary runner build settings returned to baseline.

Required next evidence is the failed live state or another deterministic reproducer.
Leave PlayMode running on recurrence; inspect the active scene, atlas existence/canvas,
route-choice state, selected object and fresh console before restarting or resetting data.
Do not accept the incident solely because the automated probes are green.

## M7.25 Route Focus And Destination Preview

Owner smoke on 2026-11-11 reported working menu/Controls, opening, legend access, interaction
selection, resource pickups and satisfactory existing audio. The current legend fits its
panel, so absent scrolling is expected rather than a failure. The owner requested clearer
route-button focus and a map-space destination cue because clue names did not make direction
clear. M7.17 remains Revise for that retest and an explicit owner verdict; unreported individual
medical, narrow-layout, terminal-reader and checkpoint cases are not claimed manually tested.

Continue worked for an earlier active save. After Destination reached, Won finalization
persists the profile and removes the ended run save, so Continue is correctly unavailable.
This is independent of the number of discovered nodes/roads. Terminal Restart preserves the
profile; New Game explicitly replaces it. No Continue/save change is part of M7.25.
Route-resource quantities remain M8.3/M8.4, not a newly discovered pickup defect or fabricated
atlas total. At that checkpoint, M7.24 miss audio and M8.5 source knowledge remained deferred.

Route preview now pairs an amber selected option with dark text, bold weight, an outline and
a non-color selection mark; alternatives use a dark background and light text. Pointer hover
sets the same focus that Enter would confirm. The known directed road and its destination are
highlighted, retaining the current-place cue; the map reveals the destination without travel.
A separate destination frame uses a slow two-second opacity pulse only when atlas animations
are enabled. With animations off it is fully visible and static. Neither mode scales text,
controls or map nodes. Prior markers are removed on selection changes and lifecycle boundaries.

The expanded existing route-order regression first failed on the prior implementation because
the focused road left its destination unhighlighted. It now checks both input orderings,
keyboard and pointer, exact active/inactive button states, correct destination, static/pulsed
frame behavior, no draft travel and the unchanged explicitly committed edge ID. The existing
layout test covers one/two/five alternatives at 1280x720 and 320x480, checks visible selected
background pixels and saves external images as `%TEMP%/HB-M725-routes-<count>-<size>.png`.
Focused atlas checks passed 4/4 with fresh Unity XML. Full graphics PlayMode on 2026-11-11
executed 250 tests: 249 passed and the unchanged
`EnemyIntentPresentationTests.Legend_MatchesSymbolsWithoutSubmittingCommands` failed on
an exact ScrollRect endpoint assertion after End: expected 0, actual -1.18285186E-07.
The isolated unchanged test reproduced the same result; no filters or assertions were
changed in that initial attempt. The failed full report is external at
`%TEMP%/tmp65b7572a.tmp` (Unity XML, 17:42:31Z-17:44:12Z). On 2026-11-11 the owner approved
extending the closed scope to the legend test: Home and End still have to reach their
respective endpoints, now within 1e-6 normalized-position tolerance. Actual overflow,
key consumption and domain-state assertions remain. Production legend code and test
filters are unchanged. The corrected focused test passed 1/1 with fresh XML. The final
full graphics PlayMode rerun passed 250/250, with zero failures/skips/inconclusive and
expected atlas/legend/terminal identities verified in fresh XML outside the project
(`%TEMP%/tmp7d5bc663.tmp`, 2026-11-11 17:54:30Z-17:56:15Z). Independent review passed on
2026-11-11 after inspecting the implementation, approved endpoint assertions and fresh reports.
Automated rendering does not replace the owner's route-readability verdict.

## M7.23 Shared Atlas And Keyboard Follow-Up

The owner accepted additional W/S menu navigation on 2026-11-09: W/A select the previous
control, S/D the next; arrows and Tab/Shift+Tab remain. In the contextual picker, A/D join
Q/E and left/right for objects, W/S join up/down and Tab/Shift+Tab for per-object actions.
Only explicit confirmation resolves a turn. The selected option text, numbered target and
diamond outline share `InteractionPresenter.SelectionColor`, the authored menu selected
color `(150, 200, 200, 255)`; unselected targets remain white squares.

L toggles the enemy-intent legend on an idle board. Page Up/Down and Home/End scroll it;
blocked and duplicate samples are discarded, and an L/movement chord cannot produce a move.

The standalone Field Notes reader no longer exists. N or the book opens the shared atlas
without observing exits, changing knowledge or selecting travel. Known nodes and roads have
mouse and keyboard controls; the former MAP KEY area contains their knowledge-filtered
details. Observations opens the profile-owned fact list in the same inspector, retaining
distinct icons, full text, provenance and neutral missing-definition entries. Reading owns
its independent input block and closing holds it through the frame. Required route choice
and the final atlas cannot be cancelled as voluntary reading.

Only the transient current-board discovery queue is cleared when leaving the board. Saved
facts, deduplication and discovery counts remain. Death/new run preserve profile knowledge;
explicit New Game still replaces the profile. There is no DTO/schema or generator change.
M8.3 route-family clues and M8.4 structured resource observations remain Planned. M8.4 is
min/max/count for completed visits, not a promised average; the current inspector cannot
invent berry totals or treat unrecorded resources as zero. Miss audio is deferred to M7.24.

Focused checks reuse `AtlasScreenTests`, `FieldNotesPresentationTests`,
`EnemyIntentPresentationTests`, `AxePresentationTests`, `OrderedBoardPresentationTests` and
`GameSessionLifecycleTests`. They exercise all known map controls and reject unknown IDs,
keep inspection save/turn-free, preserve explicit route identity, cover modal/scene/death/reset
lifecycle and render desktop/narrow pixel evidence. Atlas text uses fixed pixel sizes, a
pannable map and a scrollable inspector; resizing preserves the selected knowledge entry.
`PlayModeInfrastructureTests.WriteLiveTestReport` arms one official Unity Test Framework
XML outside the project across a live PlayMode domain reload. Final acceptance requires a
fresh report with expected identities, nonzero execution and zero failures, then review.
Automated input/rendering is not the owner's physical-key or visual/listening verdict.

On 2026-11-10 the final graphics PlayMode regression passed 250/250 with a fresh XML,
zero failures/skips and the expected atlas, interaction, legend, knowledge and production
lifecycle test identities. Focused atlas checks passed 4/4 and knowledge checks 7/7; the final
desktop/portrait render recheck passed 2/2. Independent review found terminal focus ownership,
retained final-atlas visibility and production death-popup gaps. All three were corrected;
the expanded lifecycle fixture passed 40/40 before the final full regression. It opens reading
through the production N sampler after victory, confirms facts without restarting, closes with
N/Escape/X, restores terminal actions and the map, and checks an unread popup through actual
health/starvation `HandleBoardOutcome` paths and subsequent reading. Independent recheck passed
on 2026-11-10 after inspecting the corrected code, diff and fresh reports.
M7.17 still requires the owner's retest; no visual/listening acceptance is inferred here.

## M7.9 Three-Board Opening

Fresh New Game and post-death runs use exactly three ordered boards on
`road.shelter-warm-up`, from `shelter.warm-up` to `forest.start`, all on Day 1.
The content revision is encoded in new stable segment IDs, not a profile/world reset or
a schema change. World `world-1`/1, profile schema 2 and run schema 4 remain unchanged.

| Board | Segment ID | Generation | Food items | Obstacles | Enemies |
| --- | --- | --- | --- | --- | --- |
| 1 | `segment.shelter-warm-up.v2.entry` | `ordinary.board.v1`/3 | 3-4 | 5-7 | 0 |
| 2 | `segment.shelter-warm-up.v2.stretch` | `ordinary.board.v1`/3 | 2-3 | 6-8 | 0 |
| 3 | `segment.shelter-warm-up.v2.arrival` | `ordinary.board.v1`/3 | 1-2 | 7-9 | 1 existing Shambler |

All stages retain the ordinary 8-14-move path target and version-3 bounded entry-safety
validation. Food budgets count existing food/soda items, not Food restored. Initial
100 Health/100 Food and no automatic tool grant are unchanged. Stage presets apply only
to these exact Day 1 edge/segment/version identities; old ordinary configurations retain
their prior budgets. The complete address still includes run, leg, edge, segment, day,
generation identity and deterministic seed. No random opening-length roll is introduced.

Boards 1 and 2 checkpoint the next segment and supplies without completing a day,
discovering `forest.start` or publishing choices. Board 3 alone commits Day 1, history and
arrival; the first selected road begins Day 2 and the earliest Radio Tower remains Day 6.
Pending/Consumed intro, rollback, duplicate/foreign outcome rejection and terminal ordering
use the existing travel mechanism. Continue restores each exact entry checkpoint, not
transient mid-board costs or rewards, and never replays a consumed Day intro.

Compatibility is deliberately narrow: saved `segment.shelter-warm-up.day1` on the named
Day 1 road keeps its one-board list and ordinary versions 1/2/3 or legacy version 1. Both
active-address validation and completed-history reconciliation recognize that historical
publication against the exact new three-segment catalogue. The old board is generated by
its original profile, finishes once and records one completed board, never three. Other
unknown IDs/versions/counts still reject. Existing `forest.start` checkpoints, other roads,
selected landmarks, resources and profile knowledge are not reset or prepended.

`WorldDefinitionTests` covers publication, historical versions, rejected foreign content,
all three durable boundaries and Day 2. `RouteLegTests` includes three-segment save-failure
rollback/duplicate tests. `EveryOpeningBoundaryContinuesWithoutRerollRefillOrEarlyChoice`
uses real Main reloads at each entry, compares addresses/layout hashes/Health/Food and
requires exactly one published choice. `OldOneBoardOpeningContinuesAndFinishesWithoutExtraBoards`
executes the retained old layout through its real exit. The integrated opening fixture
executes all three generated boards in both animation modes; menu-only traversal fixtures
remain combat-free so random enemy occupancy cannot obscure menu/reset assertions.
`ThreeBoardOpeningSupplyAndSafetyRepeatForOneHundredRuns` checks 100 complete openings,
300 boards twice, exact staged budgets, entry safety and equal deterministic summaries;
its artifacts are outside the project under `%TEMP%/HB-M46-*`.

The existing production seed selector previously relied on the one-board edge to fill
any requested count. With lengths three and five it could loop at an unfillable remainder;
that first full run was stopped, not counted as evidence. The selector now checks reachable
remainders while retaining complete traversals, all 47 published ordinary segment IDs and
ten day strata. The corrected daily 1000-board repeated series passed.

On 2026-11-04 final EditMode passed 1258 tests with the existing Explicit 10k milestone
skip, graphics PlayMode passed 234/234, world/compatibility checks passed 26/26 and the
opening series had zero invalid boards or fallbacks. Both 300-board runs produced
`1007f917b3b838806727e6511b59d7891b017a2e2d6045508ea4e6577d72d141`.
Independent review passed. Physical-key, audio and live-window owner acceptance
remains M7.17; original M7.8/M4.9/M6.6 human gates remain open.

## M7.10 Geographical Route Choices

The atlas orders complete exit-option objects by authoritative `WorldDirection`, never by
destination name, atlas discovery or incoming enumeration. Its left-to-right matrix is
`west`, `northwest`, `southwest`, `north`, `south`, `northeast`, `east`, `southeast`, then
unrecognized directions. Equal ranks use ordinal stable edge ID. Direction and clue stay
attached to the exact ID submitted to the existing route command; neither destination
identity nor undiscovered topology is added to labels. In particular the published
`northwest`/`northeast` choice now puts west on the left.

A single option is centered; two occupy the same horizontal row. Additional alternatives
extend that row in a clipped, pointer-scrollable viewport rather than becoming a misleading
second west/east row. Arrows/Tab visit the visual order and reveal newly focused options;
resize reveals retained focus, but unchanged focus does not undo manual scrolling. Text
keeps a readable pixel size and names the actual direction without color-only cues.
Revealing changed focus synchronizes its outline and stops pointer-scroll inertia, so the
selected option remains visible in subsequent frames after keyboard takeover.
Disabled controls are skipped; empty legal-choice lists remain empty. Focus never commits.
Reopening rebuilds the same deterministic order without changing saved route identity.

`AtlasScreenTests` covers reversed inputs, every cardinal/diagonal rank and unrecognized
directions, ordinal ties, unchanged input, matching clue/edge submission, free keyboard
browsing, single/two/five alternatives, disabled controls and manual-scroll retention.
Graphics checks measure focused bounds and full label height at 1280x720 and 320x480,
require nonblank pixels and save `%TEMP%/HB-M710-routes-<count>-<size>.png`.
On 2026-11-04 full graphics PlayMode passed 232/232; the final atlas checks after the
manual-scroll and inertia corrections passed 4/4. These injected-edge/render-target checks do not claim
physical-key or live-window human acceptance, which remains M7.17. No graph, save schema,
route ID, travel cost, scene or prefab changes are involved.

## M7.19 Owner Smoke Follow-Up

**Superseded details (2026-11-06):** M7.22 corrects three M7.19 interpretations. At that publication,
menu/route aliases were A/D and arrows, not W/S; M7.23 later accepts W/S as additional aliases.
Fresh ordinary/6 berries are on a real blocking
bush and require existing manual/axe clearing before +5 Food collection on entry. Movement
noise lasts only its own turn; interaction/tool noise and full authored audio are unchanged.
The owner explicitly kept tool-source discovery/atlas icons in the existing M8.5 order.
The dated 2026-11-05 evidence below remains historical, not the current rule for these details.

On 2026-11-05 the owner completed the entire game using only the keyboard. The verdict is
**Revise**, not acceptance: the report also identified menu focus/hover, Controls placement
and clipping, floating berries, planar sprite rotation, full-Health medical wording, missing
A/S route keys, misunderstood step audio/noise, terminal dead-end and missing axe-source marker.

The bounded repair removes the added focus frame, restores visible tint/hover, centers Controls
and preserves scrolling/lifetime, renders a cosmetic berry bush, uses free left/right sprite
mirroring, explains unavailable first aid and supports A/S plus W/D in menus/routes. Full
authored step audio is restored; instant steps remain silent and cancellation/recovery/disposal
stop only their owned cue. The explicit world goal now finalizes Won with retryable persistence,
retains the final atlas, and offers existing keyboard Restart/Menu without erasing profile
knowledge. The terminal bar is removed before menu/new-run transitions.

Final graphics PlayMode passed 248/248, including fresh XML identities and the two added
host/ancestor deactivation cases. Main/pause Controls tests verify actual selected scroll tint,
centering, all contexts, text bounds and the last binding at desktop/narrow sizes. Terminal
tests cover desktop/portrait bounds, keyboard actions, profile/statistics preservation, ended-run
deletion, cleanup/focus return and final-save failure/retry. Independent review and the focused
recheck passed. Automated rendering/playback is not an owner visual/listening verdict.

M7.20 tracks the separately requested Controls redesign; M7.21 tracks proper directional art.
The owner clarified that the lingering mechanical step-noise footprint, not the audio clip,
was the original concern. Its requested lifetime remains unresolved and the domain rule is
unchanged. The axe's stable source ID exists, but profile source discovery and atlas markers
are still M8.5; the absent marker is recorded rather than represented by a cosmetic fake.
These remain explicit follow-ups and M7.17 remains Revise pending owner decisions and retest.

## M7.22 Clarified Berry, Input and Noise Rules

`VersionSixBerriesRequireAccessibleBlockingBushes` checks deterministic supply, real blocking
`legacy.wall` bushes, existing clearing support, approachable positions and validator acceptance
over 100 seeds. `GuardedBerryValidationAlsoAppliesToFallbacks` corrupts a berry placement and
requires rejection, then forces each authored fallback through the same independent validator.
Historical catalogs/layouts 1-5 and serialized route addresses/seeds remain covered separately.

`ProductionFoodVarietiesCollectOnceWithExactHudGains` covers old version 4 and guarded version 6
with both animation modes. Blocked berry entry spends nothing, cannot move or grant Food,
and the picker cannot collect through the bush. Existing manual work removes the actual
obstacle while retaining the berry; synchronization cannot restore the bush. Entry then grants
the original fixed +5 once. Carrot behavior is unchanged. Continue rebuilds versions 1-6.

Atlas tests exercise A/D and arrows, assert that W/S are absent/ignored, and retain the free
browse-versus-confirm boundary. `NoiseTurnTests` checks movement lifetime 1 through actual
controller phases: the signal exists in the emitting turn, rejected input does not age it,
and the next accepted enemy phase sees it expired. Prototype interaction remains strength 4,
lifetime 2. Immutable noise snapshots and custom shared-lifetime profiles remain supported.
These tests use isolated saves. Owner visual acceptance remains the M7.17 retest; M8.5 is
explicitly deferred in its existing order rather than silently included in this repair.

Final validation on 2026-11-06: EditMode 1301 passed with one expected Explicit skip;
graphics PlayMode 250/250; the explicit 10,000-seed series repeated twice with zero invalid
boards/fallbacks and identical fingerprint `175600ac1f6a5091858ae2090f93866d3a501bb62d7b9cf602437e9aa7248a0f`.
Independent review passed. The world comparison against the baseline confirms that only
the 47 ordinary segment versions changed; landmark and topology are identical.

## M7.13 Illustrated Controls

The main menu offers Controls directly; the legacy Help -> Controls action opens the same
read-only view. Pause offers its own Controls entry. The modal retains the parent menu and
focus, and never resumes gameplay when it closes. Its Escape closing frame is consumed before
pause can handle another Escape. Entries retain visual/navigation order; settings and save
semantics are unchanged. No scene, prefab or package modification is required.

Walking, Interaction and Menus context tabs show a keyboard diagram and associated functions.
`PcInputBindings.GetHints` reads actual movement bindings, F/Q/E/Space constants, picker
aliases and menu keys; it does not read profile discoveries or world topology. Active keys
have both color and heavier borders, the selected context has an underline, and functions
are always listed as text. Focus uses the controls' own selection colors without an extra frame;
the keypad Enter alias has its own labeled keycap. Free visual facing is explicitly distinct from Wait;
interaction Q/E selects objects and Space confirms. Per-object actions and replacement slots,
Notes, pause, route activation, sliders and scrolling have their actual compatible aliases.

The diagram uses fixed-size type and structured keycaps, not a screenshot with stale labels.
At widths below 960 pixels it stacks above a single keyboard-scrollable binding list. Context
changes and resize rebuild labels at measured heights. Keyboard focus reaches Back, context
tabs and the scroll region; Page Up/Down/Home/End scroll without a pointer. No animation is
required. Viewing/changing context does not submit, mutate or persist gameplay.
The modal uses an independent fullscreen canvas so host-panel placement/scaling cannot offset
it. The owning menu lifetime still closes it. Main-menu Controls tints its text like its neighbors.

`ControlsHintsMatchTheImplementedContextualKeys` verifies binding parity.
`PauseControlsCanOpenAndCloseWithKeyboardOnly` verifies tabs, narrow scroll, focus return,
same-frame Escape suppression and zero costs. Production scene checks
`MainControlsDiagramAndContextsStayReadable` and `ProductionPauseControlsPreservesRunAndFocus`
cover both menu entries, all contexts, text bounds, diagram key coverage, actual nonblank
pixels, return focus and unchanged full run serialization/resolver counts. Representative
renders use 1280x720, 320x480 and 480x640 at `%TEMP%/HB-M713-<context>-<size>.png`.
The tests resize render targets/layouts and inject keyboard edges; they do not claim physical
keyboard or OS-window owner acceptance, which remains open in M7.17.

## M7.18 / M7.19 Cosmetic Facing

The 2026-11-05 owner smoke rejected planar 90-degree sprite rotation. An isolated Q edge now
faces left by horizontal mirroring and E faces right; repeated selection of the same facing
does not rotate or spin. Proper directional artwork is the future M7.21 task. The transient
child SpriteRenderer follows the root Animator's current
sprite, color and sorting; the authoritative root, Rigidbody2D and colliders never rotate.
The authored renderer is retained for animation and restored when this component disables.
Movement animation preserves cosmetic facing; WASD/arrows still produce absolute grid moves.
Facing resets on board reconstruction and is absent from all run/profile/board persistence.

The current player's full idle walking gate admits facing changes. Interaction, pause, Notes,
setup/Day, replay, terminal, disposal and replaced views block and discard the edge.
Q+E does nothing. Movement/Wait/F/N/Escape takes priority over facing in the same sample.
Held keys without another key-down edge do not spin; repeated sampling of one frame cannot
change facing twice or defer a blocked change. No TurnCommand, resolver, RNG or domain-state
reference exists in `PlayerVisualFacing`.

`VisualFacingTurnsOnlyAnIsolatedCosmeticPivot` verifies left/right flip, identity rotation, frame/hold,
chords and unchanged actor/collider transform. `VisualTurnsDoNotResolveAgeNoiseOrReplanEnemies`
compares forty turns with serialized run state, entity positions, retained Listener intent,
noise identity/lifetime and the next emission ID; resolution count stays zero. Interaction
E selects without rotating. `ProductionVisualTurnsAreVisibleFreeAndGated` captures four real
alternating Main facings, checks changed pixels and fixed actor transforms, modal/replay/terminal
gates, preserved nonzero facing through an absolute move and a normally costed Wait.
Representative production renders are in `%TEMP%/HB-M718-facing-{1|3}.png`.
These are graphics/injected-edge checks; integrated physical-key owner acceptance is M7.17.

## M7.12 Contextual Interaction Keys

`PcInputBindings` is the shared source for command keys, modal selection and subsequent
visual-facing/Controls work. Interaction entry and normal-command suppression both use F;
E no longer opens a draft. Opening retains an unselected draft: browsing and confirmation
are separate. Objects are ordered by Y, X and stable entity ID; actions on one object retain
their explicit query/action/slot order. A refresh retains the same valid object/action/slot,
never substitutes an unavailable action, and cancels a nested comparison rather than replacing it.

| Context / edge | Effect | Command / cost |
| --- | --- | --- |
| Idle F, including F + a normal command key | Open draft, or stay idle when empty/blocked | None; normal command sampling suppresses F |
| Draft or replacement F / Escape | Cancel the complete draft | None; block remains through closing frame |
| Draft Q / E, left / right | Previous / next object, deterministic wraparound | None; never rotates |
| Draft Tab / Shift+Tab, down / up | Next / previous explicit action on that object | None |
| Replacement Q/E, arrows, Tab/Shift+Tab | Select a specific occupied slot | None |
| Draft Space / Enter / keypad Enter | Confirm the selected complete action, or enter explicit replacement | At most one existing resolver invocation; accepted effects use normal costs |
| Ambiguous navigation + confirmation or opposite navigation | No selection/action | None |
| Repeated sample in the same frame / held key with no new down edge | No second toggle/action | None |
| Stale target/tool, pause, terminal, disposed/replaced board | Reject free or refuse input through the existing gates | No accepted turn/resource/noise effect |

`AxePresentationTests.ContextualKeysChooseManualOrToolWithoutLeakingWait` and
`ContextualManualSpaceResolvesOneWorkAction` execute production picker inputs and verify
single resolution, actual obstacle progress, Food/charge and emitted-noise counts.
`ToolPickupPresentationTests` covers multiple objects, stable refresh, wraparound, nested
F cancellation, same-frame ownership, empty/stale/modal/disposed cases and pointer parity.
`PlayModeInfrastructureTests.InteractionEntrySuppressesNormalCommandButIdleEDoesNot`
guards every source-level opening/suppression migration. Full PlayMode is the integration gate.
Idle Q/E visual behavior is implemented separately in M7.18; human physical-key acceptance
remains M7.17, not a claimed outcome of injected-key tests.

## M7.11 Keyboard Menu Ownership

`KeyboardMenuFocus` owns navigation only for the top visible menu. Focus is a persistent
white outline, not hover or color alone. Tab/Shift+Tab and arrows traverse active,
interactable controls; Enter/keypad Enter/Space activate the existing UI submit handler.
Left/right adjust a focused slider. Text editing retains its native keys; Tab leaves the
field. Page Up/Page Down/Home/End scroll the focused region. Navigation reveals selected
rows inside a scroll viewport. Disabled controls and invisible CanvasGroups are skipped.
Opening and closing frames cannot submit the same keyboard edge to another menu.

| Screen / actions | Keys and focus owner | Exit / return |
| --- | --- | --- |
| Main: New Game, Continue, Help, Settings, local results, Quit | `PanelScript` + `KeyboardMenuFocus`; disabled Continue skipped | Existing explicit actions; root has no implicit Quit |
| Help, Authors, Donate, legacy Controls, local results, record form | Same owner; text field keeps editing keys | Back button or Escape invokes the existing Back action; retained valid focus on reopen |
| Main settings: music/sound and Back | `SettingsPanelController` installs the same panel policy | Existing Back action or Escape; settings semantics unchanged |
| Pause: Resume, Settings, Exit to Menu | `PauseMenuController` + menu focus | Escape/Resume; existing deferred gameplay-input release |
| Pause settings | Separate menu focus; audio buttons use their existing actions | Escape/Back; parent focus retained |
| Route choice / atlas | `AtlasScreenController` + menu focus; arrows/Tab browse, Enter/Space submit the focused edge | Choice commits only through the existing route action; zero-exit final atlas has no fabricated route or cancel |
| Field Notes: known entries, list and detail regions, Close | `FieldNotesPresenter` + menu focus; N opens; both scroll regions are focusable | N/Escape/Close; returns to book and holds the gameplay gate through the frame |
| Interaction / tool pickup / replacement | Board-owned `InteractionPresenter`; current F/Q/E/Space and per-object action policy is defined above under M7.12; selected action scrolls into view | Free cancellation and delayed interaction-gate release |
| Death: Restart, Menu; post-death Notes | Terminal canvas owns focus; Notes temporarily owns the top context | Explicit existing actions only; terminal focus ends when a run starts |

Focused graphics PlayMode coverage: `PauseMenuTests`, `FieldNotesPresentationTests`,
`AtlasScreenTests`, `ToolPickupPresentationTests`, and
`GameSessionLifecycleTests.KeyboardMenuSceneFocusAndBackArePointerFree` /
`KeyboardTerminalFocusBelongsToVisibleActions`. The scene tests navigate production Menu
and inspect production terminal controls without a pointer; the component tests exercise
disabled/empty controls, nested ownership, pause/resume, scrolling layouts, replacement,
resource preservation and disposal. Saves use isolated temporary directories.
`KeyboardContinueRouteChoiceCannotBeStolenByEmptyTerminalPanel` verifies real Main Continue
at a route checkpoint. Pause disable cases close both views before unlocking gameplay.
Picker scrolling measures actual rendered row/viewport bounds, including wrapped replacement
rows. `PickerRevealsRetainedRowAfterViewportShrinks` renders Game View with an active camera
and shrinks the panel without changing selection; geometry changes reveal the retained row
again, while unchanged geometry does not override manual scrolling.
These are automated keyboard-handler/scene checks, not a claim of human physical-key or
OS-window testing. The complete owner keyboard/visual acceptance remains in M7.17.

## M7.8 Integrated Repair Gate

**Status (2026-10-30):** Technical validation passed; owner visual/audio/live-window
resize verdict **Not run / Pending**. M7.1-M7.7 are independently reviewed fixes, not owner
acceptance of the complete milestone. M4.9 and M6.6 human gates also remain open. No M8
authorization or original screenshot-seed reconstruction follows from these automated checks.

| Report / requirement | Reproduction and owning repair | Automated evidence | Remaining owner check |
| --- | --- | --- | --- |
| First choice follows the first playable day | M7.1 automatic warm-up leg; old checkpoints unchanged | Lifecycle opening/death/retry/Continue cases and integrated real-command opening | Confirm first playable flow and Day readability |
| Flash, white areas, residual board/legend | M7.2 world visibility and full-viewport clear ownership | `BoardViewportClearsOutsideTheGameplayCamera`, `ProductionLayerPixelsStayCleanAcrossResizeAndBoards`; tinted consecutive boards and overlay pixels | Observe real-window transitions without flashes/residue |
| Notes duplicate label / blinking book | M7.3 parented hover hints and stable control visual state | `FieldNotesPresentationTests`, including real busy/hover/control lifetime and portrait bounds | Hover and move repeatedly; inspect book and close hints |
| Step audio and mechanical noise | M7.19 restores full authored audio; M7.22 limits the domain step signal to its emitting turn without changing interaction/tool noise | Infrastructure audio ownership and NoiseTurnTests phase/lifetime/rejection checks | Listen to restored clips and inspect the one-turn footprint |
| Lost Food/Health `+X` | M7.5 applied gains survive accepted-turn replay and free rejection, clear at next accepted replay/boundaries | Infrastructure food/health/rejection tests; integrated actual restore, rejection and food reward feedback | Inspect gain readability through a complete turn |
| Suspected corner entry trap | M7.6 explicit synthetic trap plus retained v2 diagnostic; new v3 bounded real-rule witness | Validator trap/narrow/fallback tests, published 1000-board audit, explicit 10k x2, production Day-4 witness | Smoke clearly labeled synthetic case; original seed/resources/history still missing |
| Board/resource alignment and clipping | M7.7 symmetric viewport, fixed resource frames, shared legend/discovery slot | `ProductionBoardAndResourceHudStayCenteredAndFit`: four aspect ratios, two boards, long gains, glyph/raster/control bounds and durable discovery | Actual OS-window resize and visual inspection at desktop/portrait |
| Combined opening / later board / overlays / Continue | M7.8 regression only, no production expansion | `IntegratedRepairOpeningOverlaysAndContinueStayCoherent`, both animation modes, isolated persistent profile/run | Complete production smoke and explicit Accept or Revise |

The integrated fixture fixes run identity/seed, executes real commands to leave the generated
enemy-free warm-up, checks the first persisted route and Day 2, then executes the real bounded
entry witness with unchanged health. It checks rejected-command cost/sound/feedback, actual
restoration and food gain feedback, distinct food/step cues, repeated Notes/legend use and
production overlay captures. Continue restores the same pinned address and board-entry
checkpoint resources without replaying Day; committed profile knowledge survives. Board
progress remains transient by the existing checkpoint contract. This is not full tactical
completion of every later board. Fact feedback is injected at the recorder boundary, not
represented as an actual pit command. Existing separate mechanics tests execute that command.

Run final gates sequentially with the existing Unity CLI against this exact project, using
external temporary XML/log paths and no second Editor. Inspect each fresh XML for identity,
nonzero execution, zero failures and expected Explicit skip; run the milestone explicitly.

```powershell
unity test D:/Repos/HallowBlaze/HallowBlaze --mode EditMode --output "$env:TEMP/HB-M78-editmode.xml" -- -nographics -logFile "$env:TEMP/HB-M78-editmode.log"
unity test D:/Repos/HallowBlaze/HallowBlaze --mode PlayMode --output "$env:TEMP/HB-M78-playmode.xml" -- -logFile "$env:TEMP/HB-M78-playmode.log"
unity test D:/Repos/HallowBlaze/HallowBlaze --mode EditMode --filter HallowBlaze.Tests.EditMode.BoardGenerationSeedSeriesTests.Milestone10000SeedsHaveNoInvalidBoardsAndRepeatExactly --output "$env:TEMP/HB-M78-10000.xml" -- -nographics -logFile "$env:TEMP/HB-M78-10000.log"
```

For owner smoke, use isolated playtest storage, never reset or overwrite the real profile.
Start from the warm-up, leave via actual commands, choose a route, visit later boards/days,
and repeatedly open/close legend and Notes, including an unacknowledged discovery notice.
Resize the real window between desktop, wide and portrait sizes; inspect all resources,
suffixes, controls and board edges. Listen with sound enabled, disabled and animation off.
Also inspect the clearly labeled synthetic entry case; do not label it screenshot #3.
Record date, build/commit, sizes, observed issues and explicit **Accept** or **Revise**.
Until that evidence exists M7.8 stays Active and M7 is not complete. Automated captures are
render-target evidence, not a substitute owner verdict or a human survey session.

**Actual final automated results (2026-10-30):** Full EditMode passed 1247 tests with zero
failures and the existing Explicit 10k skip; full graphics PlayMode passed 206/206, including
the integrated case in both animation modes. The separately selected milestone passed
10,000 boards twice with zero invalid/fallbacks, unchanged predeclared 1% fallback and
300-second limits, 34.81 / 34.55 seconds and identical fingerprint
`b34eb98ddcd6d17e9451199e8d9fc2c4f2f3b38b4504425efaab810a1c47969b`.
Fresh XML/log evidence is outside the project at
`%TEMP%/HB-M71-resume-20261029/m78-{integrated|editmode|playmode|10000}.{xml|log}`.
Independent read-only review passed the technical candidate on 2026-10-30. This verdict
accepts the test implementation and evidence checkpoint only, not the full M7.8 owner gate.
No human visual/audio verdict, actual OS-window resize, survey response or recovered original
day-four seed is claimed. Those omissions continue to block final M7 acceptance.

## M7.7 Centered Board And Resource HUD

The gameplay viewport and Food/Health group share horizontal center `windowWidth / 2`.
For widths at least 800 px the left and right reservations are both 224 px. Narrower
windows use symmetric 56 px side reservations for the Notes book, plus a 160 px bottom
legend area. All sizes reserve the existing 68 px top equipment band and an 80 px resource
band below gameplay. The camera fits the unchanged authoritative board bounds into the
remaining area; the old authored overscan is not retained during gameplay. Its original
rect/size are restored when the board presentation ends.

The resource group is centered, at most 720 px wide, with 8 px outer padding. Its fixed
64 px content area uses two columns at widths at least 600 px, otherwise two 32 px rows.
Labels retain the prototype font at a fixed size of 14, centered and wrapped, without
viewport-dependent best-fit font scaling. Gain feedback does not resize the frames.
Legend placement excludes the union of the board and resource bounds. No scene/prefab,
grid coordinate, board identity, run value or gain-lifetime change is part of this repair.
Committed discovery feedback uses the legend's free panel rectangle. Opening the informational
legend temporarily obscures feedback without acknowledging or dropping the discovery queue;
closing it or ending the board restores unacknowledged feedback. Notes modal ownership is
unchanged. This shared slot prevents the previous notification from covering the portrait board.

`GameSessionLifecycleTests.ProductionBoardAndResourceHudStayCenteredAndFit` captures the
real scene at 1280x720, 1920x720, 320x480 and 320x640, across two consecutive boards.
It checks the shared center, complete board bounds (at least 160 px), long Int32 totals,
the accumulated `+137438953408` suffix, generated glyph/word/height bounds, nonblank label
pixels, digit-count frame stability and no overlap with tools, Notes or the open legend.
The fixture injects an observation at the production recorder boundary, asserts real isolated
profile persistence and visible feedback, then cycles Notes and legend, resize and board end.
It does not claim to execute a pit command in that layout case; the existing Field Notes
suite separately covers actual pit/axe/Listener presentation and discovery.
Before/after files are outside the project:
`%TEMP%/HB-M77-layout-before-<width>x<height>.png` and
`%TEMP%/HB-M77-layout-after-board<0|1>-<width>x<height>.png`.
The before test failed with a 112 px desktop offset and labels outside portrait screens.
Existing layer tests also cycle Day, Notes and tinted boards with animation on/off.

These are explicitly resized render targets with production layout calls, not a claim
of manual OS-window resizing or owner visual acceptance. Live-window resize and visual/audio
smoke remain required at M7.8. Use the lifecycle CLI command below without `-nographics`.

## M7.2 Presentation Boundaries

The production lifecycle fixture covers the initial Day, consumed-intro Continue, overlay
cancellation, first route choice and scene reload. Graphics cases additionally exercise
1280x720, 320x480 and 320x640 render targets with two distinctly tinted consecutive boards
and animation enabled/disabled. Captures remain outside the project at
`%TEMP%/HB-M72-{Day|Notes|Board}-<width>x<height>.png`.

| State | World | Resource HUD | Board Controls | Field Notes |
| --- | --- | --- | --- | --- |
| Setup / Day | Hidden by the original camera's zero culling mask | Hidden | Hidden | Closed and unavailable |
| Gameplay / legend | Visible | Visible | Visible; legend is informational | Available |
| Notes modal | Behind the opaque Notes canvas | Behind Notes | Legend closed; legend and tool controls hidden | Owns only its modal input block |
| Route choice | Hidden | Hidden | Disposed or hidden | Closed and unavailable |
| Death / terminal | Hidden | Hidden | Disposed or hidden | Available after death with its existing profile lifetime |
| Leaving Main | Scene owns its original mask again | Scene-owned | Disposed | Closed and unavailable |

The original camera previously continued to draw the player during Day and cleared only its
reduced viewport. A scene-owned, no-object full-viewport clear camera now clears the complete
display first using the original camera's authored background color. It does not alter the
board geometry, viewport reservations, save data or gameplay camera color. A magenta previous
frame regression proves that reserved margins are overwritten rather than retaining pixels.
Board boundaries close/dispose overlays and cancel the previous delayed Day callback.

Run the graphics fixture through Unity CLI, without `-nographics`:

```powershell
unity test D:/Repos/HallowBlaze/HallowBlaze --mode PlayMode --filter HallowBlaze.Tests.PlayMode.GameSessionLifecycleTests --output "$env:TEMP/HB-M72-lifecycle.xml" -- -logFile "$env:TEMP/HB-M72-lifecycle.log"
```

Inspect fresh XML identities, nonzero executed counts and failures. These tests reproduce
mechanisms in the production scene; they do not reconstruct the owner's historical frames
or replace the explicit M7.8 owner visual/audio smoke gate.

## M6.6 Curated Landmark Variants

[Protocol, variant/cost matrix and actual evidence](Validation/M6.6.md) separate technical
validation from the Pending human gate. Three authored variants solve and execute both outcomes
with and without the axe. Focused coverage includes addressed selection/order independence,
stable golden vectors, old saved selections, checkpoint-before-presentation, failure rollback,
Continue without reroll/extra writes, all outcome branches and the bounded geometric softlock audit.

```powershell
$project = (Get-Location).Path
unity test $project --mode EditMode --filter 'HallowBlaze.Tests.EditMode.LandmarkTemplateTests;HallowBlaze.Tests.EditMode.LandmarkSolverTests;HallowBlaze.Tests.EditMode.RadioTowerLandmarkTests;HallowBlaze.Tests.EditMode.LandmarkProgressTests' --output "$env:TEMP/HB-M66-domain-current.xml" -- -nographics -logFile "$env:TEMP/HB-M66-domain-current.log"
unity test $project --mode PlayMode --filter HallowBlaze.Tests.PlayMode.BoardRuntimeStartupTests.LandmarkProduction --output "$env:TEMP/HB-M66-production-current.xml" -- -logFile "$env:TEMP/HB-M66-production-current.log"
unity test $project --mode EditMode --output "$env:TEMP/HB-M66-editmode-current.xml" -- -nographics -logFile "$env:TEMP/HB-M66-editmode-current.log"
unity test $project --mode PlayMode --output "$env:TEMP/HB-M66-playmode-current.xml" -- -logFile "$env:TEMP/HB-M66-playmode-current.log"
```

Run sequentially against this exact project with no other Editor. Inspect fresh XML for expected
identities, nonzero counts and results. Do not add `-nographics` to the PlayMode capture gate.
On 2026-10-21 the final production group passed **7/7**, full EditMode passed **1237 with zero
failures and the one existing Explicit 10000-seed skip**, and full graphics PlayMode passed
**188/188**. No compile errors were reported. The code suites preceded the authorized removal
of Unity's generated analytics define; only that token and Markdown changed afterward.

All three variants have neutral before and moderator-only gate-after captures at desktop/portrait
sizes, plus a tested explicit exporter for isolated pinned profile/run boundaries. The exporter
does not launch gameplay or redirect the player's storage: follow the protocol's isolation
preflight, use a fresh temporary session per attempt, and never overwrite the real profile.
Captures and automated execution do not count as sessions. The owner authorized direct work and
self-review, not an independent verdict. M6.6 is Active with zero human participants; M4.9 is
still Pending and M7 is not authorized by these checks.

## M6.5 Bounded Landmark Solver

[LandmarkSolverTests](../Assets/Tests/EditMode/LandmarkSolverTests.cs) checks real-controller
paths, not a second movement or enemy engine. The [model, budgets and measured paths](BoardGeneration.md#m65-bounded-landmark-solver)
define the exact supported scope. Both published outcomes are solved dynamically within
50,000 states / 32 accepted commands / 10 seconds and executed on fresh owners. The supply
outcome is also solved without an axe. Repeated searches compare paths and state counts,
not elapsed time. A minimal authored blocked variant must return exhaustive `NoSolution`;
state, depth and time caps have separate incomplete results.

The fixture covers structural equality across independent snapshots, constant-hash collisions,
causal-field mutation and key immutability, caller isolation before/after construction, retained
Wait/Rest copying without initial replanning, terminal exits, starvation on an exit, invalid
budgets, unsupported equipment and missing enemy catalog. Pickup path execution rebinds to
the exact ground snapshot on the fresh board rather than weakening stale-command validation.

```powershell
$project = (Get-Location).Path
unity test $project --mode EditMode --filter HallowBlaze.Tests.EditMode.LandmarkSolverTests --output "$env:TEMP/HB-M65-focused.xml" -- -nographics -logFile "$env:TEMP/HB-M65-focused.log"
unity test $project --mode EditMode --output "$env:TEMP/HB-M65-editmode.xml" -- -nographics -logFile "$env:TEMP/HB-M65-editmode.log"
```

Run sequentially and inspect fresh XML for fixture identities and nonzero passing counts.
Acceptance on 2026-10-21: focused **26/26 Passed**, full EditMode **1225 passed, zero failed,
one pre-existing Explicit 10000-seed milestone skip**. Source compiled without new editor
diagnostics. PlayMode was not rerun: only the offline tool/tests/docs changed, not runtime
composition, scenes, UI or saves. The owner authorized direct replacement of the failed local
attempt followed by validation and self-review; no independent review verdict is claimed.
Run v5/profile v2 and M8.7 Deferred remain unchanged. At the M6.5 closeout M4.9 was Pending
and M6.6 was Planned; the later owner-authorized M6.6 work is recorded above.

## M6.4 Radio Tower

The production [radio tower layout, paths and costs](BoardGeneration.md#radio-tower-landmark)
are validated against actual authored JSON, shared rules and real prefab references.
`RadioTowerLandmarkTests` executes both outcomes, manual/axe supply alternatives, free rejected
movement, two bad-push fallbacks, zombie death and starvation. It rejects malformed pressure
links, ambiguous exit bindings and missing special prefab references; named old-arrival
publication upgrades retain their original addresses/generators. `LandmarkProgressTests`
checks v5 round-trip, v4 migration, corrupt records, reset, atomic selection/arrival rollback
and exactly-once retry without resource replay. Existing persistence suites still cover v1-v4,
isolated corrupt-file backup recovery and unsupported future schemas.

Production PlayMode cases named `BoardRuntimeStartupTests.LandmarkProduction_*` run actual
`GameManager`/`BoardManager` startup, Continue, both resolver exits, selection/run/profile save
failures, duplicate rejection and terminal starvation against isolated temporary storage.
Real prefab/animation captures at 1280x720 and 320x640 check sprite references, missing scripts,
label bounds and nonblank/changed pixels for closed/open gates. Images remain outside the
project at `%TEMP%/HB-M64-<closed|open>-<width>x<height>.png`.

```powershell
$project = (Get-Location).Path
unity test $project --mode EditMode --filter 'HallowBlaze.Tests.EditMode.RadioTowerLandmarkTests;HallowBlaze.Tests.EditMode.LandmarkProgressTests' --output "$env:TEMP/HB-M64-domain.xml" -- -nographics -logFile "$env:TEMP/HB-M64-domain.log"
unity test $project --mode PlayMode --filter HallowBlaze.Tests.PlayMode.BoardRuntimeStartupTests.LandmarkProduction --output "$env:TEMP/HB-M64-production.xml" -- -logFile "$env:TEMP/HB-M64-production.log"
```

Run sequentially and inspect fresh XML identities/counts/results, then full EditMode and
graphics PlayMode for persistence/lifecycle integration changes. The ordinary 1000-seed gate
now covers 44 ordinary segment IDs; the four-board inbound ordinary prefix is explicitly
reported separately from complete ordinary roads. It does not substitute an ordinary layout
for the authored arrival. The existing 10000-seed milestone remains Explicit, not an M6.4 gate.
Run v5/profile v2 are the current persistence versions. There is still no mid-board snapshot.
M6.4 acceptance used owner-authorized self-review, not independent review or human playtest
evidence; it neither closes M4.9 nor implements M6.5's bounded state-search solver.

## M6.3 Plates And Gates

The [plate/gate contract](GameDesignContract.md#141-plate-and-gate-rules) is implemented by
immutable `PressurePlateDefinition`/`GateDefinition` bindings and board-owned snapshots.
Plates use exact walkable non-exit `landmark.plate` terrain; gates use `landmark.gate`
terrain whose runtime walkability equals `GateState.IsOpen`. Published bindings are fixed
for that board; moving or removing the bound terrain is rejected without mutation.

| Rule | Pressure / Open State |
| --- | --- |
| `Actors` | Any actor, including player, Shambler and Listener, presses the plate |
| `Boulders` | Only an authored blocking interactive `landmark.boulder` presses it |
| `ActorsOrBoulders` | Either supported category presses it; items and arbitrary obstacles never count |
| `All` | Every plate sharing the exact link ID must be pressed |
| `Any` | At least one matching plate must be pressed |
| Several gates on one link | Each independently applies its explicitly declared `All`/`Any` rule |
| Actor or boulder leaves | Pressure and every affected gate recompute inside the successful mutation |
| Closing over an existing occupant | No damage, displacement or removal; new entry is blocked |
| Invalid link/content or duplicate binding | Reject composition/publication without changing the run or board |

`PlateGateValidator` rejects null/duplicate/missing/wrong-content bindings, unbound authored
plate/gate terrain and orphan links on either side. Diagnostics use stable IDs; link-only
diagnostics are ordinal. Both composers validate before publishing or planning initial
intents. Legacy descriptors expose link and pressure/combination data; blueprint composition
accepts explicit entity-ID bindings. The ordinary static blueprint validator remains in
force: this card neither proves dynamic puzzle solutions nor activates a production landmark.
M6.4 owns full authored content integration and M6.5 owns dynamic solving.

`TryAdd`, `TryRemove`, `TryMove` and atomic `TryPush` update pressure/gates before returning.
Unchanged pressure/walkability emits no transition, including boulder-to-player handover.
Changed plates precede changed gates in terrain-ID order within each mutation. The turn
layer consumes those committed snapshots once, after the causal effect:

```text
Move: EntityMoved -> [PressurePlateChanged...] -> [GateChanged...] -> [automatic food] -> ActionCostApplied -> [Noise] -> [terminal]
Push: InteractionPerformed -> BoulderPushed -> [PressurePlateChanged...] -> [GateChanged...] -> ActionCostApplied -> Noise -> [terminal]
Enemy: EntityMoved -> [PressurePlateChanged...] -> [GateChanged...] -> [trap/investigation feedback] -> next locked actor
Environment: effects and immediate state recomputation -> remaining plate/gate events -> terminal check -> next planning
```

No extra cost, tool charge, turn, noise or RNG is introduced by a gate transition. A closed
destination turns a retained Shambler move or Listener investigation into `EntityWaited`,
never a replan. Fresh path and noise-emission queries use new walkability immediately;
previously emitted noise footprints stay immutable. Rejected entry preserves Food, HP,
noise/pit lifetimes and event buffers. Starvation retains pressure/gate effects but skips
later phases. Clone copies bindings, immutable snapshots and undelivered transitions into
independent collections.

`PlateGateView` reads the exact event snapshot, even for an intermediate open state when
the final model is already closed. Setup/recovery reads authoritative snapshots instead.
The two visual-only prefabs (`Assets/Prefabs/Tiles/PressurePlate.prefab` and
`Assets/Prefabs/Tiles/Gate.prefab`) have no physics; four bitmap states differ by shape as
well as color. Labels expose stable link, pressure category, combination and open/closed
state with cell-constrained dimensions. The explicit Editor menu
`HallowBlaze/Authoring/Rebuild Plate Gate Views` rebuilds only their sprites/prefabs through
Unity APIs; normal gameplay and test setup never invoke it.

Run sequentially from the active Unity project, with graphics for PlayMode:

```powershell
$project = (Get-Location).Path
unity test $project --mode EditMode --filter 'HallowBlaze.Tests.EditMode.PlateGateTests;HallowBlaze.Tests.EditMode.PlateGatePrefabTests;HallowBlaze.Tests.EditMode.BoardRuntimeCompositionTests' --output "$env:TEMP/HB-M63-state.xml" -- -nographics -logFile "$env:TEMP/HB-M63-state.log"
unity test $project --mode PlayMode --filter HallowBlaze.Tests.PlayMode.PlateGatePresentationTests --output "$env:TEMP/HB-M63-presentation.xml" -- -logFile "$env:TEMP/HB-M63-presentation.log"
```

Coverage includes actor/boulder entry/exit, every category, All/Any and multiple gates,
atomic handover, clone, free rejection, gate closure between enemies, Listener, path/hearing,
environment/terminal ordering, invalid links in both composers, initial pressure, both
animation modes, rapid input, historical snapshots and cancellation recovery. Real-prefab
captures with text bounds/pixel checks at 1280x720 and 320x640 are written outside the project
as `%TEMP%/HB-M63-<closed|open>-<width>x<height>.png`. Require fresh nonzero XML, zero failures,
full EditMode and full graphics PlayMode. Run v4/profile v2 and published generators/scenes
are unchanged. These automated checks do not close the M4.9 human gate.

## M6.2 Boulder Pushing

Only the exact `BoulderPushAction.Definition` (`landmark.boulder`, blocking interactive
obstacle) opts into `push-boulder`. Both runtime composers use the same interaction catalog
and resolver. The old opt-in `push` hook remains target-only for compatibility; it is not
the boulder action. Neither contextual feedback nor axe/shovel strength is a push alternative.
Published generators and scenes do not acquire new placements through this card.

`BoardState.CanPush` is a pure preflight. `TryPush` checks the complete operation before
replacing both immutable snapshots and actor/obstacle indices. The player advances into the
boulder's former cell and the boulder moves exactly one cardinal cell away. No view, physics,
RNG, resource charge or later phase participates in that commit.

| Condition | Result |
| --- | --- |
| Existing player and explicitly supported adjacent cardinal boulder | Eligible for remaining checks |
| Missing participant, wrong layer, diagonal or same-cell target | Reject, unchanged state |
| Destination outside bounds or coordinate overflow | Reject, unchanged state |
| Terrain missing, non-walkable or an exit under the boulder or its destination | Reject, unchanged state |
| Actor or item on the boulder's current cell | Reject; no collision or collection on player advance |
| Any obstacle, actor or item at the destination | Reject; no chain, crushing or automatic collection |
| Both terrain cells walkable non-exits and required layers empty | Commit both positions atomically |
| Walkable authored plate-like terrain | Same occupancy rule; no plate activation or gate behavior before M6.3 |
| Captured target moved/disappeared, or destination became blocked after preview | Reject without cost, noise aging or later phases |
| Picker cancel or repeated confirm without a new selection | No additional command or cost |

The resolver applies the shared ordinary action-cost policy once (currently one Food),
spends no tool charge, and emits the existing interaction noise (strength 4, lifetime 2)
from the advanced player's cell against the new occupancy. Player-phase event order is:

```text
InteractionPerformed -> BoulderPushed -> ActionCostApplied -> Noise -> [PlayerStarved]
```

`BoulderPushedEvent` carries both origins and destinations as one grouped effect, not two
independent moves. Existing pre-command noise/pit aging and terminal handling remain
controller-owned. If nonterminal, locked enemies, environment and next planning run once
in their existing order. A retained enemy step into the moved boulder emits `EntityWaited`
without replanning; a fresh path query immediately reads the new occupancy/player cell.
Starvation retains the committed push but skips later phases. Rejection preserves resources,
entity snapshots, noise lifetimes and enemy opportunities.

The presenter interpolates both registered views with one shared progress value, or snaps
both with animations disabled. Replay cancellation recovers both from committed state;
it does not refund or resolve again. The one-cell `Assets/Prefabs/Tiles/Boulder.prefab`
contains only Transform and SpriteRenderer. Its PNG and metadata are Unity-authored;
`HallowBlaze/Authoring/Rebuild Boulder View` is an explicit opt-in Editor operation, never
normal gameplay or test setup. The domain never infers rules from this sprite or prefab.

Run focused checks sequentially from the active Unity project, with graphics for PlayMode:

```powershell
$project = (Get-Location).Path
unity test $project --mode EditMode --filter 'HallowBlaze.Tests.EditMode.BoulderPushTests;HallowBlaze.Tests.EditMode.BoulderPrefabTests;HallowBlaze.Tests.EditMode.BoardRuntimeCompositionTests' --output "$env:TEMP/HB-M62-state.xml" -- -nographics -logFile "$env:TEMP/HB-M62-state.log"
unity test $project --mode PlayMode --filter HallowBlaze.Tests.PlayMode.BoulderPushPresentationTests --output "$env:TEMP/HB-M62-presentation.xml" -- -logFile "$env:TEMP/HB-M62-presentation.log"
```

Coverage includes all four directions, bounds/overflow, missing/blocked terrain, actors/items,
single-chain rejection, clone ownership, stale/replayed commands, cost/noise/terminal ordering,
immediate path queries, retained enemy blocking, both composers, synchronized animation,
rapid input, independent modal ownership and cancellation recovery. Real-sprite pixel/framing
checks write before/after captures outside the project as
`%TEMP%/HB-M62-push-<before|after>-<width>x<height>.png` at 1280x720 and 320x640.
Require full EditMode and graphics PlayMode plus fresh nonzero XML with zero failures.
Run v4/profile v2 are unchanged; there is no mid-board save or M4.9 human verdict. The M6.2
matrix describes unbound terrain; explicitly bound M6.3 plates/gates now add the transitions
documented above without changing the push's movement, legality or action cost.

## M6.1 Landmark Authoring

The engine-free template loader uses externally supplied `BoardValidationCatalog` definitions;
it never infers traits from names, reads a prefab or mutates the active run/profile. See the
[schema and sanity fixture](BoardGeneration.md#m61-authored-landmark-templates) for fields,
two named goal declarations, the mandatory-tool guarantee and the deferred activation boundary.

Run the focused data and compatibility gate from the active Unity project:

```powershell
$project = (Get-Location).Path
unity test $project --mode EditMode --filter 'HallowBlaze.Tests.EditMode.LandmarkTemplateTests;HallowBlaze.Tests.EditMode.BoardBlueprintTests;HallowBlaze.Tests.EditMode.WorldDefinitionTests' --output "$env:TEMP/HB-M61-structural.xml" -- -nographics -logFile "$env:TEMP/HB-M61-structural.log"
```

Cases compare authored and ordinary blueprints, state export/reconstruction and immutable input
ownership. Invalid schemas/types/casing, duplicate keys/IDs, unresolved content/goals, foreign
placements and configuration drift reject before publication. A mandatory tool must be a concrete,
matching, observable and statically accessible spawn in every allowed variant, without crossing
an exit first. An optional tool-dependent outcome remains legal without a guarantee. This check
does not solve a future dynamic puzzle or claim tactical survival.

The separate fixture world passes graph validation for five edge-based days with eight boards;
the final road has an ordinary entry and a landmark arrival segment. The test uses actual
`RunState` travel and `BoardAddressFactory`, then current run v4 JSON round-trip to reconstruct the
same fixture blueprint/template/variant. The open fixture passes the existing board validator
with two tool-free exits and explicit budgets. Published world/generator content and run/profile
schemas remain unchanged. Named outcomes are declarations, not persisted completion results;
M6.4 owns activation and its compatible outcome-save boundary, M6.5/M6.6 solving and playtests.

Full EditMode protects the shared generation assembly and its unchanged ordinary-board encoding;
graphics PlayMode protects existing runtime composition and gameplay. Require fresh nonzero XML
with zero failures, not only a CLI completion line. No M4.9 human result is inferred from this gate.

## M5.7 Field Notes

The immutable authored registry is `Assets/GameData/Facts/Resources/SystemFacts.json`.
`FactKnowledge` owns observation-to-profile discovery outside Unity/UI, while
`FactDiscoveryRecorder` checkpoints only new facts before releasing feedback. Failed checkpoints
retain pending events for a subsequent observation or explicit commit retry. Profile v2/run v4
are unchanged; pre-existing axe/pit IDs become notes without another discovery notification.

| Stable ID | Required Presented Observation | Title / Text | Icon / Source System |
| --- | --- | --- | --- |
| `AXE_CREATES_LOUD_NOISE` | Actual axe `NoiseEvent` after its footprint/feedback, not manual clearing or preview | **A loud shortcut**: The axe clears soft wood quickly, but its loud noise travels farther than working by hand. | `axe` / Tools / Noise |
| `PIT_TRAPS_SHAMBLER` | Actual `ShamblerTrappedEvent` after pit/hold feedback, not pit creation | **Held by the ground**: A Shambler that steps into a pit waits there until the pit expires. The pit causes no damage. | `pit` / Shovel / Shambler |
| `LISTENER_FOLLOWS_NOISE` | `ListenerInvestigatedEvent` after the preceding actual movement; blocked/expired/losing-initiative Wait emits none | **Following the sound**: The Listener follows a heard noise toward its source. Its announced step stays fixed; a new sound can change the next plan. | `hearing` / Listener / Noise |

These are mechanical observations, not M7.5 tool-source sightings or atlas location knowledge.
The reader displays only profile-owned entries, with a neutral placeholder for a missing
definition and no unknown checklist/count/percentage. Reading or selecting an unknown ID never
discovers anything. The book control (or `N`) opens notes; close/`Escape` releases only their own
input owner after the current frame. Discovery feedback persists until acknowledged, queues later
discoveries and remains readable with animations disabled. It hides behind the open reader and
returns after closing. Death and a new run retain notes; explicit New Game resets them.

Run sequentially from the active project, with a graphics device for the UI gate:

```powershell
$project = (Get-Location).Path
unity test $project --mode EditMode --filter 'HallowBlaze.Tests.EditMode.FactKnowledgeTests;HallowBlaze.Tests.EditMode.PersistenceDtoTests;HallowBlaze.Tests.EditMode.PersistenceStorageTests;HallowBlaze.Tests.EditMode.ListenerTurnTests' --output "$env:TEMP/HB-M57-state.xml" -- -nographics -logFile "$env:TEMP/HB-M57-state.log"
unity test $project --mode PlayMode --filter HallowBlaze.Tests.PlayMode.FieldNotesPresentationTests --output "$env:TEMP/HB-M57-notes-play.xml" -- -logFile "$env:TEMP/HB-M57-notes-play.log"
```

Tests cover typed triggers, idempotency, unknown/missing projections, isolated checkpoint
failure/retry, pre-Field-Notes v2 saves and round-trip/new-run/death retention. Production
`GameManager`/composer/presenter scenarios cover actual axe/pit/Listener replay versus preview or
unpresented execution, independent interaction/pause ownership, free reading, queued feedback,
animation parity and New Game replacement. Render checks include text bounds, distinct bitmap
icons and nonblank pixels at 1280x720 and 320x480; captures are written outside the project as
`%TEMP%/HB-M57-notes-<width>x<height>.png`. Full EditMode and graphics PlayMode remain required.
Read fresh nonzero XML; CLI completion alone is not PASS. This does not close M4.9's human gate.

## M5.6 Conscious Tool Pickup

Only concrete board-owned tools enter the equipment picker; movement never collects them.
An accepted pickup fills the first free generic slot. With full equipment, opening the comparison
and selecting a slot are free drafts; only confirmed `Replace` submits a complete command.
The old tool remains at the original ground binding with the same ID and charges, without
inheriting the incoming authored source ID. `Leave`, Escape, cancellation, stale choices and
full-slot refusal preserve ground/equipment without cost. Preview/pickup allocates no new ID,
spends no charge and never refills a depleted tool. O-003 automatic food remains reward-before-cost.

Run focused verification sequentially from the active Unity project root:

```powershell
$project = (Get-Location).Path
unity test $project --mode EditMode --filter 'HallowBlaze.Tests.EditMode.ToolPickupTests;HallowBlaze.Tests.EditMode.BoardRuntimeCompositionTests' --output "$env:TEMP/HB-M56-domain.xml" -- -nographics -logFile "$env:TEMP/HB-M56-domain.log"
unity test $project --mode PlayMode --filter HallowBlaze.Tests.PlayMode.ToolPickupPresentationTests --output "$env:TEMP/HB-M56-view.xml" -- -logFile "$env:TEMP/HB-M56-view.log"
```

Domain cases cover first-free and full-slot transfer, exact source/instance identity, stale and
round-trip replay, duplicate/unallocated/already-equipped bindings, invalid targets, clone,
depleted tools, single noise/cost, authored traits, isolated run save round-trip and unchanged
automatic food. Composition covers both legacy descriptors and explicit blueprint/source bindings.
PlayMode uses the production picker/coordinator and checks comparisons, independent input blocks,
free cancellation/Leave, rapid confirmation, stale replacement, animation parity and actual scene
unload followed by save/reload and HUD recomposition. Pixel checks render current ground/equipment
labels at 960x540 and 320x640, writing `%TEMP%/HB-M56-exchange-<width>x<height>.png` outside the project.
These captures cover the ground/HUD, not the IMGUI modal itself; modal labels and behavior are
checked through the real picker. Full EditMode and graphics-enabled PlayMode remain regression gates.
Require fresh nonzero XML with zero failures, not only a CLI completion message.

Run schema v4 and profile schema v2 are unchanged. Ground instances remain transient board state;
no mid-board save system, source knowledge, consumable modal, bag/pockets or generic discard/drop
is implemented here. This automated gate does not replace M4.9 human prediction/readability evidence.

## M5.5 Shovel

Marked `legacy.buried-food` caches require three manual actions or one shovel use. Completion
creates one concrete `shovel.cache-ration` item with a source entity and stable board-local
instance ID before the pre-M9 compatibility adapter collects it. Food +10 precedes action cost;
overflow rejects before work, reveal or charge mutation. The same reward can remain ground
state without changing its identity when immediate transfer is disabled.

Only explicitly authored `LegacyBoardContentKind.DiggableGround` / `shovel.diggable` terrain
supports pits. Published generator configurations do not place this new content automatically.
A fresh visible pit retains four subsequent accepted commands; pre-command pits age before
enemy execution, never on rejection. Actual Shambler entry holds it until expiry without damage.
An already announced Wait still executes when the pit expires. Player and Listener movement
remain unaffected, so the exit never requires a shovel. Creation alone grants no knowledge;
only ordered presentation of `ShamblerTrappedEvent` records `PIT_TRAPS_SHAMBLER`.

Run focused checks sequentially against the active project:

```powershell
$project = (Get-Location).Path
unity test $project --mode EditMode --filter 'HallowBlaze.Tests.EditMode.ShovelTests;HallowBlaze.Tests.EditMode.BoardStateTests;HallowBlaze.Tests.EditMode.BoardRuntimeCompositionTests' --output "$env:TEMP/HB-M55-domain.xml" -- -nographics -logFile "$env:TEMP/HB-M55-domain.log"
unity test $project --mode PlayMode --filter HallowBlaze.Tests.PlayMode.ShovelPresentationTests --output "$env:TEMP/HB-M55-view.xml" -- -logFile "$env:TEMP/HB-M55-view.log"
```

The graphics gate exercises both cache routes and animation modes, visible work/lifetime/charge
labels, ordered observation, isolated profile/run round-trips and reset. Pixel-checked real-sprite
captures are written outside the project as `%TEMP%/HB-M55-cache.png` and `%TEMP%/HB-M55-pit.png`.
Full EditMode and graphics PlayMode also protect the existing axe, Listener, picker and lifecycle
paths. Require fresh nonzero XML with zero failures. These automated checks do not close M4.9's
human prediction/readability gate. Pits and uncollected cache rewards remain transient board state;
run schema v4 and profile schema v2 are unchanged.

## M4.8 Listener

**Rule:** The Listener remembers the latest noise audible at emission and takes the announced
shortest-path step toward its fixed source while that signal is active; otherwise it waits.

The [accepted rule](GameDesignContract.md#1132-accepted-listener-rule---m48) specifies event-time
hearing, emission-ID tie-breaking, lifetime and blocked-step semantics. Memory belongs to
`BoardState`, not a view or a save DTO. Source actors need not remain present. Planning is read-only;
new noise cannot replace a locked opportunity. Investigation is not an unannounced attack, including
when a player occupies its next cell. No player knowledge, perception RNG or hidden alert state is used.

| Example | Announced / actual outcome |
| --- | --- |
| No audible noise, even next to the player | Target-free Wait; no pursuit or attack |
| Older loud event followed by a quieter audible event | Higher emission ID becomes the remembered source |
| New event is outside the Listener's footprint | Previous heard target remains unchanged |
| Listener moves into an older footprint after emission | No retroactive hearing |
| Equal shortest routes toward a source | North, East, West, South first-step priority |
| New noise after an investigation was announced | Execute the old fixed step; new source controls next planning |
| Source actor leaves or is removed | Keep the original source cell while the event is active |
| Signal expires before execution or step becomes occupied/impassable | Consume the opportunity as Wait, without replanning |
| Latest signal expires while an older event remains active | Forget the target; never revive the older event |
| Several actors claim one destination | Ascending actor ID executes first; later actors see live occupancy |
| Player command is rejected, or terminates before enemy phase | Do not consume or replace the retained intent |

The production runtime accepts explicit `LegacyBoardContentKind.Listener` descriptors without a
damage variant, producing stable `enemy.listener` actors and initial Wait intents. `Enemy1` and
`Enemy2` remain Shamblers; published ordinary/legacy generation configurations are unchanged.
The intent display publishes both archetypes in the same actor-ID order. An investigation shows
a magnifier at the immediate step and a linked diamond at the heard source. Both remain frozen
until replay/recovery completes; expiration and board lifetime boundaries clear them normally.

Run focused verification from the Unity project root:

```powershell
$project = (Get-Location).Path
unity test $project --mode EditMode --filter 'HallowBlaze.Tests.EditMode.(ListenerNoiseMemoryTests|ListenerTests|ListenerTurnTests)' --output "$env:TEMP/HB-M48-domain.xml" -- -nographics -logFile "$env:TEMP/HB-M48-domain.log"
unity test $project --mode PlayMode --filter HallowBlaze.Tests.PlayMode.ListenerPresentationTests --output "$env:TEMP/HB-M48-view.xml" -- -logFile "$env:TEMP/HB-M48-view.log"
```

The PlayMode scenario uses the real composer, controller and event presenter: an adjacent
interaction emits noise, the initial Listener Wait executes, Investigate is shown before input,
and the player then leaves while the Listener moves to the exact previously announced step.
Both animation modes check source/step markers, rejection stability, resource costs, expiration
and disposal. Full EditMode and graphics-enabled PlayMode remain regression gates for Shambler
paths/cadence/attacks, mixed initiative and presentation. Keep fresh XML/log output outside the
project. M4.9 human prediction/playtests and M8.7 portable replay are not claimed by these tests.

## M4.7 Domain Noise

Propagation rule: emission freezes a board-local immutable `BoardNoise` footprint. The source
is always audible; each orthogonal step costs one strength point, including detours. A reached
cell requires existing walkable terrain and no impassable obstacle. Missing/non-walkable ground
and impassable obstacles are excluded and stop that path; walkable obstacles, actors and items
do not block. A breadth-first search finds every cell within the inclusive path budget, with
coordinate-sorted output and no random hearing roll. Later terrain changes never recompute
that event's footprint. `NoiseEvent.Noise`, the active `BoardState` snapshots and debug display
share these immutable cells; future Listener code must use `IsAudible`, not a new distance rule.

`ActionNoiseProfile` is immutable configuration. The production prototype uses movement strength
**2**, interaction strength **4**, and lifetime **2**. Movement emits at its resulting cell;
interaction emits at the actor's cell. Wait is silent, and zero in either configured strength
suppresses that action's emission. Standalone `TurnResolver` defaults remain silent unless
configured. These prototype values are not axe balance or Listener behavior.

After an accepted player phase, `TurnController` ages only noise present before that command,
before executing locked enemy intents and planning the next batch. Newly emitted noise retains
its full lifetime. Thus lifetime 2 survives the emitting command and one subsequent accepted
command, then expires on the next accepted command. Rejection and blocked input do not age or
emit noise and do not change costs. An accepted terminal action still emits before the terminal
event. Old event/snapshot values remain immutable while active state receives aged replacements.
Clone copies the emission sequence and owns independent collections; each fresh board starts
empty. Noise is not added to save DTOs or carried to another board.

The board-owned debug view draws a thin outline at every audible cell plus `S<strength>` and
`T<remaining>` at the source. It reads snapshots only, without gameplay mutation or audio, and
is refreshed at noise replay, recovery and command completion (including silent Wait). Terminal,
replacement and disposal close its lifetime. Existing scenes, prefabs and audio assets are unchanged.

Run focused gates from the Unity project root, with one Unity process at a time:

```powershell
$project = (Get-Location).Path
unity test $project --mode EditMode --filter HallowBlaze.Tests.EditMode.BoardNoiseTests --output "$env:TEMP/HB-M47-model.xml" -- -nographics -logFile "$env:TEMP/HB-M47-model.log"
unity test $project --mode EditMode --filter HallowBlaze.Tests.EditMode.NoiseTurnTests --output "$env:TEMP/HB-M47-turns.xml" -- -nographics -logFile "$env:TEMP/HB-M47-turns.log"
unity test $project --mode PlayMode --filter HallowBlaze.Tests.PlayMode.NoisePresentationTests --output "$env:TEMP/HB-M47-view.xml" -- -logFile "$env:TEMP/HB-M47-view.log"
```

The visual gate needs graphics, so do **not** add `-nographics` to its command. It compares
displayed cells with hearing queries, exercises production replay/Wait/disposal, and renders
strengths **1 / 3 / 5** side by side. Pixel checks require a nonempty image in every panel. The
test output prints `%TEMP%/HB-M47-visual-<unique-id>/noise-strengths-1-3-5.png`; this screenshot is
external evidence, not a source asset. Full EditMode and graphics-enabled PlayMode also guard
the existing command gate, resource costs, intent execution, animation parity and terminal flow.
Require a fresh nonzero passed XML report with the expected test identity, not only CLI success.

## M4.6 Generation Gates

Current generation automation uses Unity CLI against the active project, not the old
M0.7 paths below. Follow `AGENTS.md` and the `unity-validate` skill for Editor connection
diagnosis. Use one Unity process at a time; no package or Editor-version change is needed.

From the Unity project root, run the default daily 1,000-board gate:

```powershell
$project = (Get-Location).Path
unity test $project --mode EditMode --filter HallowBlaze.Tests.EditMode.BoardGenerationSeedSeriesTests.Daily1000SeedsHaveNoInvalidBoardsAndRepeatExactly --output "$env:TEMP/HB-M46-daily.xml" -- -nographics -logFile "$env:TEMP/HB-M46-daily.log"
```

Run the explicitly selected 10,000-board gate before a generation milestone:

```powershell
unity test $project --mode EditMode --filter HallowBlaze.Tests.EditMode.BoardGenerationSeedSeriesTests.Milestone10000SeedsHaveNoInvalidBoardsAndRepeatExactly --output "$env:TEMP/HB-M46-milestone.xml" -- -nographics -logFile "$env:TEMP/HB-M46-milestone.log"
```

Both tests generate the entire series twice, compare deterministic summaries and hashes,
and persist both runtime measurements outside the project. Thresholds are fixed in the
test before execution: **0 invalid boards, 0 statically unreachable exits, at most 1%
fallbacks**, and **60 seconds / 300 seconds per daily / milestone pass**, respectively.
The daily gate runs in the ordinary EditMode suite. The milestone gate is NUnit `Explicit`
and must have its own fresh nonzero passed result; an expected skip in a full-suite run
does not satisfy milestone acceptance.

For focused verification of the measurement/failure recorder, use the fixture filter
`HallowBlaze.Tests.EditMode.SeedSeriesHarnessTests`. It tests known histograms and population
variance, independent repeated runs/culture, corrupt configs, threshold and timeout failures,
complete versus partial route totals, authored resource bands and fixed sources. Expected
negative-control reports contain deliberately invalid cases; they are not production failures.

The tests print external `%TEMP%/HB-M46-<unique-id>` directories. Each contains:

- `summary.json`: runtime, deterministic fingerprint, all histograms, counts, segment supply,
  whole-traversal edge totals/variance, builder selection and rejection-code counts;
- `failures.json`: every rejected or unexecuted case, including full address/seed/configuration,
  root seed, exact requirements, fixture/segment identifiers, reasons and attempt history.

Require a fresh Unity XML report with the expected test identity, executed count greater
than zero and no failures. Keep reports outside the project; do not commit generated JSON,
XML or logs. There is no player profile, save or external-service access in these tests.
For measured results, fixture coverage and limits, see
[M4.6 seed series](BoardGeneration.md#m46-seed-series-and-measurements).

## M0.7

Projekt używa Unity `6000.3.21f1` i Unity Test Framework `1.6.0`.

Zestawy testów:

- `Assets/Tests/EditMode/HallowBlaze.Tests.EditMode.asmdef`
- `Assets/Tests/PlayMode/HallowBlaze.Tests.PlayMode.asmdef`

Oba zestawy są oznaczone jako Unity Test Assemblies. EditMode działa w Edytorze; PlayMode może działać w Edytorze i w celu testowym Playera.

Test Runner może wyświetlać ten sam zestaw testów w więcej niż jednym widoku platformy. Standardowy NUnit `[Test]` może być wyświetlany w drzewie PlayMode oraz EditMode; nazwa zestawu identyfikuje jego właściciela. Widok Playera to osobny cel wykonania, nie osobna kopia testów.

## Unity Test Runner

Uruchamiaj każdą platformę testową w osobnym procesie Unity. Nie uruchamiaj dwóch instancji Unity dla tego projektu jednocześnie.

Po każdej komendzie trzeba sprawdzić `$LASTEXITCODE` i nie uruchamiać następnej instancji, zanim poprzednia się zakończy.

Z katalogu głównego repozytorium:

```powershell
$unity = 'C:\Program Files\Unity\Hub\Editor\6000.3.21f1\Editor\Unity.exe'
$project = 'E:\Repos\HallowBlaze\HallowBlaze'
& $unity -batchmode -nographics -projectPath $project -runTests -testPlatform editmode -testResults "$project\Temp\TestResults\M0.7-EditMode.xml" -logFile "$project\Temp\TestResults\M0.7-EditMode.log"
```

```powershell
& $unity -batchmode -nographics -projectPath $project -runTests -testPlatform playmode -testResults "$project\Temp\TestResults\M0.7-PlayMode.xml" -logFile "$project\Temp\TestResults\M0.7-PlayMode.log"
```

Dla filtrowanych testów infrastruktury użyj:
```powershell
& $unity -batchmode -nographics -projectPath $project -runTests -testPlatform editmode -testResults "$project\Temp\TestResults\M0.7-EditModeInfrastructure.xml" -logFile "$project\Temp\TestResults\M0.7-EditModeInfrastructure.log" -testFilter "HallowBlaze.Tests.EditMode.EditModeInfrastructureTests.EditModeAssemblyLoads"
```

```powershell
& $unity -batchmode -nographics -projectPath $project -runTests -testPlatform playmode -testResults "$project\Temp\TestResults\M0.7-PlayModeInfrastructure.xml" -logFile "$project\Temp\TestResults\M0.7-PlayModeInfrastructure.log" -testFilter "HallowBlaze.Tests.PlayMode.PlayModeInfrastructureTests.PlayModeAssemblyLoads"
```

## Build Playera dla M0.7

```powershell
& $unity -batchmode -nographics -projectPath $project -buildWindows64Player "$project\Build\M0.7Validation\HallowBlaze.exe" -logFile "$project\Temp\TestResults\M0.7-PlayerBuild.log" -quit
```

## Ścieżki logów i XML dla M0.7

- Log EditMode: `Temp/TestResults/M0.7-EditMode.log`
- XML EditMode: `Temp/TestResults/M0.7-EditMode.xml`
- Log PlayMode: `Temp/TestResults/M0.7-PlayMode.log`
- XML PlayMode: `Temp/TestResults/M0.7-PlayMode.xml`
- Log infrastruktury PlayMode: `Temp/TestResults/M0.7-PlayModeInfrastructure.log`
- XML infrastruktury PlayMode: `Temp/TestResults/M0.7-PlayModeInfrastructure.xml`
- Log infrastruktury EditMode: `Temp/TestResults/M0.7-EditModeInfrastructure.log`
- XML infrastruktury EditMode: `Temp/TestResults/M0.7-EditModeInfrastructure.xml`
- Log Playera: `Temp/TestResults/M0.7-PlayerBuild.log`
- Wykonanie: `Build/M0.7Validation/HallowBlaze.exe`

## Sprawdzenie buildu

```powershell
$LASTEXITCODE # Oczekiwany: 0
Get-ChildItem -Path "$project\Build\M0.7Validation" -Recurse -File | Measure-Object # Oczekiwany: liczba wszystkich plików builda > 0
Get-ChildItem -Path "$project\Build\M0.7Validation" -Recurse -File -Filter "HallowBlaze.Tests*.dll" | Measure-Object # Oczekiwany: count HallowBlaze.Tests*.dll = 0
```
