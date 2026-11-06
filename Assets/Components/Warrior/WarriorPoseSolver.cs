using UnityEngine;

/// <summary>
/// Orchestrates the procedural IK system for a warrior.
/// Coordinates foot state machines, evaluates balance and reach,
/// triggers procedural steps, and applies two-bone IK to keep feet grounded.
/// One instance per warrior; called from WarriorAgent.FixedUpdate after bone rotations.
/// </summary>
public class WarriorPoseSolver : MonoBehaviour
{
    // Bone indices matching WarriorBoneController.ControlledBones
    const int Hips = 0;
    const int LeftUpperLeg = 12;
    const int RightUpperLeg = 13;
    const int LeftLowerLeg = 14;
    const int RightLowerLeg = 15;
    const int LeftFoot = 16;
    const int RightFoot = 17;

    // --- References ---

    [Header("References")]
    [Tooltip("The warrior's bone controller for accessing bone transforms.")]
    [SerializeField] internal IBoneTransformProvider boneController;

    [Tooltip("State machine controller for the left foot.")]
    [SerializeField] internal ProceduralFootController leftFootController;

    [Tooltip("State machine controller for the right foot.")]
    [SerializeField] internal ProceduralFootController rightFootController;

    // --- Settings ---

    [Header("Settings")]
    [Tooltip("Ground plane Y level where feet should rest (default 0).")]
    [SerializeField] float groundY = 0f;

    [Tooltip("Horizontal CoM shift from foot midpoint that triggers a step (default 0.25m).")]
    [SerializeField] float balanceThreshold = 0.25f;

    [Tooltip("Reach threshold — when MLP pushes foot beyond this from planted pos, trigger step (default 0.4m).")]
    [SerializeField] float stepThreshold = 0.4f;

    // --- State ---

    bool initialized; // Only set to true on successful initialization

    // --- Initialization ---

    /// <summary>
    /// Called by Unity when the component is enabled. Delegates to Initialize().
    /// </summary>
    void Awake()
    {
        Initialize();
    }

    /// <summary>
    /// Initializes the pose solver by auto-resolving component references.
    /// Can be retried after fields are assigned — does NOT set initialized=true on failure.
    /// Called automatically by Awake(), or explicitly in tests after field assignment.
    /// </summary>
    public void Initialize()
    {
        if (initialized)
            return; // Already successfully initialized — skip

        // Auto-resolve bone controller
        if (boneController == null)
        {
            var resolved = GetComponent<WarriorBoneController>();
            if (resolved != null)
                boneController = resolved;
        }
        if (boneController == null)
        {
            Debug.LogError("WarriorPoseSolver: WarriorBoneController not found and cannot be auto-resolved.", this);
            enabled = false;
            return;
        }

        // Ensure the bone controller has resolved its transforms before we read them.
        // Handles cases where WarriorPoseSolver.Awake() fires before WarriorBoneController.Awake().
        if (boneController is WarriorBoneController concrete && !concrete.IsInitialized)
        {
            concrete.InitializeBones();
        }
        if (boneController is WarriorBoneController concrete2 && !concrete2.IsInitialized)
        {
            Debug.LogError("WarriorPoseSolver: Bone controller is not initialized and cannot be initialized.", this);
            enabled = false;
            return;
        }

        // --- Adjust root Y so the hips are exactly one leg-length above ground ---
        // This produces natural standing legs (fully extended, knees at 0° bend).
        // Uses the actual bone geometry to compute the proper hip height.
        Transform hips = boneController.GetBoneTransform(Hips);
        Transform leftUpperLeg = boneController.GetBoneTransform(LeftUpperLeg);
        Transform leftLowerLeg = boneController.GetBoneTransform(LeftLowerLeg);
        Transform leftFoot = boneController.GetBoneTransform(LeftFoot);

        if (leftUpperLeg != null && leftLowerLeg != null && leftFoot != null)
        {
            // Measure actual leg lengths from bone positions
            float thighLen = (leftLowerLeg.position - leftUpperLeg.position).magnitude;
            float shinLen = (leftFoot.position - leftLowerLeg.position).magnitude;
            float totalLegLength = thighLen + shinLen;

            // Desired: upper leg is exactly one leg-length above ground
            float desiredUpperLegY = groundY + totalLegLength;
            float currentUpperLegY = leftUpperLeg.position.y;
            float deltaY = currentUpperLegY - desiredUpperLegY;

            // Move root so upper leg sits at the desired height
            // The TwoBoneIK solver uses upperLeg.position as its "hip" reference;
            // placing it at groundY + totalLegLength means fully extended legs reach ground.
            if (Mathf.Abs(deltaY) > 0.001f)
            {
                Vector3 rootPos = transform.position;
                rootPos.y -= deltaY;
                transform.position = rootPos;
            }
        }

        // Auto-resolve foot controllers if not assigned
        // GetComponents returns disabled MonoBehaviours on the same GameObject — only
        // GetComponentsInChildren/InParent filter by active state.
        if (leftFootController == null || rightFootController == null)
        {
            ProceduralFootController[] controllers = GetComponents<ProceduralFootController>();

            foreach (ProceduralFootController ctrl in controllers)
            {
                if (ctrl.Side == ProceduralFootController.FootSide.Left && leftFootController == null)
                    leftFootController = ctrl;
                if (ctrl.Side == ProceduralFootController.FootSide.Right && rightFootController == null)
                    rightFootController = ctrl;
            }
        }

        if (leftFootController == null)
        {
            Debug.LogError("WarriorPoseSolver: Left foot ProceduralFootController not found.", this);
            enabled = false;
            return;
        }
        if (rightFootController == null)
        {
            Debug.LogError("WarriorPoseSolver: Right foot ProceduralFootController not found.", this);
            enabled = false;
            return;
        }

        // Assign bone transforms to foot controllers and force re-initialization
        // ForceReinitialize re-captures bone positions AFTER the root Y adjustment above,
        // ensuring initial IK targets reflect the grounded position.
        AssignBoneTransforms(leftFootController);
        AssignBoneTransforms(rightFootController);

        initialized = true; // Only set on successful initialization
    }

    /// <summary>
    /// Main orchestration method called from WarriorAgent.FixedUpdate after ApplyOutputs().
    /// Evaluates balance and reach, triggers steps, updates foot states,
    /// applies two-bone IK, and corrects ground penetration.
    /// </summary>
    public void Solve()
    {
        if (!initialized)
            return;

        // Do not run IK while paused (e.g., during reset)
        var sim = FindObjectOfType<EvolutionSimulationManager>();
        if (sim != null && sim.IsPaused)
            return;

        // 1. Get hip transform (CoM proxy)
        Transform hips = boneController.GetBoneTransform(Hips);
        if (hips == null)
            return;

        // 2. Get current foot positions from controllers
        Vector3 leftFootPos = leftFootController.GetFootWorldPosition();
        Vector3 rightFootPos = rightFootController.GetFootWorldPosition();

        // 3. Compute foot midpoint and CoM offset
        Vector3 footMidpoint = (leftFootPos + rightFootPos) * 0.5f;
        Vector3 comOffset = hips.position - footMidpoint;
        float horizontalComOffset = new Vector3(comOffset.x, 0, comOffset.z).magnitude;

        // 4. Evaluate balance — if CoM shifted beyond threshold, move hips to center over feet
        // We move the hips bone's local position horizontally toward the foot midpoint.
        // This centers the hips over the feet, keeping feet planted.
        if (horizontalComOffset > balanceThreshold)
        {
            Vector3 comDir = new Vector3(comOffset.x, 0, comOffset.z);
            if (comDir.sqrMagnitude > 0.001f)
            {
                comDir /= comDir.magnitude;
                
                // Calculate the horizontal offset needed to center hips over foot midpoint
                // We want hips to be at the foot midpoint horizontally
                Vector3 desiredHipsOffset = -comOffset;
                desiredHipsOffset.y = 0f; // Keep only horizontal component
                
                // Limit the movement to keep hips within a reasonable range over the feet
                // Don't move more than the balance threshold (keeps hips over the foot base)
                float moveDistance = Mathf.Min(desiredHipsOffset.magnitude, balanceThreshold);
                Vector3 moveDirection = desiredHipsOffset.normalized;
                Vector3 hipMove = moveDirection * moveDistance;
                
                // Apply the horizontal movement to the hips bone's local position
                // This shifts the hips over the feet while keeping feet planted
                Vector3 currentLocalPos = hips.localPosition;
                Vector3 newLocalPos = currentLocalPos + new Vector3(hipMove.x, 0f, hipMove.z);
                hips.localPosition = newLocalPos;
                
                // Also move the spine and chest bones to maintain body alignment
                Transform spine = boneController.GetBoneTransform(1); // Spine
                Transform chest = boneController.GetBoneTransform(2); // Chest
                
                if (spine != null)
                {
                    // Move spine by half the hip movement to distribute the shift
                    spine.localPosition = spine.localPosition + new Vector3(hipMove.x * 0.5f, 0f, hipMove.z * 0.5f);
                }
                
                if (chest != null)
                {
                    // Move chest by one-third the hip movement
                    chest.localPosition = chest.localPosition + new Vector3(hipMove.x * 0.33f, 0f, hipMove.z * 0.33f);
                }
            }
        }

        // 5. Evaluate reach — if MLP pushed a foot beyond stepThreshold from its planted position
        Transform leftFootBone = boneController.GetBoneTransform(LeftFoot);
        Transform rightFootBone = boneController.GetBoneTransform(RightFoot);

        if (leftFootBone != null)
        {
            Vector3 leftBoneWorldPos = leftFootBone.position;
            Vector3 leftPlantedXZ = new Vector3(leftFootPos.x, 0, leftFootPos.z);
            Vector3 leftBoneXZ = new Vector3(leftBoneWorldPos.x, 0, leftBoneWorldPos.z);
            float leftDrift = (leftBoneXZ - leftPlantedXZ).magnitude;
            if (leftDrift > stepThreshold && leftFootController.GetState() == ProceduralFootController.FootState.Planted)
            {
                // Foot drifted from planted position — request step to current bone position
                leftFootController.RequestStep(new Vector3(leftBoneWorldPos.x, 0, leftBoneWorldPos.z));
            }
        }

        if (rightFootBone != null)
        {
            Vector3 rightBoneWorldPos = rightFootBone.position;
            Vector3 rightPlantedXZ = new Vector3(rightFootPos.x, 0, rightFootPos.z);
            Vector3 rightBoneXZ = new Vector3(rightBoneWorldPos.x, 0, rightBoneWorldPos.z);
            float rightDrift = (rightBoneXZ - rightPlantedXZ).magnitude;
            if (rightDrift > stepThreshold && rightFootController.GetState() == ProceduralFootController.FootState.Planted)
            {
                // Foot drifted from planted position — request step to current bone position
                rightFootController.RequestStep(new Vector3(rightBoneWorldPos.x, 0, rightBoneWorldPos.z));
            }
        }

        // 6. Update foot states — advance state machines
        leftFootController.UpdateState();
        rightFootController.UpdateState();

        // 7. Apply TwoBoneIK for each leg chain
        Transform root = transform; // Warrior root transform
        Transform leftUpperLeg = boneController.GetBoneTransform(LeftUpperLeg);
        Transform leftLowerLeg = boneController.GetBoneTransform(LeftLowerLeg);
        Transform rightUpperLeg = boneController.GetBoneTransform(RightUpperLeg);
        Transform rightLowerLeg = boneController.GetBoneTransform(RightLowerLeg);

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

        // 8. Correct ground penetration — ensure foot bones never go below groundY
        // Use the actual bone position (not the IK target) as the corrected target
        if (leftFootBone != null && leftFootBone.position.y < groundY)
        {
            Vector3 correctedLeft = leftFootBone.position;
            correctedLeft.y = groundY;
            if (leftUpperLeg != null && leftLowerLeg != null)
            {
                TwoBoneIK.Solve(correctedLeft, root, leftUpperLeg, leftLowerLeg, leftFootBone, groundY);
            }
        }
        if (rightFootBone != null && rightFootBone.position.y < groundY)
        {
            Vector3 correctedRight = rightFootBone.position;
            correctedRight.y = groundY;
            if (rightUpperLeg != null && rightLowerLeg != null)
            {
                TwoBoneIK.Solve(correctedRight, root, rightUpperLeg, rightLowerLeg, rightFootBone, groundY);
            }
        }

        // 9. Vertical grounding: move the warrior root up/down so at least one foot touches ground.
        // Reads the ACTUAL bone world positions after IK to handle cases where the root is too high
        // (e.g., hips above leg-reach distance). The root's X,Z are locked by WarriorBoneController;
        // only Y is adjusted here.
        AdjustRootVerticalForGrounding(leftFootBone, rightFootBone);
    }

    /// <summary>
    /// Moves the warrior root's Y position so at least one foot touches the ground.
    /// Preserves the root's X,Z (those are locked by WarriorBoneController).
    /// </summary>
    void AdjustRootVerticalForGrounding(Transform leftFootBone, Transform rightFootBone)
    {
        Transform root = transform;
        if (root == null) return;

        // Find the lowest actual foot world Y after IK
        float lowestFootY = Mathf.Infinity;
        int groundedFeet = 0;

        if (leftFootBone != null)
        {
            lowestFootY = Mathf.Min(lowestFootY, leftFootBone.position.y);
            if (leftFootBone.position.y <= groundY + 0.01f)
                groundedFeet++;
        }
        if (rightFootBone != null)
        {
            lowestFootY = Mathf.Min(lowestFootY, rightFootBone.position.y);
            if (rightFootBone.position.y <= groundY + 0.01f)
                groundedFeet++;
        }

        // If both feet are floating, move the root down so the lower foot touches ground
        if (groundedFeet == 0 && lowestFootY < Mathf.Infinity)
        {
            float deltaY = lowestFootY - groundY;
            Vector3 rootPos = root.position;
            rootPos.y -= deltaY;
            root.position = rootPos;

            // Update foot controllers to track the new world positions
            if (leftFootBone != null)
                leftFootController.currentFootPosition = leftFootBone.position;
            if (rightFootBone != null)
                rightFootController.currentFootPosition = rightFootBone.position;
        }
    }

    /// <summary>
    /// Solves leg IK with grounded targets to ensure legs point downward.
    /// Called during Reset() to establish proper initial leg orientation.
    /// </summary>
    void SolveLegOrientation()
    {
        // Get bone transforms
        Transform leftUpperLeg = boneController.GetBoneTransform(LeftUpperLeg);
        Transform leftLowerLeg = boneController.GetBoneTransform(LeftLowerLeg);
        Transform rightUpperLeg = boneController.GetBoneTransform(RightUpperLeg);
        Transform rightLowerLeg = boneController.GetBoneTransform(RightLowerLeg);
        Transform leftFootBone = boneController.GetBoneTransform(LeftFoot);
        Transform rightFootBone = boneController.GetBoneTransform(RightFoot);

        // Get foot positions from controllers and force Y to ground level
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

    /// <summary>
    /// Resets both foot controllers to Planted state at their current bone positions.
    /// Called at the start of a new round.
    /// </summary>
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

    /// <summary>
    /// Updates the ground Y level and propagates it to both foot controllers.
    /// </summary>
    /// <param name="y">New Y coordinate of the ground plane.</param>
    public void SetGroundY(float y)
    {
        groundY = y;
        if (initialized)
        {
            leftFootController.SetGroundY(y);
            rightFootController.SetGroundY(y);
        }
    }

    /// <summary>
    /// Assigns the correct bone transforms to a foot controller based on its side,
    /// sets the ground Y level, and forces re-initialization.
    /// Uses ForceReinitialize so foot controllers always capture bone positions AFTER
    /// the root Y adjustment, ensuring grounded initial IK targets.
    /// </summary>
    /// <param name="controller">The foot controller to configure.</param>
    void AssignBoneTransforms(ProceduralFootController controller)
    {
        if (controller.Side == ProceduralFootController.FootSide.Left)
        {
            controller.SetUpperLegTransform(boneController.GetBoneTransform(LeftUpperLeg));
            controller.SetLowerLegTransform(boneController.GetBoneTransform(LeftLowerLeg));
            controller.SetFootTransform(boneController.GetBoneTransform(LeftFoot));
        }
        else
        {
            controller.SetUpperLegTransform(boneController.GetBoneTransform(RightUpperLeg));
            controller.SetLowerLegTransform(boneController.GetBoneTransform(RightLowerLeg));
            controller.SetFootTransform(boneController.GetBoneTransform(RightFoot));
        }
        controller.SetGroundY(groundY);
        controller.enabled = true; // Re-enable in case Awake disabled it due to null transforms
        controller.ForceReinitialize(); // Always re-captures positions after root Y adjustment
    }
}