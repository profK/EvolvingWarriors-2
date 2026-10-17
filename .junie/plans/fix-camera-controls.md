---
sessionId: session-261017-155256-16n5
---

# Requirements

### Overview & Goals
Fix the WASD and mouse camera controls in `FirstPersonCameraController.cs` so that:
- WASD keys move the camera horizontally across the ground plane
- Left mouse button drag rotates the camera correctly (drag up = look up, drag left = turn left)

### Current Bugs
1. **Mouse pitch is inverted**: Dragging the mouse upward tilts the camera down instead of up. The sign in `HandleMouseLook()` is wrong.
2. **WASD movement follows camera tilt**: When the camera looks down (e.g., 70-degree pitch in TrainingScene), pressing W moves the camera downward into the ground instead of forward on the ground plane.

### Scope
- **In Scope**: Fix `FirstPersonCameraController.cs` — correct the mouse pitch sign and project WASD movement onto the horizontal plane.
- **Out of Scope**: Changing InputManager settings, adding new camera modes, or modifying scene files.

# Technical Design

### Current Implementation
`FirstPersonCameraController.cs` (107 lines) is a flying camera controller with:
- `HandleMouseLook()`: LMB drag rotates yaw/pitch using `Input.mousePosition` deltas
- `HandleMovement()`: WASD moves camera using `transform.forward` and `transform.right`

Both scenes (SampleScene, TrainingScene) have this component enabled on their main cameras.

### Key Decisions
- **Mouse pitch sign**: Change `pitchAngle += dragDelta.y * ...` to `pitchAngle -= dragDelta.y * ...` so dragging upward (positive screen Y) tilts the camera up.
- **WASD ground-plane projection**: Flatten `transform.forward` by zeroing its Y component before using it for movement. This ensures W/S always moves forward/backward on the ground plane regardless of camera tilt angle.

### Proposed Changes
In `FirstPersonCameraController.cs`:

**Fix 1 — Mouse pitch sign (line 75):**
```csharp
// Before:
pitchAngle += dragDelta.y * mouseSensitivityY;
// After:
pitchAngle -= dragDelta.y * mouseSensitivityY;
```

**Fix 2 — WASD movement projection (lines 98-99):**
```csharp
// Before:
Vector3 forward = transform.forward;
Vector3 right = transform.right;
// After:
Vector3 forward = new Vector3(transform.forward.x, 0f, transform.forward.z).normalized;
Vector3 right = new Vector3(transform.right.x, 0f, transform.right.z).normalized;
```

### Risks
- Flattening `right` is unnecessary (right vector is always horizontal for a camera rotated around Y axis), but it's harmless and makes the code more robust against unexpected rotations.

# Testing

### Validation Approach
Verify in Unity Editor by playing both scenes:

### Key Scenarios
1. **SampleScene** (30° pitch camera): Press W → camera moves forward on ground. Press S → backward. Press A/D → strafe left/right.
2. **TrainingScene** (70° pitch camera): Same WASD behavior — camera moves horizontally across the ground plane, not downward.
3. **Mouse drag**: Click and drag upward → camera tilts up. Drag downward → tilts down. Drag left → turns left. Drag right → turns right.

### Edge Cases
- Camera at extreme pitch angles (near ±80°): WASD still moves horizontally.
- Camera level (0° pitch): WASD and mouse work as expected.

# Delivery Steps

### ✓ Step 1: Fix mouse pitch sign and WASD ground-plane projection in FirstPersonCameraController
Edit `FirstPersonCameraController.cs` to fix two bugs:

- **Line 75**: Change `pitchAngle += dragDelta.y * mouseSensitivityY` to `pitchAngle -= dragDelta.y * mouseSensitivityY` — corrects inverted mouse pitch so dragging upward looks up.
- **Lines 98-99**: Replace `transform.forward` and `transform.right` with flattened versions (`new Vector3(..., 0f, ...).normalized`) — projects movement onto the horizontal plane so WASD always moves across the ground regardless of camera tilt.
- Update the comment on line 74 to reflect the corrected behavior: "Drag upward (positive y on screen) tilts up."