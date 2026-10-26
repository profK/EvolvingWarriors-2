using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Central state machine for the training simulation.
/// 
/// Lifecycle: Initializing → Running → Evaluating → Evolving → Resetting → (loop back to Running)
/// 
/// On scene start:
/// - Spawns warrior pairs from the prefab
/// - Randomizes initial genomes (generation 0)
/// - Activates all warriors and starts the first round
/// 
/// Each round:
/// - Warriors fight for the configured duration
/// - Scores are collected
/// - Genetic evolution produces new genomes
/// - Warriors receive new genomes and reset for the next round
/// </summary>
public class EvolutionSimulationManager : MonoBehaviour
{
    [Header("Population")]
    [Tooltip("The warrior prefab to instantiate. Must have WarriorAgent, WarriorBoneController, and WarriorObservationEncoder components.")]
    [SerializeField] GameObject warriorPrefab;

    [Tooltip("Number of warrior pairs to spawn (total warriors = pairs × 2).")]
    [SerializeField] int pairCount = 4;

    [Tooltip("Distance between the two warriors in each pair (sword-length ≈ 1.5m).")]
    [Range(0.5f, 3f)]
    [SerializeField] float pairSeparation = 1.5f;

    [Tooltip("Distance between adjacent pairs in the grid.")]
    [Range(3f, 15f)]
    [SerializeField] float interPairDistance = 6f;

    [Header("Simulation")]
    [Tooltip("Duration of each round in seconds.")]
    [Range(5f, 120f)]
    [SerializeField] float roundDuration = 30f;

    [Tooltip("Maximum angular rotation speed for bone controller (degrees per second).")]
    [Range(30f, 360f)]
    [SerializeField] float maxAngularSpeed = 180f;

    [Tooltip("Random seed for reproducible evolution. Use -1 for random seed.")]
    [SerializeField] long randomSeed = -1L;

    [Header("Genetic Algorithm")]
    [Tooltip("Fraction of top performers preserved unchanged (0.0–1.0).")]
    [Range(0f, 1f)]
    [SerializeField] float eliteFraction = 0.10f;

    [Tooltip("Number of individuals sampled per tournament selection.")]
    [Range(2, 10)]
    [SerializeField] int tournamentSize = 3;

    [Tooltip("Probability of swapping each gene during crossover (0.0–1.0).")]
    [Range(0f, 1f)]
    [SerializeField] float crossoverProbability = 0.70f;

    [Tooltip("Probability of mutating each gene (0.0–1.0).")]
    [Range(0f, 1f)]
    [SerializeField] float mutationProbability = 0.02f;

    [Tooltip("Standard deviation of Gaussian mutation.")]
    [Range(0.01f, 1f)]
    [SerializeField] float mutationStrength = 0.10f;

    [Header("Network")]
    [Tooltip("Number of hidden layers.")]
    [Range(1, 4)]
    [SerializeField] int hiddenLayerCount = 2;

    [Tooltip("Neurons per hidden layer.")]
    [Range(16, 256)]
    [SerializeField] int neuronsPerLayer = 128;

    /// <summary>Current simulation state.</summary>
    public SimulationState State { get; private set; }

    /// <summary>Current generation number (0-based).</summary>
    public int Generation { get; private set; }

    /// <summary>Time remaining in the current round.</summary>
    public float TimeRemaining { get; private set; }

    /// <summary>Number of warrior pairs in the population.</summary>
    public int PairCount => pairCount;

    /// <summary>Total number of warriors (pairCount × 2).</summary>
    public int PopulationSize => pairCount * 2;

    /// <summary>Whether the simulation is currently paused.</summary>
    public bool IsPaused { get; private set; }

    // Internal state
    WarriorSpawner spawner;
    WarriorAgent[] population;
    System.Random rng;
    float roundTimer;
    bool isPaused;
    EvolutionCheckpointManager checkpointManager;

    void Awake()
    {
        // After a script recompile during play mode, destroy any stale warriors from the
        // previous compilation so they don't coexist with freshly spawned ones.
        // Use FindObjectsOfType because the spawner's spawnedWarriors array is reset
        // when the spawner component re-awakens after a recompile.
        // DestroyImmediate is used here because we're in Awake() before any coroutines run;
        // regular Destroy() would defer until end of frame, allowing stale warriors'
        // FixedUpdate to still execute before they're actually removed.
        WarriorAgent[] staleAgents = FindObjectsOfType<WarriorAgent>();
        foreach (WarriorAgent agent in staleAgents)
        {
            if (agent != null)
                DestroyImmediate(agent.gameObject, false);
        }

        // Initialize RNG
        int seed = (randomSeed < 0) ? UnityEngine.Random.Range(0, int.MaxValue) : (int)randomSeed;
        rng = new System.Random(seed);
        Debug.Log($"EvolutionSimulationManager: RNG seed = {seed}");

        // Set up spawner
        spawner = GetComponent<WarriorSpawner>();
        if (spawner == null)
        {
            Debug.LogError("EvolutionSimulationManager requires a WarriorSpawner component.");
            enabled = false;
            return;
        }

        // Set up checkpoint manager (optional)
        checkpointManager = GetComponent<EvolutionCheckpointManager>();

        // Configure spawner
        ConfigureSpawner();

        // Validate configuration
        ValidateConfiguration();

        // Start the simulation
        State = SimulationState.Initializing;
        Generation = 0;
        StartCoroutine(InitializeFirstGeneration());
    }

    void ConfigureSpawner()
    {
        spawner.WarriorPrefab = warriorPrefab;
        spawner.PairCount = pairCount;
        spawner.PairSeparation = pairSeparation;
        spawner.InterPairDistance = interPairDistance;
    }

    void ConfigureAgent(WarriorAgent agent)
    {
        // Set bone controller angular speed
        if (agent.GetComponent<WarriorBoneController>() != null)
        {
            agent.GetComponent<WarriorBoneController>().MaxAngularSpeed = maxAngularSpeed;
        }
        
        // If the agent doesn't have an MLP yet, create one with the configured topology
        // (WarriorAgent.Awake() auto-creates one, but we use the configured hidden layer settings)
        // For now the agent's Awake() handles MLP creation; the network config fields
        // are available for future use when we need custom topology per generation.
    }

    void ValidateConfiguration()
    {
        if (pairCount < 1)
        {
            Debug.LogWarning("EvolutionSimulationManager: pairCount < 1, setting to 1.");
            pairCount = 1;
        }
        if (roundDuration <= 0)
        {
            Debug.LogWarning("EvolutionSimulationManager: roundDuration must be positive.");
            roundDuration = 30f;
        }
    }

    IEnumerator InitializeFirstGeneration()
    {
        // Spawn warriors
        GameObject[] warriors = spawner.SpawnWarriors();
        population = new WarriorAgent[warriors.Length];

        for (int i = 0; i < warriors.Length; i++)
        {
            WarriorAgent agent = warriors[i].GetComponent<WarriorAgent>();
            if (agent == null)
            {
                Debug.LogError($"Warrior at index {i} has no WarriorAgent component. Ensure the prefab includes it.");
                yield break;
            }

            // Configure the agent with the configured network topology
            ConfigureAgent(agent);
            agent.Randomize();
            population[i] = agent;
        }

        // Wire up opponent references — each pair knows each other
        WireOpponents();

        // Enable scoring
        EnableScoring();

        // Start the first round
        State = SimulationState.Running;
        roundTimer = roundDuration;
        TimeRemaining = roundDuration;

        // Activate all warriors
        ActivateAll();

        Debug.Log($"Generation {Generation}: {population.Length} warriors spawned and activated.");
    }

    void WireOpponents()
    {
        // Each pair: even index ↔ odd index
        for (int i = 0; i < population.Length; i += 2)
        {
            if (i + 1 < population.Length)
            {
                population[i].Opponent = population[i + 1];
                population[i + 1].Opponent = population[i];
            }
        }
    }

    void ActivateAll()
    {
        foreach (WarriorAgent agent in population)
        {
            if (agent != null)
                agent.Activate();
        }
    }

    void DeactivateAll()
    {
        foreach (WarriorAgent agent in population)
        {
            if (agent != null)
                agent.Deactivate();
        }
    }

    void EnableScoring()
    {
        foreach (WarriorAgent agent in population)
        {
            if (agent != null)
            {
                var scorer = agent.GetComponent<WarriorCombatScorer>();
                if (scorer != null)
                    scorer.EnableScoring();
            }
        }
    }

    void DisableScoring()
    {
        foreach (WarriorAgent agent in population)
        {
            if (agent != null)
            {
                var scorer = agent.GetComponent<WarriorCombatScorer>();
                if (scorer != null)
                    scorer.DisableScoring();
            }
        }
    }

    void Update()
    {
        if (State != SimulationState.Running || isPaused)
            return;

        // Countdown timer
        roundTimer -= Time.deltaTime;
        TimeRemaining = roundTimer;

        if (roundTimer <= 0)
        {
            TimeRemaining = 0f;
            StartCoroutine(EvaluateAndEvolve());
        }
    }

    /// <summary>
    /// Pauses the simulation. Stops timer countdown, disables warrior inference,
    /// and disables scoring. Round state is preserved.
    /// </summary>
    public void Pause()
    {
        if (isPaused) return;
        isPaused = true;
        IsPaused = true;

        // Deactivate warriors so inference stops
        DeactivateAll();
        DisableScoring();

        Debug.Log("Simulation paused.");
    }

    /// <summary>
    /// Resumes the simulation from the paused state. Restores timer,
    /// re-enables warrior inference and scoring.
    /// </summary>
    public void Resume()
    {
        if (!isPaused) return;
        isPaused = false;
        IsPaused = false;

        // Re-activate warriors and scoring
        ActivateAll();
        EnableScoring();

        Debug.Log("Simulation resumed.");
    }

    /// <summary>
    /// Toggles the simulation pause state.
    /// </summary>
    public void TogglePause()
    {
        if (isPaused)
            Resume();
        else
            Pause();
    }

    IEnumerator EvaluateAndEvolve()
    {
        State = SimulationState.Evaluating;
        Debug.Log($"Generation {Generation}: Round ended. Evaluating scores...");

        // Deactivate all warriors
        DeactivateAll();
        DisableScoring();

        // Log scores
        int bestScore = int.MinValue;
        int worstScore = int.MaxValue;
        int totalScore = 0;
        foreach (WarriorAgent agent in population)
        {
            if (agent != null)
            {
                totalScore += agent.Score;
                if (agent.Score > bestScore) bestScore = agent.Score;
                if (agent.Score < worstScore) worstScore = agent.Score;
            }
        }
        Debug.Log($"Generation {Generation}: Best={bestScore}, Worst={worstScore}, Avg={totalScore / (float)population.Length:F1}");

        // Find the best genome for checkpointing
        WarriorAgent bestAgent = null;
        foreach (WarriorAgent agent in population)
        {
            if (agent != null && agent.Score == bestScore)
            {
                bestAgent = agent;
                break;
            }
        }

        // Notify checkpoint manager
        if (checkpointManager != null)
        {
            checkpointManager.OnGenerationComplete(Generation, bestScore, bestAgent?.Genome);
        }

        // Run genetic evolution
        State = SimulationState.Evolving;
        Genome[] nextGenomes = GeneticEvolution.NextGeneration(
            population,
            eliteFraction,
            tournamentSize,
            crossoverProbability,
            mutationProbability,
            mutationStrength,
            rng);

        if (nextGenomes == null)
        {
            Debug.LogError("EvolutionSimulationManager: Evolution failed. Stopping simulation.");
            State = SimulationState.Initializing; // Dead state
            yield break;
        }

        // Reset and assign new genomes
        State = SimulationState.Resetting;
        Generation++;

        for (int i = 0; i < population.Length; i++)
        {
            if (population[i] != null)
            {
                population[i].ResetRound();
                population[i].InstallGenome(nextGenomes[i]);
            }
        }

        // Re-enable scoring and activate for next round
        EnableScoring();
        ActivateAll();

        State = SimulationState.Running;
        roundTimer = roundDuration;
        TimeRemaining = roundDuration;

        Debug.Log($"Generation {Generation}: New genomes installed. Round starting.");
        yield return null;
    }
}