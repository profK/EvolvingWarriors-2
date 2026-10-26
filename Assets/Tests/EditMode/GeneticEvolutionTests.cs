using System;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Tests for the genetic algorithm operations used by GeneticEvolution.
/// 
/// Note: GeneticEvolution.NextGeneration takes WarriorAgent[] (a MonoBehaviour),
/// so we cannot invoke it in edit-mode tests. Instead, we test the core GA
/// operations (selection, crossover, mutation, elitism) directly on Genome objects,
/// using the same algorithms as GeneticEvolution.
/// </summary>
[TestFixture]
public class GeneticEvolutionTests
{
    // --- GA operation helpers (mirror GeneticEvolution logic) ---

    internal static Genome[] NextGeneration(
        Genome[] population,
        int[] scores,
        float eliteFraction,
        int tournamentSize,
        float crossoverProbability,
        float mutationProbability,
        float mutationStrength,
        System.Random rng)
    {
        int popSize = population.Length;
        if (popSize < 2)
            return null;

        // Create indexed array and sort by score descending
        int[] indices = new int[popSize];
        for (int i = 0; i < popSize; i++) indices[i] = i;
        Array.Sort(indices, (a, b) => scores[b].CompareTo(scores[a]));

        Genome[] nextGen = new Genome[popSize];

        // Elitism
        int eliteCount = Mathf.Max(1, Mathf.CeilToInt(popSize * eliteFraction));
        eliteCount = Mathf.Min(eliteCount, popSize);
        for (int i = 0; i < eliteCount; i++)
            nextGen[i] = population[indices[i]].Clone();

        // Fill rest via selection, crossover, mutation
        for (int i = eliteCount; i < popSize; i++)
        {
            Genome parentA = TournamentSelect(population, scores, indices, tournamentSize, rng);
            Genome parentB = TournamentSelect(population, scores, indices, tournamentSize, rng);

            if (parentB.Equals(parentA) && popSize > 1)
                parentB = TournamentSelect(population, scores, indices, tournamentSize, rng);

            Genome child = UniformCrossover(parentA, parentB, crossoverProbability, rng);
            nextGen[i] = GaussianMutate(child, mutationProbability, mutationStrength, rng);
        }

        return nextGen;
    }

    static Genome TournamentSelect(Genome[] population, int[] scores, int[] sortedIndices, int tournamentSize, System.Random rng)
    {
        Genome best = null;
        int bestScore = int.MinValue;
        for (int t = 0; t < tournamentSize; t++)
        {
            int idx = rng.Next(population.Length);
            if (best == null || scores[idx] > bestScore)
            {
                best = population[idx];
                bestScore = scores[idx];
            }
        }
        return best;
    }

    internal static Genome UniformCrossover(Genome parentA, Genome parentB, float crossoverProbability, System.Random rng)
    {
        int size = parentA.Size;
        float[] childGenes = new float[size];
        for (int i = 0; i < size; i++)
        {
            if (rng.NextDouble() < crossoverProbability)
                childGenes[i] = parentB.ToArray()[i];
            else
                childGenes[i] = parentA.ToArray()[i];
        }
        return new Genome(childGenes);
    }

    internal static Genome GaussianMutate(Genome genome, float mutationProbability, float mutationStrength, System.Random rng)
    {
        float[] genes = genome.ToArray();
        for (int i = 0; i < genes.Length; i++)
        {
            if (rng.NextDouble() < mutationProbability)
                genes[i] += GaussianRandom(rng) * mutationStrength;
        }
        return new Genome(genes);
    }

    static float GaussianRandom(System.Random rng)
    {
        double u1 = rng.NextDouble();
        double u2 = rng.NextDouble();
        while (u1 == 0.0) u1 = rng.NextDouble();
        double radius = Math.Sqrt(-2.0 * Math.Log(u1));
        double theta = 2.0 * Math.PI * u2;
        return (float)(radius * Math.Cos(theta));
    }

    static Genome[] CreateRandomPopulation(int size, int genomeSize, System.Random rng)
    {
        var pop = new Genome[size];
        for (int i = 0; i < size; i++)
        {
            float[] genes = new float[genomeSize];
            for (int j = 0; j < genomeSize; j++)
                genes[j] = (float)(rng.NextDouble() * 2.0f - 1.0f);
            pop[i] = new Genome(genes);
        }
        return pop;
    }

    static int[] CreateRandomScores(int size, System.Random rng)
    {
        var scores = new int[size];
        for (int i = 0; i < size; i++)
            scores[i] = rng.Next(0, 100);
        return scores;
    }

    // --- Test 7: PopulationSizeRemainsConstant ---
    [Test]
    public void PopulationSizeRemainsConstant()
    {
        int popSize = 20;
        int genomeSize = 100;
        var rng = new System.Random(42);

        Genome[] population = CreateRandomPopulation(popSize, genomeSize, rng);
        int[] scores = CreateRandomScores(popSize, rng);

        Genome[] nextGen = NextGeneration(population, scores, 0.1f, 3, 0.5f, 0.1f, 0.5f, rng);

        Assert.AreEqual(popSize, nextGen.Length,
            "Population size must remain constant after one evolution cycle.");
    }

    // --- Test 8: EliteGenomesRemainUnchanged ---
    [Test]
    public void EliteGenomesRemainUnchanged()
    {
        int popSize = 10;
        int genomeSize = 50;
        var rng = new System.Random(123);

        // Create population with deterministic genomes and descending scores
        Genome[] population = new Genome[popSize];
        int[] scores = new int[popSize];
        for (int i = 0; i < popSize; i++)
        {
            float[] genes = new float[genomeSize];
            for (int j = 0; j < genomeSize; j++)
                genes[j] = (float)(i * 0.1f + j * 0.01f);
            population[i] = new Genome(genes);
            scores[i] = (popSize - i) * 10; // Descending: 100, 90, 80, ...
        }

        // Save elite originals (top 20% = 2 elites)
        float eliteFraction = 0.2f;
        int eliteCount = Mathf.Max(1, Mathf.CeilToInt(popSize * eliteFraction));
        eliteCount = Mathf.Min(eliteCount, popSize);

        // Sort indices by score descending to find elites
        int[] indices = new int[popSize];
        for (int i = 0; i < popSize; i++) indices[i] = i;
        Array.Sort(indices, (a, b) => scores[b].CompareTo(scores[a]));

        var eliteOriginals = new Genome[eliteCount];
        for (int i = 0; i < eliteCount; i++)
            eliteOriginals[i] = population[indices[i]].Clone();

        Genome[] nextGen = NextGeneration(population, scores, eliteFraction, 3, 0.5f, 0.1f, 0.5f, rng);

        for (int i = 0; i < eliteCount; i++)
        {
            Assert.IsTrue(nextGen[i].Equals(eliteOriginals[i]),
                $"Elite genome [{i}] should remain unchanged in next generation.");
        }
    }

    // --- Test 9: MutationChangesGenes ---
    [Test]
    public void MutationChangesGenes()
    {
        int genomeSize = 100;
        var rng = new System.Random(999);

        // Create a genome with known values
        float[] genes = new float[genomeSize];
        for (int i = 0; i < genomeSize; i++)
            genes[i] = 0.5f;
        var original = new Genome(genes);

        // Apply mutation with 100% probability to ensure changes
        var mutated = GaussianMutate(original, 1.0f, 0.5f, rng);

        // Verify at least some genes changed
        int changedCount = 0;
        for (int i = 0; i < genomeSize; i++)
        {
            if (Math.Abs(original[i] - mutated[i]) > 0.001f)
                changedCount++;
        }

        Assert.Greater(changedCount, 0,
            "Mutation with 100% probability should change at least some genes.");
        Assert.IsTrue(mutated.IsValid(),
            "Mutated genome must remain valid (no NaN or Infinity).");
    }

    // --- Test 10: CrossoverProducesValidGenomes ---
    [Test]
    public void CrossoverProducesValidGenomes()
    {
        int genomeSize = 50;
        var rng = new System.Random(777);

        // Create two distinct parent genomes
        float[] genesA = new float[genomeSize];
        float[] genesB = new float[genomeSize];
        for (int i = 0; i < genomeSize; i++)
        {
            genesA[i] = 1.0f;
            genesB[i] = -1.0f;
        }

        var parentA = new Genome(genesA);
        var parentB = new Genome(genesB);

        // Perform crossover
        var child = UniformCrossover(parentA, parentB, 0.5f, rng);

        // Verify child is valid
        Assert.IsTrue(child.IsValid(), "Crossover child must be a valid genome (no NaN/Infinity).");
        Assert.AreEqual(genomeSize, child.Size, "Crossover child must have the same size as parents.");

        // Verify child genes come from one of the parents
        bool hasGeneFromA = false;
        bool hasGeneFromB = false;
        for (int i = 0; i < genomeSize; i++)
        {
            if (Math.Abs(child[i] - genesA[i]) < 0.001f)
                hasGeneFromA = true;
            if (Math.Abs(child[i] - genesB[i]) < 0.001f)
                hasGeneFromB = true;
        }
        Assert.IsTrue(hasGeneFromA, "Child should have at least one gene from parent A.");
        Assert.IsTrue(hasGeneFromB, "Child should have at least one gene from parent B.");
    }

    // --- Test 11: NegativeFitnessScoresHandled ---
    [Test]
    public void NegativeFitnessScoresHandled()
    {
        int popSize = 10;
        int genomeSize = 30;
        var rng = new System.Random(333);

        // Create population where ALL scores are negative
        Genome[] population = new Genome[popSize];
        int[] scores = new int[popSize];
        for (int i = 0; i < popSize; i++)
        {
            float[] genes = new float[genomeSize];
            for (int j = 0; j < genomeSize; j++)
                genes[j] = (float)(rng.NextDouble() * 2.0f - 1.0f);
            population[i] = new Genome(genes);
            scores[i] = -(i + 1) * 10; // All negative: -10, -20, -30, ...
        }

        // Evolution should work even with all-negative fitness
        Genome[] nextGen = NextGeneration(population, scores, 0.2f, 3, 0.5f, 0.1f, 0.5f, rng);

        Assert.IsNotNull(nextGen, "Next generation should not be null with negative scores.");
        Assert.AreEqual(popSize, nextGen.Length, "Population size should be preserved with negative scores.");

        // All genomes in next generation should be valid
        for (int i = 0; i < nextGen.Length; i++)
        {
            Assert.IsTrue(nextGen[i].IsValid(),
                $"Genome [{i}] in next generation must be valid with negative fitness scores.");
        }
    }

    // --- Test 12: SmallPopulationsSupported ---
    [Test]
    public void SmallPopulationsSupported()
    {
        int popSize = 2;
        int genomeSize = 20;
        var rng = new System.Random(555);

        Genome[] population = CreateRandomPopulation(popSize, genomeSize, rng);
        int[] scores = CreateRandomScores(popSize, rng);

        Genome[] nextGen = NextGeneration(population, scores, 0.5f, 2, 0.5f, 0.1f, 0.5f, rng);

        Assert.IsNotNull(nextGen, "Next generation should not be null for population of 2.");
        Assert.AreEqual(popSize, nextGen.Length, "Population size should be preserved for small populations.");

        // Verify all genomes are valid
        for (int i = 0; i < nextGen.Length; i++)
        {
            Assert.IsTrue(nextGen[i].IsValid(),
                $"Genome [{i}] must be valid for small population.");
            Assert.AreEqual(genomeSize, nextGen[i].Size,
                $"Genome [{i}] must have correct size for small population.");
        }
    }
}