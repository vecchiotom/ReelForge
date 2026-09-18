# Workflow execution optimization

Three independent mechanisms that make workflow executions — especially *repeated*
executions of the same workflow over an unchanged project — cheaper in tokens, wall-clock
time, and money:

1. **[Step-result caching](#1-step-result-caching)** — a repeated execution reuses a prior
   successful step's output instead of re-calling the agent or re-running ffmpeg.
2. **[The prompt-context budget](#2-the-prompt-context-budget)** — a deterministic,
   non-LLM bound on how much prior-step output is concatenated into an agent's prompt.
3. **[Sandbox edit tools](#3-sandbox-edit-tools)** — agents change an existing file with a
   targeted edit instead of re-emitting the whole file.

All three are **additive and independently switchable**. With every knob at its default
the platform behaves exactly as before apart from the intended savings; with all three
disabled it is byte-for-byte the old behaviour.

---

## 1. Step-result caching

### The problem

Every execution of a workflow re-runs every step from scratch. Running
`quick-win-promo` twice on a project whose files have not changed pays for the five
analysis agents twice, even though — given identical inputs, identical prompts and an
identical file inventory — the second run is asking the same question and (modulo
sampling) getting the same answer. The same is far worse for the video templates, where a
`VideoAnalyze` step can burn fifteen minutes of ffmpeg decode plus a full ASR pass before
a single token is spent.

### The cache key

A cache entry is looked up by `(ProjectId, CacheKey)`, where `CacheKey` is a SHA-256 over a
length-prefixed, field-named encoding of:

| Field | Why it is in the key |
|---|---|
| `SchemaVersion` | Lets a future change to the encoding invalidate every existing entry at once. |
| `ProjectId` | Entries are never shared across projects. |
| `StepType`, `AgentType`, `AgentDefinitionId` | Different step/agent → different answer. |
| `AgentSystemPrompt`, `AgentOutputSchemaName`, `AgentAssignedSkillsJson` | Editing an agent's prompt, its output schema or its skills must invalidate everything it ever produced. Hashing the prompt *text* (rather than a version counter) means this is true even for an edit made directly in the database. |
| `AgentInferenceProviderId` | Pointing an agent at a different model is a different agent. |
| The six step config blobs (`Extract`/`VideoAnalyze`/`VideoCompile`/`EditRoom`/`GraphicsRoom`/`ColorGradeRoom`), `AgentInputContextMode`, `SelectedPriorStepOrdersJson`, `InputMappingJson`, `ConditionExpression`, `MaxIterations`, `MinScore` | The step's own configuration. |
| **`ResolvedInput`** | The fully-built step input string. This is the load-bearing one: because the input transitively contains every upstream step's output, *any* change anywhere upstream changes this string and therefore the key. There is no separate upstream-invalidation mechanism because none is needed. |
| `UserRequest` | The free-text request is part of the prompt. |
| **`ProjectFileFingerprint`** | The one thing the resolved input does *not* cover. An agent holding `ToolGroup.ProjectRead` reads project files through tools, so that state never appears in its prompt. The fingerprint — a SHA-256 over every `ProjectFile` row's `(Id, StorageKey, SizeBytes, UploadedAt, SummaryStatus, IndexingStatus)`, ordered by id — closes that hole. Without it, uploading a new file would not invalidate an analysis agent's cached answer *about that project's files*. |

Fields are encoded name-prefixed and length-prefixed specifically so that no two different
field sets can collide through plain concatenation (`{A="ab", B="c"}` must not hash the
same as `{A="a", B="bc"}`); there is a unit test pinning exactly that.

### What is cacheable, and what is deliberately not

`StepCachePolicy` decides. A `WorkflowStep.CacheMode` of `Never` or `Always` overrides it;
`Default` (the default, stored as `null`) falls through to the policy:

| Step type | Cached by default | Why |
|---|---|---|
| `Extract` | yes | Deterministic and pure. |
| `VideoAnalyze` | yes | The single most expensive step in the platform. Deterministic given the same source. |
| `VideoCompile` | yes | Minutes of ffmpeg encode. Its `OutputStorageKey` is validated (below) before a hit is accepted. |
| `EditRoom`, `ColorGradeRoom` | yes | Many LLM calls per run; neither writes anything outside its own output. |
| `Agent`, where the agent's tool grant contains **none** of `ProjectWrite` / `SandboxAuthoring` / `SandboxRender` | yes | The output *is* the whole effect of the step. |
| `Agent`, where it does | **no** | These agents mutate state that lives **outside** the cached output — project files, the sandbox workspace, a rendered media object. Replaying the output would silently skip a side effect that downstream steps depend on: a cached `AuthorAgent` result would hand the next step a storage key for a render that this execution's sandbox never produced. This excludes `AuthorAgent`, `RemotionComponentTranslator`, `MotionGraphicsPlanner` and `MotionGraphicsDirector`. |
| `GraphicsRoom` | **no** | Same reason — its director may render an overlay asset during synthesis. |
| `ReviewLoop` | **no** | Its entire purpose is to judge *this* iteration's output afresh. A cached score would defeat the loop. |
| `Conditional`, `ForEach`, `Parallel` | no | Control flow; nothing worth caching. |

### Safety properties

- **The cache can never fail a workflow.** Every database and S3 operation inside the
  lookup and store paths is wrapped; any failure is logged at warning level and degrades to
  "no cache". A cache that is down is a cache that is slow, never a broken execution.
- **Artifact validation.** When `ValidateStorageArtifacts` is on (default), a hit carrying an
  `OutputStorageKey` or `ArtifactStorageKey` is only accepted after the object is confirmed
  to still exist in MinIO. A stale row pointing at a swept object is deleted and treated as
  a miss, rather than handing a downstream step a key that 404s.
- **A hit is indistinguishable downstream.** A cache hit takes the same persistence and
  event-publishing path as a real result: the same `WorkflowStepResult` write, the same
  `WorkflowStepCompleted` / diagnostics publish, the same step-output-history append, the
  same `accumulatedOutput` update. Nothing after the executor needs to know.
- **No foreign keys.** `workflow_step_cache_entries` deliberately has no FK to
  `projects` or `workflow_definitions`, so editing a workflow definition does not cascade
  away entries that are still perfectly valid. Entries are reclaimed by TTL instead.

A hit is recorded on the step result as `FromCache = true` with `TokensUsed = 0` (this
execution genuinely spent nothing) and `CachedTokensSaved` carrying what the original run
cost, which is what the execution UI reports as savings.

### Configuration

| Key | Default | Meaning |
|---|---|---|
| `WorkflowEngine:StepCache:Enabled` | `true` | Master switch. |
| `WorkflowEngine:StepCache:TtlHours` | `168` | Entry lifetime (7 days). |
| `WorkflowEngine:StepCache:MaxEntryChars` | `2000000` | Outputs larger than this are not stored. |
| `WorkflowEngine:StepCache:ValidateStorageArtifacts` | `true` | HEAD the object before accepting a hit that carries a storage key. |

---

## 2. The prompt-context budget

### The problem

`StepExecutionContext.BuildFullWorkflowInput()` concatenates **every** prior step's full
output into the next agent's prompt. Across the eleven-step default pipeline that is
quadratic: step 11 pays for steps 1-10 in full, step 10 paid for 1-9, and so on. Most of
that is analysis output the production agents skim once and never refer to again.

### The mechanism

A deterministic, non-LLM budget (`AgentInputBudget`) applied **only** when the composed
input exceeds `MaxInputChars`:

1. The most recent `RecentStepsVerbatim` entries (by descending `StepOrder`) are protected
   and passed through untouched.
2. Every older entry is compacted by `JsonOutputDigest`: parsed as JSON and rewritten with
   long strings truncated (`"…(+N chars)"`), over-long arrays capped with a trailing
   `"…(+N more items omitted by context budget)"` element, and subtrees past a depth limit
   elided. **The digest is still valid JSON.** Output that is not JSON gets middle-out
   truncation with an explicit character count in the marker.
3. If it still does not fit, entries are dropped oldest-first, each replaced by a one-line
   marker that keeps its `## Step N: Label` header so the agent knows the step existed and
   can go re-read it with the project/file tools.
4. If even the protected entries alone exceed the budget, they are returned intact. We
   never corrupt a machine-consumed contract to hit a number.

### Safety contract

This is the part that matters, and it mirrors the contract already documented on
`SetPromptOutputOverride`:

- Budgeting is **prompt-only**. `StepOutputHistory` itself is untouched, the persisted
  `WorkflowStepResult.OutputJson` is untouched, and every deterministic by-`StepOrder`
  consumer — `VideoCompileStepExecutor` resolving a decision, a graphics plan, a music plan
  or an analysis artifact — keeps reading the raw output.
- `AgentInputContextMode.PreviousStepOnly` is **never** digested or truncated. The previous
  step's output is routinely a machine-consumed contract (a `{view, meta}` envelope, a
  structured decision) that the next agent must see whole.
- The most recent N entries in `FullWorkflow`/`SelectedPriorSteps` mode are protected for
  the same reason.
- `CustomMappedSubset` is never digested; the mapping already narrowed it deliberately.
- **When the input already fits, the result is byte-identical to the previous behaviour.**
  There is a regression test that builds the legacy concatenation by hand and asserts
  equality, rather than calling the production helper.

### Configuration

| Key | Default | Meaning |
|---|---|---|
| `WorkflowEngine:ContextBudget:Enabled` | `true` | Master switch. |
| `WorkflowEngine:ContextBudget:MaxInputChars` | `160000` | Overall cap; `<= 0` disables. |
| `WorkflowEngine:ContextBudget:MaxDigestedStepChars` | `12000` | Per-step cap once digesting engages. |
| `WorkflowEngine:ContextBudget:RecentStepsVerbatim` | `2` | How many trailing steps are protected. |
| `WorkflowEngine:ContextBudget:DigestMaxStringChars` | `600` | String truncation threshold inside the digest. |
| `WorkflowEngine:ContextBudget:DigestMaxArrayItems` | `25` | Array cap inside the digest. |
| `WorkflowEngine:ContextBudget:DigestMaxDepth` | `12` | Depth cap inside the digest. |

---

## 3. Sandbox edit tools

### The problem

An agent authoring Remotion code in the sandbox had exactly one way to change a file:
`WriteSandboxFile(path, content)` — re-emit the entire file. Changing three lines of a
400-line composition cost 400 lines of output tokens, and, worse, made long files
*unreliable*: a model re-emitting a file it did not write character-for-character silently
drops code it fully intended to keep. Most of the `ReviewLoop` iterations in the default
pipeline are spent recovering from exactly that.

### The tools

| Tool | Group | Purpose |
|---|---|---|
| `EditSandboxFile(path, oldText, newText, replaceAll)` | `SandboxAuthoring` | Replace one unique snippet. Preferred over `WriteSandboxFile` for any change to an existing file. |
| `ApplySandboxFileEdits(path, edits[])` | `SandboxAuthoring` | Several hunks in **one** read and **one** write. |
| `ReadSandboxFileLines(path, startLine, lineCount)` | `SandboxBrowse` | Read part of a large file with line numbers, instead of paying for all of it. |
| `GetSandboxFileOutline(path)` | `SandboxBrowse` | Imports, exported symbols with line numbers, and `<Composition>` ids — so the agent can locate code before reading it. |

### Design decisions

- **All-or-nothing.** In a multi-edit call, edits are applied in order against the result of
  the previous one, entirely in memory; if any one fails, nothing is written. A partially
  applied edit list leaves the sandbox in a state neither the agent nor the caller can
  reason about.
- **Ambiguity is an error, not a guess.** Zero matches fails with an instruction to re-read
  and copy the exact text; more than one match fails asking for more surrounding context,
  unless `replaceAll` was set explicitly.
- **Failures return JSON, they do not throw.** A bad `oldText` comes back as
  `{"ok": false, "error": "..."}` so the model corrects itself on the next turn, rather than
  burning a whole step retry.
- **Line endings are normalised.** Models routinely emit `\r\n` against an `\n` file (or the
  reverse). Failing on that would push the agent straight back to the whole-file rewrite
  this feature exists to eliminate, so the editor reconciles the two before matching.
- **Matching is ordinal, never regex.** `oldText` containing `.*` or `$1` is literal text.
- **Scoping.** The edit tools mutate, so they live in `ToolGroup.SandboxAuthoring`; the read
  tools are in `ToolGroup.SandboxBrowse`. Both are registered in `ToolGroupCatalog` and
  `AgentToolProvider`, which `ToolScopingDriftGuardTests` pins together.

The default system prompts for `AuthorAgent`, `RemotionComponentTranslator`,
`MotionGraphicsPlanner` and `MotionGraphicsDirector` now direct the model to prefer a
targeted edit, and to locate code with the outline + ranged read rather than reading whole
files. The tools are worthless if the model does not reach for them.
