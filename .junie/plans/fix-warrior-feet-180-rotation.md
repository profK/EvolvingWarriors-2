---
sessionId: session-261023-172935-1vn1
---

# Technical Design

### Root Cause

In `TwoBoneIK.cs` (line 251), the foot rotation is computed as:

```csharp
Quaternion targetFootRot = Quaternion.LookRotation(footForward, bendNormal);
```

`bendNormal` is the upward direction in the bend plane (pointing toward sky, away from ground). `Quaternion.LookRotation(forward, up)` maps the `up` parameter to the transform's local Y-axis.

Unity's humanoid rig (Kevin Iglesias' `HumanCharacterDummy_M`) has the foot bone's local Y-axis pointing **down** through the sole of the foot. So assigning `bendNormal` (which points up) to the foot's Y-axis effectively rotates the foot 180° — the sole faces sky instead of ground.

### Fix

**File**: `Assets/Components/Warrior/TwoBoneIK.cs` — line 251

Change from:
```csharp
Quaternion targetFootRot = Quaternion.LookRotation(footForward, bendNormal);
```

To:
```csharp
Quaternion targetFootRot = Quaternion.LookRotation(footForward, -bendNormal);
```

Negating `bendNormal` maps the downward direction to the foot's Y-axis, which aligns with the mesh's sole-facing-down orientation. This is the standard convention for Unity humanoid foot bones.

### Data Flow

The full pipeline:
1. `WarriorAgent.FixedUpdate()` → calls `boneController.ApplyOutputs()` (MLP bone rotations) → then `poseSolver.Solve()` (IK correction)
2. `WarriorPoseSolver.Solve()` → calls `TwoBoneIK.Solve()` for each leg with the IK target position from the `ProceduralFootController`
3. `TwoBoneIK.Solve()` → computes upper leg, lower leg, and foot rotations via law-of-cosines IK
4. The foot rotation (line 251) is the only place where the 180° error occurs

### Impact

- Single-line change in `TwoBoneIK.cs`
- No changes needed to `WarriorPoseSolver`, `ProceduralFootController`, or `WarriorBoneController`
- Existing tests in `AnimationTests.cs` remain valid (they test bone length, joint limits, and angular speed — none cover foot rotation direction)
- The `sideDir` variable used at line 247 (`footForward = Vector3.Cross(bendNormal, sideDir).normalized`) is unaffected because negating `bendNormal` in the cross product is already compensated by the `LookRotation` change

# Delivery Steps

### ✓ Step 1: Fix foot rotation in TwoBoneIK.cs
Negate the up vector in the foot rotation computation so soles face ground.

- Edit `Assets/Components/Warrior/TwoBoneIK.cs` line 251
- Change `Quaternion.LookRotation(footForward, bendNormal)` to `Quaternion.LookRotation(footForward, -bendNormal)`
- Update the XML comment on line 245 to reflect the corrected behavior (sole faces ground)
- Verify the existing tests in `AnimationTests.cs` still pass