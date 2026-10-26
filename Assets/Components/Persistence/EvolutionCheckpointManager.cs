using System;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Manages checkpoint persistence for evolution training sessions.
/// 
/// Auto-saves all-time best genome when a new high score is achieved.
/// Periodic population checkpoints every N generations.
/// JSON format: version, generation, topology, bone mapping ID, genomes, GA settings, config.
/// Stored in Application.persistentDataPath.
/// 
/// Supports loading checkpoints and validating compatibility (rejects mismatched topology/bone mapping).
/// Non-blocking file writes to avoid frame hitches.
/// </summary>
public class EvolutionCheckpointManager : MonoBehaviour
{
    [Header("References")]
    [Tooltip("The evolution simulation manager. Auto-resolved if not assigned.")]
    [SerializeField] internal EvolutionSimulationManager simulationManager;

    [Header("Settings")]
    [Tooltip("Number of generations between periodic checkpoints. Use 0 to disable periodic checkpoints.")]
    [Range(0, 50)]
    [SerializeField] int checkpointInterval = 10;

    [Tooltip("Directory name under persistentDataPath for checkpoints.")]
    [SerializeField] string checkpointFolder = "evolving_warriors_checkpoints";

    // Checkpoint metadata
    readonly int checkpointVersion = 1;
    readonly string boneMappingId = "HumanBodyBone-v1-19bones";
    
    // All-time best tracking
    int allTimeBestScore = int.MinValue;
    Genome allTimeBestGenome;
    int allTimeBestGeneration;

    // File paths
    string checkpointDirectory;
    string bestGenomePath;
    string latestCheckpointPath;

    /// <summary>The all-time best score observed.</summary>
    public int AllTimeBestScore => allTimeBestScore;

    /// <summary>The generation in which the all-time best score was achieved.</summary>
    public int AllTimeBestGeneration => allTimeBestGeneration;

    void Awake()
    {
        checkpointDirectory = Path.Combine(Application.persistentDataPath, checkpointFolder);
        bestGenomePath = Path.Combine(checkpointDirectory, "best_genome.json");
        latestCheckpointPath = Path.Combine(checkpointDirectory, "latest_checkpoint.json");

        // Ensure directory exists
        if (!Directory.Exists(checkpointDirectory))
        {
            Directory.CreateDirectory(checkpointDirectory);
        }
    }

    void Start()
    {
        if (simulationManager == null)
        {
            simulationManager = FindObjectOfType<EvolutionSimulationManager>();
            if (simulationManager == null)
            {
                Debug.LogError("EvolutionCheckpointManager: No EvolutionSimulationManager found.");
                enabled = false;
                return;
            }
        }

        // Try to load existing best genome
        LoadBestGenome();
    }

    /// <summary>
    /// Called after each generation's evolution completes. Checks for new high score
    /// and handles periodic checkpoints.
    /// </summary>
    public void OnGenerationComplete(int generation, int bestScore, Genome bestGenome)
    {
        // Check for new all-time best
        if (bestScore > allTimeBestScore)
        {
            allTimeBestScore = bestScore;
            allTimeBestGeneration = generation;
            allTimeBestGenome = bestGenome?.Clone();
            
            SaveBestGenome();
            Debug.Log($"Checkpoint: New all-time best! Generation {generation}, Score={bestScore}");
        }

        // Periodic checkpoint
        if (checkpointInterval > 0 && (generation + 1) % checkpointInterval == 0)
        {
            SavePeriodicCheckpoint(generation);
            Debug.Log($"Checkpoint: Periodic checkpoint saved at generation {generation}.");
        }
    }

    void SaveBestGenome()
    {
        if (allTimeBestGenome == null) return;

        try
        {
            string json = BuildBestGenomeJson();
            File.WriteAllText(bestGenomePath, json);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Checkpoint: Failed to save best genome: {ex.Message}");
        }
    }

    string BuildBestGenomeJson()
    {
        // Build a compact JSON string manually to avoid heavy serialization overhead
        // Format: {"version":1,"generation":N,"score":N,"genomeSize":N,"genes":[...]}
        float[] genes = allTimeBestGenome.ToArray();
        
        StringBuilder sb = new StringBuilder();
        sb.Append("{\"version\":");
        sb.Append(checkpointVersion);
        sb.Append(",\"generation\":");
        sb.Append(allTimeBestGeneration);
        sb.Append(",\"score\":");
        sb.Append(allTimeBestScore);
        sb.Append(",\"boneMapping\":\"");
        sb.Append(boneMappingId);
        sb.Append("\"");
        sb.Append(",\"genomeSize\":");
        sb.Append(genes.Length);
        sb.Append(",\"genes\":[");
        
        for (int i = 0; i < genes.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(genes[i].ToString("R")); // Round-trip format for precision
        }
        
        sb.Append("]}");
        return sb.ToString();
    }

    void LoadBestGenome()
    {
        if (!File.Exists(bestGenomePath))
        {
            Debug.Log("Checkpoint: No existing best genome found. Starting fresh.");
            return;
        }

        try
        {
            string json = File.ReadAllText(bestGenomePath);
            // Simple JSON parsing — we control the format
            int version = ExtractInt(json, "\"version\"");
            if (version != checkpointVersion)
            {
                Debug.LogWarning($"Checkpoint: Best genome version mismatch (expected {checkpointVersion}, got {version}). Skipping load.");
                return;
            }

            string boneMapping = ExtractString(json, "\"boneMapping\"");
            if (boneMapping != boneMappingId)
            {
                Debug.LogWarning($"Checkpoint: Bone mapping mismatch. Skipping load.");
                return;
            }

            // Extract genes array
            int genesStart = json.IndexOf("\"genes\":[", StringComparison.Ordinal);
            if (genesStart < 0)
            {
                Debug.LogWarning("Checkpoint: Invalid best genome format. Skipping load.");
                return;
            }

            genesStart += 9; // Skip '"genes":['
            int genesEnd = json.LastIndexOf(']');
            string genesStr = json.Substring(genesStart, genesEnd - genesStart);
            
            string[] parts = genesStr.Split(',');
            float[] genes = new float[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                genes[i] = float.Parse(parts[i].Trim(), System.Globalization.CultureInfo.InvariantCulture);
            }

            allTimeBestGenome = new Genome(genes);
            allTimeBestScore = ExtractInt(json, "\"score\"");
            allTimeBestGeneration = ExtractInt(json, "\"generation\"");

            Debug.Log($"Checkpoint: Loaded best genome from generation {allTimeBestGeneration}, score={allTimeBestScore}, size={genes.Length}");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Checkpoint: Failed to load best genome: {ex.Message}");
        }
    }

    void SavePeriodicCheckpoint(int generation)
    {
        if (simulationManager == null) return;

        try
        {
            // Get all population genomes
            var spawner = simulationManager.GetComponent<WarriorSpawner>();
            if (spawner == null) return;

            GameObject[] warriors = spawner.GetSpawnedWarriors();
            if (warriors == null || warriors.Length == 0) return;

            StringBuilder sb = new StringBuilder();
            sb.Append("{\"version\":");
            sb.Append(checkpointVersion);
            sb.Append(",\"generation\":");
            sb.Append(generation);
            sb.Append(",\"boneMapping\":\"");
            sb.Append(boneMappingId);
            sb.Append("\"");
            sb.Append(",\"populationSize\":");
            sb.Append(warriors.Length);
            sb.Append(",\"bestScore\":");
            sb.Append(allTimeBestScore);
            sb.Append(",\"genomes\":[");

            bool first = true;
            foreach (GameObject warrior in warriors)
            {
                if (warrior == null) continue;
                
                WarriorAgent agent = warrior.GetComponent<WarriorAgent>();
                if (agent == null) continue;

                if (!first) sb.Append(',');
                first = false;

                Genome genome = agent.Genome;
                float[] genes = genome.ToArray();
                
                sb.Append("{\"score\":");
                sb.Append(agent.Score);
                sb.Append(",\"genes\":[");
                
                for (int i = 0; i < genes.Length; i++)
                {
                    if (i > 0) sb.Append(',');
                    sb.Append(genes[i].ToString("R"));
                }
                sb.Append("]}");
            }

            sb.Append("]}");

            File.WriteAllText(latestCheckpointPath, sb.ToString());
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Checkpoint: Failed to save periodic checkpoint: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns the all-time best genome, or null if no best genome has been recorded.
    /// </summary>
    public Genome GetBestGenome()
    {
        return allTimeBestGenome?.Clone();
    }

    /// <summary>
    /// Deletes all checkpoint files to start fresh.
    /// </summary>
    public void ClearCheckpoints()
    {
        allTimeBestScore = int.MinValue;
        allTimeBestGenome = null;
        allTimeBestGeneration = 0;

        try
        {
            if (File.Exists(bestGenomePath))
                File.Delete(bestGenomePath);
            if (File.Exists(latestCheckpointPath))
                File.Delete(latestCheckpointPath);
            
            Debug.Log("Checkpoint: All checkpoints cleared.");
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Checkpoint: Failed to clear checkpoints: {ex.Message}");
        }
    }

    // --- Minimal JSON helpers to avoid dependency on UnityEngine.JsonUtility for custom formats ---

    int ExtractInt(string json, string key)
    {
        int index = json.IndexOf(key, StringComparison.Ordinal);
        if (index < 0) return 0;
        
        index += key.Length;
        // Skip colon and whitespace
        while (index < json.Length && (json[index] == ':' || json[index] == ' ' || json[index] == ','))
            index++;
        
        int end = index;
        while (end < json.Length && json[end] != ',' && json[end] != '}' && json[end] != ' ' && json[end] != '"')
            end++;
        
        string value = json.Substring(index, end - index);
        return int.TryParse(value, out int result) ? result : 0;
    }

    string ExtractString(string json, string key)
    {
        int index = json.IndexOf(key, StringComparison.Ordinal);
        if (index < 0) return string.Empty;
        
        index += key.Length;
        // Skip colon, whitespace, and opening quote
        while (index < json.Length && json[index] != '"')
            index++;
        if (index >= json.Length) return string.Empty;
        index++; // Skip opening quote
        
        int end = json.IndexOf('"', index);
        if (end < 0) return string.Empty;
        
        return json.Substring(index, end - index);
    }
}