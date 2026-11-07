using UnityEngine;

/// <summary>
/// Main orchestrator for a warrior agent.
/// 
/// In FixedUpdate: encodes observations, runs MLP inference, applies bone rotations,
/// then runs IK correction for foot grounding. Manages score, opponent reference,
/// and active/inactive state.
/// </summary>
public class WarriorAgent : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The warrior's neural network.")]
    [SerializeField] MLP neuralNetwork;

    [Tooltip("The warrior's genome (trainable parameters).")]
    [SerializeField] Genome genome;

    [Tooltip("The warrior's assigned opponent.")]
    [SerializeField] WarriorAgent opponent;

    [Header("Components")]
    [Tooltip("Control mode for this warrior.")]
    [SerializeField] WarriorControlMode controlMode = WarriorControlMode.DirectBoneControl;

    [Tooltip("Bone rotation controller.")]
    [SerializeField] WarriorBoneController boneController;

    [Tooltip("Hand IK adapter (used when controlMode is HandIKControl).")]
    [SerializeField] IKWarriorMLPAdapter handIKAdapter;

    [Tooltip("Observation encoder.")]
    [SerializeField] WarriorObservationEncoder observationEncoder;

    [Tooltip("Combat scorer.")]
    [SerializeField] WarriorCombatScorer combatScorer;

    [Tooltip("Pose solver for foot IK and grounding.")]
    [SerializeField] WarriorPoseSolver poseSolver;

    /// <summary>Control mode of this warrior.</summary>
    public WarriorControlMode ControlMode
    {
        get => controlMode;
        set => controlMode = value;
    }

    /// <summary>The warrior's current score for this round.</summary>
    public int Score { get; private set; }

    /// <summary>Whether this agent is actively running inference and animation.</summary>
    public bool IsActive { get; private set; }

    /// <summary>The warrior's assigned opponent.</summary>
    public WarriorAgent Opponent
    {
        get => opponent;
        set
        {
            opponent = value;
            if (observationEncoder != null)
                observationEncoder.OpponentRoot = value?.transform;
        }
    }

    /// <summary>The warrior's genome.</summary>
    public Genome Genome => genome;

    /// <summary>The warrior's neural network.</summary>
    public MLP NeuralNetwork => neuralNetwork;

    // Pre-allocated buffers for inference
    float[] inputBuffer;
    float[] outputBuffer;
    Vector3 spawnPosition; // Captured at spawn time to lock root X,Z

    void Awake()
    {
        // Auto-resolve component references
        if (boneController == null)
            boneController = GetComponent<WarriorBoneController>();
        if (handIKAdapter == null)
            handIKAdapter = GetComponent<IKWarriorMLPAdapter>();
        if (observationEncoder == null)
            observationEncoder = GetComponent<WarriorObservationEncoder>();
        if (combatScorer == null)
            combatScorer = GetComponent<WarriorCombatScorer>();

        // Auto-detect control mode: if explicitly set to HandIKControl, or if
        // IK adapter is present without a bone controller (fallback for manual setup).
        // When both bone controller and IK adapter are present (as on the IK prefab),
        // the explicitly serialized controlMode determines the mode.
        if (controlMode == WarriorControlMode.HandIKControl || (handIKAdapter != null && boneController == null))
        {
            controlMode = WarriorControlMode.HandIKControl;
        }

        // Tell the bone controller to skip hips/leg position locks in IK mode.
        // The pose solver has full authority over those bones.
        if (boneController != null)
            boneController.IKMode = (controlMode == WarriorControlMode.HandIKControl);

        // Capture spawn position for root locking (used in HandIKControl mode)
        spawnPosition = transform.position;

        // Create MLP if not assigned
        if (neuralNetwork == null)
        {
            int inputDim = observationEncoder != null ? observationEncoder.InputDimension : WarriorBoneController.BoneCount * 7 + 7 + WarriorBoneController.BoneCount * 4 + 8;
            int outputDim = (controlMode == WarriorControlMode.HandIKControl) ? IKWarriorMLPAdapter.OutputDimension : WarriorBoneController.OutputDimension;
            neuralNetwork = new MLP(inputDim, new int[] { 128, 64 }, outputDim);
        }

        // Create genome from MLP if not assigned
        if (genome == null)
        {
            genome = new Genome(neuralNetwork.ExportGenome());
        }

        // Allocate inference buffers
        inputBuffer = new float[neuralNetwork.InputSize];
        outputBuffer = new float[neuralNetwork.OutputSize];

        // Link scorer back to this agent
        if (combatScorer != null)
            combatScorer.Agent = this;

        // Auto-resolve pose solver
        if (poseSolver == null)
            poseSolver = GetComponent<WarriorPoseSolver>();

        IsActive = false;
        Score = 0;
    }

    void FixedUpdate()
    {
        if (!IsActive) return;

        // DEBUG: Increment frame counter every FixedUpdate so logging works
        debugFrameCounter++;

        // 1. Encode observations
        observationEncoder.Encode(inputBuffer);

        // 2. Run MLP inference
        neuralNetwork.Forward(inputBuffer, outputBuffer);

        // 3. Apply outputs according to control mode
        if (controlMode == WarriorControlMode.HandIKControl && handIKAdapter != null)
        {
            handIKAdapter.ApplyOutputs(outputBuffer);

            // In IK mode, the bone controller's ApplyOutputs is not called, so we must
            // lock the root X,Z ourselves (prevents drift from pose solver balance corrections).
            if (boneController != null)
                LockRootPosition();

            // DEBUG: Log AFTER ApplyOutputs so we see the updated targets
            if (debugFrameCounter % 60 == 0) DebugIKOutputs();
        }
        else if (boneController != null)
        {
            boneController.ApplyOutputs(outputBuffer);

            // DEBUG: Log AFTER ApplyOutputs
            if (debugFrameCounter % 60 == 0) DebugBoneOutputs();
        }

        // 4. Apply IK correction for foot grounding
        if (poseSolver != null)
            poseSolver.Solve();
    }

    /// <summary>
    /// Locks the root transform's X and Z to spawn position, preserving Y.
    /// Called in HandIKControl mode since boneController.ApplyOutputs (which does this)
    /// is bypassed in that mode.
    /// </summary>
    void LockRootPosition()
    {
        Vector3 pos = transform.position;
        pos.x = spawnPosition.x;
        pos.z = spawnPosition.z;
        transform.position = pos;
    }

    // DEBUG: Frame counter incremented every FixedUpdate
    int debugFrameCounter = 0;

    void DebugIKOutputs()
    {
        if (outputBuffer == null || outputBuffer.Length < 6) return;

        var sb = new System.Text.StringBuilder();
        sb.Append($"[IK #{name}] MLP=[rR={outputBuffer[0]:F1} azR={outputBuffer[1]:F1} elR={outputBuffer[2]:F1} | rL={outputBuffer[3]:F1} azL={outputBuffer[4]:F1} elL={outputBuffer[5]:F1}] ");

        if (handIKAdapter != null && handIKAdapter.handIKController != null)
        {
            var ikCtrl = handIKAdapter.handIKController;
            var t = ikCtrl.CurrentTargets;
            sb.Append($"RTgt={t.rightWorldPos:F2} LTgt={t.leftWorldPos:F2} ");

            if (ikCtrl.rightHand != null) sb.Append($"RHand={ikCtrl.rightHand.position:F2} ");
            if (ikCtrl.leftHand != null) sb.Append($"LHand={ikCtrl.leftHand.position:F2} ");
            if (ikCtrl.modelRoot != null) sb.Append($"Root={ikCtrl.modelRoot.position:F2} ");
            sb.Append($"ArmLens=({ikCtrl.rightArmLength:F2},{ikCtrl.leftArmLength:F2})");
        }

        if (boneController != null)
        {
            var hip = boneController.GetBoneTransform(0);
            if (hip != null) sb.Append($" Hips={hip.position:F2}");
        }

        Debug.Log(sb.ToString());
    }

    void DebugBoneOutputs()
    {
        if (outputBuffer == null || outputBuffer.Length < 57) return;

        var sb = new System.Text.StringBuilder();
        sb.Append($"[BONE #{name}] ");
        string[] boneNames = { "LUpArm", "RUpArm", "LLowArm", "RLowArm" };
        for (int i = 0; i < 4; i++)
        {
            int baseIdx = 6 + i; // indices 6,7,8,9
            if (i > 0) sb.Append(" ");
            sb.Append($"{boneNames[i]}=({outputBuffer[baseIdx]:F1},{outputBuffer[baseIdx+1]:F1},{outputBuffer[baseIdx+2]:F1})");
        }

        if (boneController != null)
        {
            var hip = boneController.GetBoneTransform(0);
            if (hip != null) sb.Append($" Hips={hip.position:F2}");
        }

        Debug.Log(sb.ToString());
    }

    /// <summary>
    /// Activates the agent for combat. Enables inference and animation.
    /// </summary>
    public void Activate()
    {
        IsActive = true;
    }

    /// <summary>
    /// Deactivates the agent. Stops inference and animation.
    /// </summary>
    public void Deactivate()
    {
        IsActive = false;
    }

    /// <summary>
    /// Resets the agent for a new round.
    /// </summary>
    public void ResetRound()
    {
        Score = 0;
        if (boneController != null)
            boneController.ResetToRestPose();

        // Reset foot IK state
        if (poseSolver != null)
            poseSolver.Reset();
    }

    /// <summary>
    /// Installs a new genome into this warrior's neural network.
    /// </summary>
    public void InstallGenome(Genome newGenome)
    {
        if (newGenome == null) throw new System.ArgumentNullException(nameof(newGenome));
        if (neuralNetwork != null && newGenome.Size != neuralNetwork.GenomeSize)
        {
            throw new System.ArgumentException($"Genome size {newGenome.Size} does not match network genome size {neuralNetwork.GenomeSize}.", nameof(newGenome));
        }
        genome = newGenome;
        neuralNetwork.ImportGenome(newGenome.ToArray());
    }

    /// <summary>
    /// Randomizes this warrior's neural network weights.
    /// </summary>
    public void Randomize()
    {
        neuralNetwork.InitializeRandom();
        genome = new Genome(neuralNetwork.ExportGenome());
    }

    /// <summary>
    /// Adds to this warrior's score. Called by the combat scorer.
    /// </summary>
    public void AddScore(int delta)
    {
        Score += delta;
    }
}
