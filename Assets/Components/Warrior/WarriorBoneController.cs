using System;
using UnityEngine;
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Assembly-CSharp-Editor")]

/// <summary>
/// Minimal interface for accessing bone transforms by index.
/// Allows both the real WarriorBoneController and test mocks to work with WarriorPoseSolver.
/// Defined here so it compiles in the main Assembly-CSharp project without editor regeneration.
/// </summary>
public interface IBoneTransformProvider
{
    Transform GetBoneTransform(int boneIndex);
}

/// <summary>
/// Controls bone rotations for a warrior based on neural network outputs.
/// Maps HumanBodyBones to Transform references, stores rest-pose values,
/// applies Quaternion.RotateTowards with configurable max angular speed.
/// </summary>
[RequireComponent(typeof(Animator))]
public class WarriorBoneController : MonoBehaviour, IBoneTransformProvider
{
    public static readonly HumanBodyBones[] ControlledBones = new HumanBodyBones[]
    {
        HumanBodyBones.Hips,
        HumanBodyBones.Spine,
        HumanBodyBones.Chest,
        HumanBodyBones.UpperChest,
        HumanBodyBones.Neck,
        HumanBodyBones.Head,
        HumanBodyBones.LeftUpperArm,
        HumanBodyBones.RightUpperArm,
        HumanBodyBones.LeftLowerArm,
        HumanBodyBones.RightLowerArm,
        HumanBodyBones.LeftHand,
        HumanBodyBones.RightHand,
        HumanBodyBones.LeftUpperLeg,
        HumanBodyBones.RightUpperLeg,
        HumanBodyBones.LeftLowerLeg,
        HumanBodyBones.RightLowerLeg,
        HumanBodyBones.LeftFoot,
        HumanBodyBones.RightFoot,
    };

    public const int BoneCount = 19;
    public const int OutputsPerBone = 3;
    public const int OutputDimension = BoneCount * OutputsPerBone; // 57

    [Tooltip("Maximum angular rotation speed in degrees per second.")]
    [SerializeField] float maxAngularSpeed = 180f;

    public float MaxAngularSpeed
    {
        get => maxAngularSpeed;
        set => maxAngularSpeed = Mathf.Max(0f, value);
    }

    readonly Transform[] boneTransforms = new Transform[BoneCount];
    readonly Quaternion[] restLocalRotations = new Quaternion[BoneCount];
    readonly Vector3[] restLocalPositions = new Vector3[BoneCount];
    readonly Vector3[] restLocalScales = new Vector3[BoneCount];
    readonly Quaternion[] targetLocalRotations = new Quaternion[BoneCount];
    bool initialized;
    Animator animator;
    Vector3 spawnPosition;

    void Awake()
    {
        animator = GetComponent<Animator>();
        if (animator == null)
        {
            Debug.LogError("WarriorBoneController requires an Animator component.", this);
            enabled = false;
            return;
        }

        // Disable the Animator so it doesn't override bone rotations set by the neural network.
        // The Animator is only needed here for GetBoneTransform() to resolve HumanBodyBones.
        animator.enabled = false;

        // Capture the spawn position so the root never moves from its assigned location.
        // This enforces the design requirement: "Root remains at spawn location."
        spawnPosition = transform.position;

        InitializeBones();
    }

    public void InitializeBones()
    {
        int foundCount = 0;
        for (int i = 0; i < ControlledBones.Length; i++)
        {
            Transform bone = animator.GetBoneTransform(ControlledBones[i]);
            if (bone != null)
            {
                boneTransforms[i] = bone;
                restLocalRotations[i] = bone.localRotation;
                restLocalPositions[i] = bone.localPosition;
                restLocalScales[i] = bone.localScale;
                targetLocalRotations[i] = bone.localRotation;
                foundCount++;
            }
            else
            {
                Debug.LogWarning($"WarriorBoneController: Bone {ControlledBones[i]} not found.");
            }
        }
        if (foundCount < ControlledBones.Length)
            Debug.LogWarning($"WarriorBoneController: Only {foundCount}/{ControlledBones.Length} bones resolved.");

        // Correct leg bones from X-parallel to Y-parallel with feet downward (parallel to Y axis)
        int[] legBoneIndices = { 12, 13, 14, 15 };
        foreach (int idx in legBoneIndices)
        {
            if (boneTransforms[idx] != null)
            {
                restLocalRotations[idx] = Quaternion.Euler(0f, 0f, 0f) * restLocalRotations[idx];
                targetLocalRotations[idx] = restLocalRotations[idx];
            }
        }

        // Fix hip rest position to sit centered above feet (index 0 = Hips)
        if (boneTransforms[0] != null)
        {
            Vector3 hipsPos = restLocalPositions[0];
            hipsPos.x = 0f;
            hipsPos.z = 0f;
            hipsPos.y = 1.0f; // Hips 1 units above feet plane
            restLocalPositions[0] = hipsPos;
        }

        initialized = true;
    }

    public bool IsInitialized => initialized;

    public int ResolvedBoneCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < BoneCount; i++)
                if (boneTransforms[i] != null) count++;
            return count;
        }
    }

    /// <summary>
    /// Converts MLP output array to target rotations and applies them with speed limiting.
    /// Output layout: per bone, 3 values as Euler angle offsets (degrees) from rest pose.
    /// </summary>
    public void ApplyOutputs(float[] outputs)
    {
        if (!initialized) return;
        if (outputs == null || outputs.Length < OutputDimension) return;

        float angularStep = maxAngularSpeed * Time.fixedDeltaTime;

        for (int i = 0; i < BoneCount; i++)
        {
            Transform bone = boneTransforms[i];
            if (bone == null) continue;

            // Lock b-thigh.l / b-thigh.r and all bones beneath them (leg chain 12-17)
            if (i >= 12 && i <= 17)
            {
                bone.localRotation = restLocalRotations[i];
                bone.localPosition = restLocalPositions[i];
                bone.localScale = restLocalScales[i];
                targetLocalRotations[i] = restLocalRotations[i];
                continue;
            }

            int idx = i * OutputsPerBone;
            float ox = Guard(outputs[idx]);
            float oy = Guard(outputs[idx + 1]);
            float oz = Guard(outputs[idx + 2]);

            // Convert output values (-1..1) to rotation offsets and combine with rest pose
            Quaternion offset = Quaternion.Euler(ox * 90f, oy * 90f, oz * 90f);
            targetLocalRotations[i] = restLocalRotations[i] * offset;

            // Apply anatomical joint limits
            ApplyJointLimits(i, ref targetLocalRotations[i]);

            // Smooth rotation toward target
            bone.localRotation = Quaternion.RotateTowards(bone.localRotation, targetLocalRotations[i], angularStep);

            // Enforce: never change bone position or scale
            bone.localPosition = restLocalPositions[i];
            bone.localScale = restLocalScales[i];
        }

        // Enforce: root transform stays at spawn location.
        // Prevents physics displacement from bone rotations (Rigidbody constraints may not be frozen).
        transform.position = spawnPosition;
    }

    /// <summary>
    /// Resets all controlled bones to their rest-pose rotations.
    /// </summary>
    public void ResetToRestPose()
    {
        if (!initialized) return;
        for (int i = 0; i < BoneCount; i++)
        {
            Transform bone = boneTransforms[i];
            if (bone == null) continue;
            bone.localRotation = restLocalRotations[i];
            bone.localPosition = restLocalPositions[i];
            bone.localScale = restLocalScales[i];
            targetLocalRotations[i] = restLocalRotations[i];
        }

        // Keep knees at captured rest bend (do not override with thigh rotation)
        // (Previously copied thigh rot to shin; removed so shins keep their rest pose.)

        // Also restore the root position to spawn location after reset.
        transform.position = spawnPosition;
    }

    /// <summary>
    /// Returns the rest-pose local rotation for the specified bone index.
    /// </summary>
    public Quaternion GetRestRotation(int boneIndex)
    {
        if (boneIndex < 0 || boneIndex >= BoneCount) throw new ArgumentOutOfRangeException(nameof(boneIndex));
        return restLocalRotations[boneIndex];
    }

    /// <summary>
    /// Returns the rest-pose local position for the specified bone index.
    /// </summary>
    public Vector3 GetRestPosition(int boneIndex)
    {
        if (boneIndex < 0 || boneIndex >= BoneCount) throw new ArgumentOutOfRangeException(nameof(boneIndex));
        return restLocalPositions[boneIndex];
    }

    /// <summary>
    /// Returns the Transform for the specified bone index.
    /// </summary>
    public Transform GetBoneTransform(int boneIndex)
    {
        if (boneIndex < 0 || boneIndex >= BoneCount) throw new ArgumentOutOfRangeException(nameof(boneIndex));
        return boneTransforms[boneIndex];
    }

    static float Guard(float v)
    {
        return float.IsNaN(v) || float.IsInfinity(v) ? 0f : v;
    }

    /// <summary>
    /// Applies anatomical joint limits to the target rotation.
    /// Limits are expressed as maximum deviation from rest pose in degrees per axis.
    /// </summary>
    void ApplyJointLimits(int boneIndex, ref Quaternion targetRotation)
    {
        Transform bone = boneTransforms[boneIndex];
        if (bone == null) return;

        // Get the rotation relative to rest pose
        Quaternion relativeRot = Quaternion.Inverse(restLocalRotations[boneIndex]) * targetRotation;
        Vector3 eulerAngles = relativeRot.eulerAngles;

        // Normalize to [-180, 180]
        if (eulerAngles.x > 180f) eulerAngles.x -= 360f;
        if (eulerAngles.y > 180f) eulerAngles.y -= 360f;
        if (eulerAngles.z > 180f) eulerAngles.z -= 360f;

        // Apply per-bone limits (degrees)
        float[] limits = GetJointLimits(boneIndex);
        eulerAngles.x = Mathf.Clamp(eulerAngles.x, -limits[0], limits[0]);
        eulerAngles.y = Mathf.Clamp(eulerAngles.y, -limits[1], limits[1]);
        eulerAngles.z = Mathf.Clamp(eulerAngles.z, -limits[2], limits[2]);

        targetRotation = restLocalRotations[boneIndex] * Quaternion.Euler(eulerAngles);
    }

    /// <summary>
    /// Pre-computed per-axis joint limits in degrees for each bone.
    /// [boneIndex, axis] = maximum deviation from rest pose.
    /// </summary>
    static readonly float[][] jointLimits = new float[][]
    {
        new float[] { 30f, 45f, 30f },   // 0: Hips
        new float[] { 45f, 45f, 30f },    // 1: Spine
        new float[] { 45f, 45f, 30f },    // 2: Chest
        new float[] { 30f, 30f, 20f },    // 3: UpperChest
        new float[] { 45f, 45f, 30f },    // 4: Neck
        new float[] { 45f, 45f, 30f },    // 5: Head
        new float[] { 160f, 60f, 45f },   // 6: LeftUpperArm
        new float[] { 160f, 60f, 45f },   // 7: RightUpperArm
        new float[] { 145f, 15f, 15f },   // 8: LeftLowerArm
        new float[] { 145f, 15f, 15f },   // 9: RightLowerArm
        new float[] { 90f, 90f, 90f },    // 10: LeftHand
        new float[] { 90f, 90f, 90f },    // 11: RightHand
        new float[] { 120f, 45f, 30f },   // 12: LeftUpperLeg
        new float[] { 120f, 45f, 30f },   // 13: RightUpperLeg
        new float[] { 160f, 15f, 15f },   // 14: LeftLowerLeg
        new float[] { 160f, 15f, 15f },   // 15: RightLowerLeg
        new float[] { 45f, 30f, 30f },    // 16: LeftFoot
        new float[] { 45f, 30f, 30f },    // 17: RightFoot
    };

    static float[] GetJointLimits(int boneIndex)
    {
        if (boneIndex >= 0 && boneIndex < jointLimits.Length)
            return jointLimits[boneIndex];
        return new float[] { 90f, 90f, 90f };
    }
}