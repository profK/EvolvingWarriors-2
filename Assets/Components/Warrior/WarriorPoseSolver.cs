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
            var wbc = GetComponent<WarriorBoneController>();
            if (wbc != null)
                boneController = wbc;
        }
        if (boneController == null)
        {
            Debug.LogError("WarriorPoseSolver: WarriorBoneController not found and cannot be auto-resolved.", this);
            enabled = false;
            return;
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

        // Assign bone transforms to foot controllers and initialize them
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

        // 4. Evaluate balance — if CoM shifted beyond threshold, trigger step on the farther foot
        if (horizontalComOffset > balanceThreshold)
        {
            Vector3 comDir = new Vector3(comOffset.x, 0, comOffset.z);
            if (comDir.sqrMagnitude > 0.001f)
            {
                comDir /= comDir.magnitude;
                // Project each foot's position onto CoM direction
                float leftProj = Vector3.Dot(new Vector3(leftFootPos.x, 0, leftFootPos.z), comDir);
                float rightProj = Vector3.Dot(new Vector3(rightFootPos.x, 0, rightFootPos.z), comDir);
                // Step with the foot that is behind the CoM shift (smaller projection)
                Vector3 stepTarget = hips.position + comDir * 0.3f;
                if (leftProj < rightProj)
                {
                    leftFootController.RequestStep(new Vector3(stepTarget.x, 0, stepTarget.z));
                }
                else
                {
                    rightFootController.RequestStep(new Vector3(stepTarget.x, 0, stepTarget.z));
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
            TwoBoneIK.Solve(leftTarget, root, leftUpperLeg, leftLowerLeg, leftFootBone, groundY);
        }
        if (rightUpperLeg != null && rightLowerLeg != null && rightFootBone != null)
        {
            Vector3 rightTarget = rightFootController.GetFootWorldPosition();
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
    /// sets the ground Y level, and initializes the controller.
    /// Called from Initialize() after resolving foot controller references.
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
        controller.Initialize();
    }
}