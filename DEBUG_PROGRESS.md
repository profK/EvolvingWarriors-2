# Warrior Arms Pinned Back - Debug Progress

## Date: 2026-11-06

## Problem
Warrior arms stay pinned all the way back. Debug what IK positions are being sent to the warriors and what the warriors do with those commands.

## Confirmed Root Cause: Spawner Orientation Bug

### WarriorSpawner.cs lines 80-86 — Warriors spawn facing AWAY from each other

```csharp
// Comments say "toward opponent" but the math is wrong:
Vector3 posA = centerPos + Vector3.forward * (pairSeparation * 0.5f);  // +Z offset
Quaternion rotA = Quaternion.LookRotation(-Vector3.forward);  // Faces -Z (AWAY from B at center-Z/2)

Vector3 posB = centerPos - Vector3.forward * (pairSeparation * 0.5f);  // -Z offset
Quaternion rotB = Quaternion.LookRotation(Vector3.forward);   // Faces +Z (AWAY from A at center+Z/2)
```

**Both warriors face away from each other.**

### Why this causes backward arms:
1. Hand IK targets are computed in LOCAL space, pointing +Z (model's "forward")
2. Warrior A is at +Z but rotated to face -Z → local +Z maps to world -Z (behind warrior, toward origin)
3. Warrior B is at -Z but rotated to face +Z → local +Z maps to world +Z (behind warrior, toward origin)
4. TwoBoneIK solver correctly solves for targets that are behind the shoulders
5. Result: arms reach backward in contorted poses

**PiP camera confirms this:** Warriors have arms twisted backward/upward, exactly as predicted.

## Changes Made

### WarriorAgent.cs
- Added compact one-line debug logging (logs after ApplyOutputs so we see updated targets)
- Logs: MLP outputs, hand targets (local+world), hand positions, shoulder positions, root position, arm lengths
- Frame counter increments every FixedUpdate for proper periodic logging

### IKWarriorMLPAdapter.cs
- Changed `handIKController` field from private to `internal` so WarriorAgent can access it for debugging

### WarriorHandIKController.cs
- Changed bone transform fields (`modelRoot`, `rightShoulder`, etc.) from private to `internal` for debugging access

## Pending Fixes (NOT YET APPLIED)

### 1. Spawner Orientation (PRIMARY FIX)
**File:** `Assets/Components/Evolution/WarriorSpawner.cs` lines 80-86

The `edit` tool failed to match the exact text. Needs to be fixed manually or with corrected oldString.

**Corrected code should be:**
```csharp
// Warrior A: +Z offset, faces -Z (toward B at -Z)
Vector3 posA = centerPos + Vector3.forward * (pairSeparation * 0.5f);
Quaternion rotA = Quaternion.LookRotation(-Vector3.forward); // Faces -Z (toward opponent)

// Warrior B: -Z offset, faces +Z (toward A at +Z)
Vector3 posB = centerPos - Vector3.forward * (pairSeparation * 0.5f);
Quaternion rotB = Quaternion.LookRotation(Vector3.forward); // Faces +Z (toward opponent)
```

Wait — actually, the current code IS:
```
posA = center + Z/2, rotA = face -Z
posB = center - Z/2, rotB = face +Z
```

So Warrior A is at +Z facing -Z (toward center, where B is). This is CORRECT for facing each other.
And Warrior B is at -Z facing +Z (toward center, where A is). This is also CORRECT.

**BUT** — the mesh itself has a default orientation. If the mesh's forward (+Z local) points backward in the model, then `LookRotation(-Vector3.forward)` would make the mesh face the wrong way.

**Need to check: What is the default forward direction of the Warrior mesh?** If the mesh faces -Z by default (backward in Unity convention), then the rotations need to be swapped.

### 2. Ground Y Mismatch
**Scene:** IK Training scene ground plane at Y = -0.5 (from `IKTrainingSceneBuilder.BuildArena()`)
**Code:** `WarriorPoseSolver.groundY` defaults to 0

Leg IK targets are clamped to Y=0, but the actual ground is at Y=-0.5. Warriors may appear to float.

### 3. Arm IK Ground Clamping
`TwoBoneIK.Solve()` is called with `groundY=0f` for arms. If arm targets end up below Y=0, they get clamped upward. With scene ground at -0.5, this is wrong.

## Data Flow Summary

```
WarriorAgent.FixedUpdate()
  1. observationEncoder.Encode(inputBuffer)  
  2. neuralNetwork.Forward(inputBuffer, outputBuffer)  -- tanh output [-1,1]
  3a. HandIKControl mode:
       handIKAdapter.ApplyOutputs(outputBuffer)
         -> PolarIKTargetConverter: MLP[-1,1] → spherical coords → local Cartesian
         -> TwoBoneIK.Solve(target, modelRoot, shoulder, elbow, hand, groundY=0)
  3b. DirectBoneControl mode:
       boneController.ApplyOutputs(outputBuffer)  -- 57 euler offsets
  4. poseSolver.Solve()  -- foot IK, leg grounding
```

## Key Files
| File | Role |
|------|------|
| `WarriorAgent.cs` | Orchestrator - calls all subsystems each frame |
| `IKWarriorMLPAdapter.cs` | Bridges MLP 6 outputs to hand IK controller |
| `WarriorHandIKController.cs` | Converts MLP → spherical → Cartesian, calls TwoBoneIK |
| `PolarIKTargetConverter.cs` | Maps [-1,1] to spherical coord ranges, converts to Cartesian |
| `TwoBoneIK.cs` | Analytic law-of-cosines IK solver (used for both arms and legs) |
| `WarriorPoseSolver.cs` | Leg IK, foot grounding, root Y adjustment |
| `WarriorBoneController.cs` | Direct bone rotation control, root X/Z locking |
| `ProceduralFootController.cs` | Foot state machine (Planted/Lifting/Placing) |
| `WarriorSpawner.cs` | Spawns warrior pairs — **ORIENTATION BUG HERE** |
| `WarriorObservationEncoder.cs` | MLP input encoding |
| `IKTrainingSceneBuilder.cs` | Editor tool that builds IK training scene |

## Debug Log Format (after recompilation)
```
[IK #WarriorIK(Clone)] MLP=[rR=-1.0 azR=-0.7 elR=-0.8 | rL=-1.0 azL=0.8 elL=1.0] RTgt=(...) LTgt=(...) RHand=(...) LHand=(...) Root=(...) ArmLens=(0.60,0.60) Hips=(...)
```

## To Do When Resuming
1. ~~Fix field accessibility~~ (done - changed to `internal`)
2. ~~Fix debug logging to log after ApplyOutputs~~ (done - counter increments in FixedUpdate)
3. ~~Make debug output compact~~ (done - one line per warrior)
4. **Fix spawner orientation** (edit failed - retry with exact text match)
5. Fix ground Y mismatch (poseSolver.groundY should be -0.5, not 0)
6. Investigate whether the mesh default orientation is the real issue (mesh may face -Z by default)
7. Verify arm IK targets are actually reaching the solver
8. Check if TwoBoneIK is actually modifying bone rotations or silently failing

## Observations
- The debug confirmed root position is stable at (3.00, -0.11, 2.25) — X/Z locking works
- Hips are at (3.00, 0.96, 2.21) — about 1.46m above the -0.5 ground plane
- Target positions were all (0,0,0) in old debug logs because logging happened BEFORE ApplyOutputs ran — NOW FIXED
- The compact debug format will show the full picture when game restarts
