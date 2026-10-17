using System;
using UnityEngine;

/// <summary>
/// Represents a warrior's genome: a flattened array of all trainable MLP parameters.
/// 
/// Genome layout (deterministic, layer-by-layer):
///   Layer 0 (input→hidden[0]): weights[0..N-1], biases[0..M-1]
///   Layer 1 (hidden[0]→hidden[1]): weights[0..N-1], biases[0..M-1]
///   ...
///   Last layer (hidden[last]→output): weights[0..N-1], biases[0..O-1]
/// 
/// All genomes in the population must have identical lengths.
/// </summary>
public sealed class Genome
{
    readonly float[] genes;

    /// <summary>Number of genes (trainable parameters) in this genome.</summary>
    public int Size => genes.Length;

    /// <summary>
    /// Creates a new genome of the specified size, initialized to zero.
    /// </summary>
    public Genome(int size)
    {
        if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size), "Genome size must be positive.");
        genes = new float[size];
    }

    /// <summary>
    /// Creates a genome from an existing array. The array is cloned — modifications to
    /// the source array do not affect this genome.
    /// </summary>
    public Genome(float[] source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (source.Length == 0) throw new ArgumentException("Source genome must not be empty.", nameof(source));
        
        // Validate for NaN/Infinity
        for (int i = 0; i < source.Length; i++)
        {
            if (float.IsNaN(source[i]) || float.IsInfinity(source[i]))
                throw new ArgumentException($"Source genome contains invalid value at index {i}.", nameof(source));
        }

        genes = (float[])source.Clone();
    }

    /// <summary>
    /// Gets or sets a gene at the specified index.
    /// </summary>
    public float this[int index]
    {
        get
        {
            if (index < 0 || index >= genes.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            return genes[index];
        }
        set
        {
            if (index < 0 || index >= genes.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentException("Gene value must not be NaN or Infinity.");
            genes[index] = value;
        }
    }

    /// <summary>
    /// Returns a copy of the internal gene array. Safe to modify without affecting this genome.
    /// </summary>
    public float[] ToArray()
    {
        return (float[])genes.Clone();
    }

    /// <summary>
    /// Creates a deep copy of this genome.
    /// </summary>
    public Genome Clone()
    {
        return new Genome(genes);
    }

    /// <summary>
    /// Validates that all genes contain finite values.
    /// </summary>
    public bool IsValid()
    {
        for (int i = 0; i < genes.Length; i++)
        {
            if (float.IsNaN(genes[i]) || float.IsInfinity(genes[i]))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Replaces any NaN or Infinity values with zero.
    /// Returns true if any corrections were made.
    /// </summary>
    public bool CorrectInvalidValues()
    {
        bool corrected = false;
        for (int i = 0; i < genes.Length; i++)
        {
            if (float.IsNaN(genes[i]) || float.IsInfinity(genes[i]))
            {
                genes[i] = 0f;
                corrected = true;
            }
        }
        return corrected;
    }

    /// <summary>
    /// Compares two genomes for equality (element-by-element).
    /// </summary>
    public bool Equals(Genome other)
    {
        if (other == null) return false;
        if (genes.Length != other.genes.Length) return false;
        for (int i = 0; i < genes.Length; i++)
        {
            if (genes[i] != other.genes[i]) return false;
        }
        return true;
    }

    /// <summary>
    /// Computes the mean absolute value of all genes (useful for monitoring evolution).
    /// </summary>
    public float MeanAbsoluteValue()
    {
        float sum = 0f;
        for (int i = 0; i < genes.Length; i++)
            sum += Mathf.Abs(genes[i]);
        return sum / genes.Length;
    }

    /// <summary>
    /// Returns a brief description of the genome.
    /// </summary>
    public override string ToString()
    {
        return $"Genome[{genes.Length}]";
    }
}
