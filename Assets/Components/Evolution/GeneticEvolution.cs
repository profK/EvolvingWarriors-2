using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Genetic algorithm engine for evolving warrior genomes.
/// 
/// Strategy:
/// - Elitism: preserves top N% of the population unchanged
/// - Tournament selection (configurable size) for parents
/// - Uniform crossover (configurable probability)
/// - Per-gene Gaussian mutation (configurable probability and strength)
/// </summary>
public static class GeneticEvolution
{
    /// <summary>
    /// Produces the next generation of genomes from the current population.
    /// </summary>
    /// <param name="population">Current warriors with their genomes and scores.</param>
    /// <param name="eliteFraction">Fraction of top performers preserved unchanged (0.0–1.0).</param>
    /// <param name="tournamentSize">Number of individuals sampled per tournament.</param>
    /// <param name="crossoverProbability">Probability of swapping each gene during crossover.</param>
    /// <param name="mutationProbability">Probability of mutating each gene.</param>
    /// <param name="mutationStrength">Standard deviation of Gaussian mutation.</param>
    /// <param name="rng">Random number generator for reproducibility.</param>
    /// <returns>New array of genomes, one per population member.</returns>
    public static Genome[] NextGeneration(
        WarriorAgent[] population,
        float eliteFraction,
        int tournamentSize,
        float crossoverProbability,
        float mutationProbability,
        float mutationStrength,
        System.Random rng)
    {
        int popSize = population.Length;
        if (popSize < 2)
        {
            Debug.LogError("GeneticEvolution: Population must contain at least 2 warriors.");
            return null;
        }

        // Sort by score descending for elitism
        Array.Sort(population, (a, b) => b.Score.CompareTo(a.Score));

        Genome[] nextGen = new Genome[popSize];

        // Elitism: preserve top individuals
        int eliteCount = Mathf.Max(1, Mathf.CeilToInt(popSize * eliteFraction));
        eliteCount = Mathf.Min(eliteCount, popSize);

        for (int i = 0; i < eliteCount; i++)
        {
            nextGen[i] = population[i].Genome.Clone();
        }

        // Fill the rest via selection, crossover, mutation
        for (int i = eliteCount; i < popSize; i++)
        {
            Genome parentA = TournamentSelect(population, tournamentSize, rng);
            Genome parentB = TournamentSelect(population, tournamentSize, rng);

            // Ensure parents are different when possible
            if (parentB.Equals(parentA) && popSize > 1)
            {
                parentB = TournamentSelect(population, tournamentSize, rng);
            }

            Genome child = UniformCrossover(parentA, parentB, crossoverProbability, rng);
            nextGen[i] = GaussianMutate(child, mutationProbability, mutationStrength, rng);
        }

        return nextGen;
    }

    /// <summary>
    /// Tournament selection: picks the best individual from a random subset.
    /// Supports negative fitness values.
    /// </summary>
    static Genome TournamentSelect(WarriorAgent[] population, int tournamentSize, System.Random rng)
    {
        WarriorAgent best = null;
        int bestScore = int.MinValue;

        for (int t = 0; t < tournamentSize; t++)
        {
            int idx = rng.Next(population.Length);
            WarriorAgent candidate = population[idx];
            if (best == null || candidate.Score > bestScore)
            {
                best = candidate;
                bestScore = candidate.Score;
            }
        }

        return best.Genome;
    }

    /// <summary>
    /// Uniform crossover: for each gene, randomly pick from parentA or parentB.
    /// </summary>
    static Genome UniformCrossover(Genome parentA, Genome parentB, float crossoverProbability, System.Random rng)
    {
        int size = parentA.Size;
        float[] childGenes = new float[size];

        for (int i = 0; i < size; i++)
        {
            if (rng.NextDouble() < crossoverProbability)
            {
                childGenes[i] = parentB.ToArray()[i];
            }
            else
            {
                childGenes[i] = parentA.ToArray()[i];
            }
        }

        return new Genome(childGenes);
    }

    /// <summary>
    /// Per-gene Gaussian mutation: each gene has a chance to be perturbed by N(0, σ).
    /// Returns a new Genome with mutations applied.
    /// </summary>
    static Genome GaussianMutate(Genome genome, float mutationProbability, float mutationStrength, System.Random rng)
    {
        float[] genes = genome.ToArray();

        for (int i = 0; i < genes.Length; i++)
        {
            if (rng.NextDouble() < mutationProbability)
            {
                genes[i] += GaussianRandom(rng) * mutationStrength;
            }
        }

        return new Genome(genes);
    }

    /// <summary>
    /// Box-Muller transform for generating a standard normal random variable.
    /// </summary>
    static float GaussianRandom(System.Random rng)
    {
        double u1 = rng.NextDouble();
        double u2 = rng.NextDouble();

        // Avoid log(0)
        while (u1 == 0.0)
            u1 = rng.NextDouble();

        double radius = Math.Sqrt(-2.0 * Math.Log(u1));
        double theta = 2.0 * Math.PI * u2;

        return (float)(radius * Math.Cos(theta));
    }
}
