using UnityEngine;

/// <summary>
/// Controls two-bone IK for right and left arms on a warrior.
/// Positions hands according to target positions while preserving hand/weapon orientations.
/// </summary>
public class WarriorHandIKController : MonoBehaviour
{
    [Header("Bones")]
    [SerializeField] Transform modelRoot;
    [SerializeField] Transform rightShoulder;
    [SerializeField] Transform rightElbow;
    [SerializeField] Transform rightHand;
    [SerializeField] Transform leftShoulder;
    [SerializeField] Transform leftElbow;
    [SerializeField] Transform leftHand;

    [Header("IK Weights")]
    [Range(0f, 1f)]
    [SerializeField] float rightWeight = 1f;

    [Range(0f, 1f)]
    [SerializeField] float leftWeight = 1f;

    [Header("Converter Settings")]
    [SerializeField] PolarIKTargetConverter.HandLimits rightLimits = PolarIKTargetConverter.HandLimits.DefaultRight;
    [SerializeField] PolarIKTargetConverter.HandLimits leftLimits = PolarIKTargetConverter.HandLimits.DefaultLeft;

    [Header("Debug")]
    [SerializeField] bool showDebugGizmos = true;

    private PolarIKTargetConverter converter;
    private PolarIKTargetConverter.HandTargets currentTargets;
    private float rightArmLength;
    private float leftArmLength;
    private float[] lastOutputs;

    public PolarIKTargetConverter Converter => converter;
    public PolarIKTargetConverter.HandTargets CurrentTargets => currentTargets;
    public float RightWeight { get => rightWeight; set => rightWeight = Mathf.Clamp01(value); }
    public float LeftWeight { get => leftWeight; set => leftWeight = Mathf.Clamp01(value); }
    public bool ShowDebugGizmos { get => showDebugGizmos; set => showDebugGizmos = value; }
    public float[] LastOutputs => lastOutputs;

    void Awake()
    {
        if (modelRoot == null)
            modelRoot = transform;

        AutoResolveBones();
        CalculateArmLengths();

        converter = new PolarIKTargetConverter
        {
            RightLimits = rightLimits,
            LeftLimits = leftLimits
        };
    }

    void AutoResolveBones()
    {
        var boneController = GetComponent<WarriorBoneController>();
        if (boneController != null)
        {
            // Humanoid bone indices from WarriorBoneController:
            // 6: RightShoulder, 7: RightArm (Upper), 8: RightForeArm (Elbow), 9: RightHand
            // 2: LeftShoulder, 3: LeftArm (Upper), 4: LeftForeArm (Elbow), 5: LeftHand
            if (rightShoulder == null) rightShoulder = boneController.GetBoneTransform(7); // Upper arm
            if (rightElbow == null) rightElbow = boneController.GetBoneTransform(8);
            if (rightHand == null) rightHand = boneController.GetBoneTransform(9);

            if (leftShoulder == null) leftShoulder = boneController.GetBoneTransform(3); // Upper arm
            if (leftElbow == null) leftElbow = boneController.GetBoneTransform(4);
            if (leftHand == null) leftHand = boneController.GetBoneTransform(5);
        }
    }

    void CalculateArmLengths()
    {
        if (rightShoulder != null && rightElbow != null && rightHand != null)
        {
            float upper = Vector3.Distance(rightShoulder.position, rightElbow.position);
            float lower = Vector3.Distance(rightElbow.position, rightHand.position);
            rightArmLength = upper + lower;
        }
        else
        {
            rightArmLength = 0.6f;
        }

        if (leftShoulder != null && leftElbow != null && leftHand != null)
        {
            float upper = Vector3.Distance(leftShoulder.position, leftElbow.position);
            float lower = Vector3.Distance(leftElbow.position, leftHand.position);
            leftArmLength = upper + lower;
        }
        else
        {
            leftArmLength = 0.6f;
        }
    }

    /// <summary>
    /// Updates hand IK targets from 6 MLP outputs and solves IK.
    /// </summary>
    public void ApplyOutputs(float[] outputs)
    {
        if (outputs == null || outputs.Length < 6)
            return;

        lastOutputs = outputs;

        if (converter == null)
        {
            converter = new PolarIKTargetConverter
            {
                RightLimits = rightLimits,
                LeftLimits = leftLimits
            };
        }

        Vector3? rShoulderLocal = (rightShoulder != null && modelRoot != null) ? modelRoot.InverseTransformPoint(rightShoulder.position) : (Vector3?)null;
        Vector3? lShoulderLocal = (leftShoulder != null && modelRoot != null) ? modelRoot.InverseTransformPoint(leftShoulder.position) : (Vector3?)null;

        currentTargets = converter.ConvertOutputs(outputs, modelRoot, rShoulderLocal, lShoulderLocal, rightArmLength, leftArmLength);

        SolveIK();
    }

    /// <summary>
    /// Solves two-bone IK for right and left arms while preserving hand world orientation.
    /// </summary>
    public void SolveIK()
    {
        if (rightWeight > 0.001f && rightShoulder != null && rightElbow != null && rightHand != null)
        {
            Quaternion originalHandRot = rightHand.rotation;
            Vector3 target = currentTargets.rightWorldPos;
            if (rightWeight < 0.999f)
            {
                target = Vector3.Lerp(rightHand.position, target, rightWeight);
            }

            TwoBoneIK.Solve(target, modelRoot, rightShoulder, rightElbow, rightHand, 0f);
            rightHand.rotation = originalHandRot;
        }

        if (leftWeight > 0.001f && leftShoulder != null && leftElbow != null && leftHand != null)
        {
            Quaternion originalHandRot = leftHand.rotation;
            Vector3 target = currentTargets.leftWorldPos;
            if (leftWeight < 0.999f)
            {
                target = Vector3.Lerp(leftHand.position, target, leftWeight);
            }

            TwoBoneIK.Solve(target, modelRoot, leftShoulder, leftElbow, leftHand, 0f);
            leftHand.rotation = originalHandRot;
        }
    }

    void OnDrawGizmosSelected()
    {
        if (!showDebugGizmos || modelRoot == null) return;

        // Right target (Red/Magenta)
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(currentTargets.rightWorldPos, 0.05f);
        Gizmos.DrawLine(modelRoot.position, currentTargets.rightWorldPos);
        if (rightShoulder != null)
        {
            Gizmos.color = new Color(1f, 0.5f, 0f); // Orange
            Gizmos.DrawLine(rightShoulder.position, currentTargets.rightWorldPos);
        }

        // Left target (Blue/Cyan)
        Gizmos.color = Color.blue;
        Gizmos.DrawWireSphere(currentTargets.leftWorldPos, 0.05f);
        Gizmos.DrawLine(modelRoot.position, currentTargets.leftWorldPos);
        if (leftShoulder != null)
        {
            Gizmos.color = Color.cyan;
            Gizmos.DrawLine(leftShoulder.position, currentTargets.leftWorldPos);
        }
    }
}
