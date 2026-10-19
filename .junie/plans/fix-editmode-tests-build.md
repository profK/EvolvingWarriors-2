---
sessionId: session-261019-131106-1lfb
---

# Technical Design

### Root Cause

The `EditModeTests.csproj` is missing a `<ProjectReference>` to `Assembly-CSharp.csproj`. 

Unity's asset pipeline uses `.asmdef` files to define assembly references — `EditModeTests.asmdef` correctly lists `"Assembly-CSharp"` in its `references` array. However, Rider/MSBuild does **not** read `.asmdef` files; it builds from `.csproj` files which use `<ProjectReference>` elements to resolve cross-assembly dependencies.

The three missing types are all defined in the global namespace within `Assembly-CSharp.csproj`:
- `IBoneTransformProvider` — defined in `Assets/Components/Warrior/WarriorBoneController.cs` (line 10)
- `ProceduralFootController` — defined in `Assets/Components/Animation/ProceduralFootController.cs` (line 9)
- `WarriorPoseSolver` — defined in `Assets/Components/Animation/WarriorPoseSolver.cs` (line 9)

All three are already listed as `<Compile>` entries in `Assembly-CSharp.csproj`. The solution file includes all three projects. The only gap is the missing `<ProjectReference>` in `EditModeTests.csproj`.

### Fix

Add a `<ProjectReference>` element to `EditModeTests.csproj` pointing to `Assembly-CSharp.csproj` with the correct project GUID (`{065e1e7e-fa2f-2831-40b8-da14d4dd2435}`), matching what the solution file already knows.

### Why This Happens

Unity's `.csproj` generation (via the Rider package) sometimes fails to emit `<ProjectReference>` elements for asmdef-based references, especially after newly created `.asmdef` files or after project structure changes. Regenerating project files from Unity (Assets > Reload or Edit > Render Pipeline > Render Object) typically fixes this, but editing the `.csproj` directly is a valid immediate fix.

# Delivery Steps

### ✓ Step 1: Add Assembly-CSharp ProjectReference to EditModeTests.csproj
Insert a `<ProjectReference>` element into `EditModeTests.csproj` so MSBuild can resolve types from the main assembly.

- Add `<ProjectReference Include="Assembly-CSharp.csproj">` with the project GUID `{065e1e7e-fa2f-2831-40b8-da14d4dd2435}` and `<Private>false</Private>` to prevent DLL copying.
- Place it in a new `<ItemGroup>` after the existing `<Compile>` entries (after line 53).
- This resolves all five build errors: `IBoneTransformProvider`, `ProceduralFootController` (x3), and `WarriorPoseSolver`.

### ✓ Step 2: Verify build succeeds
Confirm the fix resolves all compilation errors.

- Run `dotnet build` on the solution to verify all five errors are gone.
- Verify no new errors are introduced.