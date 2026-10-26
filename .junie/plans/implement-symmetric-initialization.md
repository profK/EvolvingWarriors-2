---
sessionId: session-261025-123907-u3z7
---

# Requirements

### Overview & Goals

Fix the "warriors roll over into a tight ball" bug by implementing symmetric weight and bias initialization in the range [-1, 1] instead of Xavier initialization with zero biases.

**Expected outcome:**
- Weights and biases are initialized with symmetric values in [-1, 1]
- Expected weighted sum is near zero
- After activation functions, neurons activate more evenly
- Warriors start near rest pose with minor random perturbations in different directions

### Scope

**In Scope:**
- Modify `MLP.InitializeRandom()` to use symmetric initialization [-1, 1] for both weights and biases
- Update documentation comments to explain the rationale

**Out of Scope:**
- No changes to the genetic evolution algorithm
- No changes to the inference logic (Forward method)
- No changes to the network topology

### User Stories

- As a developer, I want the MLP to use symmetric initialization so that warriors don't all move in the same direction at startup
- As a developer, I want weights and biases to center around zero so that the network starts with balanced activation

### Functional Requirements

1. **Symmetric Weight Initialization:** `InitializeRandom()` must produce weights uniformly distributed in [-1, 1]
2. **Symmetric Bias Initialization:** `InitializeRandom()` must produce biases uniformly distributed in [-1, 1]
3. **No Xavier Scaling:** Remove the `sqrt(6 / (fanIn + fanOut))` scaling factor
4. **Documentation:** Update XML doc comments to explain the symmetric initialization rationale

### Non-Functional Requirements

- Zero-allocation inference must remain intact (no new allocations in InitializeRandom)
- Backward compatibility: existing saved genomes can still be loaded
- Deterministic behavior with same RNG seed

# Technical Design

### Current Implementation

**File:** `Assets/Components/MLP/MLP.cs`

**Current `InitializeRandom()` method (lines 103-127):**
```csharp
public void InitializeRandom()
{
    int offset = 0;
    for (int l = 0; l < layerCount - 1; l++)
    {
        int fromSize = layerSizes[l];
        int toSize = layerSizes[l + 1];
        int weightCount = layerWeightCounts[l];
        int biasCount = layerBiasCounts[l];

        // Xavier uniform: scale = sqrt(6 / (fanIn + fanOut))
        float scale = Mathf.Sqrt(6f / (fromSize + toSize));

        for (int i = 0; i < weightCount; i++)
            genome[offset + i] = (UnityEngine.Random.value * 2f - 1f) * scale;

        offset += weightCount;

        // Biases start at zero
        for (int i = 0; i < biasCount; i++)
            genome[offset + i] = 0f;

        offset += biasCount;
    }
}
```

**Problems:**
1. Weights are scaled by Xavier factor (not needed for [-1, 1] symmetric init)
2. Biases are hardcoded to zero (should be symmetric [-1, 1])

### Key Decisions

1. **Use `Random.value * 2.0 - 1.0` directly** - This produces uniform [-1, 1] without any scaling factor. The rationale is that for a feed-forward network with tanh activations, symmetric initialization in [-1, 1] ensures the expected weighted sum is near zero, leading to balanced activation.

2. **Keep the same method signature** - No API changes required; `InitializeRandom()` remains the entry point for random initialization.

3. **No changes to Forward()** - The inference logic doesn't need modification; only initialization changes.

### Proposed Changes

**File:** `Assets/Components/MLP/MLP.cs`

**Changes to `InitializeRandom()` method:**
- Remove the Xavier scale calculation
- Change weight initialization from `(Random.value * 2 - 1) * scale` to `Random.value * 2 - 1`
- Change bias initialization from `0f` to `Random.value * 2 - 1`
- Update XML doc comment to explain symmetric initialization

**Before:**
```csharp
/// <summary>
/// Initializes all weights using Xavier/Glorot uniform initialization and zeros biases.
/// </summary>
public void InitializeRandom()
{
    // ... Xavier scale calculation ...
    for (int i = 0; i < weightCount; i++)
        genome[offset + i] = (UnityEngine.Random.value * 2f - 1f) * scale;
    
    // Biases start at zero
    for (int i = 0; i < biasCount; i++)
        genome[offset + i] = 0f;
}
```

**After:**
```csharp
/// <summary>
/// Initializes all weights and biases with symmetric uniform distribution in [-1, 1].
/// This ensures the expected weighted sum is near zero, promoting balanced activation
/// across neurons and preventing all warriors from moving in the same direction.
/// </summary>
public void InitializeRandom()
{
    int offset = 0;
    for (int l = 0; l < layerCount - 1; l++)
    {
        int fromSize = layerSizes[l];
        int toSize = layerSizes[l + 1];
        int weightCount = layerWeightCounts[l];
        int biasCount = layerBiasCounts[l];

        // Symmetric initialization in [-1, 1] for both weights and biases
        for (int i = 0; i < weightCount; i++)
            genome[offset + i] = (UnityEngine.Random.value * 2f - 1f);

        offset += weightCount;

        for (int i = 0; i < biasCount; i++)
            genome[offset + i] = (UnityEngine.Random.value * 2f - 1f);

        offset += biasCount;
    }
}
```

# Delivery Steps

### ✓ Step 1: Update MLP initialization logic
Update MLP.cs InitializeRandom() method to use symmetric initialization

- Modify the InitializeRandom() method in Assets/Components/MLP/MLP.cs
- Remove the Xavier scale calculation (Mathf.Sqrt(6f / (fromSize + toSize)))
- Change weight initialization to use Random.value * 2f - 1f directly (no scaling)
- Change bias initialization from 0f to Random.value * 2f - 1f
- Update the XML doc comment to explain symmetric initialization rationale

### ✓ Step 2: Verify compilation
Verify the changes compile correctly

- Run dotnet build Assembly-CSharp.csproj
- Confirm 0 compilation errors
- Verify the method signature remains unchanged (backward compatible)