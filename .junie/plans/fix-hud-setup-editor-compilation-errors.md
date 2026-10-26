---
sessionId: session-261021-062012-1svi
---

# Technical Design

### Current Implementation

Phase 6 (EvolutionHUD + CheckpointManager + HUDSetupEditor) was created in the previous session. The HUDSetupEditor is an Editor menu tool that auto-configures the HUD Canvas UI elements in the Unity scene.

### Key Decisions

- Changed `[SerializeField]` fields to `[SerializeField] internal` on EvolutionHUD and EvolutionCheckpointManager so the Editor assembly can access them
- Removed duplicate variable declaration in HUDSetupEditor to fix CS0136 scope collision

### Proposed Changes

1. **EvolutionHUD.cs** — Add `internal` modifier to all 12 `[SerializeField]` fields
2. **EvolutionCheckpointManager.cs** — Add `internal` modifier to `simulationManager` field
3. **HUDSetupEditor.cs** — Remove inner `var hud` declaration that shadows outer scope

### Verification

Both `Assembly-CSharp.csproj` and `Assembly-CSharp-Editor.csproj` compile with 0 errors.

# Testing

### Validation Approach

- Compiled both Assembly-CSharp and Assembly-CSharp-Editor projects
- Verified 0 errors in both

### Key Scenarios

- Editor assembly can access EvolutionHUD serialized fields
- Editor assembly can access EvolutionCheckpointManager serialized fields
- No variable shadowing in HUDSetupEditor.SetupHUD()

# Delivery Steps

###   Step 1: Fix field visibility in EvolutionHUD and EvolutionCheckpointManager
Change all [SerializeField] fields to [SerializeField] internal so the Editor assembly can access them.

- EvolutionHUD.cs: 12 fields (simulationManager, generationText, timeRemainingText, stateText, pairsText, populationText, bestScoreText, worstScoreText, averageScoreText, previousGenText, pauseButton, pauseButtonText)
- EvolutionCheckpointManager.cs: 1 field (simulationManager)

###   Step 2: Fix variable shadowing in HUDSetupEditor and verify compilation
Remove duplicate `hud` variable declaration and verify both projects compile.

- HUDSetupEditor.cs: Remove inner `var hud` in the if-block that shadows the outer scope variable
- Verify Assembly-CSharp.csproj builds with 0 errors
- Verify Assembly-CSharp-Editor.csproj builds with 0 errors