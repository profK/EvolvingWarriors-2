# Evolving Warriors — Implementation Plan

## Project Inspection Summary

**Unity 6 (6000.3.15f1)**, URP 17.3.0, Test Framework 1.6.0, TextMesh Pro, Timeline, Input System, AI Navigation installed.

**Existing assets:**
- `HumanCharacterDummy_M` — base humanoid rig (Kevin Iglesias, FBX-based, full bone hierarchy with Animator, Rigidbody, ConfigurableJoints)
- `HumanCharacterDummy_M Armed` — variant with shield
- `Warrior.prefab` — variant of the base with 2 demo components removed (no custom components added yet)
- `SampleScene.unity` — default URP starter scene

**Custom code:** None. `Components/` and `Editor/` folders are empty. All 154 `.cs` files are third-party (Infinity PBR demo scripts, URP settings, TutorialInfo).

**What can be reused:**
- Humanoid bone hierarchy (hips → spine → chest → upper chest → neck → head, left/right shoulder chains, left/right leg chains)
- Animator component (will be disabled)
- Rigidbody on the root
- Infinity PBR equipment system (for sword/shield attachment)
- TextMesh Pro for HUD
- Test Framework for automated tests

**What must be built from scratch:** Everything — MLP, genome, GA, spawning, scoring, bone controller, IK/stepping, evolution manager, HUD, checkpointing.

**User decisions:**
- Warrior base: Armed variant with shield
- Sword: User will provide sword model
- Arena: Create automatically
- Animator: Disable — all motion from MLP bone rotations with procedural stepping

---

## Phase 1 — Core Infrastructure

### 1.1 MLP (Multilayer Perceptron)
- Lightweight CPU feed-forward network: `Input → Dense(128, tanh) → Dense(64, tanh) → Output`
- Methods: `Forward(input)`, `ExportGenome()`, `ImportGenome()`, `DeepCopy()`
- Cached intermediate buffers to avoid per-frame allocations
- NaN/Infinity guards on input and output
- Xavier/Glorot weight initialization for first generation

### 1.2 Genome
- Flattened `float[]` representing all weights and biases
- Deterministic mapping: layer-by-layer, weights-then-biases
- `Export()` / `Import()` / `Clone()` / `Validate()`
- Round-trip accuracy test
- Genome size: `inputSize × 128 + 128 + 128 × 64 + 64 + 64 × outputSize + outputSize`
  - With 216 inputs and 57 outputs: `27,648 + 128 + 8,192 + 64 + 3,648 + 57 = 39,737` parameters

**File structure:** `Assets/Components/MLP/`
- `MLP.cs`
- `Genome.cs`

---

## Phase 2 — Warrior Agent Components

### 2.1 WarriorAgent (main orchestrator)
- References: own MLP, genome, score, opponent reference, bone controller, observation encoder
- `FixedUpdate()` drives the inference → rotation pipeline
- State: active/inactive per simulation phase
- Public API for manager to set/reset state

### 2.2 WarriorBoneController
- Maps `HumanBodyBones` enum to actual `Transform` references via `Animator.GetBoneTransform()`
- Stores rest-pose local rotation/position/scale per bone
- Applies `Quaternion.RotateTowards()` with configurable max angular speed
- Enforces anatomical joint limits (configurable per bone)
- Controlled bones (19): hips, spine, chest, upper chest, neck, head, left/right upper arm, left/right lower arm, left/right hand, left/right upper leg, left/right lower leg, left/right foot
- Output dimension: 19 bones × 3 angular values = **57**

### 2.3 WarriorObservationEncoder
- Opponent bone observations (per bone): relative position (3) + rotation quaternion (4) = 7 × 19 = 133
- Opponent root: relative position (3) + forward direction (3) + distance (1) = 7
- Self observations (per bone): current local rotation vs rest pose quaternion (4) × 19 = 76
- Total input dimension: **216** (calculated automatically from bone count)
- Fixed deterministic layout order

**File structure:** `Assets/Components/Warrior/`
- `WarriorAgent.cs`
- `WarriorBoneController.cs`
- `WarriorObservationEncoder.cs`

---

## Phase 3 — Procedural Animation

### 3.1 ProceduralFootController
- Per-foot planted state machine: Planted → Lifting → Placing → Planted
- Ground raycast for foot placement
- Two-bone IK (upper leg → lower leg → foot) for leg reaching
- Step triggered by balance/reach thresholds (NOT by MLP)
- Smooth arc trajectory for foot movement
- Configurable thresholds

### 3.2 WarriorPoseSolver
- Applies MLP rotation targets through bone controller
- Resolves IK constraints for feet
- Collision-aware pose correction
- Priority: bone dimensions > ground penetration > joint limits > self-interpenetration > MLP targets
- Bounded correction process, no unbounded iterative solver

**File structure:** `Assets/Components/Animation/`
- `ProceduralFootController.cs`
- `WarriorPoseSolver.cs`
- `TwoBoneIK.cs`

---

## Phase 4 — Combat Scoring

### 4.1 AttackSurface (sword)
- Trigger collider on sword transform (in right hand)
- Identifies the owning warrior
- Reports contact events to scorer

### 4.2 DamageSurface (head, torso)
- Trigger colliders on head and torso transforms
- Head and torso are separate logical surfaces
- Multiple physical colliders per logical surface deduplicated

### 4.3 WarriorCombatScorer
- `OnTriggerEnter` / `OnTriggerExit` state tracking
- Contact begins: +1 (opponent) or -1 (self)
- Contact continues: no additional scoring
- Contact ends: clear state
- Opponent validation: only scores against assigned opponent
- Disabled during init/eval/evolution/reset/pause
- Contact state cleared during reset

**File structure:** `Assets/Components/Combat/`
- `AttackSurface.cs`
- `DamageSurface.cs`
- `WarriorCombatScorer.cs`

---

## Phase 5 — Population & Evolution

### 5.1 EvolutionSimulationManager
- Central state machine: Initializing → Running → Evaluating → Evolving → Resetting → (loop)
- Inspector-exposed configuration:
  - Population: pair count (default 4), warrior prefab, pair separation distance, inter-pair distance, random seed
  - Simulation: round duration (30s), max angular speed (180°/s), auto-save, checkpoint interval (10 gens), diagnostic logging
  - Network: hidden layers, neurons per layer, weight init params
  - GA: elite fraction (0.10), tournament size (3), crossover probability (0.70), mutation probability (0.02), mutation strength (0.10)
- Spawns warrior pairs on grid layout
- Assigns fixed reciprocal opponent pairs
- Manages generation lifecycle
- Validates all configuration values

### 5.2 WarriorSpawner
- Instantiates warriors from the Armed prefab variant
- Grid layout with configurable pair separation and inter-pair distance
- Warriors face each other within sword range
- Collision layers to prevent cross-pair interference
- Deterministic placement

### 5.3 GeneticEvolution
- Collects all genomes + scores at round end
- Elitism: preserves top N% unchanged (handles small populations)
- Tournament selection (size 3), supports negative fitness
- Uniform crossover (probability 0.70)
- Per-gene Gaussian mutation (probability 0.02, σ=0.10)
- Produces exactly `warriorCount` new genomes
- Validates new genomes before assignment

**File structure:** `Assets/Components/Evolution/`
- `EvolutionSimulationManager.cs`
- `WarriorSpawner.cs`
- `GeneticEvolution.cs`
- `SimulationState.cs` (enum)

---

## Phase 6 — UI & Persistence

### 6.1 EvolutionHUD (TextMesh Pro)
- Top-center: generation number, round time remaining, simulation state
- Mid-screen compact: pair count, population, highest/lowest/average score
- Previous generation statistics display (after generation completes)
- Pause/Resume button
- Stops inference, animation, scoring, timer when paused
- Resumes same round with preserved state

### 6.2 EvolutionCheckpointManager
- Auto-saves all-time best genome on new high score
- Periodic population checkpoints every N generations (default 10)
- JSON format: version, generation, topology, bone mapping ID, all genomes, GA settings, config
- Stored in `Application.persistentDataPath`
- Load and validate compatibility (reject mismatched topology/bone mapping)
- Non-blocking file writes

**File structure:** `Assets/Components/UI/`, `Assets/Components/Persistence/`
- `EvolutionHUD.cs`
- `EvolutionCheckpointManager.cs`

---

## Phase 7 — Scene & Prefab Configuration

### 7.1 Arena Setup (auto-created)
- Ground plane at Y=0
- Directional light + ambient
- Camera positioned to view all pairs
- `EvolutionSimulationManager` game object with all defaults configured

### 7.2 Warrior Prefab Updates
- Base: `HumanCharacterDummy_M Armed`
- Add: WarriorAgent, WarriorBoneController, WarriorObservationEncoder, ProceduralFootController, WarriorCombatScorer
- Add: AttackSurface (sword — awaiting user's model)
- Add: DamageSurface colliders on head and torso bones
- Disable: Animator component
- Configure: Rigidbody with frozen position (no translation)

### 7.3 Scene Configuration
- Update `SampleScene.unity` with arena elements
- Or create new `EvolutionArena.unity` scene

---

## Phase 8 — Automated Tests (43 tests)

### Neural Network Tests (1–6)
1. Independent warriors do not share weights
2. Identical genomes produce identical outputs
3. Genome export/import preserves parameters
4. Observation dimensions match the input layer
5. Output dimensions match controlled bones
6. Valid inputs do not produce NaN or infinite outputs

### Genetic Algorithm Tests (7–12)
7. Population size remains constant
8. Elite genomes remain unchanged
9. Mutation changes genes when configured
10. Crossover produces valid genomes
11. Negative fitness scores are handled
12. Small valid populations are supported

### Scoring Tests (13–20)
13. Initial sword/opponent-head contact awards one point
14. Continued contact awards no additional points
15. Contact exit and reentry awards another point
16. Sword/self-torso contact subtracts one point
17. Being hit does not subtract points from defender
18. Unrelated warrior contacts do not score
19. Multiple colliders on one logical surface do not duplicate scoring
20. Reset does not generate artificial scoring events

### Animation Tests (21–28)
21. Bone lengths remain constant
22. Bone scales remain constant
23. Bone local positions unchanged (except justified constraints)
24. Joint limits are respected
25. Maximum angular speed is respected
26. Foot grounding works in representative poses
27. Procedural stepping does not translate the root
28. Invalid poses are corrected or rejected safely

### Population Tests (29–35)
29. Exactly two warriors spawn per pair
30. Every warrior has one opponent
31. Opponent relationships are reciprocal
32. Pair assignments remain fixed across generations
33. Every warrior has one independent genome
34. All genomes share the same topology
35. Evolution replaces genomes correctly

### Lifecycle Tests (36–43)
36. Rounds end at the configured duration
37. Generation transitions occur exactly once
38. Scores reset between generations
39. Contact state resets correctly
40. Pausing preserves the round state
41. Resuming continues the same round
42. Checkpoints can be saved and loaded
43. Multiple generations run without accumulating stale state

**File structure:** `Assets/Tests/`
- `MLPTests.cs`
- `GeneticEvolutionTests.cs`
- `ScoringTests.cs`
- `AnimationTests.cs`
- `PopulationTests.cs`
- `LifecycleTests.cs`

---

## Phase 9 — Integration Verification

- Configure the actual Unity scene and warrior prefab
- Verify warriors spawn correctly in pairs
- Verify warriors face assigned opponents within sword range
- Verify MLP inference produces visible bone motion
- Verify feet remain grounded through IK and stepping
- Verify sword contacts produce correct scores
- Verify defender scores unaffected by being hit
- Verify opponent pairings remain unchanged across generations
- Verify generation completes, genomes evolve, warriors reset, next generation starts
- Run multiple consecutive generations
- Inspect Unity Console for errors and warnings
- Resolve discovered problems

---

## Required Deliverables (at completion)

1. Summary of implemented architecture
2. List of created files
3. List of modified files
4. Existing components reused
5. Prefab changes
6. Scene configuration changes
7. Inspector parameters and defaults
8. Neural network input dimension: **216**
9. Neural network output dimension: **57**
10. Actual neural network parameter count: **39,737**
11. Genetic algorithm configuration
12. Test results
13. Known limitations
14. Checkpoint storage location
15. Instructions for running the simulation
16. Instructions for pausing and resuming
17. Any remaining manual Unity Editor setup

---

## Non-Negotiable Requirements

- All warriors instantiate the same prefab
- Every warrior has its own independent MLP
- All MLPs have identical topology
- Initial weights randomized independently
- Evolution uses genetic algorithms only (no backprop, no PPO, no gradient descent)
- Only major body bones controlled (19 bones)
- Opponent observations include bone positions and rotations
- Self observations include current bone rotations
- Neural inference every physics tick (FixedUpdate)
- MLP outputs represent target joint rotations
- Bone rotation speed configurable (max angular velocity)
- Bone dimensions never change
- Root remains at spawn location
- Feet grounded except during procedural step
- Stepping uses procedural control and IK (not MLP-controlled)
- Joint limits and collision-aware correction required
- Sword/opponent contact = +1 point
- Sword/self contact = -1 point
- Being hit does NOT subtract points from defender
- Sustained contact scores only once
- New contact after separation can score again
- Opponents remain paired across generations
- Genetic evolution operates on entire population
- Every warrior receives one new genome each generation
- Scores, poses, contact states reset between rounds
- Best genomes and periodic checkpoints saved
- Runtime HUD displays generation statistics
- In-game Pause/Resume button provided
- Training continues until Unity Play Mode stopped

---

## Execution Order

1. **Phase 1** — MLP + Genome (compile, unit test)
2. **Phase 2** — WarriorAgent + BoneController + ObservationEncoder (compile, test)
3. **Phase 3** — FootController + PoseSolver + TwoBoneIK (compile, test)
4. **Phase 4** — AttackSurface + DamageSurface + CombatScorer (compile, test)
5. **Phase 5** — EvolutionManager + Spawner + GeneticEvolution (compile, test)
6. **Phase 6** — HUD + CheckpointManager (compile, test)
7. **Phase 7** — Scene setup + prefab configuration
8. **Phase 8** — Automated tests (all 43)
9. **Phase 9** — Integration verification + multi-generation run