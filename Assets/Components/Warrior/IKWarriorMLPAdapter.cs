using UnityEngine;

/// <summary>
/// Mode of control for the warrior.
/// </summary>
public enum WarriorControlMode
{
    /// <summary>
    /// Direct Bone Control (default): MLP outputs 57 bone rotations applied by WarriorBoneController.
    /// </summary>
    DirectBoneControl,

    /// <summary>
    /// Hand IK Control: MLP outputs 6 spherical-coordinate values for right and left hand positions applied by WarriorHandIKController.
    /// </summary>
    HandIKControl
}

/// <summary>
/// Adapts MLP outputs to the WarriorHandIKController.
/// </summary>
public class IKWarriorMLPAdapter : MonoBehaviour
{
    public const int OutputDimension = 6;

    [SerializeField] internal WarriorHandIKController handIKController;

    void Awake()
    {
        if (handIKController == null)
            handIKController = GetComponent<WarriorHandIKController>();
    }

    public void ApplyOutputs(float[] outputs)
    {
        if (handIKController != null)
        {
            handIKController.ApplyOutputs(outputs);
        }
    }
}
