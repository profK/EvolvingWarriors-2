using UnityEngine;

/// <summary>
/// Encodes warrior observations into a fixed-length float array for MLP input.
/// 
/// Input layout (deterministic order):
///   [0..N]   Opponent bone observations (per bone: relative position 3 + rotation 4)
///   [N..N+6] Opponent root data (relative position 3 + forward 3 + distance 1)
///   [N+7..]  Self bone observations (per bone: rotation vs rest pose quaternion 4)
///   [end-8]  Time-varying signals (8 values: sin/cos pairs at 4 frequencies)
/// 
/// Total dimension is calculated automatically from the bone count plus time signals.
/// </summary>
public class WarriorObservationEncoder : MonoBehaviour
{
    WarriorBoneController boneController;
    Transform root;

    /// <summary>Number of time-varying signals appended to observations.</summary>
    const int TimeSignalCount = 8;

    /// <summary>Total input dimension. Computed from constants so it's available before Awake().</summary>
    public int InputDimension => WarriorBoneController.BoneCount * OpponentBoneObsSize + OpponentRootObsSize + WarriorBoneController.BoneCount * SelfBoneObsSize + TimeSignalCount;

    /// <summary>Per-bone observation size: 3 position + 4 rotation = 7.</summary>
    const int OpponentBoneObsSize = 7;

    /// <summary>Opponent root observation size: 3 position + 3 forward + 1 distance = 7.</summary>
    const int OpponentRootObsSize = 7;

    /// <summary>Per-bone self observation size: 4 quaternion = 4.</summary>
    const int SelfBoneObsSize = 4;

    /// <summary>Pre-allocated output buffer.</summary>
    float[] buffer;

    /// <summary>Opponent reference for observations.</summary>
    public Transform OpponentRoot
    {
        get => opponentRoot;
        set => opponentRoot = value;
    }
    Transform opponentRoot;

    void Awake()
    {
        boneController = GetComponent<WarriorBoneController>();
        root = transform;

        buffer = new float[InputDimension];
    }

    /// <summary>
    /// Encodes the current observations into the provided array.
    /// The array must have length >= InputDimension.
    /// </summary>
    public void Encode(float[] output)
    {
        if (output == null || output.Length < InputDimension)
        {
            Debug.LogError($"ObservationEncoder: Output array too small. Required: {InputDimension}");
            return;
        }
        if (opponentRoot == null)
        {
            // No opponent assigned yet — fill with zeros
            for (int i = 0; i < InputDimension; i++)
                output[i] = 0f;
            return;
        }

        int idx = 0;
        int boneCount = WarriorBoneController.BoneCount;

        // --- Opponent bone observations ---
        for (int i = 0; i < boneCount; i++)
        {
            Transform bone = boneController.GetBoneTransform(i);
            if (bone == null)
            {
                // Bone not resolved — fill zeros
                for (int j = 0; j < OpponentBoneObsSize; j++)
                    output[idx + j] = 0f;
                idx += OpponentBoneObsSize;
                continue;
            }

            // Relative position: opponent bone position in observer's root space
            Vector3 relPos = root.InverseTransformPoint(bone.position);
            output[idx] = relPos.x;
            output[idx + 1] = relPos.y;
            output[idx + 2] = relPos.z;

            // Relative rotation: opponent bone rotation in observer's root space
            Quaternion relRot = Quaternion.Inverse(root.rotation) * bone.rotation;
            output[idx + 3] = relRot.x;
            output[idx + 4] = relRot.y;
            output[idx + 5] = relRot.z;
            output[idx + 6] = relRot.w;

            idx += OpponentBoneObsSize;
        }

        // --- Opponent root data ---
        Vector3 oppRelPos = root.InverseTransformPoint(opponentRoot.position);
        output[idx] = oppRelPos.x;
        output[idx + 1] = oppRelPos.y;
        output[idx + 2] = oppRelPos.z;

        // Opponent forward direction in observer's root space
        Quaternion invRootRot = Quaternion.Inverse(root.rotation);
        Vector3 oppForward = invRootRot * opponentRoot.forward;
        output[idx + 3] = oppForward.x;
        output[idx + 4] = oppForward.y;
        output[idx + 5] = oppForward.z;

        // Distance to opponent
        output[idx + 6] = (oppRelPos.magnitude);

        idx += OpponentRootObsSize;

        // --- Self bone observations ---
        for (int i = 0; i < boneCount; i++)
        {
            Transform bone = boneController.GetBoneTransform(i);
            if (bone == null)
            {
                for (int j = 0; j < SelfBoneObsSize; j++)
                    output[idx + j] = 0f;
                idx += SelfBoneObsSize;
                continue;
            }

            // Current rotation relative to rest pose
            Quaternion restRot = boneController.GetRestRotation(i);
            Quaternion relativeRot = Quaternion.Inverse(restRot) * bone.localRotation;
            output[idx] = relativeRot.x;
            output[idx + 1] = relativeRot.y;
            output[idx + 2] = relativeRot.z;
            output[idx + 3] = relativeRot.w;

            idx += SelfBoneObsSize;
        }

        // --- Time-varying signals ---
        // Provides the MLP with a temporal signal so it can produce continuously
        // changing outputs even when the opponent is stationary. Without this, the
        // network converges to a single static pose and freezes.
        // 4 frequency bands: slow (0.5 Hz), medium (1 Hz), fast (2 Hz), very fast (4 Hz)
        // Each band provides sin and cos, giving the network phase information.
        float time = Time.time;
        output[idx] = Mathf.Sin(time * 0.5f * Mathf.PI * 2f);
        output[idx + 1] = Mathf.Cos(time * 0.5f * Mathf.PI * 2f);
        output[idx + 2] = Mathf.Sin(time * 1f * Mathf.PI * 2f);
        output[idx + 3] = Mathf.Cos(time * 1f * Mathf.PI * 2f);
        output[idx + 4] = Mathf.Sin(time * 2f * Mathf.PI * 2f);
        output[idx + 5] = Mathf.Cos(time * 2f * Mathf.PI * 2f);
        output[idx + 6] = Mathf.Sin(time * 4f * Mathf.PI * 2f);
        output[idx + 7] = Mathf.Cos(time * 4f * Mathf.PI * 2f);

        // Guard for NaN/Infinity
        for (int i = 0; i < InputDimension; i++)
        {
            if (float.IsNaN(output[i]) || float.IsInfinity(output[i]))
                output[i] = 0f;
        }
    }

    /// <summary>
    /// Returns a copy of the current observations.
    /// Allocates — use only when necessary.
    /// </summary>
    public float[] EncodeAllocated()
    {
        Encode(buffer);
        return (float[])buffer.Clone();
    }
}
