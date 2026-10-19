---
sessionId: session-261017-184511-1tkk
---

# Requirements

### Overview & Goals
Implement procedural foot IK (Inverse Kinematics) to keep the warrior's feet planted on the y=0 ground plane. Feet maintain their x,z position when planted and only move when the warrior takes a procedural step.

### Scope
**In Scope:** Two-bone IK solver per leg, per-foot planted state machine, ground raycasting to y=0 plane, step triggering by balance/reach thresholds, smooth foot arc trajectory, integration with WarriorBoneController, pose correction for ground penetration.
**Out of Scope:** Animator animation, root transform movement, bone scale changes, network output dimension changes.

### Acceptance Criteria
1. Both feet remain at y=0 at all times except during brief step transitions
2. Foot x,z does not change while in Planted state
3. Steps only trigger when balance or reach thresholds exceeded
4. Two-bone IK positions foot while respecting leg bone lengths
5. Root transform never moves from spawn position
6. No ground penetration — feet never below y=0
7. Bone lengths constant during IK solving

# Technical Design

### Current Implementation
- **WarriorAgent.cs**: FixedUpdate pipeline — encode observations, MLP inference, apply bone rotations
- **WarriorBoneController.cs**: Controls 19 bones, applies RotateTowards with speed limiting, enforces joint limits, pins root to spawnPosition, never changes bone local positions
- **WarriorObservationEncoder.cs**: 216 inputs from opponent bones, opponent root, self bones, time signals
- **Warrior.prefab**: Has WarriorAgent, WarriorBoneController, WarriorObservationEncoder, WarriorCombatScorer
- **No Animation/ directory exists** — Phase 3 components not yet implemented
- Warriors spawn at y=0; ground mesh at y=-0.5

### Key Decisions
- **Ground plane is y=0**: Functional ground for feet is spawn height, not visual mesh
- **IK after MLP rotations**: Pipeline order is MLP → bone rotations → IK correction; IK overrides leg bones for grounding
- **Procedural step triggering**: Steps triggered by internal thresholds (balance/reach), not MLP outputs
- **CCIK two-bone solver**: Cyclic Coordinate Descent for each leg chain (UpperLeg → LowerLeg → Foot)
- **Foot state machine owns position**: Planted state fixes x,z; Lifting/Placing moves via arc trajectory

### Proposed Changes
**New Files:**
- `Assets/Components/Animation/TwoBoneIK.cs` — Static CCIK solver utility
- `Assets/Components/Animation/ProceduralFootController.cs` — Per-foot state machine MonoBehaviour
- `Assets/Components/Animation/WarriorPoseSolver.cs` — IK orchestration MonoBehaviour

**Modified Files:**
- `WarriorAgent.cs` — Add PoseSolver reference; call `PoseSolver.Solve()` after `boneController.ApplyOutputs()` in FixedUpdate
- `Warrior.prefab` — Add WarriorPoseSolver + two ProceduralFootController instances (left/right)

### Components
**TwoBoneIK (static)**: `Solve(targetPos, rootTfm, upperLegTfm, lowerLegTfm, footTfm, groundY)` — CCIK iteration, preserves bone lengths, sets localRotation on upperLeg and lowerLeg

**ProceduralFootController (per foot)**: State machine (Planted→Lifting→Placing), stores planted x,z, raycasts to y=0, configurable step threshold (0.4m) and balance threshold (0.25m), sinusoidal arc lift (0.15m peak, 0.4s duration)

**WarriorPoseSolver**: Evaluates balance (CoM from hip vs feet), evaluates reach, triggers steps, updates foot states, applies TwoBoneIK per leg, corrects ground penetration

### Data Contracts
- Bone indices: LeftUpperLeg=12, RightUpperLeg=13, LeftLowerLeg=14, RightLowerLeg=15, LeftFoot=16, RightFoot=17
- Leg chain: UpperLeg → LowerLeg → Foot (IK solves UpperLeg + LowerLeg rotations)
- Ground raycast: from (foot.x, foot.y+1.0, foot.z) downward to y=0
- Default thresholds: step=0.4m, balance=0.25m, arcLift=0.15m, stepDuration=0.4s

# Testing

### Key Scenarios
1. Foot grounding at rest — both feet snap to y=0 on spawn and remain
2. Planted state holds x,z — no drift while Planted
3. Step triggering — CoM shift beyond threshold triggers Lifting state
4. IK accuracy — foot reaches within 0.05m of target position
5. Bone length preservation — upperLeg and lowerLeg lengths constant
6. Root pinned — root position unchanged after IK
7. No penetration — feet never below y=0
8. Step arc trajectory — smooth sinusoidal lift during Lifting/Placing

### Edge Cases
- Warrior at leg extension limit — IK gracefully fails, foot as close as possible
- Both feet simultaneously need steps — stagger to prevent instability
- NaN/Infinity from IK solver — guard and skip correction
- Very short step distances — skip step if below minimum threshold

# Delivery Steps

### ✓ Step 1: Create TwoBoneIK static solver utility
Implement the two-bone IK solver as a static utility class.
- Create `Assets/Components/Animation/TwoBoneIK.cs`
- Implement CCIK (Cyclic Coordinate Descent) algorithm solving for knee and ankle rotations
- Input: target world position, root transform, upperLeg transform, lowerLeg transform, foot transform, ground Y
- Preserve bone lengths by computing from rest pose
- Clamp knee angle to anatomical limits (0-160°)
- Set localRotation on upperLeg and lowerLeg transforms
- Guard against NaN/Infinity outputs

### ✓ Step 2: Create ProceduralFootController state machine
Implement per-foot planted state machine as a MonoBehaviour.
- Create `Assets/Components/Animation/ProceduralFootController.cs`
- Implement state enum: Planted, Lifting, Placing
- Track planted x,z position; store on Planted state entry
- Raycast from above to y=0 plane for valid foot placement
- `RequestStep(targetXZ)` method to initiate step to new position
- `UpdateState()` method for state transitions and arc trajectory
- Sinusoidal lift arc: 0.15m peak height, 0.4s duration per phase
- Configurable thresholds: stepDistance (0.4m), balanceThreshold (0.25m)
- `GetFootWorldPosition()` to report current IK target

### ✓ Step 3: Create WarriorPoseSolver orchestrator
Implement IK orchestration component that coordinates foot controllers and IK solver.
- Create `Assets/Components/Animation/WarriorPoseSolver.cs`
- Auto-resolve references in Awake(): left/right foot controllers, bone controller, root transform
- `Solve()` method called from WarriorAgent.FixedUpdate:
  - Evaluate balance: compute CoM from hip position relative to foot midpoint
  - Evaluate reach: check if current pose pushes feet beyond step threshold
  - Trigger steps via `RequestStep()` when thresholds exceeded
  - Update foot states via `UpdateState()` on each controller
  - Apply TwoBoneIK.Solve() for each leg chain with current foot target
  - Correct ground penetration: clamp foot Y to groundY (0)
- Configurable groundY field (default 0)
- Configurable step and balance thresholds

### ✓ Step 4: Integrate IK into WarriorAgent and update prefab
Wire IK system into the existing warrior pipeline and prefab.
- Modify `WarriorAgent.cs`:
  - Add `WarriorPoseSolver poseSolver` field with [SerializeField]
  - Auto-resolve in Awake(): `GetComponent<WarriorPoseSolver>()`
  - In FixedUpdate(): after `boneController.ApplyOutputs(outputBuffer)`, call `poseSolver.Solve()`
- Modify `Warrior.prefab`:
  - Add WarriorPoseSolver component to root GameObject
  - Add two ProceduralFootController instances (one for left foot, one for right)
  - Configure left controller with LeftFoot side, right with RightFoot side
  - Wire bone transform references for leg chains