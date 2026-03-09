# Built-in Agents Reference

> **New:** every agent has access to a `FailWorkflow(reason)` tool. Calling this helper
> will abort the workflow immediately with a human-readable explanation. Check the
> "Tool reference" section for full details.


This document describes every built-in agent shipped with the ReelForge Workflow Engine. Agents are organised by pipeline phase. Each entry covers the agent's purpose, the tools it can call, the structured output it produces, and any configuration knobs available.

---

## Table of Contents

- [Built-in Agents Reference](#built-in-agents-reference)
  - [Table of Contents](#table-of-contents)
  - [Architecture overview](#architecture-overview)
  - [Pipeline phases](#pipeline-phases)
  - [Category: Analysis](#category-analysis)
    - [CodeStructureAnalyzer](#codestructureanalyzer)
    - [DependencyAnalyzer](#dependencyanalyzer)
    - [ComponentInventoryAnalyzer](#componentinventoryanalyzer)
    - [RouteAndApiAnalyzer](#routeandapianalyzer)
    - [StyleAndThemeExtractor](#styleandthemeextractor)
  - [Category: Translation](#category-translation)
    - [RemotionComponentTranslator](#remotioncomponenttranslator)
    - [AnimationStrategy](#animationstrategy)
  - [Category: Production](#category-production)
    - [Scriptwriter](#scriptwriter)
    - [Director](#director)
    - [Author](#author)
  - [Category: Quality](#category-quality)
    - [Review](#review)
  - [Tool reference](#tool-reference)
    - [ProjectFileAgentTools](#projectfileagenttools)
    - [ReactRemotionSandboxTools](#reactremotionsandboxtools)
    - [ProjectDataFormatterTools](#projectdataformattertools)
  - [Overriding system prompts](#overriding-system-prompts)

---

## Architecture overview

Each built-in agent extends `ReelForgeAgentBase`, which wraps an `IChatClient` and exposes an `AIAgent` (Microsoft Agents AI). Agents receive a curated list of `AIFunction` tools resolved by `AgentToolProvider.GetTools(AgentType)` at registration time. Structured output types are enforced via `ChatResponseFormat.ForJsonSchema<T>()`.

Tool access follows the **principle of least privilege**: an agent is given only the tools it legitimately needs for its phase. Analysis agents never touch the sandbox; only the `Author` agent can trigger a video render.

---

## Pipeline phases

```
Analysis ──────────────────────────────────── Translation ── Production ── Quality
    │                                               │               │          │
CodeStructureAnalyzer                RemotionComponentTranslator  Scriptwriter  Review
DependencyAnalyzer                   AnimationStrategy            Director      (loop back
ComponentInventoryAnalyzer                                         Author        if score < 9)
RouteAndApiAnalyzer
StyleAndThemeExtractor
```

The default workflow executes phases sequentially. The Review loop re-runs `Author` until `ReviewOutput.PassesReview == true` or `MaxIterations` is reached.

### Parallel Execution

Any subset of agents can be grouped into a **Parallel step** (`StepType.Parallel`). The `ParallelStepExecutor` runs all configured agents concurrently using `Parallel.ForEachAsync` and merges their individual outputs into a JSON array:

```json
[
  { "agentName": "CodeStructureAnalyzer", "output": "{...}" },
  { "agentName": "DependencyAnalyzer",    "output": "{...}" }
]
```

All five Analysis agents are natural candidates for parallel execution because they are independent and read-only. Grouping them into a single Parallel step reduces total pipeline time from ~5× a single agent call to ~1× (bounded by the slowest agent in the group).

A `ForEach` step placed after a Parallel step can iterate over each `{"agentName","output"}` element, passing each result to a downstream agent for further processing.

---

## Category: Analysis

Analysis agents read source files from the project workspace and format findings into structured JSON. They have **no sandbox access** and cannot write files.

---

### CodeStructureAnalyzer

| | |
|---|---|
| **AgentType** | `CodeStructureAnalyzer` |
| **Class** | `Analysis.CodeStructureAnalyzerAgent` |
| **Output schema** | `CodeStructureOutput` |
| **Config key** | `Agents:CodeStructureAnalyzer:SystemPrompt` |

**Purpose**  
Maps the overall directory and module structure of the source code. Identifies the project type (React SPA, Next.js App, etc.), framework version, major directories, entry points, and high-level architectural pattern.

**Tools**

| Tool | Source | Description |
|------|--------|-------------|
| `ListProjectFiles` | `ProjectFileAgentTools` | Enumerate all files in the project workspace |
| `ReadProjectFile` | `ProjectFileAgentTools` | Read a specific file by ID, storage key, or filename |
| `ReadFileTree` | `ProjectDataFormatterTools` | Parse and format a directory/file listing |
| `ReadFileContent` | `ProjectDataFormatterTools` | Extract content from a known file path |

**Output** (`CodeStructureOutput`)

```json
{
  "projectType": "Next.js App",
  "framework": "Next.js 15",
  "directories": [
    { "path": "app/", "purpose": "App Router pages and layouts", "fileCount": 14 }
  ],
  "entryPoints": ["app/layout.tsx", "app/page.tsx"],
  "overallArchitecture": "Feature-based"
}
```

---

### DependencyAnalyzer

| | |
|---|---|
| **AgentType** | `DependencyAnalyzer` |
| **Class** | `Analysis.DependencyAnalyzerAgent` |
| **Output schema** | `DependencyAnalysisOutput` |
| **Config key** | `Agents:DependencyAnalyzer:SystemPrompt` |

**Purpose**  
Focuses exclusively on UI-related dependencies: frameworks, styling libraries, component kits, animation libraries, and icon sets. Ignores backend/infra packages. Produces production recommendations relevant to Remotion rendering (e.g., `"Animation library detected — can be replicated in Remotion"`).

**Tools**

| Tool | Source | Description |
|------|--------|-------------|
| `ListProjectFiles` | `ProjectFileAgentTools` | Enumerate project files |
| `ReadProjectFile` | `ProjectFileAgentTools` | Read package manifests by reference |
| `ReadPackageManifest` | `ProjectDataFormatterTools` | Parse `package.json` / `.csproj` content |
| `ReadFileContent` | `ProjectDataFormatterTools` | Read any supplementary file |

**Output** (`DependencyAnalysisOutput`)

```json
{
  "dependencies": [
    { "name": "@mantine/core", "version": "^8.0.0", "purpose": "component library", "isCore": true }
  ],
  "devDependencies": ["@types/react"],
  "packageManager": "pnpm",
  "recommendations": [
    { "type": "info", "description": "Framer Motion detected — animations can be replicated in Remotion." }
  ]
}
```

---

### ComponentInventoryAnalyzer

| | |
|---|---|
| **AgentType** | `ComponentInventoryAnalyzer` |
| **Class** | `Analysis.ComponentInventoryAnalyzerAgent` |
| **Output schema** | `ComponentInventoryOutput` |
| **Config key** | `Agents:ComponentInventoryAnalyzer:SystemPrompt` |

**Purpose**  
Enumerates every UI component in the codebase with its file path, TypeScript props, single responsibility, and internal dependencies. Works across React (`.tsx`/`.jsx`), Vue (`.vue`), and Angular (`.component.ts`).

**Tools**

| Tool | Source | Description |
|------|--------|-------------|
| `ListProjectFiles` | `ProjectFileAgentTools` | List all project files |
| `ReadProjectFile` | `ProjectFileAgentTools` | Read individual component files |
| `ReadFileContent` | `ProjectDataFormatterTools` | Parse file content |
| `ListFilesByExtension` | `ProjectDataFormatterTools` | Filter files by extension (`.tsx`, `.vue`, etc.) |

**Output** (`ComponentInventoryOutput`)

```json
{
  "components": [
    {
      "name": "ProductCard",
      "filePath": "components/products/ProductCard.tsx",
      "props": [
        { "name": "title", "type": "string", "required": true, "defaultValue": null }
      ],
      "responsibility": "Displays product thumbnail, name, and CTA button",
      "dependencies": ["PriceTag", "AddToCartButton"]
    }
  ],
  "totalComponents": 42,
  "commonPatterns": ["Container/Presentational", "Custom Hooks"]
}
```

---

### RouteAndApiAnalyzer

| | |
|---|---|
| **AgentType** | `RouteAndApiAnalyzer` |
| **Class** | `Analysis.RouteAndApiAnalyzerAgent` |
| **Output schema** | `RouteAndApiOutput` |
| **Config key** | `Agents:RouteAndApiAnalyzer:SystemPrompt` |

**Purpose**  
Extracts all client-side routes, API endpoints, and the routing strategy. Identifies dynamic parameters, authentication guards, and HTTP methods.

**Tools**

| Tool | Source | Description |
|------|--------|-------------|
| `ListProjectFiles` | `ProjectFileAgentTools` | List all project files |
| `ReadProjectFile` | `ProjectFileAgentTools` | Read route/handler files |
| `ReadFileContent` | `ProjectDataFormatterTools` | Parse file content |
| `SearchPatterns` | `ProjectDataFormatterTools` | Find route definitions via regex-like patterns |

**Output** (`RouteAndApiOutput`)

```json
{
  "clientRoutes": [
    { "path": "/products/[id]", "componentName": "ProductPage", "parameters": ["id"], "requiresAuth": false }
  ],
  "apiEndpoints": [
    { "method": "GET", "path": "/api/products", "purpose": "List all products", "parameters": ["page", "limit"] }
  ],
  "routingStrategy": "File-based (Next.js App Router)"
}
```

---

### StyleAndThemeExtractor

| | |
|---|---|
| **AgentType** | `StyleAndThemeExtractor` |
| **Class** | `Analysis.StyleAndThemeExtractorAgent` |
| **Output schema** | `StyleAndThemeOutput` |
| **Config key** | `Agents:StyleAndThemeExtractor:SystemPrompt` |

**Purpose**  
Extracts the complete design system: color palette, typography, spacing scale, and styling approach. Output is used downstream to maintain brand fidelity in Remotion-generated videos.

**Tools**

| Tool | Source | Description |
|------|--------|-------------|
| `ListProjectFiles` | `ProjectFileAgentTools` | List all project files |
| `ReadProjectFile` | `ProjectFileAgentTools` | Read style configuration files |
| `ReadStyleConfig` | `ProjectDataFormatterTools` | Parse CSS, SCSS, or Tailwind config content |
| `ReadFileContent` | `ProjectDataFormatterTools` | Read any supplementary file |

**Output** (`StyleAndThemeOutput`)

```json
{
  "colors": {
    "primary": "#7950f2",
    "secondary": "#228be6",
    "background": "#1a1b1e",
    "text": "#c1c2c5",
    "additional": { "success": "#40c057", "error": "#fa5252" }
  },
  "typography": {
    "primaryFont": "'Inter', sans-serif",
    "secondaryFont": null,
    "fontSizes": { "sm": "14px", "base": "16px", "lg": "18px" }
  },
  "spacing": { "unit": "rem", "scale": { "xs": "0.25rem", "sm": "0.5rem", "md": "1rem" } },
  "componentStyles": ["Card shadows", "Button variants"],
  "stylingApproach": "Mantine v8"
}
```

---

## Category: Translation

Translation agents bridge the gap between the source analysis and a renderable Remotion project. They have **read/write access to both the project workspace and the sandbox**, but only `RemotionComponentTranslator` can install packages and run build checks.

---

### RemotionComponentTranslator

| | |
|---|---|
| **AgentType** | `RemotionComponentTranslator` |
| **Class** | `Translation.RemotionComponentTranslatorAgent` |
| **Output schema** | `RemotionComponentOutput` |
| **Config key** | `Agents:RemotionComponentTranslator:SystemPrompt` |

**Purpose**  
Converts the component inventory and style analysis into production-ready Remotion React components. Writes source files to the sandbox, installs any extra npm dependencies, and verifies correctness with `CheckLintAndTypeErrors`.

**Tools**

| Tool | Source | Description |
|------|--------|-------------|
| `ListProjectFiles` | `ProjectFileAgentTools` | Read project artefacts |
| `ReadProjectFile` | `ProjectFileAgentTools` | Read analysis outputs stored as project files |
| `WriteProjectFile` | `ProjectFileAgentTools` | Persist generated components as project files |
| `GetSandboxStatus` | `ReactRemotionSandboxTools` | Check if the sandbox is ready before writing |
| `EnsureSandbox` | `ReactRemotionSandboxTools` | Initialise the sandbox for this execution |
| `GetSandbox` | `ReactRemotionSandboxTools` | Retrieve sandbox metadata |
| `ListSandboxFiles` | `ReactRemotionSandboxTools` | Inspect existing sandbox contents |
| `ReadSandboxFile` | `ReactRemotionSandboxTools` | Read an existing sandbox file |
| `WriteSandboxFile` | `ReactRemotionSandboxTools` | Write generated TSX/JSX to the sandbox |
| `DeleteSandboxPath` | `ReactRemotionSandboxTools` | Clean up obsolete files |
| `InstallNpmPackages` | `ReactRemotionSandboxTools` | Add extra npm packages required by generated code |
| `CheckLintAndTypeErrors` | `ReactRemotionSandboxTools` | Validate TypeScript types and ESLint rules |
| `RunSandboxNpmScript` | `ReactRemotionSandboxTools` | Run `build` or `typecheck` scripts |

**Output** (`RemotionComponentOutput`)

```json
{
  "components": [
    {
      "componentName": "HeroBanner",
      "remotionCode": "import { AbsoluteFill } from 'remotion'; ...",
      "durationInFrames": 90,
      "description": "Opening hero with animated headline",
      "defaultProps": { "headline": "Ship Faster" }
    }
  ],
  "remotionVersion": "4.0",
  "requiredImports": ["framer-motion"]
}
```

---

### AnimationStrategy

| | |
|---|---|
| **AgentType** | `AnimationStrategyAgent` |
| **Class** | `Translation.AnimationStrategyAgentImpl` |
| **Output schema** | `AnimationStrategyOutput` |
| **Config key** | `Agents:AnimationStrategy:SystemPrompt` |

**Purpose**  
Designs the full frame-level animation plan: scene sequencing, per-element animation timings, transition types, and overall pacing tone. Reads existing sandbox files created by `RemotionComponentTranslator` but does not modify them.

**Tools**

| Tool | Source | Description |
|------|--------|-------------|
| `ListProjectFiles` | `ProjectFileAgentTools` | Read project artefacts |
| `ReadProjectFile` | `ProjectFileAgentTools` | Access component/analysis outputs |
| `GetSandboxStatus` | `ReactRemotionSandboxTools` | Confirm sandbox state |
| `GetSandbox` | `ReactRemotionSandboxTools` | Retrieve sandbox metadata |
| `ListSandboxFiles` | `ReactRemotionSandboxTools` | Browse sandbox file tree |
| `ReadSandboxFile` | `ReactRemotionSandboxTools` | Read component files for timing context |

**Output** (`AnimationStrategyOutput`)

```json
{
  "scenes": [
    {
      "id": "scene_01",
      "componentName": "HeroBanner",
      "startFrame": 0,
      "durationInFrames": 90,
      "transitionType": "fade",
      "transitionDurationInFrames": 15,
      "animations": [
        { "elementId": "headline", "animationType": "slideUp", "startFrame": 0, "durationInFrames": 30, "parameters": {} }
      ]
    }
  ],
  "totalDurationInFrames": 270,
  "fps": 30,
  "pacing": {
    "overallTone": "energetic",
    "averageSceneDuration": 90,
    "pacingNotes": ["Keep intro under 3 s for attention retention"]
  }
}
```

---

## Category: Production

Production agents synthesise all prior analysis and translation work into creative deliverables — scripts, shot plans, and finally the full render manifest — before handing off to the quality gate.

---

### Scriptwriter

| | |
|---|---|
| **AgentType** | `ScriptwriterAgent` |
| **Class** | `Production.ScriptwriterAgentImpl` |
| **Output schema** | `ScriptwriterOutput` |
| **Config key** | `Agents:Scriptwriter:SystemPrompt` |

**Purpose**  
Writes the voiceover and on-screen caption script for every scene. Uses the app's purpose, features, and target audience (read from project files) to produce benefit-focused, naturally timed copy.

**Tools**

| Tool | Source | Description |
|------|--------|-------------|
| `ListProjectFiles` | `ProjectFileAgentTools` | Enumerate available project artefacts |
| `ReadProjectFile` | `ProjectFileAgentTools` | Read analysis and component outputs |
| `GetSandboxStatus` | `ReactRemotionSandboxTools` | Check sandbox state |
| `ListSandboxFiles` | `ReactRemotionSandboxTools` | Browse sandbox for context |
| `ReadSandboxFile` | `ReactRemotionSandboxTools` | Read component files for content reference |

**Output** (`ScriptwriterOutput`)

```json
{
  "title": "ReelForge – Ship Faster",
  "durationInSeconds": 30,
  "narrative": "Hero to CTA arc showcasing the workflow builder and fast render times.",
  "scenes": [
    {
      "sceneId": "scene_01",
      "startTime": 0,
      "duration": 8,
      "voiceover": "Building promotional videos used to take days. Not anymore.",
      "onScreenText": ["ReelForge", "Generate videos in minutes"],
      "visualDescription": "Animated hero banner with headline fade-in"
    }
  ]
}
```

---

### Director

| | |
|---|---|
| **AgentType** | `DirectorAgent` |
| **Class** | `Production.DirectorAgentImpl` |
| **Output schema** | `DirectorOutput` |
| **Config key** | `Agents:Director:SystemPrompt` |

**Purpose**  
Provides shot-level cinematographic direction: camera angles, movement, visual elements to emphasise, and audio guidance (music style, sound effects, voiceover tone). Ensures the video has a clear narrative arc from opening hook to closing call-to-action.

**Tools**

| Tool | Source | Description |
|------|--------|-------------|
| `ListProjectFiles` | `ProjectFileAgentTools` | Enumerate project artefacts |
| `ReadProjectFile` | `ProjectFileAgentTools` | Read script and component data |
| `GetSandboxStatus` | `ReactRemotionSandboxTools` | Check sandbox state |
| `ListSandboxFiles` | `ReactRemotionSandboxTools` | Browse sandbox contents |
| `ReadSandboxFile` | `ReactRemotionSandboxTools` | Read Remotion components for visual reference |

**Output** (`DirectorOutput`)

```json
{
  "shots": [
    {
      "shotId": "shot_01",
      "sceneId": "scene_01",
      "description": "Opening hero — brand logo dissolve-in",
      "startTime": 0,
      "duration": 3,
      "camera": { "angle": "wide", "movement": "static", "focus": "center" },
      "visualElements": ["brand logo", "tagline text"]
    }
  ],
  "visualTheme": {
    "mood": "professional and energetic",
    "colorGrading": "dark with vibrant accent highlights",
    "visualMotifs": ["gradient overlays", "rounded corners"]
  },
  "audio": {
    "musicStyle": "upbeat electronic",
    "soundEffects": "subtle UI click sounds at transitions",
    "voiceover": "calm, authoritative, mid-pace"
  },
  "totalDurationInSeconds": 30
}
```

---

### Author

| | |
|---|---|
| **AgentType** | `AuthorAgent` |
| **Class** | `Production.AuthorAgentImpl` |
| **Output schema** | `RenderManifestOutput` |
| **Config key** | `Agents:Author:SystemPrompt` |

**Purpose**  
The final assembler. Takes all prior outputs and produces a `RenderManifestOutput` JSON, then orchestrates the full Remotion render pipeline: writing the manifest to the sandbox, triggering `RenderVideoAndUploadToStorage`, and finalising the sandbox with `CompleteSandbox`. This is the **only agent** that can trigger video rendering.

**Tools**

| Tool | Source | Description |
|------|--------|-------------|
| `ListProjectFiles` | `ProjectFileAgentTools` | Enumerate all project artefacts |
| `ReadProjectFile` | `ProjectFileAgentTools` | Read all prior-stage outputs |
| `WriteProjectFile` | `ProjectFileAgentTools` | Persist the render manifest as a project file |
| `GetSandboxStatus` | `ReactRemotionSandboxTools` | Verify sandbox readiness |
| `EnsureSandbox` | `ReactRemotionSandboxTools` | Initialise sandbox if not already created |
| `GetSandbox` | `ReactRemotionSandboxTools` | Read sandbox metadata |
| `ListSandboxFiles` | `ReactRemotionSandboxTools` | Inspect sandbox contents |
| `ReadSandboxFile` | `ReactRemotionSandboxTools` | Read existing sandbox files |
| `WriteSandboxFile` | `ReactRemotionSandboxTools` | Write the assembled manifest and entry point |
| `DeleteSandboxPath` | `ReactRemotionSandboxTools` | Clean up obsolete sandbox files |
| `InstallNpmPackages` | `ReactRemotionSandboxTools` | Install any last-mile dependencies |
| `CheckLintAndTypeErrors` | `ReactRemotionSandboxTools` | Validate code before render |
| `RunSandboxNpmScript` | `ReactRemotionSandboxTools` | Run `build` / `typecheck` scripts |
| `RunSandboxRemotionCommand` | `ReactRemotionSandboxTools` | Execute Remotion CLI (`render`, `still`, `compositions`) |
| `RenderVideoAndUploadToStorage` | `ReactRemotionSandboxTools` | Render composition and upload result to S3 (stored under `projects/{projectId}/outputFiles/`) |
| `CompleteSandbox` | `ReactRemotionSandboxTools` | Finalise and tear down the sandbox |

**Output** (`RenderManifestOutput`)

```json
{
  "projectName": "ReelForge Promo v1",
  "video": { "width": 1920, "height": 1080, "fps": 30, "durationInFrames": 900 },
  "compositions": [
    {
      "id": "scene_01",
      "componentName": "HeroBanner",
      "startFrame": 0,
      "durationInFrames": 90,
      "props": { "headline": "Ship Faster" },
      "script": {
        "voiceover": "Building promotional videos used to take days. Not anymore.",
        "captions": ["ReelForge", "Generate videos in minutes"]
      }
    }
  ],
  "assets": [],
  "metadata": {}
}
```

---

## Category: Quality

### Review

| | |
|---|---|
| **AgentType** | `ReviewAgent` |
| **Class** | `Quality.ReviewAgentImpl` |
| **Output schema** | `ReviewOutput` |
| **Config key** | `Agents:Review:SystemPrompt` |

**Purpose**  
Quality gate for the full production pipeline. Scores the `RenderManifest`, script, and component outputs from 1–10 across four criteria: narrative clarity, visual accuracy, timing, and completeness. Sets `passesReview = true` only when `overallScore >= 9` with no critical issues. When `passesReview` is `false`, the `ReviewLoopStepExecutor` sends the pipeline back to `Author` for revision.

**Tools**

| Tool | Source | Description |
|------|--------|-------------|
| `ListProjectFiles` | `ProjectFileAgentTools` | Enumerate all project artefacts |
| `ReadProjectFile` | `ProjectFileAgentTools` | Read the render manifest and prior outputs |
| `GetSandboxStatus` | `ReactRemotionSandboxTools` | Check sandbox state |
| `GetSandbox` | `ReactRemotionSandboxTools` | Retrieve sandbox metadata |
| `ListSandboxFiles` | `ReactRemotionSandboxTools` | Browse sandbox output files |
| `ReadSandboxFile` | `ReactRemotionSandboxTools` | Inspect rendered artefacts |
| `CheckLintAndTypeErrors` | `ReactRemotionSandboxTools` | Verify technical correctness of source code |

**Output** (`ReviewOutput`)

```json
{
  "overallScore": 9,
  "criteria": [
    { "name": "narrativeClarity", "score": 9, "feedback": "Hook is strong, CTA lands clearly." },
    { "name": "visualAccuracy",   "score": 9, "feedback": "Brand colors and typography match source." },
    { "name": "timing",           "score": 8, "feedback": "Scene 3 could be trimmed by ~10 frames." },
    { "name": "completeness",     "score": 10, "feedback": "All compositions present and renderable." }
  ],
  "strengths": ["Consistent brand voice", "Smooth fade transitions"],
  "improvementAreas": ["Scene 3 pacing slightly slow"],
  "passesReview": true,
  "summary": "Production-ready. Minor timing note for future iteration."
}
```

---

## Tool reference

### ProjectFileAgentTools

Scoped to the current execution's project. All methods resolve the project ID from the active `WorkflowExecutionContext`.

| Method | Description |
|--------|-------------|
| `ListProjectFiles()` | Returns JSON array of `ProjectWorkspaceFile` metadata for all files in the project |
| `ReadProjectFile(fileReference)` | Reads file content by ID, storage key, or original filename |
| `WriteProjectFile(fileName, content, contentType?)` | Creates or replaces a text file in the project workspace |

### ReactRemotionSandboxTools

### WorkflowAbortTool

Provides a single helper that agents can call to abort the entire workflow when
further progress is impossible. The tool throws a special exception that bypasses
retry logic and is surfaced to the user as the execution error message.

| Method | Allowed agents | Description |
|--------|---------------|-------------|
| `FailWorkflow(reason)` | **all** | Abort workflow immediately with a human‑readable `reason`. Should be
used only for unrecoverable conditions such as missing required inputs or
inconsistent state; transient issues should allow normal failure + retry.


Scoped to the current execution's sandbox (identified by `executionId`). All write/exec operations are validated server-side.

| Method | Allowed agents | Description |
|--------|---------------|-------------|
| `GetSandboxStatus()` | All translation/production/review | Check sandbox existence and readiness |
| `EnsureSandbox()` | Translator, Author | Create or reuse the execution sandbox |
| `GetSandbox()` | Translator, AnimationStrategy, Author, Review | Retrieve sandbox metadata |
| `ListSandboxFiles(path?)` | Translator, AnimationStrategy, Director, Scriptwriter, Author, Review | List files/directories inside sandbox |
| `ReadSandboxFile(path)` | Translator, AnimationStrategy, Director, Scriptwriter, Author, Review | Read a UTF-8 text file |
| `WriteSandboxFile(path, content)` | Translator, Author | Write UTF-8 text to sandbox |
| `DeleteSandboxPath(path)` | Translator, Author | Delete a file or directory |
| `InstallNpmPackages(packages[])` | Translator, Author | Install npm packages; names are validated server-side |
| `CheckLintAndTypeErrors()` | Translator, Author, Review | Run TypeScript type-check + ESLint |
| `RunSandboxNpmScript(script, args?, timeout?)` | Translator, Author | Run `build`, `render`, `typecheck`, `compositions`, or `lint` |
| `RunSandboxRemotionCommand(command, args?, timeout?)` | Author | Run `render`, `still`, or `compositions` via Remotion CLI |
| `RenderVideoAndUploadToStorage(compositionId, outputFileName, remotionArgs?)` | Author | Full render pipeline: render → upload to S3 → attach storage key to step result |
| `CompleteSandbox()` | Author | Finalise and delete the sandbox |

### ProjectDataFormatterTools

Static helpers that format in-memory data passed to the LLM context. They do not perform I/O themselves.

| Method | Description |
|--------|-------------|
| `ReadFileTree(fileListingData)` | Returns the file listing as-is for LLM consumption |
| `ReadFileContent(filePath, projectData)` | Returns `filePath` + `projectData` for targeted file reads |
| `ListFilesByExtension(extension, fileListingData)` | Filters a listing to files matching `extension` |
| `ReadPackageManifest(manifestContent)` | Passes manifest content directly to the LLM |
| `SearchPatterns(pattern, sourceContent)` | Returns `pattern` + `sourceContent` for pattern-based searches |
| `ReadStyleConfig(styleContent)` | Passes style config content directly to the LLM |

---

## Overriding system prompts

Every built-in agent reads its system prompt from `appsettings.json` before using the hardcoded default:

```json
{
  "Agents": {
    "CodeStructureAnalyzer": {
      "SystemPrompt": "Your custom prompt here."
    },
    "RemotionComponentTranslator": {
      "SystemPrompt": "..."
    }
  }
}
```

The config key is always `Agents:<AgentName>:SystemPrompt` where `<AgentName>` matches the string passed to `ReelForgeAgentBase` constructor (`"CodeStructureAnalyzer"`, `"DependencyAnalyzer"`, `"ComponentInventoryAnalyzer"`, `"RouteAndApiAnalyzer"`, `"StyleAndThemeExtractor"`, `"RemotionComponentTranslator"`, `"AnimationStrategy"`, `"Scriptwriter"`, `"Director"`, `"Author"`, `"Review"`).

Override via Docker Compose environment variables using the standard ASP.NET double-underscore convention:

```yaml
environment:
  Agents__Author__SystemPrompt: "Your custom Author prompt."
```
