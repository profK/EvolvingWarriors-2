using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests for simulation lifecycle logic used by EvolutionSimulationManager.
/// Note: EvolutionSimulationManager is a MonoBehaviour and cannot be instantiated in edit mode.
/// We test the core lifecycle operations using standalone helpers that mirror the actual algorithms.
/// </summary>
[TestFixture]
public class LifecycleTests
{
    // --- Mock agent for lifecycle tests ---

    class MockAgent
    {
        public int Score { get; internal set; }
        public MockAgent Opponent { get; set; }
        public Genome Genome { get; internal set; }
        public bool IsActive { get; internal set; }
        public string Name;
        public HashSet<string> ActiveContacts { get; private set; } = new HashSet<string>();

        public MockAgent(Genome genome, string name)
        {
            this.Genome = genome;
            this.Name = name;
            Score = 0;
            IsActive = false;
        }
        public void AddScore(int delta) => Score += delta;
        public void InstallGenome(Genome newGenome)
        {
            if (newGenome == null) throw new ArgumentNullException(nameof(newGenome));
            Genome = newGenome;
        }
        public void ResetRound()
        {
            Score = 0;
            ActiveContacts.Clear();
        }
        public void Activate() => IsActive = true;
        public void Deactivate() => IsActive = false;
    }

    // --- Mock scorer for contact state tests ---

    class MockScorer
    {
        readonly MockAgent agent;
        readonly HashSet<string> activeContacts = new HashSet<string>();
        bool scoringEnabled = true;

        public MockScorer(MockAgent agent) => this.agent = agent;
        public void EnableScoring() => scoringEnabled = true;
        public void DisableScoring()
        {
            scoringEnabled = false;
            activeContacts.Clear();
        }
        public void OnContactBegin(string surfaceId, MockAgent owner)
        {
            if (!scoringEnabled) return;
            if (activeContacts.Contains(surfaceId)) return;
            activeContacts.Add(surfaceId);
            if (owner == agent) agent.AddScore(-1);
            else if (owner == agent.Opponent) agent.AddScore(1);
        }
        public void OnContactEnd(string surfaceId) => activeContacts.Remove(surfaceId);
        public int ActiveContactCount => activeContacts.Count;
    }

    // --- Simulation state machine (mirrors EvolutionSimulationManager) ---

    enum State { Initializing, Running, Evaluating, Evolving, Resetting }

    class MockSimulation
    {
        public State CurrentState { get; internal set; }
        public int Generation { get; internal set; }
        public float TimeRemaining { get; internal set; }
        public bool IsPaused { get; internal set; }

        readonly MockAgent[] population;
        readonly MockScorer[] scorers;
        readonly float roundDuration;
        float roundTimer;
        System.Random rng;

        public MockSimulation(int pairCount, int genomeSize, float roundDuration, System.Random rng)
        {
            this.roundDuration = roundDuration;
            this.rng = rng;
            int total = pairCount * 2;
            population = new MockAgent[total];
            scorers = new MockScorer[total];
            for (int i = 0; i < total; i++)
            {
                population[i] = new MockAgent(new Genome(genomeSize), $"Warrior_{i}");
                scorers[i] = new MockScorer(population[i]);
            }
            // Wire opponents
            for (int i = 0; i < total; i += 2)
            {
                if (i + 1 < total)
                {
                    population[i].Opponent = population[i + 1];
                    population[i + 1].Opponent = population[i];
                }
            }
            CurrentState = State.Initializing;
            Generation = 0;
        }

        public void InitializeAndStart()
        {
            // Mirrors InitializeFirstGeneration
            foreach (var agent in population)
            {
                // Randomize would happen here; skip for determinism
                agent.Genome = new Genome(50);
            }
            foreach (var scorer in scorers)
                scorer.EnableScoring();
            foreach (var agent in population)
                agent.Activate();

            CurrentState = State.Running;
            roundTimer = roundDuration;
            TimeRemaining = roundDuration;
        }

        public void Tick(float deltaTime)
        {
            if (CurrentState != State.Running || IsPaused) return;
            roundTimer -= deltaTime;
            TimeRemaining = roundTimer;
            if (roundTimer <= 0)
            {
                TimeRemaining = 0f;
                EvaluateAndEvolve();
            }
        }

        void EvaluateAndEvolve()
        {
            CurrentState = State.Evaluating;
            // Deactivate all
            foreach (var agent in population)
                agent.Deactivate();
            foreach (var scorer in scorers)
                scorer.DisableScoring();

            // Run evolution
            CurrentState = State.Evolving;
            Genome[] genomes = new Genome[population.Length];
            int[] scores = new int[population.Length];
            for (int i = 0; i < population.Length; i++)
            {
                genomes[i] = population[i].Genome;
                scores[i] = population[i].Score;
            }
            Genome[] nextGenomes = GeneticEvolutionTests.NextGeneration(
                genomes, scores, 0.1f, 3, 0.7f, 0.02f, 0.1f, rng);

            // Reset and install
            CurrentState = State.Resetting;
            Generation++;
            for (int i = 0; i < population.Length; i++)
            {
                population[i].ResetRound();
                population[i].InstallGenome(nextGenomes[i]);
            }

            // Re-enable for next round
            foreach (var scorer in scorers)
                scorer.EnableScoring();
            foreach (var agent in population)
                agent.Activate();

            CurrentState = State.Running;
            roundTimer = roundDuration;
            TimeRemaining = roundDuration;
        }

        public void Pause()
        {
            if (IsPaused) return;
            IsPaused = true;
            foreach (var agent in population)
                agent.Deactivate();
            foreach (var scorer in scorers)
                scorer.DisableScoring();
        }

        public void Resume()
        {
            if (!IsPaused) return;
            IsPaused = false;
            foreach (var agent in population)
                agent.Activate();
            foreach (var scorer in scorers)
                scorer.EnableScoring();
        }

        public MockAgent[] GetPopulation() => population;
        public MockScorer[] GetScorers() => scorers;
    }

    // --- Test 8: RoundsEndAtConfiguredDuration ---
    [Test]
    public void RoundsEndAtConfiguredDuration()
    {
        float duration = 30f;
        var sim = new MockSimulation(3, 50, duration, new System.Random(42));
        sim.InitializeAndStart();

        // Tick until time runs out
        sim.Tick(29.9f);
        Assert.Greater(sim.TimeRemaining, 0f, "Time should not have reached 0 yet.");
        Assert.AreEqual(State.Running, sim.CurrentState, "Should still be running.");

        sim.Tick(0.1f);
        Assert.AreEqual(0f, sim.TimeRemaining, 0.01f, "Time should reach 0 at round end.");
        Assert.AreEqual(State.Running, sim.CurrentState,
            "After EvaluateAndEvolve completes, state should return to Running.");
    }

    // --- Test 9: GenerationTransitionsOccurOnce ---
    [Test]
    public void GenerationTransitionsOccurOnce()
    {
        float duration = 30f;
        var sim = new MockSimulation(3, 50, duration, new System.Random(42));
        sim.InitializeAndStart();
        Assert.AreEqual(0, sim.Generation, "First generation should be 0.");

        // Assign scores so evolution has meaningful data
        for (int i = 0; i < sim.GetPopulation().Length; i++)
            sim.GetPopulation()[i].Score = i * 10;

        sim.Tick(duration); // Trigger round end
        Assert.AreEqual(1, sim.Generation, "After first round, generation should be 1.");

        // Assign new scores and tick again
        for (int i = 0; i < sim.GetPopulation().Length; i++)
            sim.GetPopulation()[i].Score = i * 5;

        sim.Tick(duration); // Trigger second round end
        Assert.AreEqual(2, sim.Generation, "After second round, generation should be 2.");
    }

    // --- Test 10: ScoresResetBetweenGenerations ---
    [Test]
    public void ScoresResetBetweenGenerations()
    {
        float duration = 30f;
        var sim = new MockSimulation(3, 50, duration, new System.Random(42));
        sim.InitializeAndStart();

        // Accumulate scores during the round
        foreach (var agent in sim.GetPopulation())
            agent.AddScore(5);

        sim.Tick(duration); // Trigger round end and evolution

        foreach (var agent in sim.GetPopulation())
        {
            Assert.AreEqual(0, agent.Score,
                $"{agent.Name} score should be 0 at start of new generation.");
        }
    }

    // --- Test 11: ContactStateResetsCorrectly ---
    [Test]
    public void ContactStateResetsCorrectly()
    {
        float duration = 30f;
        var sim = new MockSimulation(3, 50, duration, new System.Random(42));
        sim.InitializeAndStart();

        // Simulate some contacts during the round
        var scorers = sim.GetScorers();
        var population = sim.GetPopulation();
        scorers[0].OnContactBegin("head", population[1]);
        scorers[1].OnContactBegin("torso", population[0]);

        Assert.AreEqual(1, scorers[0].ActiveContactCount, "Scorer 0 should have 1 active contact.");
        Assert.AreEqual(1, scorers[1].ActiveContactCount, "Scorer 1 should have 1 active contact.");

        sim.Tick(duration); // Trigger round end and evolution

        // After reset, all contact state should be cleared
        foreach (var scorer in scorers)
        {
            Assert.AreEqual(0, scorer.ActiveContactCount,
                "Active contacts should be cleared after round reset.");
        }
    }

    // --- Test 12: PausingPreservesRoundState ---
    [Test]
    public void PausingPreservesRoundState()
    {
        float duration = 30f;
        var sim = new MockSimulation(3, 50, duration, new System.Random(42));
        sim.InitializeAndStart();

        // Tick partway through the round
        sim.Tick(15f);
        float timeBeforePause = sim.TimeRemaining;

        // Accumulate some scores
        foreach (var agent in sim.GetPopulation())
            agent.AddScore(3);

        sim.Pause();
        Assert.IsTrue(sim.IsPaused, "Simulation should be paused.");

        // While paused, timer should not advance
        sim.Tick(5f);
        Assert.AreEqual(timeBeforePause, sim.TimeRemaining,
            "Timer should not advance while paused.");

        // Scores should be preserved
        int expectedScore = 3;
        foreach (var agent in sim.GetPopulation())
        {
            Assert.AreEqual(expectedScore, agent.Score,
                $"{agent.Name} score should be preserved while paused.");
        }

        // Warriors should be deactivated while paused
        foreach (var agent in sim.GetPopulation())
        {
            Assert.IsFalse(agent.IsActive,
                $"{agent.Name} should be deactivated while paused.");
        }
    }

    // --- Test 13: ResumingContinuesSameRound ---
    [Test]
    public void ResumingContinuesSameRound()
    {
        float duration = 30f;
        var sim = new MockSimulation(3, 50, duration, new System.Random(42));
        sim.InitializeAndStart();

        sim.Tick(15f);
        sim.Pause();
        sim.Resume();
        Assert.IsFalse(sim.IsPaused, "Simulation should not be paused after resume.");

        // Warriors should be reactivated
        foreach (var agent in sim.GetPopulation())
        {
            Assert.IsTrue(agent.IsActive,
                $"{agent.Name} should be reactivated after resume.");
        }

        // Continue ticking — timer should advance
        sim.Tick(15f);
        Assert.AreEqual(0f, sim.TimeRemaining, 0.01f,
            "Timer should advance after resume and reach 0.");
    }

    // --- Test 14: CheckpointsCanBeSavedAndLoaded ---
    [Test]
    public void CheckpointsCanBeSavedAndLoaded()
    {
        // Mirror EvolutionCheckpointManager save/load logic
        // Build JSON string manually (mirrors BuildBestGenomeJson)
        var originalGenome = new Genome(new float[] { 0.5f, -0.3f, 0.8f, 0.1f, -0.9f });
        int generation = 5;
        int score = 42;

        // Save: build JSON manually
        float[] genes = originalGenome.ToArray();
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.Append("{\"version\":1,\"generation\":");
        sb.Append(generation);
        sb.Append(",\"score\":");
        sb.Append(score);
        sb.Append(",\"boneMapping\":\"HumanBodyBone-v1-19bones\"");
        sb.Append(",\"genomeSize\":");
        sb.Append(genes.Length);
        sb.Append(",\"genes\":[");
        for (int i = 0; i < genes.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(genes[i].ToString("R"));
        }
        sb.Append("]}");
        string json = sb.ToString();

        // Load: parse JSON manually (mirrors LoadBestGenome)
        int genesStart = json.IndexOf("\"genes\":[", System.StringComparison.Ordinal) + 9;
        int genesEnd = json.LastIndexOf(']');
        string genesStr = json.Substring(genesStart, genesEnd - genesStart);
        string[] parts = genesStr.Split(',');
        float[] loadedGenes = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            loadedGenes[i] = float.Parse(parts[i].Trim(), System.Globalization.CultureInfo.InvariantCulture);

        var loadedGenome = new Genome(loadedGenes);

        // Verify loaded genome matches original
        Assert.AreEqual(originalGenome.Size, loadedGenome.Size,
            "Loaded genome size should match original.");
        for (int i = 0; i < originalGenome.Size; i++)
        {
            Assert.AreEqual(originalGenome[i], loadedGenome[i], 1e-6f,
                $"Gene [{i}] should be preserved after save/load round-trip.");
        }
        Assert.IsTrue(loadedGenome.IsValid(), "Loaded genome should be valid.");
    }

    // --- Test 15: MultipleGenerationsWithoutStaleState ---
    [Test]
    public void MultipleGenerationsWithoutStaleState()
    {
        float duration = 30f;
        var sim = new MockSimulation(4, 50, duration, new System.Random(42));
        sim.InitializeAndStart();

        // Run 3 generations
        for (int gen = 0; gen < 3; gen++)
        {
            // Assign scores for this generation
            for (int i = 0; i < sim.GetPopulation().Length; i++)
                sim.GetPopulation()[i].Score = i * 10 + gen;

            // Tick through the round
            sim.Tick(duration);

            // Verify clean state after evolution
            Assert.AreEqual(gen + 1, sim.Generation,
                $"Generation should be {gen + 1} after {gen + 1} round(s).");
            Assert.AreEqual(State.Running, sim.CurrentState,
                $"State should be Running after generation {gen + 1}.");

            // All scores should be reset
            foreach (var agent in sim.GetPopulation())
            {
                Assert.AreEqual(0, agent.Score,
                    $"{agent.Name} score should be 0 at start of generation {gen + 1}.");
            }

            // All genomes should be valid
            foreach (var agent in sim.GetPopulation())
            {
                Assert.IsTrue(agent.Genome.IsValid(),
                    $"{agent.Name} genome should be valid in generation {gen + 1}.");
                Assert.AreEqual(50, agent.Genome.Size,
                    $"{agent.Name} genome size should be preserved in generation {gen + 1}.");
            }

            // All warriors should be active
            foreach (var agent in sim.GetPopulation())
            {
                Assert.IsTrue(agent.IsActive,
                    $"{agent.Name} should be active in generation {gen + 1}.");
            }

            // All scorers should be enabled (contact count starts at 0)
            foreach (var scorer in sim.GetScorers())
            {
                Assert.AreEqual(0, scorer.ActiveContactCount,
                    "Active contacts should be 0 at start of generation.");
            }
        }
    }
}
