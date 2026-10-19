---
sessionId: session-261019-151739-c3wz
---

# Requirements

### Overview
Fix two compilation errors caused by assembly definition misconfiguration:
- `WarriorPoseSolver.cs` cannot find `IBoneTransformProvider`
- `WarriorAgent.cs` cannot find `WarriorPoseSolver`

### Root Cause
`Animation.asmdef` isolates the Animation folder into a separate assembly restricted to Editor-only builds. `WarriorPoseSolver` (in Animation) references `IBoneTransformProvider` (in Assembly-CSharp) — this works because Animation references Assembly-CSharp. But `WarriorAgent` (in Assembly-CSharp) references `WarriorPoseSolver` — this FAILS because Assembly-CSharp is auto-generated and cannot reference the Animation assembly.

### Proposed Change
**Move `WarriorPoseSolver.cs` (and its `.meta` file) from `Assets/Components/Animation/` to `Assets/Components/Warrior/`** where `WarriorAgent.cs` and `WarriorBoneController.cs` already live. This keeps `WarriorPoseSolver` in Assembly-CSharp where all its dependencies and consumers are.

`ProceduralFootController` and `TwoBoneIK` stay in the Animation assembly — they are generic animation utilities.

### Why This Works
- `WarriorPoseSolver` will compile in Assembly-CSharp alongside `IBoneTransformProvider` and `WarriorAgent`
- No asmdef changes needed — the Warrior folder has no asmdef
- `EditModeTests` already references both Assembly-CSharp and Animation, so tests still work
- Only non-auto-generated files are modified (the .cs source and its .meta)

# Technical Design

### Current Architecture
```
Assembly-CSharp (auto-generated, no asmdef)
├── WarriorBoneController.cs (defines IBoneTransformProvider)
├── WarriorAgent.cs (references WarriorPoseSolver)
└── ... other components

Animation (Animation.asmdef, Editor-only, references Assembly-CSharp)
├── WarriorPoseSolver.cs (uses IBoneTransformProvider) ← PROBLEM: isolated here
├── ProceduralFootController.cs
└── TwoBoneIK.cs
```

### After Change
```
Assembly-CSharp
├── WarriorBoneController.cs (defines IBoneTransformProvider)
├── WarriorAgent.cs (references WarriorPoseSolver) ✅
├── WarriorPoseSolver.cs (uses IBoneTransformProvider) ✅
└── ... other components

Animation (asmdef unchanged)
├── ProceduralFootController.cs
└── TwoBoneIK.cs
```

### Files Modified
- **Move** `Assets/Components/Animation/WarriorPoseSolver.cs` → `Assets/Components/Warrior/WarriorPoseSolver.cs`
- **Move** `Assets/Components/Animation/WarriorPoseSolver.cs.meta` → `Assets/Components/Warrior/WarriorPoseSolver.cs.meta`
- **No changes** to `Animation.asmdef`, `EditModeTests.asmdef`, or any .csproj files
- `WarriorPoseSolver.cs` source code is unchanged — it only uses `UnityEngine` and `IBoneTransformProvider`, both available in Assembly-CSharp

# Delivery Steps

### ✓ Step 1: Move WarriorPoseSolver to Warrior folder
Move `WarriorPoseSolver.cs` and its `.meta` file from `Assets/Components/Animation/` to `Assets/Components/Warrior/` alongside `WarriorAgent.cs` and `WarriorBoneController.cs`.

- Move `Assets/Components/Animation/WarriorPoseSolver.cs` to `Assets/Components/Warrior/WarriorPoseSolver.cs`
- Move `Assets/Components/Animation/WarriorPoseSolver.cs.meta` to `Assets/Components/Warrior/WarriorPoseSolver.cs.meta`
- No source code changes needed — the file content is correct, only the location changes
- After the move, `WarriorPoseSolver` compiles in Assembly-CSharp where `IBoneTransformProvider` is defined and where `WarriorAgent` expects it

### ✓ Step 2: Verify compilation resolves both errors
Verify that both CS0246 errors are resolved after the move.

- Confirm `WarriorPoseSolver.cs` compiles — `IBoneTransformProvider` is now in the same assembly (Assembly-CSharp)
- Confirm `WarriorAgent.cs` compiles — `WarriorPoseSolver` is now in the same assembly (Assembly-CSharp)
- Confirm `EditModeTests` still compiles — it references both Assembly-CSharp and Animation, so it can see everything
- Confirm `Animation.asmdef` still works — `ProceduralFootController` and `TwoBoneIK` remain in the Animation assembly