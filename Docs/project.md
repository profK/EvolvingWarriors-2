# EvolvingWarriors Project Documentation

## Overview

EvolvingWarriors is a Unity C# game that simulates evolving warriors using neural networks (Multi-Layer Perceptrons) and genetic algorithms. Each warrior is an animated character whose movements are controlled by an MLP neural network. The network's weights are encoded as a genome, and a genetic algorithm evolves the population over generations to produce warriors that fight more effectively.

The simulation runs as a state machine: warriors are spawned, they fight, combat metrics are scored, the best performers are selected, their genomes undergo crossover and mutation, and the next generation begins.

## Architecture

```
┌─────────────────────────────────────────────────────┐
│  EvolutionSimulationManager (state machine)          │
│  └── Governs the simulate loop and generation cycle │
├─────────────────────────────────────────────────────┤
│  GeneticEvolution (GA engine)                        │
│  └── Elitism, tournament selection, crossover, mutation │
├─────────────────────────────────────────────────────┤
│  WarriorSpawner                                      │
│  └── Instantiates warrior prefabs with genomes      │
├─────────────────────────────────────────────────────┤
│  For each warrior:                                   │
│    ├── WarriorAgent (orchestrator)                   │
│    │   ├── WarriorObservationEncoder (input)         │
│    │   ├── MLP (inference)                           │
│    │   └── WarriorBoneController (output)            │
│    ├── WarriorPoseSolver (procedural IK)             │
│    │   └── TwoBoneIK (solver)                        │
│    ├── WarriorCombatScorer (scoring)                 │
│    ├── AttackSurface (offense)                       │
│    └── DamageSurface (defense)                       │
├─────────────────────────────────────────────────────┤
│  EvolutionHUD (UI display)                           │
│  EvolutionCheckpointManager (save/load)              │
│  FirstPersonCameraController (camera)                │
└─────────────────────────────────────────────────────┘
```

---

## Components

### Genome
**Path**: `Components/MLP/Genome.cs`  
**Responsibility**: Data structure representing a warrior's genetic material.  
**Details**: A genome is a flat array of floating-point values that encode all trainable parameters: MLP layer weights, biases, bone rotation ranges, and any hyperparameters subject to evolution. The genome supports standard genetic operations: crossover (blending two parent genomes), mutation (adding random noise), and cloning. It defines the length of the parameter vector and provides indexed accessors for the MLP weights and other configurable values. The genome acts as the bridge between the genetic algorithm and the neural network: a genome can be unpacked into an MLP for inference, and an MLP's parameters can be repacked into a genome for evolution.

### MLP (Multi-Layer Perceptron)
**Path**: `Components/MLP/MLP.cs`  
**Responsibility**: Feed-forward neural network for warrior decision making.  
**Details**: Implements a fully-connected multi-layer perceptron with configurable layer sizes. The network performs forward passes: input vector through hidden layers with activation functions (typically tanh or sigmoid) to produce output activations. Supports serialization to and from a flat float array for genome encoding. The network has no backpropagation; weights are set externally by the genetic algorithm. Typical configuration: one input layer matching observation dimensions, one or two hidden layers, and an output layer matching the number of controllable bone angles.

---

---

### Evolution Simulation Manager
**Path**: `Components/Evolution/EvolutionSimulationManager.cs`
**Responsibility**: State-machine orchestrator for the evolution simulation.
**Details**: Drives the entire evolution loop. Implements a finite state machine with states including Initializing, Spawning, Simulating, Scoring, Evolving, and Ready. On each transition it coordinates with WarriorSpawner to instantiate warriors, waits the simulation duration, collects scores from WarriorCombatScorer components, passes results to GeneticEvolution for selection/crossover/mutation, then spawns the next generation. Exposes public methods for starting, pausing, and stepping the simulation manually. Notifies EvolutionHUD of state changes and generation progress. Holds a reference to the current simulation state (SimulationState) containing generation number, population, and statistics.

### Genetic Evolution
**Path**: `Components/Evolution/GeneticEvolution.cs`
**Responsibility**: Genetic algorithm engine for evolving warrior populations.
**Details**: Implements a standard evolutionary algorithm with elitism, tournament selection, crossover, and mutation. Given a population of genomes and their fitness scores, it selects the top-performing individuals as elites (carried directly to the next generation), then uses tournament selection to pick parents for breeding. Crossover blends two parent genomes by mixing their values (uniform or one-point style), and mutation adds Gaussian noise scaled by a mutation rate and variance parameter. The population size is fixed; each generation produces exactly enough offspring to replace the non-elite slots. Supports configurable parameters: elite count, tournament size, crossover rate, mutation rate, and mutation variance. The evolved population is returned as a new array of Genome objects.

### Simulation State
**Path**: `Components/Evolution/SimulationState.cs`
**Responsibility**: Holds the mutable runtime state of an evolution session.
**Details**: A data structure containing the current generation number, the full population of genomes, per-warrior fitness scores, best-of-generation and all-time best scores, and aggregate statistics such as mean and standard deviation of fitness. Also tracks the current simulation phase. Used by EvolutionSimulationManager to persist state across generation transitions and checkpoints. Provides methods to reset the state for a fresh simulation or to advance it to the next generation.

### Warrior Spawner
**Path**: `Components/Evolution/WarriorSpawner.cs`
**Responsibility**: Instantiates and positions warrior prefabs for each generation.
**Details**: Takes a list of genomes and instantiates the corresponding warrior GameObjects from a prefab. Each spawned warrior is positioned at a designated spawn point (e.g., opposite sides of an arena). The spawner injects each warrior's genome into its WarriorAgent so the MLP is configured before the simulation begins. Supports clearing all spawned warriors from a previous generation. Spawn positions are configurable, allowing different arena layouts.

---

### Warrior Agent
**Path**: `Components/Warrior/WarriorAgent.cs`
**Responsibility**: Per-warrior behavior orchestrator that runs the neural network inference loop.
**Details**: The core MonoBehaviour on each warrior GameObject. On start, it unpacks its assigned genome into an MLP instance. Each frame (or at a fixed inference interval), it uses WarriorObservationEncoder to build an input vector from the environment (opponent position, distance, orientation, own health, etc.), runs a forward pass through the MLP, and feeds the output activations to WarriorBoneController to set target bone rotations. Tracks the warrior's alive/dead state and exposes methods for applying damage and checking vitality. The agent is the bridge between the abstract genome and the physical warrior character.

### Warrior Bone Controller
**Path**: `Components/Warrior/WarriorBoneController.cs`
**Responsibility**: Maps MLP output activations to skeletal bone rotations.
**Details**: Takes the raw output vector from the MLP and maps each value to a specific bone's rotation angle. Activation values (typically in [-1, 1]) are scaled by per-bone rotation ranges stored in the genome. Supports smoothing or interpolation between frames to avoid jerky movements. Each controllable bone has a rest pose and a rotation range; the controller applies the neural network's output as a delta from rest. Handles the full skeletal hierarchy: spine, arms, legs, head, etc. Works in tandem with WarriorPoseSolver for inverse kinematics targets.

### Warrior Observation Encoder
**Path**: `Components/Warrior/WarriorObservationEncoder.cs`
**Responsibility**: Encodes environmental observations into a fixed-size input vector for the MLP.
**Details**: Gathers data from the warrior's perspective: relative position and direction to the opponent, distance to opponent, own health, current velocities, and any other relevant game state. Normalizes these values into the [-1, 1] or [0, 1] range expected by the network's input layer. The encoding order and dimensions are fixed and must match the MLP's input layer size. Provides a single method that returns the observation vector, called by WarriorAgent each inference step.

### Warrior Pose Solver
**Path**: `Components/Warrior/WarriorPoseSolver.cs`
**Responsibility**: Procedural inverse kinematics solver for natural-looking poses.
**Details**: Takes the target rotations from WarriorBoneController and refines them using IK solvers for more natural movement. For example, it ensures feet stay grounded during walking animations and that the arms reach toward the opponent during attacks. Delegates to TwoBoneIK for limb chains (upper/lower segments). Runs after the bone controller to override or adjust specific joints. Supports multiple IK chains: arms for attacking, legs for procedural locomotion. The solver helps bridge the gap between raw neural network outputs and physically plausible animations.

### Two-Bone IK
**Path**: `Components/Warrior/TwoBoneIK.cs`
**Responsibility**: Two-bone inverse kinematics solver.
**Details**: A standalone utility that solves the position and orientation of a two-segment limb chain given a target position. Given the root joint, middle joint, end effector, limb segment lengths, and a target position, it calculates the angles for both joints using the law of cosines. Supports an optional look-at pole vector for controlling the bend direction. Used by WarriorPoseSolver for arm and leg IK chains. Clamps solutions to valid ranges to avoid over-rotation.

### Procedural Foot Controller
**Path**: `Components/Warrior/ProceduralFootController.cs`
**Responsibility**: Procedural foot placement and ground snapping.
**Details**: Ensures the warrior's feet remain grounded during movement by raycasting downward and snapping the foot to the detected surface. Implements a simple step-down mechanic for uneven terrain. Works with the leg IK chain to provide realistic foot placement. Prevents foot sliding during standing poses. Configurable raycast distance and snap threshold.

---

### Warrior Combat Scorer
**Path**: `Components/Combat/WarriorCombatScorer.cs`
**Responsibility**: Tracks and computes combat metrics for fitness evaluation.
**Details**: Attached to each warrior to track damage dealt, damage received, time-to-kill, distance maintained, and other combat-relevant metrics. Exposes a Score property that combines these metrics into a single fitness value using a weighted formula. The EvolutionSimulationManager reads these scores at the end of each simulation round. Supports different scoring modes: damage-based, kill-based, or a hybrid. Provides methods for recording events (hit landed, damage taken) during the fight.

### Attack Surface
**Path**: `Components/Combat/AttackSurface.cs`
**Responsibility**: Defines the hit area for a warrior's attack.
**Details**: A MonoBehaviour component that represents an attack volume (typically a box or sphere near a weapon hand). When activated, it performs a physics overlap or raycast to detect enemies within its area. On hit, it notifies the target's DamageSurface and the owner's WarriorCombatScorer. Configurable parameters include range, damage value, and activation duration. Multiple attack surfaces can exist on one warrior (e.g., punch, kick, weapon swing).

### Damage Surface
**Path**: `Components/Combat/DamageSurface.cs`
**Responsibility**: Defines the hit area for receiving damage.
**Details**: A MonoBehaviour component representing the warrior's body volume that can be struck. Listens for overlap events from AttackSurfaces and applies damage to the warrior's health. On damage, it notifies the local WarriorCombatScorer and the attacking warrior's scorer. Configurable hitbox dimensions and damage modifiers. When health reaches zero, it signals the WarriorAgent that the warrior is defeated.

---

### Evolution HUD
**Path**: `Components/UI/EvolutionHUD.cs`
**Responsibility**: In-game user interface displaying evolution status.
**Details**: A MonoBehaviour that renders the current generation number, population size, best fitness score, mean fitness, and simulation state on-screen. Typically uses Unity UI elements (TextMeshPro) for display. Updates each generation change and optionally each frame for dynamic values. Provides visual feedback for the simulation progress: which generation is running, how many warriors remain alive, and the best-of-generation stats. Can be paused or hidden during manual testing.

### First-Person Camera Controller
**Path**: `Components/Camera/FirstPersonCameraController.cs`
**Responsibility**: Camera movement and viewing controls.
**Details**: A MonoBehaviour that controls the main camera for observing the simulation. Supports free-look orbiting, follow-mode for tracking a specific warrior, or a fixed cinematic angle. Input handling for mouse-based rotation, WASD panning, and scroll-zoom. Smooth interpolation for all camera movements. Allows the user to switch between camera modes during the simulation.

### Evolution Checkpoint Manager
**Path**: `Components/Persistence/EvolutionCheckpointManager.cs`
**Responsibility**: Save and load checkpoints for evolution sessions.
**Details**: Persists the full simulation state to disk, including the current population of genomes, generation number, fitness statistics, and random number generator state. Supports both manual checkpoints (triggered by the user) and automatic checkpoints at configurable intervals. Saved data is serialized to binary or JSON files in a designated save directory. Loading a checkpoint restores the simulation to that exact state, allowing the evolution to continue from that point. Useful for long-running evolutions where the application might close unexpectedly.

---

---

## Editor Tools

### HUD Setup Editor
**Path**: `Editor/HUDSetupEditor.cs`
**Responsibility**: Custom Unity Editor window for configuring the EvolutionHUD.
**Details**: A MenuItem or EditorWindow that lets the user configure HUD settings without manual scripting. Provides fields for text element references, layout options, and update frequency. With a single button click, it wires up the EvolutionHUD component to the appropriate UI elements in the current scene. Reduces manual setup errors by validating references before applying them.

### T-Pose Tool
**Path**: `Editor/TposeTool.cs`
**Responsibility**: Editor utility for resetting warriors to their T-pose rest position.
**Details**: A custom Editor tool that resets all bone rotations on a selected warrior prefab or GameObject to their default T-pose values. Useful for artists and developers establishing the base animation pose before neural network control takes over. Can be invoked via a context menu or toolbar button. Validates that the target has a valid Animator or skeletal hierarchy.

### Training Scene Builder
**Path**: `Editor/TrainingSceneBuilder.cs`
**Responsibility**: Automated scene setup for training simulations.
**Details**: An Editor script that programmatically builds a training scene with the arena, spawn points, lighting, and any required configuration objects. Creates and positions the EvolutionSimulationManager, EvolutionHUD, camera controller, and checkpoint manager. Configures default parameters (population size, arena dimensions, spawn positions). Allows developers to generate a ready-to-run training scene with one click, eliminating manual scene setup.

---

## Tests (Edit Mode)

### Animation Tests
**Path**: `Tests/EditMode/AnimationTests.cs`
**Responsibility**: Unit tests for bone animation and IK systems.
**Details**: Tests the WarriorBoneController's rotation mapping, ensuring MLP output activations correctly translate to bone angles within expected ranges. Tests the TwoBoneIK solver with known geometric configurations to verify correct joint angles. Tests the WarriorPoseSolver's integration with bone controllers. Validates edge cases: zero activations, maximum activations, clamped ranges.

### Genetic Evolution Tests
**Path**: `Tests/EditMode/GeneticEvolutionTests.cs`
**Responsibility**: Unit tests for the genetic algorithm engine.
**Details**: Verifies elitism preserves the best genomes unchanged. Tests tournament selection produces valid parents from the population. Verifies crossover produces offspring that blend parent genes. Tests mutation applies noise correctly and respects rate/variance parameters. Edge cases include populations at minimum size, zero mutation rate, and extreme fitness distributions.

### Lifecycle Tests
**Path**: `Tests/EditMode/LifecycleTests.cs`
**Responsibility**: Integration tests for the full evolution simulation lifecycle.
**Details**: Tests the state machine transitions of EvolutionSimulationManager through a complete cycle: Initializing → Spawning → Simulating → Scoring → Evolving → next generation. Verifies that warriors are spawned correctly, scores are collected, and the next generation's population reflects evolved genomes. Tests manual step, start, and pause operations. Validates that checkpoints can be saved and loaded without data loss.

### MLP Tests
**Path**: `Tests/EditMode/MLPTests.cs`
**Responsibility**: Unit tests for the neural network implementation.
**Details**: Tests forward propagation with known weight matrices and input vectors. Verifies activation functions (tanh, sigmoid) produce correct outputs. Tests serialization and deserialization of network parameters, ensuring round-trip fidelity. Tests edge cases: zero inputs, extreme values, networks with varying layer configurations. Validates that the genome-to-MLP unpacking and MLP-to-genome repacking are inverse operations.

### Population Tests
**Path**: `Tests/EditMode/PopulationTests.cs`
**Responsibility**: Unit tests for population management operations.
**Details**: Tests population creation, sizing constraints, and genome initialization. Verifies that a population of the correct size is maintained across generation transitions. Tests the statistical calculations used for tracking: mean fitness, standard deviation, best score. Validates that population shuffle, filter, and sort operations behave correctly.

### Scoring Tests
**Path**: `Tests/EditMode/ScoringTests.cs`
**Responsibility**: Unit tests for combat scoring logic.
**Details**: Tests the WarriorCombatScorer's fitness calculations with various scenarios: one-sided fights, mutual damage, draws, early termination. Verifies that damage events are recorded correctly and the final score reflects the weighted formula. Tests edge cases: zero damage, maximum damage, simultaneous kills. Validates that AttackSurface and DamageSurface interactions produce correct scorer updates.

---

_End of documentation._
