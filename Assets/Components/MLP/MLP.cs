using System;
using UnityEngine;

/// <summary>
/// Lightweight CPU feed-forward multilayer perceptron for warrior neural control.
/// Architecture: Input → Dense(128, tanh) → Dense(64, tanh) → Output (tanh-bounded)
/// 
/// Designed for zero-allocation inference: all intermediate buffers are pre-allocated.
/// Supports deterministic genome import/export for genetic evolution.
/// </summary>
public sealed class MLP
{
    // --- Topology ---
    readonly int[] layerSizes; // includes input and output layers
    readonly int layerCount;   // total layers including input/output

    // --- Parameters (stored linearly: layer i weights then biases) ---
    readonly int[] layerWeightCounts; // number of weight elements per hidden/output layer
    readonly int[] layerBiasCounts;   // number of bias elements per hidden/output layer
    readonly int genomeSize;           // total trainable parameters

    // --- Cached inference buffers (pre-allocated, zero-allocation during Forward) ---
    float[] layerActivations; // activation values for each layer (input through output)
    float[] layerActivationsBackup; // backup for genome export without corrupting current state

    // --- Genome storage ---
    float[] genome; // flattened weights and biases

    /// <summary>
    /// Creates an MLP with the specified topology.
    /// hiddenLayers[i] = number of neurons in hidden layer i.
    /// Output activation is tanh-bounded; hidden activations use tanh.
    /// </summary>
    public MLP(int inputSize, int[] hiddenLayers, int outputSize)
    {
        if (inputSize <= 0) throw new ArgumentOutOfRangeException(nameof(inputSize));
        if (outputSize <= 0) throw new ArgumentOutOfRangeException(nameof(outputSize));
        if (hiddenLayers == null || hiddenLayers.Length == 0)
            throw new ArgumentException("At least one hidden layer is required.", nameof(hiddenLayers));

        this.inputSize = inputSize;
        this.outputSize = outputSize;
        this.hiddenLayers = (int[])hiddenLayers.Clone();
        layerCount = hiddenLayers.Length + 2; // input + hidden + output

        layerSizes = new int[layerCount];
        layerSizes[0] = inputSize;
        for (int i = 0; i < hiddenLayers.Length; i++)
            layerSizes[i + 1] = hiddenLayers[i];
        layerSizes[layerCount - 1] = outputSize;

        // Calculate weight and bias counts per layer (layers 1..layerCount-1 are trainable)
        int trainableLayerCount = layerCount - 1;
        layerWeightCounts = new int[trainableLayerCount];
        layerBiasCounts = new int[trainableLayerCount];
        genomeSize = 0;
        for (int i = 0; i < trainableLayerCount; i++)
        {
            int fromSize = layerSizes[i];
            int toSize = layerSizes[i + 1];
            layerWeightCounts[i] = fromSize * toSize;
            layerBiasCounts[i] = toSize;
            genomeSize += layerWeightCounts[i] + layerBiasCounts[i];
        }

        // Pre-allocate inference buffers: one buffer sized for the largest layer
        int maxLayerSize = inputSize;
        for (int i = 1; i < layerCount; i++)
            if (layerSizes[i] > maxLayerSize) maxLayerSize = layerSizes[i];
        layerActivations = new float[maxLayerSize];
        layerActivationsBackup = new float[maxLayerSize];

        genome = new float[genomeSize];

        InitializeRandom();
    }

    readonly int inputSize;
    readonly int outputSize;
    readonly int[] hiddenLayers;

    /// <summary>Number of input neurons.</summary>
    public int InputSize => inputSize;

    /// <summary>Number of output neurons.</summary>
    public int OutputSize => outputSize;

    /// <summary>Hidden layer sizes (excludes input and output).</summary>
    public int[] HiddenLayers => (int[])hiddenLayers.Clone();

    /// <summary>Total number of trainable parameters (weights + biases).</summary>
    public int GenomeSize => genomeSize;

    /// <summary>Number of layers including input and output.</summary>
    public int LayerCount => layerCount;

    /// <summary>Size of each layer (input through output).</summary>
    public int[] LayerSizes => (int[])layerSizes.Clone();

    /// <summary>
    /// Initializes all weights using Xavier/Glorot uniform initialization and zeros biases.
    /// </summary>
    public void InitializeRandom()
    {
        int offset = 0;
        for (int l = 0; l < layerCount - 1; l++)
        {
            int fromSize = layerSizes[l];
            int toSize = layerSizes[l + 1];
            int weightCount = layerWeightCounts[l];
            int biasCount = layerBiasCounts[l];

            // Xavier uniform: scale = sqrt(6 / (fanIn + fanOut))
            float scale = Mathf.Sqrt(6f / (fromSize + toSize));

            for (int i = 0; i < weightCount; i++)
                genome[offset + i] = (UnityEngine.Random.value * 2f - 1f) * scale;

            offset += weightCount;

            // Biases start at zero
            for (int i = 0; i < biasCount; i++)
                genome[offset + i] = 0f;

            offset += biasCount;
        }
    }

    /// <summary>
    /// Runs forward inference. Input is copied into the first layer,
    /// then propagated through hidden layers with tanh activation,
    /// and finally through the output layer with tanh-bounded activation.
    /// 
    /// Does NOT allocate memory. Input array must have length >= InputSize.
    /// Output array must have length >= OutputSize.
    /// </summary>
    public void Forward(float[] input, float[] output)
    {
        if (input == null) throw new ArgumentNullException(nameof(input));
        if (output == null) throw new ArgumentNullException(nameof(output));
        if (input.Length < inputSize)
            throw new ArgumentException($"Input array too small. Required: {inputSize}, got: {input.Length}", nameof(input));
        if (output.Length < outputSize)
            throw new ArgumentException($"Output array too small. Required: {outputSize}, got: {output.Length}", nameof(output));

        // Guard input for NaN/Infinity
        for (int i = 0; i < inputSize; i++)
        {
            float v = input[i];
            layerActivations[i] = float.IsNaN(v) || float.IsInfinity(v) ? 0f : v;
        }

        // Propagate through each trainable layer
        int genomeOffset = 0;
        for (int l = 0; l < layerCount - 1; l++)
        {
            int fromSize = layerSizes[l];
            int toSize = layerSizes[l + 1];
            int weightCount = layerWeightCounts[l];
            int biasCount = layerBiasCounts[l];

            // Use layerActivations as the source for every layer.
            // For hidden layers, write into the backup buffer to avoid aliasing
            // (writing into layerActivations while still reading from it corrupts
            // the inputs for higher-j neurons in the same layer).
            // For the output layer, write directly into the output array.
            float[] src = layerActivations;
            float[] dst = l == layerCount - 2 ? output : layerActivationsBackup;

            // Compute weighted sum + bias
            for (int j = 0; j < toSize; j++)
            {
                float sum = genome[genomeOffset + weightCount + j]; // bias
                int wOffset = j * fromSize;
                for (int k = 0; k < fromSize; k++)
                {
                    sum += src[k] * genome[genomeOffset + wOffset + k];
                }

                // Apply tanh activation (mathf.Tanh not available in all Unity versions)
                dst[j] = Tanh(sum);
            }

            // After a hidden layer, copy results back so the next layer reads from layerActivations.
            if (l != layerCount - 2)
            {
                for (int i = 0; i < toSize; i++)
                    layerActivations[i] = layerActivationsBackup[i];
            }

            genomeOffset += weightCount + biasCount;
        }

        // Guard output for NaN/Infinity (shouldn't happen with tanh, but safety check)
        for (int i = 0; i < outputSize; i++)
        {
            if (float.IsNaN(output[i]) || float.IsInfinity(output[i]))
                output[i] = 0f;
        }
    }

    /// <summary>
    /// Exports the current genome (weights and biases) as a flattened float array.
    /// Returns a new array — safe to modify without affecting this network.
    /// </summary>
    public float[] ExportGenome()
    {
        return (float[])genome.Clone();
    }

    /// <summary>
    /// Imports a genome into this network. The genome must have the exact same size.
    /// Creates a deep copy internally — the provided array is not retained.
    /// </summary>
    public void ImportGenome(float[] newGenome)
    {
        if (newGenome == null) throw new ArgumentNullException(nameof(newGenome));
        if (newGenome.Length != genomeSize)
            throw new ArgumentException($"Genome size mismatch. Expected: {genomeSize}, got: {newGenome.Length}", nameof(newGenome));

        // Validate for NaN/Infinity
        for (int i = 0; i < newGenome.Length; i++)
        {
            if (float.IsNaN(newGenome[i]) || float.IsInfinity(newGenome[i]))
                throw new ArgumentException($"Genome contains invalid value at index {i}.", nameof(newGenome));
        }

        Array.Copy(newGenome, genome, genomeSize);
    }

    /// <summary>
    /// Creates a deep copy of this MLP with identical topology and genome values.
    /// The copy is fully independent — modifying one does not affect the other.
    /// </summary>
    public MLP DeepCopy()
    {
        var copy = new MLP(inputSize, hiddenLayers, outputSize, genomeSize);
        Array.Copy(genome, copy.genome, genomeSize);
        return copy;
    }

    /// <summary>
    /// Creates an MLP with pre-set genome storage (used by DeepCopy).
    /// Does NOT randomize weights.
    /// </summary>
    internal MLP(int inputSize, int[] hiddenLayers, int outputSize, int expectedGenomeSize)
    {
        this.inputSize = inputSize;
        this.outputSize = outputSize;
        this.hiddenLayers = (int[])hiddenLayers.Clone();
        layerCount = hiddenLayers.Length + 2;

        layerSizes = new int[layerCount];
        layerSizes[0] = inputSize;
        for (int i = 0; i < hiddenLayers.Length; i++)
            layerSizes[i + 1] = hiddenLayers[i];
        layerSizes[layerCount - 1] = outputSize;

        int trainableLayerCount = layerCount - 1;
        layerWeightCounts = new int[trainableLayerCount];
        layerBiasCounts = new int[trainableLayerCount];
        genomeSize = 0;
        for (int i = 0; i < trainableLayerCount; i++)
        {
            int fromSize = layerSizes[i];
            int toSize = layerSizes[i + 1];
            layerWeightCounts[i] = fromSize * toSize;
            layerBiasCounts[i] = toSize;
            genomeSize += layerWeightCounts[i] + layerBiasCounts[i];
        }

        if (expectedGenomeSize != genomeSize)
            throw new ArgumentException($"Genome size mismatch in DeepCopy. Expected: {expectedGenomeSize}, computed: {genomeSize}");

        int maxLayerSize = inputSize;
        for (int i = 1; i < layerCount; i++)
            if (layerSizes[i] > maxLayerSize) maxLayerSize = layerSizes[i];
        layerActivations = new float[maxLayerSize];
        layerActivationsBackup = new float[maxLayerSize];

        genome = new float[genomeSize];
    }

    /// <summary>
    /// Validates that the genome contains no NaN or Infinity values.
    /// Returns true if the genome is valid.
    /// </summary>
    public bool ValidateGenome()
    {
        for (int i = 0; i < genome.Length; i++)
        {
            if (float.IsNaN(genome[i]) || float.IsInfinity(genome[i]))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Returns a string describing the network topology.
    /// </summary>
    public override string ToString()
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.Append("MLP[");
        sb.Append(inputSize);
        for (int i = 0; i < hiddenLayers.Length; i++)
            sb.Append("->").Append(hiddenLayers[i]);
        sb.Append("->").Append(outputSize).Append("]");
        sb.Append(" params=").Append(genomeSize);
        return sb.ToString();
    }

    /// <summary>
    /// Hyperbolic tangent approximation.
    /// Uses the mathematical definition: tanh(x) = (e^(2x) - 1) / (e^(2x) + 1)
    /// Clamped to [-1, 1] for numerical stability.
    /// </summary>
    static float Tanh(float x)
    {
        // Clamp input to prevent overflow in exp
        if (x > 19f) return 1f;
        if (x < -19f) return -1f;
        float e2x = Mathf.Exp(2f * x);
        return (e2x - 1f) / (e2x + 1f);
    }
}