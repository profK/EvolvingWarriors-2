using UnityEngine;

/// <summary>
/// Manages the planted state machine for a single foot (left or right).
/// Tracks whether the foot is planted, lifting, or placing, and computes
/// the current IK target position based on sinusoidal arc trajectories.
/// One instance per foot; orchestrated by WarriorPoseSolver.
/// </summary>
public class ProceduralFootController : MonoBehaviour
{
    /// <summary>Which foot this controller manages.</summary>
    public enum FootSide { Left, Right }

    /// <summary>Current state of this foot in the step cycle.</summary>
    public enum FootState { Planted, Lifting, Placing }

    // --- Configuration ---

    [Tooltip("Which foot this controller manages (Left or Right).")]
    [SerializeField] internal FootSide side;

    [Tooltip("Transform of the foot bone (the IK effector).")]
    [SerializeField] internal Transform footTransform;

    [Tooltip("Transform of the upper leg bone (thigh).")]
    [SerializeField] internal Transform upperLegTransform;

    [Tooltip("Transform of the lower leg bone (shin).")]
    [SerializeField] internal Transform lowerLegTransform;

    /// <summary>Sets the foot bone transform. Called by WarriorPoseSolver during initialization.</summary>
    public void SetFootTransform(Transform t) => footTransform = t;

    /// <summary>Sets the upper leg bone transform. Called by WarriorPoseSolver during initialization.</summary>
    public void SetUpperLegTransform(Transform t) => upperLegTransform = t;

    /// <summary>Sets the lower leg bone transform. Called by WarriorPoseSolver during initialization.</summary>
    public void SetLowerLegTransform(Transform t) => lowerLegTransform = t;

    [Tooltip("Horizontal distance that triggers a step (default 0.4m)")]
    [SerializeField] float stepThreshold = 0.4f;

    [Tooltip("CoM shift from foot midpoint that triggers a step (default 0.25m)")]
    [SerializeField] float balanceThreshold = 0.25f;

    [Tooltip("Peak lift height during step arc (default 0.15m)")]
    [SerializeField] float arcLiftHeight = 0.15f;

    [Tooltip("Duration of each step phase: Lifting and Placing (default 0.4s)")]
    [SerializeField] float stepPhaseDuration = 0.4f;

    [Tooltip("Minimum step distance — steps shorter than this are skipped (default 0.05m)")]
    [SerializeField] float minStepDistance = 0.05f;

    // --- State tracking ---

    bool initialized; // Prevents double-init; only set to true on successful initialization
    FootState state = FootState.Planted;
    Vector3 plantedPosition; // X,Z stored when entering Planted state; Y is groundY
    Vector3 stepTargetXZ; // Target X,Z for the current step
    float phaseTimer; // Timer within the current Lifting or Placing phase
    Vector3 currentFootPosition; // Current world position of the foot (IK target)
    float groundY = 0f; // Ground plane Y level, set by orchestrator

    // --- Properties ---

    /// <summary>Which foot this controller manages.</summary>
    public FootSide Side => side;

    /// <summary>Current state of this foot.</summary>
    public FootState CurrentState => state;

    /// <summary>Transform of the foot bone.</summary>
    public Transform FootTransform { get => footTransform; set => footTransform = value; }

    /// <summary>Transform of the upper leg bone.</summary>
    public Transform UpperLegTransform { get => upperLegTransform; set => upperLegTransform = value; }

    /// <summary>Transform of the lower leg bone.</summary>
    public Transform LowerLegTransform { get => lowerLegTransform; set => lowerLegTransform = value; }

    /// <summary>Horizontal distance threshold that triggers a step.</summary>
    public float StepThreshold => stepThreshold;

    /// <summary>CoM shift threshold that triggers a step for balance.</summary>
    public float BalanceThreshold => balanceThreshold;

    /// <summary>Peak lift height during step arc.</summary>
    public float ArcLiftHeight => arcLiftHeight;

    /// <summary>Duration of each step phase (Lifting and Placing).</summary>
    public float StepPhaseDuration => stepPhaseDuration;

    // --- Initialization ---

    /// <summary>
    /// Called by Unity when the component is enabled. Delegates to Initialize().
    /// </summary>
    void Awake()
    {
        Initialize();
    }

    /// <summary>
    /// Initializes the foot controller.
    /// Captures the foot's current world position and enters Planted state.
    /// Called automatically by Awake(), or explicitly in tests after field assignment.
    /// Can be retried after transforms are assigned — does NOT set initialized=true on failure.
    /// </summary>
    public void Initialize()
    {
        if (initialized)
            return; // Already successfully initialized — skip

        // Guard against missing transform references — disable component to prevent crashes
        // Do NOT set initialized = true here — allow retry after fields are assigned
        if (footTransform == null)
        {
            Debug.LogError("ProceduralFootController: footTransform not assigned for " + side, this);
            enabled = false;
            return;
        }
        if (upperLegTransform == null || lowerLegTransform == null)
        {
            Debug.LogError("ProceduralFootController: leg transforms not assigned for " + side, this);
            enabled = false;
            return;
        }

        // Initialize from foot transform's current position
        currentFootPosition = footTransform.position;
        // Clamp Y to ground level — the functional ground for feet
        plantedPosition = new Vector3(currentFootPosition.x, groundY, currentFootPosition.z);
        state = FootState.Planted;
        phaseTimer = 0f;
        initialized = true; // Only set on successful initialization
    }

    // --- Public API ---

    /// <summary>
    /// Requests this foot to step to a new X,Z position.
    /// Only valid when the foot is in Planted state.
    /// </summary>
    /// <param name="targetXZ">Desired X,Z target position for the step.</param>
    /// <returns>True if the step was accepted; false if skipped (wrong state or too short).</returns>
    public bool RequestStep(Vector3 targetXZ)
    {
        if (!initialized || !enabled)
            return false;

        if (state != FootState.Planted)
            return false;

        // Compute horizontal distance from current planted position
        Vector2 delta = new Vector2(targetXZ.x - plantedPosition.x, targetXZ.z - plantedPosition.z);
        float distance = delta.magnitude;

        // Skip micro-steps to prevent noise-induced stepping
        if (distance < minStepDistance)
            return false;

        // Accept the step and transition to Lifting
        stepTargetXZ = targetXZ;
        phaseTimer = 0f;
        state = FootState.Lifting;

        return true;
    }

    /// <summary>
    /// Updates the foot state machine and computes the current IK target position.
    /// Called every FixedUpdate from the orchestrator.
    /// </summary>
    public void UpdateState()
    {
        if (!initialized || !enabled)
            return;

        switch (state)
        {
            case FootState.Planted:
                UpdatePlanted();
                break;

            case FootState.Lifting:
                UpdateLifting();
                break;

            case FootState.Placing:
                UpdatePlacing();
                break;
        }
    }

    /// <summary>
    /// Returns the current world position for this foot, which serves as the
    /// IK target position for the TwoBoneIK solver.
    /// Falls back to the actual bone position if not yet initialized.
    /// </summary>
    public Vector3 GetFootWorldPosition()
    {
        if (!initialized && footTransform != null)
            return footTransform.position;
        return currentFootPosition;
    }

    /// <summary>
    /// Returns the current state of this foot.
    /// </summary>
    public FootState GetState()
    {
        return state;
    }

    /// <summary>
    /// Sets the ground Y level for this foot. Called by the orchestrator.
    /// </summary>
    /// <param name="y">Y coordinate of the ground plane.</param>
    public void SetGroundY(float y)
    {
        groundY = y;
    }

    /// <summary>
    /// Resets this foot to Planted state at the foot transform's current position.
    /// Called by the orchestrator at round start.
    /// </summary>
    public void Reset()
    {
        if (footTransform == null)
            return;

        // Capture current foot position and clamp Y to ground level
        // This ensures feet are at groundY even if foot bone starts at non-zero Y (e.g., T-position)
        Vector3 capturedPos = footTransform.position;
        currentFootPosition = new Vector3(capturedPos.x, groundY, capturedPos.z);
        
        plantedPosition = new Vector3(currentFootPosition.x, groundY, currentFootPosition.z);
        stepTargetXZ = plantedPosition; // Reset step target to avoid latent state inconsistency
        state = FootState.Planted;
        phaseTimer = 0f;
    }

    /// <summary>
    /// Test-friendly variant of UpdateState() that accepts a custom delta time
    /// instead of relying on Time.fixedDeltaTime. Allows deterministic phase
    /// advancement in edit-mode tests.
    /// </summary>
    /// <param name="deltaTime">Time delta to advance the phase timer.</param>
    internal void UpdateStateWithDelta(float deltaTime)
    {
        if (!initialized)
            return;

        switch (state)
        {
            case FootState.Planted:
                UpdatePlanted();
                break;

            case FootState.Lifting:
                UpdateLiftingWithDelta(deltaTime);
                break;

            case FootState.Placing:
                UpdatePlacingWithDelta(deltaTime);
                break;
        }
    }

    // --- State update methods ---

    /// <summary>
    /// Planted state: foot stays at its planted X,Z with Y clamped to ground.
    /// </summary>
    void UpdatePlanted()
    {
        currentFootPosition = new Vector3(plantedPosition.x, groundY, plantedPosition.z);
    }

    /// <summary>
    /// Lifting state: foot lifts in a sinusoidal arc while moving from planted
    /// position toward the step target. Transitions to Placing at peak.
    /// </summary>
    void UpdateLifting()
    {
        UpdateLiftingWithDelta(Time.fixedDeltaTime);
    }

    /// <summary>
    /// Lifting state update with custom delta time (for testing).
    /// </summary>
    void UpdateLiftingWithDelta(float deltaTime)
    {
        phaseTimer += deltaTime;
        float t = Mathf.Clamp01(phaseTimer / stepPhaseDuration);

        // Sinusoidal arc: 0 → peak → 0 over the phase duration
        float lift = Mathf.Sin(t * Mathf.PI) * arcLiftHeight;

        // Linear interpolation of X,Z from planted position toward step target
        currentFootPosition.x = Mathf.Lerp(plantedPosition.x, stepTargetXZ.x, t);
        currentFootPosition.z = Mathf.Lerp(plantedPosition.z, stepTargetXZ.z, t);
        currentFootPosition.y = groundY + lift;

        // Transition to Placing when the lift phase completes
        if (t >= 1.0f)
        {
            state = FootState.Placing;
            phaseTimer = 0f;
        }
    }

    /// <summary>
    /// Placing state: foot descends from arc peak while holding the target X,Z.
    /// Transitions back to Planted when the arc completes.
    /// </summary>
    void UpdatePlacing()
    {
        UpdatePlacingWithDelta(Time.fixedDeltaTime);
    }

    /// <summary>
    /// Placing state update with custom delta time (for testing).
    /// </summary>
    void UpdatePlacingWithDelta(float deltaTime)
    {
        phaseTimer += deltaTime;
        float t = Mathf.Clamp01(phaseTimer / stepPhaseDuration);

        // Sinusoidal arc: peak → 0 over the phase duration (foot descends)
        float lift = Mathf.Sin(t * Mathf.PI) * arcLiftHeight;

        // Stay at the target X,Z — foot is already at stepTargetXZ from Lifting phase
        currentFootPosition.x = stepTargetXZ.x;
        currentFootPosition.z = stepTargetXZ.z;
        currentFootPosition.y = groundY + lift;

        // Transition to Planted when the place phase completes
        if (t >= 1.0f)
        {
            // Update planted position to the new step target
            plantedPosition = new Vector3(stepTargetXZ.x, groundY, stepTargetXZ.z);
            state = FootState.Planted;
            phaseTimer = 0f;
        }
    }
}