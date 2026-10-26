---
sessionId: session-261025-221320-b022
---

# Requirements

### Overview & Goals

Fix the leg orientation issue where legs point up/sideways instead of downward toward the ground. The warrior starts in T-position with feet at y=0, but legs don't orient properly to keep feet grounded.

**Expected outcome:**
- Feet remain at y=groundY (typically 0)
- Hips position horizontally over the foot midpoint
- Legs point downward from hips to feet
- Warrior starts in stable stance with vertical legs

### Scope

**In scope:**
- Modify `WarriorPoseSolver.cs` to ensure IK targets are grounded before solving
- Add logic to force foot Y positions to groundY before calling TwoBoneIK.Solve
- Update the `Reset()` method to ensure proper initial leg orientation

**Out of scope:**
- Changing the balance correction logic (already centers hips over feet)
- Modifying the ProceduralFootController state machine
- Changing the TwoBoneIK solver algorithm

### User Stories

- As a developer, I want legs to point downward so the warrior stands stably on the ground
- As a developer, I want feet to stay at y=0 during T-position start so the warrior doesn't float or twist
- As a developer, I want hips to stay over the foot base so balance correction works correctly

### Functional Requirements

1. **Grounded IK Targets:** Before calling `TwoBoneIK.Solve`, explicitly set the target Y position to `groundY`
2. **Initial Leg Orientation:** When `Reset()` is called (round start), ensure legs are oriented downward
3. **Balance Correction Compatibility:** Balance correction must not interfere with leg verticality

### Non-Functional Requirements

- Maintain existing performance characteristics (no additional allocations in hot path)
- Preserve existing API contracts with `ProceduralFootController` and `TwoBoneIK`
- No changes to public interfaces or serialized fields

# Technical Design

### Current Implementation

**WarriorPoseSolver.cs** (lines 142-185): Balance correction moves hips horizontally to center over feet, but doesn't adjust leg rotations to maintain vertical alignment.

**WarriorPoseSolver.cs** (lines 228-237): TwoBoneIK.Solve is called with targets from `GetFootWorldPosition()`, but these targets may not be properly grounded relative to the hip position.

**ProceduralFootController.cs** (lines 201-206): `GetFootWorldPosition()` returns `currentFootPosition`, which should already be clamped to groundY in planted state (line 276), but the IK solver doesn't enforce this.

**TwoBoneIK.cs** (lines 51-53): Clamps target Y to groundY if below ground, but this happens after computing bone lengths from current world positions (lines 56-58), which can cause issues if the foot bone is already below ground.

### Key Decisions

1. **Force groundY at IK target level** (not in ProceduralFootController): This ensures the fix is localized to where IK is called, minimizing side effects on the foot state machine.

2. **Add explicit groundY clamping before TwoBoneIK.Solve**: Even though ProceduralFootController should return grounded positions, explicitly forcing Y=groundY in WarriorPoseSolver provides defense-in-depth.

3. **Update Reset() to call SolveLegOrientation**: After resetting foot controllers, explicitly solve leg orientation to ensure downward pointing at round start.

### Proposed Changes

**File: Assets/Components/Warrior/WarriorPoseSolver.cs**

1. **Lines 228-237**: Force IK targets to groundY before calling TwoBoneIK.Solve:
   ```csharp
   if (leftUpperLeg != null && leftLowerLeg != null && leftFootBone != null)
   {
       Vector3 leftTarget = leftFootController.GetFootWorldPosition();
       leftTarget.y = groundY; // Force ground level
       TwoBoneIK.Solve(leftTarget, root, leftUpperLeg, leftLowerLeg, leftFootBone, groundY);
   }
   if (rightUpperLeg != null && rightLowerLeg != null && rightFootBone != null)
   {
       Vector3 rightTarget = rightFootController.GetFootWorldPosition();
       rightTarget.y = groundY; // Force ground level
       TwoBoneIK.Solve(rightTarget, root, rightUpperLeg, rightLowerLeg, rightFootBone, groundY);
   }
   ```

2. **Lines 265-274 (Reset method)**: Add explicit leg orientation after resetting foot controllers:
   ```csharp
   public void Reset()
   {
       if (!initialized)
           return;
       
       leftFootController.SetGroundY(groundY);
       leftFootController.Reset();
       rightFootController.SetGroundY(groundY);
       rightFootController.Reset();
       
       // Ensure legs point downward after reset
       SolveLegOrientation();
   }
   ```

3. **Add new private method** (after Solve method, before Reset):
   ```csharp
   void SolveLegOrientation()
   {
       // Ensure legs point downward by solving IK with grounded targets
       Transform hips = boneController.GetBoneTransform(Hips);
       if (hips == null) return;
       
       Transform leftUpperLeg = boneController.GetBoneTransform(LeftUpperLeg);
       Transform leftLowerLeg = boneController.GetBoneTransform(LeftLowerLeg);
       Transform rightUpperLeg = boneController.GetBoneTransform(RightUpperLeg);
       Transform rightLowerLeg = boneController.GetBoneTransform(RightLowerLeg);
       Transform leftFootBone = boneController.GetBoneTransform(LeftFoot);
       Transform rightFootBone = boneController.GetBoneTransform(RightFoot);
       
       // Get current foot positions and force Y to ground level
       Vector3 leftFootPos = leftFootController.GetFootWorldPosition();
       Vector3 rightFootPos = rightFootController.GetFootWorldPosition();
       leftFootPos.y = groundY;
       rightFootPos.y = groundY;
       
       // Solve IK with grounded targets to orient legs downward
       if (leftUpperLeg != null && leftLowerLeg != null && leftFootBone != null)
           TwoBoneIK.Solve(leftFootPos, transform, leftUpperLeg, leftLowerLeg, leftFootBone, groundY);
       if (rightUpperLeg != null && rightLowerLeg != null && rightFootBone != null)
           TwoBoneIK.Solve(rightFootPos, transform, rightUpperLeg, rightLowerLeg, rightFootBone, groundY);
   }
   ```

### Risks

- **Minimal risk**: Changes are localized to IK target preparation and reset path
- **No performance impact**: GroundY clamping is a single vector assignment per leg
- **Backward compatible**: No API changes, only internal logic refinement

# Delivery Steps

### ✓ Step 1: Force IK targets to groundY
Force IK targets to groundY before calling TwoBoneIK.Solve in WarriorPoseSolver.cs

- Modify lines 228-237 in WarriorPoseSolver.cs to explicitly set leftTarget.y = groundY before calling TwoBoneIK.Solve for the left leg
- Modify lines 233-237 to explicitly set rightTarget.y = groundY before calling TwoBoneIK.Solve for the right leg
- This ensures IK targets are always grounded regardless of ProceduralFootController state

### ✓ Step 2: Add SolveLegOrientation helper method
Add SolveLegOrientation helper method to WarriorPoseSolver.cs

- Add new private method SolveLegOrientation() after the Solve method (around line 259)
- Method gets foot positions from controllers and forces Y to groundY
- Calls TwoBoneIK.Solve with grounded targets to orient legs downward
- Handles null checks for all bone transforms

### ✓ Step 3: Update Reset() method
Update Reset() method to call SolveLegOrientation

- Modify lines 265-274 in WarriorPoseSolver.cs
- After resetting both foot controllers, call SolveLegOrientation() to ensure proper initial leg orientation
- This fixes the T-position start issue where legs point sideways instead of down