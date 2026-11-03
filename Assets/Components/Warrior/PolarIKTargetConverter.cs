using UnityEngine;

/// <summary>
/// Converts 6 neural network outputs into bounded spherical/polar coordinates
/// and transforms them into 3D Cartesian coordinates for left and right hand IK targets.
/// 
/// Coordinate system convention:
/// - Origin: Warrior model root.
/// - Unity local +Y is up.
/// - Unity local +Z is forward.
/// - Azimuth (theta) is rotation around local Y axis (0 = +Z, positive = toward +X).
/// - Elevation (phi) is angle above/below local XZ plane (0 = XZ plane, positive = +Y).
/// </summary>
public class PolarIKTargetConverter
{
    [System.Serializable]
    public struct HandLimits
    {
        public float minRadius;
        public float maxRadius;
        public float minAzimuthRad;
        public float maxAzimuthRad;
        public float minElevationRad;
        public float maxElevationRad;
        public float reachTolerance;

        public static HandLimits DefaultRight => new HandLimits
        {
            minRadius = 0.2f,
            maxRadius = 1.2f,
            minAzimuthRad = -Mathf.PI * 0.5f,  // -90 deg (across chest)
            maxAzimuthRad = Mathf.PI * 0.75f,  // +135 deg (to the right/back)
            minElevationRad = -Mathf.PI * 0.35f, // -63 deg (down)
            maxElevationRad = Mathf.PI * 0.45f,  // +81 deg (up)
            reachTolerance = 0.05f
        };

        public static HandLimits DefaultLeft => new HandLimits
        {
            minRadius = 0.2f,
            maxRadius = 1.2f,
            minAzimuthRad = -Mathf.PI * 0.75f, // -135 deg (to the left/back)
            maxAzimuthRad = Mathf.PI * 0.5f,   // +90 deg (across chest)
            minElevationRad = -Mathf.PI * 0.35f, // -63 deg (down)
            maxElevationRad = Mathf.PI * 0.45f,  // +81 deg (up)
            reachTolerance = 0.05f
        };
    }

    public HandLimits RightLimits { get; set; } = HandLimits.DefaultRight;
    public HandLimits LeftLimits { get; set; } = HandLimits.DefaultLeft;

    public struct SphericalCoord
    {
        public float radius;
        public float azimuth;
        public float elevation;
    }

    public struct HandTargets
    {
        public SphericalCoord rightSpherical;
        public SphericalCoord leftSpherical;
        public Vector3 rightLocalPos;
        public Vector3 leftLocalPos;
        public Vector3 rightWorldPos;
        public Vector3 leftWorldPos;
    }

    // Previous valid targets in case of NaN / Inf
    private Vector3 lastRightLocal = new Vector3(0.3f, 0.9f, 0.5f);
    private Vector3 lastLeftLocal = new Vector3(-0.3f, 0.9f, 0.5f);

    /// <summary>
    /// Converts a raw value from MLP [-1, 1] (or unbounded) to [minVal, maxVal].
    /// Clamps input to [-1, 1] first.
    /// </summary>
    public static float MapRange(float mlpVal, float minVal, float maxVal)
    {
        if (float.IsNaN(mlpVal) || float.IsInfinity(mlpVal))
            return (minVal + maxVal) * 0.5f;

        float clamped = Mathf.Clamp(mlpVal, -1f, 1f);
        float t01 = (clamped + 1f) * 0.5f;
        return Mathf.Lerp(minVal, maxVal, t01);
    }

    /// <summary>
    /// Converts spherical coordinates (radius, azimuth, elevation) to local Cartesian position.
    /// </summary>
    public static Vector3 SphericalToCartesian(float radius, float azimuth, float elevation)
    {
        float x = radius * Mathf.Cos(elevation) * Mathf.Sin(azimuth);
        float y = radius * Mathf.Sin(elevation);
        float z = radius * Mathf.Cos(elevation) * Mathf.Cos(azimuth);

        return new Vector3(x, y, z);
    }

    /// <summary>
    /// Processes 6 MLP outputs [rR, azR, elR, rL, azL, elL] and returns Cartesian hand targets.
    /// </summary>
    public HandTargets ConvertOutputs(float[] outputs, Transform rootTransform, Vector3? rightShoulderLocal = null, Vector3? leftShoulderLocal = null, float rightMaxArmLength = 0f, float leftMaxArmLength = 0f)
    {
        HandTargets result = new HandTargets();

        if (outputs == null || outputs.Length < 6)
        {
            result.rightLocalPos = lastRightLocal;
            result.leftLocalPos = lastLeftLocal;
            if (rootTransform != null)
            {
                result.rightWorldPos = rootTransform.TransformPoint(result.rightLocalPos);
                result.leftWorldPos = rootTransform.TransformPoint(result.leftLocalPos);
            }
            return result;
        }

        // Right hand: index 0, 1, 2
        float rR = MapRange(outputs[0], RightLimits.minRadius, RightLimits.maxRadius);
        float azR = MapRange(outputs[1], RightLimits.minAzimuthRad, RightLimits.maxAzimuthRad);
        float elR = MapRange(outputs[2], RightLimits.minElevationRad, RightLimits.maxElevationRad);

        result.rightSpherical = new SphericalCoord { radius = rR, azimuth = azR, elevation = elR };
        Vector3 rightLocal = SphericalToCartesian(rR, azR, elR);

        // Apply secondary shoulder-relative reach constraint if shoulder position is provided
        if (rightShoulderLocal.HasValue && rightMaxArmLength > 0f)
        {
            rightLocal = ConstrainToShoulderReach(rightLocal, rightShoulderLocal.Value, rightMaxArmLength + RightLimits.reachTolerance);
        }

        if (float.IsNaN(rightLocal.x) || float.IsInfinity(rightLocal.x))
            rightLocal = lastRightLocal;
        else
            lastRightLocal = rightLocal;

        result.rightLocalPos = rightLocal;

        // Left hand: index 3, 4, 5
        float rL = MapRange(outputs[3], LeftLimits.minRadius, LeftLimits.maxRadius);
        float azL = MapRange(outputs[4], LeftLimits.minAzimuthRad, LeftLimits.maxAzimuthRad);
        float elL = MapRange(outputs[5], LeftLimits.minElevationRad, LeftLimits.maxElevationRad);

        result.leftSpherical = new SphericalCoord { radius = rL, azimuth = azL, elevation = elL };
        Vector3 leftLocal = SphericalToCartesian(rL, azL, elL);

        // Apply secondary shoulder-relative reach constraint if shoulder position is provided
        if (leftShoulderLocal.HasValue && leftMaxArmLength > 0f)
        {
            leftLocal = ConstrainToShoulderReach(leftLocal, leftShoulderLocal.Value, leftMaxArmLength + LeftLimits.reachTolerance);
        }

        if (float.IsNaN(leftLocal.x) || float.IsInfinity(leftLocal.x))
            leftLocal = lastLeftLocal;
        else
            lastLeftLocal = leftLocal;

        result.leftLocalPos = leftLocal;

        if (rootTransform != null)
        {
            result.rightWorldPos = rootTransform.TransformPoint(result.rightLocalPos);
            result.leftWorldPos = rootTransform.TransformPoint(result.leftLocalPos);
        }
        else
        {
            result.rightWorldPos = result.rightLocalPos;
            result.leftWorldPos = result.leftLocalPos;
        }

        return result;
    }

    private static Vector3 ConstrainToShoulderReach(Vector3 targetLocal, Vector3 shoulderLocal, float maxReach)
    {
        Vector3 fromShoulder = targetLocal - shoulderLocal;
        float dist = fromShoulder.magnitude;
        if (dist > maxReach && dist > 0.0001f)
        {
            fromShoulder = (fromShoulder / dist) * maxReach;
            return shoulderLocal + fromShoulder;
        }
        return targetLocal;
    }
}
