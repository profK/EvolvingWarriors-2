using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests for population management logic used by EvolutionSimulationManager and WarriorSpawner.
/// Note: These are MonoBehaviours and cannot be instantiated in edit mode.
/// We test the core population management operations using standalone helpers.
/// </summary>
[TestFixture]
public class PopulationTests
{
    class MockAgent
    {
        public int Score { get; internal set; }
        public MockAgent Opponent { get; set; }
        public Genome Genome { get; internal set; }
        public string Name;

        public MockAgent(Genome genome, string name)
        {
            this.Genome = genome;
            this.Name = name;
            Score = 0;
        }
        public void AddScore(int delta) => Score += delta;
        public void InstallGenome(Genome newGenome)
        {
            if (newGenome == null) throw new ArgumentNullException(nameof(newGenome));
            Genome = newGenome;
        }
        public void ResetRound() => Score = 0;
        public void Randomize()
        {
            var rng = new System.Random(Name.GetHashCode());
            float[] genes = new float[Genome.Size];
            for (int i = 0; i < genes.Length; i++)
                genes[i] = (float)(rng.NextDouble() * 2.0f - 1.0f);
            Genome = new Genome(genes);
        }
    }

    static MockAgent[] SpawnWarriors(int pairCount, int genomeSize)
    {
        int total = pairCount * 2;
        var agents = new MockAgent[total];
        for (int i = 0; i < total; i++)
            agents[i] = new MockAgent(new Genome(genomeSize), $"Warrior_{i}");
        return agents;
    }

    static void WireOpponents(MockAgent[] population)
    {
        for (int i = 0; i < population.Length; i += 2)
        {
            if (i + 1 < population.Length)
            {
                population[i].Opponent = population[i + 1];
                population[i + 1].Opponent = population[i];
            }
        }
    }

    static void EvolveAndReset(MockAgent[] population, System.Random rng)
    {
        Genome[] genomes = new Genome[population.Length];
        int[] scores = new int[population.Length];
        for (int i = 0; i < population.Length; i++)
        {
            genomes[i] = population[i].Genome;
            scores[i] = population[i].Score;
        }
        Genome[] nextGenomes = GeneticEvolutionTests.NextGeneration(
            genomes, scores, 0.1f, 3, 0.7f, 0.02f, 0.1f, rng);
        for (int i = 0; i < population.Length; i++)
        {
            if (population[i] != null)
            {
                population[i].ResetRound();
                population[i].InstallGenome(nextGenomes[i]);
            }
        }
    }

    [Test]
    public void ExactlyTwoWarriorsPerPair()
    {
        int pairCount = 4;
        MockAgent[] population = SpawnWarriors(pairCount, 50);
        Assert.AreEqual(pairCount * 2, population.Length,
            "Spawner should create exactly 2 warriors per pair slot.");
    }

    [Test]
    public void EveryWarriorHasOneOpponent()
    {
        int pairCount = 5;
        MockAgent[] population = SpawnWarriors(pairCount, 50);
        WireOpponents(population);
        int assigned = 0;
        foreach (var agent in population)
            if (agent.Opponent != null) assigned++;
        Assert.AreEqual(population.Length, assigned,
            "Every warrior should have exactly one opponent assigned.");
    }

    [Test]
    public void OpponentRelationshipsReciprocal()
    {
        int pairCount = 3;
        MockAgent[] population = SpawnWarriors(pairCount, 50);
        WireOpponents(population);
        foreach (var agent in population)
        {
            Assert.IsNotNull(agent.Opponent, $"{agent.Name} should have an opponent.");
            Assert.AreEqual(agent, agent.Opponent.Opponent,
                $"{agent.Name} and {agent.Opponent.Name} should be reciprocal.");
        }
    }

    [Test]
    public void PairAssignmentsFixedAcrossGenerations()
    {
        int pairCount = 3;
        MockAgent[] population = SpawnWarriors(pairCount, 50);
        WireOpponents(population);
        var originalOpponents = new Dictionary<MockAgent, MockAgent>();
        foreach (var agent in population)
            originalOpponents[agent] = agent.Opponent;
        for (int i = 0; i < population.Length; i++)
            population[i].Score = i * 10;
        EvolveAndReset(population, new System.Random(42));
        foreach (var agent in population)
        {
            Assert.AreEqual(originalOpponents[agent], agent.Opponent,
                $"{agent.Name} should retain the same opponent across generations.");
        }
    }

    [Test]
    public void EveryWarriorHasIndependentGenome()
    {
        int pairCount = 4;
        MockAgent[] population = SpawnWarriors(pairCount, 50);
        for (int i = 0; i < population.Length; i++)
        {
            for (int j = i + 1; j < population.Length; j++)
            {
                Assert.IsFalse(ReferenceEquals(population[i].Genome, population[j].Genome),
                    $"{population[i].Name} and {population[j].Name} should have independent genomes.");
            }
        }
    }

    [Test]
    public void AllGenomesShareSameTopology()
    {
        int pairCount = 4;
        int genomeSize = 50;
        MockAgent[] population = SpawnWarriors(pairCount, genomeSize);
        foreach (var agent in population)
        {
            Assert.AreEqual(genomeSize, agent.Genome.Size,
                $"{agent.Name} genome size should match the configured topology.");
        }
    }

    [Test]
    public void EvolutionReplacesGenomesCorrectly()
    {
        int pairCount = 3;
        MockAgent[] population = SpawnWarriors(pairCount, 50);
        var originalGenomes = new Genome[population.Length];
        for (int i = 0; i < population.Length; i++)
            originalGenomes[i] = population[i].Genome.Clone();
        for (int i = 0; i < population.Length; i++)
            population[i].Score = i * 10;
        EvolveAndReset(population, new System.Random(42));
        for (int i = 0; i < population.Length; i++)
        {
            Assert.IsNotNull(population[i].Genome,
                $"{population[i].Name} should have a genome after evolution.");
            Assert.IsTrue(population[i].Genome.IsValid(),
                $"{population[i].Name} genome should be valid after evolution.");
            Assert.AreEqual(50, population[i].Genome.Size,
                $"{population[i].Name} genome size should be preserved.");
        }
        foreach (var agent in population)
        {
            Assert.AreEqual(0, agent.Score,
                $"{agent.Name} score should be reset to 0 after evolution.");
        }
    }
}
