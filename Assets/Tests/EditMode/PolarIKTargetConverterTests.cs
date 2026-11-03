using NUnit.Framework;
using UnityEngine;

public class PolarIKTargetConverterTests
{
    [Test]
    public void TestForwardTarget()
    {
        // Azimuth = 0, Elevation = 0, Radius = 1.0 => (0, 0, 1)
        Vector3 pos = PolarIKTargetConverter.SphericalToCartesian(1.0f, 0f, 0f);
        Assert.AreEqual(0f, pos.x, 1e-4f);
        Assert.AreEqual(0f, pos.y, 1e-4f);
        Assert.AreEqual(1f, pos.z, 1e-4f);
    }

    [Test]
    public void TestRightTarget()
    {
        // Azimuth = +90 deg (+PI/2), Elevation = 0, Radius = 1.0 => (1, 0, 0)
        Vector3 pos = PolarIKTargetConverter.SphericalToCartesian(1.0f, Mathf.PI * 0.5f, 0f);
        Assert.AreEqual(1f, pos.x, 1e-4f);
        Assert.AreEqual(0f, pos.y, 1e-4f);
        Assert.AreEqual(0f, pos.z, 1e-4f);
    }

    [Test]
    public void TestLeftTarget()
    {
        // Azimuth = -90 deg (-PI/2), Elevation = 0, Radius = 1.0 => (-1, 0, 0)
        Vector3 pos = PolarIKTargetConverter.SphericalToCartesian(1.0f, -Mathf.PI * 0.5f, 0f);
        Assert.AreEqual(-1f, pos.x, 1e-4f);
        Assert.AreEqual(0f, pos.y, 1e-4f);
        Assert.AreEqual(0f, pos.z, 1e-4f);
    }

    [Test]
    public void TestUpwardTarget()
    {
        // Azimuth = 0, Elevation = +90 deg (+PI/2), Radius = 1.0 => (0, 1, 0)
        Vector3 pos = PolarIKTargetConverter.SphericalToCartesian(1.0f, 0f, Mathf.PI * 0.5f);
        Assert.AreEqual(0f, pos.x, 1e-4f);
        Assert.AreEqual(1f, pos.y, 1e-4f);
        Assert.AreEqual(0f, pos.z, 1e-4f);
    }

    [Test]
    public void TestDownwardTarget()
    {
        // Azimuth = 0, Elevation = -90 deg (-PI/2), Radius = 1.0 => (0, -1, 0)
        Vector3 pos = PolarIKTargetConverter.SphericalToCartesian(1.0f, 0f, -Mathf.PI * 0.5f);
        Assert.AreEqual(0f, pos.x, 1e-4f);
        Assert.AreEqual(-1f, pos.y, 1e-4f);
        Assert.AreEqual(0f, pos.z, 1e-4f);
    }

    [Test]
    public void TestRootTranslationAndRotation()
    {
        var go = new GameObject("Root");
        go.transform.position = new Vector3(10f, 5f, -2f);
        go.transform.rotation = Quaternion.Euler(0f, 90f, 0f); // Face +X

        var converter = new PolarIKTargetConverter();
        // MLP outputs all 0 => mid-range values
        float[] outputs = new float[6];
        var targets = converter.ConvertOutputs(outputs, go.transform);

        // Check that world pos equals TransformPoint of local pos
        Vector3 expectedRightWorld = go.transform.TransformPoint(targets.rightLocalPos);
        Vector3 expectedLeftWorld = go.transform.TransformPoint(targets.leftLocalPos);

        Assert.AreEqual(expectedRightWorld.x, targets.rightWorldPos.x, 1e-4f);
        Assert.AreEqual(expectedRightWorld.y, targets.rightWorldPos.y, 1e-4f);
        Assert.AreEqual(expectedRightWorld.z, targets.rightWorldPos.z, 1e-4f);

        Assert.AreEqual(expectedLeftWorld.x, targets.leftWorldPos.x, 1e-4f);
        Assert.AreEqual(expectedLeftWorld.y, targets.leftWorldPos.y, 1e-4f);
        Assert.AreEqual(expectedLeftWorld.z, targets.leftWorldPos.z, 1e-4f);

        Object.DestroyImmediate(go);
    }

    [Test]
    public void TestReachConstraintsAndInvalidOutputs()
    {
        var converter = new PolarIKTargetConverter();
        float[] invalidOutputs = new float[] { float.NaN, float.PositiveInfinity, -100f, float.NegativeInfinity, float.NaN, 100f };

        var targets = converter.ConvertOutputs(invalidOutputs, null);
        Assert.IsFalse(float.IsNaN(targets.rightLocalPos.x));
        Assert.IsFalse(float.IsNaN(targets.rightLocalPos.y));
        Assert.IsFalse(float.IsNaN(targets.rightLocalPos.z));
        Assert.IsFalse(float.IsNaN(targets.leftLocalPos.x));
        Assert.IsFalse(float.IsNaN(targets.leftLocalPos.y));
        Assert.IsFalse(float.IsNaN(targets.leftLocalPos.z));

        // Test shoulder reach clamping
        Vector3 shoulderLocal = new Vector3(0.2f, 1.2f, 0f);
        float maxArm = 0.6f;
        float[] reachOutputs = new float[] { 1f, 1f, 1f, 1f, 1f, 1f }; // Maximum radius
        var clampedTargets = converter.ConvertOutputs(reachOutputs, null, shoulderLocal, -shoulderLocal, maxArm, maxArm);

        float distR = Vector3.Distance(clampedTargets.rightLocalPos, shoulderLocal);
        Assert.LessOrEqual(distR, maxArm + converter.RightLimits.reachTolerance + 1e-4f);
    }
}
