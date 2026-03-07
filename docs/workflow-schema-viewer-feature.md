# Workflow Builder Output Schema Feature

## Overview

The workflow builder now provides interactive schema visualization to help users understand available output fields from previous workflow steps. This is particularly useful when configuring Conditional, ForEach, and ReviewLoop step types that depend on outputs from earlier steps.

## What's New

### 1. Enhanced Backend Schema Generation

**File**: `inference/src/ReelForge.WorkflowEngine/Agents/ReelForgeAgentBase.cs`

The `GenerateJsonSchemaDocumentation` method now uses reflection to extract detailed property information from agent output schema types, including:

- Property names and types
- Nested object structures
- Array element types
- Type descriptions (when available from attributes)

**Example Output Schema**:
```json
{
  "type": "object",
  "schemaType": "ReelForge.Shared.Data.OutputSchemas.CodeStructureOutput",
  "properties": {
    "ProjectType": { "type": "string" },
    "Framework": { "type": "string" },
    "Directories": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "Path": { "type": "string" },
          "Purpose": { "type": "string" },
          "FileCount": { "type": "integer" }
        }
      }
    },
    "EntryPoints": {
      "type": "array",
      "items": { "type": "string" }
    },
    "OverallArchitecture": { "type": "string" }
  }
}
```

### 2. New Frontend Component: OutputSchemaViewer

**File**: `web/components/workflows/OutputSchemaViewer.tsx`

A collapsible accordion component that displays available output fields from all previous workflow steps. Features include:

- **Step-by-step organization**: Each previous step is shown in a separate accordion item
- **Field explorer**: Shows all available fields with their types
- **Nested structure visualization**: Displays object hierarchies and array schemas
- **Copy-to-clipboard**: One-click copying of field expression paths (e.g., `{{steps.1.components}}`)
- **Smart parsing**: Handles various JSON schema formats automatically

### 3. Updated Step Configuration Components

All three step type configuration components now include the schema viewer:

#### ConditionalStepConfig
**File**: `web/components/workflows/ConditionalStepConfig.tsx`

Shows available fields to use in condition expressions like:
- `[score] >= 9`
- `[components.length] > 5`

#### ForEachStepConfig
**File**: `web/components/workflows/ForEachStepConfig.tsx`

Shows available collection fields to iterate over, such as:
- `{{steps.1.components}}`
- `{{steps.2.scenes}}`

#### ReviewLoopStepConfig
**File**: `web/components/workflows/ReviewLoopStepConfig.tsx`

Shows available fields from previous steps, with a note that the ReviewLoop agent expects a `score` field (1-10) in the target step output.

### 4. Enhanced Workflow Nodes

All workflow node components now feature a **Settings icon** (⚙️) button that opens a configuration modal with the full schema viewer:

- **ConditionalNode**: Opens modal showing condition expression editor + available schemas
- **ForEachNode**: Opens modal showing loop configuration + available collection fields
- **ReviewLoopNode**: Opens modal showing review configuration + available score fields

**Files**:
- `web/components/workflows/nodes/ConditionalNode.tsx`
- `web/components/workflows/nodes/ForEachNode.tsx`
- `web/components/workflows/nodes/ReviewLoopNode.tsx`

### 5. FlowchartBuilder Context Passing

**File**: `web/components/workflows/FlowchartBuilder.tsx`

The FlowchartBuilder now passes workflow context to each node:
- `allSteps`: Complete list of workflow steps
- `currentStepIndex`: Index of the current step (for filtering previous steps)

This enables each node to show only relevant output schemas from preceding steps.

## How to Use

### For Users

1. **Create a workflow** with multiple steps in the workflow builder
2. **Add an agent step** (e.g., CodeStructureAnalyzer) that produces structured output
3. **Add a Conditional, ForEach, or ReviewLoop step** after the agent step
4. **Click the Settings icon (⚙️)** on the step node
5. **View available schemas** in the accordion at the bottom of the modal
6. **Click to expand** a previous step's schema to see all available fields
7. **Copy field expressions** by clicking the copy icon next to any field
8. **Paste expressions** into your condition, loop source, or other configuration fields

### Example Workflow

```
Step 1: CodeStructureAnalyzer
  Output: { "components": [...], "framework": "Next.js", ... }

Step 2: ForEach (Loop over components)
  Click ⚙️ → View schemas → Expand "Step 1" → Copy "{{steps.1.components}}"
  Loop Source Expression: {{steps.1.components}}

Step 3: RemotionComponentTranslator (processes each component)
  Agent processes one component per iteration

Step 4: ReviewLoop (Quality check)
  Click ⚙️ → View schemas → See available fields from Step 3
  Min Score: 9
  Loop Target: Step 3
```

## Expression Syntax

### NCalc Expressions (Conditional Steps)
Use **square brackets** for field references:
- `[score] >= 9`
- `[passesReview] == true`
- `[components.length] > 5`

### Template Expressions (ForEach, InputMapping)
Use **double curly braces** for field references:
- `{{steps.1.components}}`
- `{{steps.2.scenes}}`
- `{{steps.3.title}}`

## Benefits

1. **Reduced Errors**: Users can see exactly what fields are available, preventing typos and incorrect field references
2. **Better UX**: No need to remember output schema structures or look at documentation
3. **Faster Workflow Creation**: Copy-paste field paths instead of typing them manually
4. **Self-Documenting**: The schema viewer serves as live documentation for each agent's output
5. **Type Safety**: Shows field types (string, integer, array, object) to help users understand data structures

## Technical Details

### Schema Generation Flow

1. **Agent Registration**: Each agent specifies its `outputSchemaType` in the constructor
2. **Schema Generation**: `ReelForgeAgentBase` uses reflection to generate JSON schema on instantiation
3. **Database Storage**: Schema is stored in `agent_definitions.output_schema_json` column
4. **API Response**: Schema is included in `GET /api/v1/agents` responses
5. **Frontend Parsing**: `OutputSchemaViewer` parses the JSON schema and renders an interactive UI

### Performance Considerations

- Schema generation happens once per agent at startup
- Schemas are cached in memory and served via SWR on the frontend
- Modal-based UI ensures schema viewer doesn't slow down the main workflow canvas
- Accordion design allows users to expand only the schemas they need

## Future Enhancements

Potential improvements for future iterations:

1. **Search/Filter**: Add search functionality to find specific fields across all schemas
2. **Field Validation**: Real-time validation of expressions against available schemas
3. **Autocomplete**: IDE-like autocomplete when typing field references
4. **Schema Diff**: Show what changed between workflow runs
5. **Custom Descriptions**: Allow users to add documentation to custom agent output fields
6. **Visual Field Picker**: Click-to-insert field references instead of copy-paste

## Migration Notes

This feature is **fully backward compatible**. Existing workflows will continue to work without changes. The schema viewer is purely additive and doesn't modify workflow execution logic.

## Testing

To test the feature:

1. Start the full stack: `docker compose up --build`
2. Create a project and upload some source files
3. Create a workflow with at least 2 agent steps
4. Add a Conditional or ForEach step after the agent steps
5. Click the ⚙️ icon on the step node
6. Verify that the schema viewer shows previous steps' output schemas
7. Click copy icons and verify expressions are copied correctly
8. Test workflow execution to ensure expressions work as expected
