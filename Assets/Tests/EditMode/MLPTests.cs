using System;
using NUnit.Framework;

[TestFixture]
public class MLPTests
{
    // --- Test 1: IndependentWarriorsDoNotShareWeights ---
    [Test]
    public void IndependentWarriorsDoNotShareWeights()
    {
        int inputSize = 10;
        int outputSize = 5;
        int[] hidden = new int[] { 32, 16 };

        var mlp1 = new MLP(inputSize, hidden, outputSize);
        var mlp2 = new MLP(inputSize, hidden, outputSize);

        float[] input = new float[inputSize];
        for (int i = 0; i < inputSize; i++)
            input[i] = 0.5f;

        float[] output1 = new float[outputSize];
        float[] output2 = new float[outputSize];

        mlp1.Forward(input, output1);
        mlp2.Forward(input, output2);

        // Two independently initialized MLPs should produce different outputs
        bool allEqual = true;
        for (int i = 0; i < outputSize; i++)
        {
            if (Math.Abs(output1[i] - output2[i]) > 0.001f)
            {
                allEqual = false;
                break;
            }
        }
        Assert.IsFalse(allEqual, "Two independently created MLPs with same topology should produce different outputs for the same input.");
    }

    // --- Test 2: IdenticalGenomesProduceIdenticalOutputs ---
    [Test]
    public void IdenticalGenomesProduceIdenticalOutputs()
    {
        int inputSize = 10;
        int outputSize = 5;
        int[] hidden = new int[] { 32, 16 };

        var mlp1 = new MLP(inputSize, hidden, outputSize);
        float[] genome = mlp1.ExportGenome();

        var mlp2 = new MLP(inputSize, hidden, outputSize);
        mlp2.ImportGenome(genome);

        float[] input = new float[inputSize];
        for (int i = 0; i < inputSize; i++)
            input[i] = (float)(i % 7) / 7.0f;

        float[] output1 = new float[outputSize];
        float[] output2 = new float[outputSize];

        mlp1.Forward(input, output1);
        mlp2.Forward(input, output2);

        for (int i = 0; i < outputSize; i++)
        {
            Assert.AreEqual(output1[i], output2[i], 1e-6f,
                $"Output[{i}] should be identical for MLPs with the same genome.");
        }
    }

    // --- Test 3: GenomeExportImportPreservesParameters ---
    [Test]
    public void GenomeExportImportPreservesParameters()
    {
        int inputSize = 8;
        int outputSize = 4;
        int[] hidden = new int[] { 24, 12 };

        var original = new MLP(inputSize, hidden, outputSize);
        float[] exported = original.ExportGenome();

        var imported = new MLP(inputSize, hidden, outputSize);
        imported.ImportGenome(exported);

        float[] importedAgain = imported.ExportGenome();

        Assert.AreEqual(exported.Length, importedAgain.Length, "Exported genome lengths must match.");
        for (int i = 0; i < exported.Length; i++)
        {
            Assert.AreEqual(exported[i], importedAgain[i], 1e-6f,
                $"Genome parameter [{i}] should be preserved after export-import round-trip.");
        }
    }

    // --- Test 4: InputDimensionsMatchEncoder ---
    [Test]
    public void InputDimensionsMatchEncoder()
    {
        // WarriorObservationEncoder.InputDimension is computed as:
        // BoneCount * OpponentBoneObsSize(7) + OpponentRootObsSize(7) +
        // BoneCount * SelfBoneObsSize(4) + TimeSignalCount(8)
        // = 19*7 + 7 + 19*4 + 8 = 133 + 7 + 76 + 8 = 224
        int expectedInputDim = WarriorBoneController.BoneCount * 7 + 7 + WarriorBoneController.BoneCount * 4 + 8;

        var mlp = new MLP(expectedInputDim, new int[] { 128, 64 }, WarriorBoneController.OutputDimension);
        Assert.AreEqual(expectedInputDim, mlp.InputSize,
            "MLP input dimension must match WarriorObservationEncoder.InputDimension.");
    }

    // --- Test 5: OutputDimensionsMatchControlledBones ---
    [Test]
    public void OutputDimensionsMatchControlledBones()
    {
        var mlp = new MLP(10, new int[] { 32, 16 }, WarriorBoneController.OutputDimension);
        Assert.AreEqual(WarriorBoneController.OutputDimension, mlp.OutputSize,
            "MLP output dimension must match WarriorBoneController.OutputDimension (57).");
    }

    // --- Test 6: ValidInputsDoNotProduceNaN ---
    [Test]
    public void ValidInputsDoNotProduceNaN()
    {
        int inputSize = 10;
        int outputSize = 5;
        int[] hidden = new int[] { 32, 16 };

        var mlp = new MLP(inputSize, hidden, outputSize);

        var rng = new System.Random(42);
        float[] input = new float[inputSize];
        float[] output = new float[outputSize];

        // Feed multiple random valid inputs
        for (int trial = 0; trial < 100; trial++)
        {
            for (int i = 0; i < inputSize; i++)
                input[i] = (float)(rng.NextDouble() * 2.0f - 1.0f); // [-1, 1]

            mlp.Forward(input, output);

            for (int i = 0; i < outputSize; i++)
            {
                Assert.IsFalse(float.IsNaN(output[i]),
                    $"Output[{i}] is NaN after trial {trial} with valid inputs.");
                Assert.IsFalse(float.IsInfinity(output[i]),
                    $"Output[{i}] is Infinity after trial {trial} with valid inputs.");
            }
        }
    }
}
