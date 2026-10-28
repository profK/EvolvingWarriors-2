using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Runtime HUD that displays evolution training statistics.
/// 
/// Top-center: generation number, highest score, average score, time remaining, simulation state
/// Mid-screen compact: pair count, population, lowest score
/// Previous generation statistics display (after generation completes)
/// Pause/Resume button
/// 
/// The HUD reads from EvolutionSimulationManager and population state.
/// It stops inference, animation, scoring, and timer when paused.
/// Resumes same round with preserved state.
/// </summary>
public class EvolutionHUD : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The evolution simulation manager to read state from. Auto-resolved if not assigned.")]
    [SerializeField] internal EvolutionSimulationManager simulationManager;

    [Header("Top Bar")]
    [Tooltip("Displays: Gen N | Best: X | Avg: Y.Y")]
    [SerializeField] internal TextMeshProUGUI generationText;

    [Tooltip("Displays: Best Score: N (used in top bar)")]
    [SerializeField] internal TextMeshProUGUI bestScoreText;

    [Tooltip("Displays: Average Score: X.X (used in top bar)")]
    [SerializeField] internal TextMeshProUGUI averageScoreText;

    [Tooltip("Displays: Time Remaining: X.Xs")]
    [SerializeField] internal TextMeshProUGUI timeRemainingText;

    [Tooltip("Displays: State (Running/Evaluating/Evolving/Resetting)")]
    [SerializeField] internal TextMeshProUGUI stateText;

    [Header("Stats Panel")]
    [Tooltip("Displays: Pairs: N")]
    [SerializeField] internal TextMeshProUGUI pairsText;

    [Tooltip("Displays: Population: N")]
    [SerializeField] internal TextMeshProUGUI populationText;

    [Tooltip("Displays: Worst Score: N")]
    [SerializeField] internal TextMeshProUGUI worstScoreText;

    [Header("Previous Generation")]
    [Tooltip("Displays previous generation stats after evolution completes.")]
    [SerializeField] internal TextMeshProUGUI previousGenText;

    [Header("Controls")]
    [Tooltip("Pause/Resume button.")]
    [SerializeField] internal Button pauseButton;

    [Tooltip("Text on the pause button.")]
    [SerializeField] internal TextMeshProUGUI pauseButtonText;

    [Tooltip("Reset button to restore warrior to rest pose.")]
    [SerializeField] internal Button resetButton;

    // Previous generation stats
    int prevGeneration;
    int prevBestScore;
    int prevWorstScore;
    float prevAverageScore;
    bool hasPrevGenStats;

    // Cached population reference to avoid repeated GetComponent calls
    WarriorAgent[] cachedPopulation;

    // Cached score stats shared between Update() and UpdateScoreStats()
    int cachedBest;
    float cachedAverage;
    bool hasCachedStats;

    void Start()
    {
        if (simulationManager == null)
        {
            simulationManager = FindObjectOfType<EvolutionSimulationManager>();
            if (simulationManager == null)
            {
                Debug.LogError("EvolutionHUD: No EvolutionSimulationManager found in scene.");
                enabled = false;
                return;
            }
        }

        if (pauseButton != null)
        {
            pauseButton.onClick.AddListener(() =>
            {
                simulationManager.TogglePause();
                UpdatePauseButtonText();
                // Directly freeze/unfreeze all warrior agents so pause stops motion
                foreach (var agent in FindObjectsOfType<WarriorAgent>())
                {
                    if (simulationManager.IsPaused)
                        agent.Deactivate();
                    else
                        agent.Activate();
                }
            });
        }

        if (resetButton != null)
        {
            resetButton.onClick.AddListener(() =>
            {
                foreach (var wbc in FindObjectsOfType<WarriorBoneController>())
                    wbc.ResetToRestPose();
            });
        }

        // Initialize static info
        if (pairsText != null)
            pairsText.text = $"Pairs: {simulationManager.PairCount}";

        if (populationText != null)
            populationText.text = $"Population: {simulationManager.PopulationSize}";

        // Clear previous generation display
        if (previousGenText != null)
            previousGenText.text = string.Empty;

        UpdatePauseButtonText();
    }

    public void PauseClicked()
    {
        simulationManager?.TogglePause();
        UpdatePauseButtonText();
        foreach (var agent in FindObjectsOfType<WarriorAgent>())
        {
            if (simulationManager != null && simulationManager.IsPaused)
                agent.Deactivate();
            else
                agent.Activate();
        }
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            simulationManager?.TogglePause();
            UpdatePauseButtonText();
            foreach (var agent in FindObjectsOfType<WarriorAgent>())
            {
                if (simulationManager != null && simulationManager.IsPaused)
                    agent.Deactivate();
                else
                    agent.Activate();
            }
        }

        if (Input.GetKeyDown(KeyCode.R))
        {
            foreach (var wbc in FindObjectsOfType<WarriorBoneController>())
                wbc.ResetToRestPose();
        }

        if (simulationManager == null) return;

        // Update live scores FIRST during running/evaluating states so cached values are current
        if (simulationManager.State == SimulationState.Running ||
            simulationManager.State == SimulationState.Evaluating)
        {
            UpdateScoreStats();
        }

        // Update generation and scores in top bar (uses up-to-date cached values)
        if (generationText != null)
        {
            if (hasCachedStats)
                generationText.text = $"Gen {simulationManager.Generation} | Best: {cachedBest} | Avg: {cachedAverage:F1}";
            else
                generationText.text = $"Generation: {simulationManager.Generation}";
        }

        // Update best and average score text fields using the same cached values
        if (bestScoreText != null)
        {
            if (hasCachedStats)
                bestScoreText.text = $"Best Score: {cachedBest}";
            else
                bestScoreText.text = "Best Score: --";
        }

        if (averageScoreText != null)
        {
            if (hasCachedStats)
                averageScoreText.text = $"Average Score: {cachedAverage:F1}";
            else
                averageScoreText.text = "Average Score: --";
        }

        // Update time remaining
        if (timeRemainingText != null)
        {
            if (simulationManager.State == SimulationState.Running)
                timeRemainingText.text = $"Time: {simulationManager.TimeRemaining:F1}s";
            else
                timeRemainingText.text = "Time: --";
        }

        // Update state
        if (stateText != null)
        {
            string stateStr = simulationManager.IsPaused ? "Paused" : simulationManager.State.ToString();
            stateText.text = $"State: {stateStr}";
        }

        // Clear previous gen display when a new round starts running
        if (simulationManager.State == SimulationState.Running && hasPrevGenStats)
        {
            hasPrevGenStats = false;
            if (previousGenText != null)
                previousGenText.text = string.Empty;
        }
    }

    void UpdateScoreStats()
    {
        // Get population from spawner if not cached
        if (cachedPopulation == null || cachedPopulation.Length == 0)
        {
            var spawner = simulationManager.GetComponent<WarriorSpawner>();
            if (spawner != null)
            {
                GameObject[] warriors = spawner.GetSpawnedWarriors();
                if (warriors != null && warriors.Length > 0)
                {
                    cachedPopulation = new WarriorAgent[warriors.Length];
                    for (int i = 0; i < warriors.Length; i++)
                    {
                        cachedPopulation[i] = warriors[i]?.GetComponent<WarriorAgent>();
                    }
                }
            }
        }

        if (cachedPopulation == null || cachedPopulation.Length == 0) return;

        int best = int.MinValue;
        int worst = int.MaxValue;
        int total = 0;
        int count = 0;

        foreach (WarriorAgent agent in cachedPopulation)
        {
            if (agent != null)
            {
                int score = agent.Score;
                total += score;
                if (score > best) best = score;
                if (score < worst) worst = score;
                count++;
            }
        }

        if (count == 0) return;

        cachedBest = best;
        cachedAverage = total / (float)count;
        hasCachedStats = true;

        // Update mid-screen worst score (best and average are displayed in top bar via Update())
        if (worstScoreText != null)
            worstScoreText.text = $"Worst: {worst}";

        // Store current stats as "previous" when evolution completes
        if (simulationManager.State == SimulationState.Evaluating)
        {
            prevGeneration = simulationManager.Generation;
            prevBestScore = best;
            prevWorstScore = worst;
            prevAverageScore = total / (float)count;
            hasPrevGenStats = true;
            UpdatePreviousGenDisplay();
        }
    }

    void UpdatePreviousGenDisplay()
    {
        if (previousGenText == null || !hasPrevGenStats) return;

        previousGenText.text = $"Gen {prevGeneration}: Best={prevBestScore}, Worst={prevWorstScore}, Avg={prevAverageScore:F1}";
    }

    void UpdatePauseButtonText()
    {
        if (pauseButtonText == null) return;

        if (simulationManager != null && simulationManager.IsPaused)
            pauseButtonText.text = "Resume";
        else
            pauseButtonText.text = "Pause";
    }

    /// <summary>
    /// Called when warriors are reset between rounds to refresh cached population.
    /// </summary>
    public void RefreshPopulationCache()
    {
        cachedPopulation = null;
    }
}
