/// <summary>
/// Current phase of the training simulation lifecycle.
/// </summary>
public enum SimulationState
{
    /// <summary>Initializing the first generation (random genomes).</summary>
    Initializing,

    /// <summary>Warriors are actively fighting. Scores accumulate.</summary>
    Running,

    /// <summary>Round ended. Collecting final scores for evaluation.</summary>
    Evaluating,

    /// <summary>Running genetic evolution to produce next-generation genomes.</summary>
    Evolving,

    /// <summary>Resetting warriors with new genomes for the next round.</summary>
    Resetting,
}
