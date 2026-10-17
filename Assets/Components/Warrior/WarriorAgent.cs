using UnityEngine;

/// <summary>
/// Main orchestrator for a warrior agent.
/// 
/// In FixedUpdate: encodes observations, runs MLP inference, applies bone rotations.
/// Manages score, opponent reference, and active/inactive state.
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
    [Tooltip("Bone rotation controller.")]
    [SerializeField] WarriorBoneController boneController;

    [Tooltip("Observation encoder.")]
    [SerializeField] WarriorObservationEncoder observationEncoder;

    [Tooltip("Combat scorer.")]
    [SerializeField] WarriorCombatScorer combatScorer;

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

    void Awake()
    {
        // Auto-resolve component references
        if (boneController == null)
            boneController = GetComponent<WarriorBoneController>();
        if (observationEncoder == null)
            observationEncoder = GetComponent<WarriorObservationEncoder>();
        if (combatScorer == null)
            combatScorer = GetComponent<WarriorCombatScorer>();

        // Create MLP if not assigned
        if (neuralNetwork == null)
        {
            int inputDim = observationEncoder != null ? observationEncoder.InputDimension : 216;
            int outputDim = WarriorBoneController.OutputDimension;
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

        IsActive = false;
        Score = 0;
    }

    void FixedUpdate()
    {
        if (!IsActive) return;

        // 1. Encode observations
        observationEncoder.Encode(inputBuffer);

        // 2. Run MLP inference
        neuralNetwork.Forward(inputBuffer, outputBuffer);

        // 3. Apply bone rotations
        boneController.ApplyOutputs(outputBuffer);
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
    }

    /// <summary>
    /// Installs a new genome into this warrior's neural network.
    /// </summary>
    public void InstallGenome(Genome newGenome)
    {
        if (newGenome == null) throw new System.ArgumentNullException(nameof(newGenome));
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
