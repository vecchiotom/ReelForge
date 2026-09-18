# Upstream source

This directory vendors a curated subset of Remotion's official agent-skills corpus, pinned to a
specific commit rather than tracked live — the whole point of vendoring is to remove the runtime
dependency on a network fetch (see `inference/src/ReelForge.WorkflowEngine/Services/Skills/SkillCorpusService.cs`
and `docs/video-editing.md`/`CLAUDE.md` for how these are loaded and enforced).

- **Source repository:** https://github.com/remotion-dev/skills
- **Pinned commit SHA:** `bbb139d5ba3709b1ffeb27184e9579c681230a08`
- **Pinned on:** 2026-09-18
- **Fetched via:** `https://raw.githubusercontent.com/remotion-dev/skills/bbb139d5ba3709b1ffeb27184e9579c681230a08/skills/{dir}/...`
  for each of the five directories below (file listing obtained from
  `https://api.github.com/repos/remotion-dev/skills/git/trees/main?recursive=1`, unauthenticated).

## Directories vendored

Only these five directories — matching `SkillCatalog.All` in
`inference/src/ReelForge.Shared/Skills/SkillCatalog.cs` — were pulled, each containing its own
`SKILL.md` (YAML frontmatter: `name`, `description`, `version`) plus flat topic `.md` files:

- `remotion-create/`
- `remotion-markup/`
- `remotion-render/`
- `remotion-captions/`
- `remotion-multimedia/`

## Deliberately excluded

- **`skills/remotion-best-practices/`** — a bundle directory that re-nests full copies of the
  other skill directories' content (e.g. `skills/remotion-best-practices/remotion-markup/...`),
  which would duplicate every topic under a different path.
- **`skills/remotion-markup/remotion-maps/`** — a nested sub-directory (third-party mapping
  providers: Mapbox/Cesium/MapTiler/static-map) living *inside* `remotion-markup` in the live
  tree, unrelated to a headless promotional-video pipeline. Excluded regardless of nesting depth.
- **Non-documentation files** inside each vendored directory — `agents/openai.yaml` (an
  agent-runner config, not content) and `assets/remotion-icon.svg` (a logo asset) were not pulled;
  only `SKILL.md` and sibling topic `.md` files were vendored.
- Every other top-level directory in the upstream repo (`remotion-saas`, `remotion-studio`,
  `remotion-upgrade`, `remotion-docs`, `remotion-interactivity`, `remotion-maps` at the top level,
  etc.) — none of it applies to ReelForge's automated, headless Remotion codegen pipeline.

## Refreshing this corpus

Bumping the pinned commit is a deliberate, reviewable action, not automatic:

1. Pick a new commit on `remotion-dev/skills` (usually `main`'s current tip).
2. Re-fetch the five directories above (SKILL.md + topic `.md` files only, same exclusions) from
   `https://raw.githubusercontent.com/remotion-dev/skills/<new-sha>/skills/{dir}/...`.
3. Diff the result against what's currently checked in here.
4. Update the "Pinned commit SHA" / "Pinned on" lines above.
5. Rebuild the `workflow-engine` image (`inference/src/ReelForge.WorkflowEngine/Dockerfile` copies
   this whole directory in) and confirm `GET /api/v1/workflow-engine/skills/status` reports all
   five skills loaded with no validation failures.
