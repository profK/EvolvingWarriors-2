# EvolvingWarriors Unity Project

## File discovery rules

- Always use OpenCode's glob, grep, and read tools for file discovery.
- Do not use bash find or shell pipelines to discover files.
- Search Assets first.
- Exclude Library, Temp, Logs, obj, Packages, and generated files.
- Exclude TextMesh Pro examples and third-party sample scripts.
- Use case-insensitive searches when appropriate.
- Never repeat identical search filters.
- Never generate long chains of grep commands.
- Limit search results to 30 files or fewer.
- If a search fails twice, stop and explain the exact failure.

## Development rules

- This is a Unity C# project.
- Inspect existing code before modifying it.
- Prefer modifying existing systems over creating duplicate managers.
- Use Unity MCP tools for inspecting scenes, GameObjects, and components.
- Use OpenCode's edit tools for modifying C# files.
- Do not modify third-party packages without explicit permission.
- After changes, check Unity compilation errors.

## Project source layout

Primary game code is located in:
- Assets/Components/UI/
- Assets/Components/Combat/
- Assets/Components/Camera/
- Assets/Components/Warrior/
- Assets/Components/Evolution/
- Assets/Components/Persistence/
- Assets/Components/MLP/

Supporting code:
- Assets/Editor/ - custom Unity Editor tools
- Assets/Tests/EditMode/ - unit tests

## Search priorities

1. Search Assets/Components first.
2. Search Assets/Editor for editor functionality.
3. Search Assets/Tests for test implementations.
4. Never search third-party assets unless explicitly necessary.

## Important systems

- EvolutionSimulationManager.cs manages the simulation.
- EvolutionHUD.cs handles the HUD.
- WarriorCombatScorer.cs handles combat scoring.
- WarriorAgent.cs implements warrior agent behavior.
- WarriorBoneController.cs controls bone rotations.
- ProceduralFootController.cs handles foot movement.
- GeneticEvolution.cs implements evolution.
- MLP.cs implements the neural network.

Use these filenames as starting points when investigating related features.
