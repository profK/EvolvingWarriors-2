---
sessionId: session-261024-131002-1m63
---

# Current State

### Git Status
- **Last commit**: `97c81a1 TposeTool`
- **Working tree**: contains uncommitted changes since that commit

### Uncommitted Changes Identified

**Warrior Feet (TwoBoneIK.cs)**:
- Added foot rotation logic — sole always faces ground, forward direction projected onto ground plane
- Improved bend direction comments explaining `bendNormal` geometry
- NaN/Infinity guards in computed rotations

**HUD (EvolutionHUD.cs + TrainingScene.unity)**:
- New `EvolutionHUD.cs` script — reads generation, time, state, scores from `EvolutionSimulationManager` and displays in UI
- New UI canvas in `TrainingScene.unity` with generation text, time remaining, state text, and pause button
- New `HUDSetupEditor.cs` editor helper
- New `EvolutionCheckpointManager.cs` for per-generation best-score tracking
- TextMesh Pro assets added

**EvolutionSimulationManager.cs**:
- New properties: `PairCount`, `PopulationSize`, `IsPaused`
- Pause/Resume/TogglePause methods
- Checkpoint manager integration on generation complete
- `DeactivateAll()` / `ActivateAll()` helpers

**Tests**:
- `TwoBoneIKTests.cs`: visibility fixes, degenerate-case X offsets (0.01) to avoid `dir` parallel to `bendAxis`
- `WarriorPoseSolverTests.cs`: visibility fixes
- `ProceduralFootControllerTests.cs`: visibility fixes
- New `FootOnFloorTests.cs`

**Unrelated changes** (to be discarded): Magic Pig asset store files, ReadmeEditor, Packages/manifest, Recovery scene, Plan.md, URP settings

### What to Keep vs Discard
- **KEEP**: TwoBoneIK.cs, EvolutionHUD.cs, HUDSetupEditor.cs, EvolutionCheckpointManager.cs, EvolutionSimulationManager.cs, TrainingScene.unity, test files, TextMesh Pro
- **DISCARD**: Magic Pig PBR changes, ReadmeEditor, Packages, Recovery scene, Plan.md, URP settings, indexLayout.xml

# Technical Design

### Strategy: Reset Hard + Restore from Backups

1. **Save** all working tree changes to /tmp patches and copy new files to /tmp
2. **`git reset --hard HEAD`** to roll back to `97c81a1 TposeTool`
3. **Reapply warrior feet**: apply TwoBoneIK.cs patch
4. **Reapply HUD**: restore EvolutionHUD.cs, HUDSetupEditor.cs, EvolutionCheckpointManager.cs from backups; reapply TrainingScene.unity diff; reapply EvolutionSimulationManager.cs diff
5. **Reapply tests**: restore TwoBoneIKTests.cs, WarriorPoseSolverTests.cs, ProceduralFootControllerTests.cs patches; copy FootOnFloorTests.cs
6. **Restore TextMesh Pro** folders from backups
7. **Discard** all unrelated changes (Magic Pig, Packages, Recovery scene, etc.)

### Files to Reapply
| Category | Files |
|---|---|
| Warrior Feet | `TwoBoneIK.cs` (patch) |
| HUD UI | `EvolutionHUD.cs` + meta (copy), `TrainingScene.unity` (patch) |
| HUD Support | `HUDSetupEditor.cs` + meta (copy), `EvolutionCheckpointManager.cs` + meta (copy) |
| Simulation Manager | `EvolutionSimulationManager.cs` (patch) |
| Tests | `TwoBoneIKTests.cs`, `WarriorPoseSolverTests.cs`, `ProceduralFootControllerTests.cs` (patches), `FootOnFloorTests.cs` + meta (copy) |
| Dependencies | TextMesh Pro Fonts/Resources/Shaders/Sprites (copy) |

### Risks
- TrainingScene.unity patch may have merge conflicts if the scene was large — manual review needed
- TextMesh Pro assets may need to be re-imported by Unity after restore
- Unity's Library/ cache may need refresh after file restoration

# Delivery Steps

### ✓ Step 1: Save working tree changes to /tmp
Back up all uncommitted changes before resetting.

### ✓ Step 2: Reset to last commit and reapply HUD + feet changes
Roll back to `97c81a1 TposeTool` and selectively restore only the HUD and warrior feet work.

### ✓ Step 3: Verify restoration and clean up
Confirm the restored files compile and the project is in a clean state with only the intended changes.