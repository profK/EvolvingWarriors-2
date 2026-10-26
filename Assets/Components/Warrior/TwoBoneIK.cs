using UnityEngine;

/// <summary>
/// Static utility for two-bone inverse kinematics solving.
/// Uses an analytic law-of-cosines approach to position a two-bone chain
/// (upper leg → lower leg → foot) toward a target world position.
/// Only modifies localRotation on the upper leg and lower leg transforms;
/// bone localPositions are never changed.
/// </summary>
public static class TwoBoneIK
{
    /// <summary>
    /// Anatomical knee bend limit in degrees. 0° = fully extended, 160° = maximum bend.
    /// </summary>
    const float MaxKneeAngleDegrees = 160f;
    const float MaxKneeAngleRad = 160f * Mathf.Deg2Rad;

    /// <summary>
    /// Minimum distance from hip to target before the solver considers the request degenerate.
    /// </summary>
    const float MinHipToTargetDistance = 0.001f;

    /// <summary>
    /// Checks whether a float value is valid (not NaN or Infinity).
    /// </summary>
    static bool IsValid(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

    /// <summary>
    /// Solves two-bone IK for a single leg chain.
    /// </summary>
    /// <param name="targetPosition">Desired world position for the foot.</param>
    /// <param name="rootTransform">The root (hips) transform of the character.</param>
    /// <param name="upperLegTransform">Transform of the upper leg bone (thigh).</param>
    /// <param name="lowerLegTransform">Transform of the lower leg bone (shin).</param>
    /// <param name="footTransform">Transform of the foot bone (the effector).</param>
    /// <param name="groundY">Y coordinate of the ground plane; targets below this are clamped.</param>
    /// <returns>True if the solver successfully positioned the leg; false on failure (transforms unchanged).</returns>
    public static bool Solve(
        Vector3 targetPosition,
        Transform rootTransform,
        Transform upperLegTransform,
        Transform lowerLegTransform,
        Transform footTransform,
        float groundY)
    {
        // Validate input transforms
        if (rootTransform == null || upperLegTransform == null ||
            lowerLegTransform == null || footTransform == null)
            return false;

        // Clamp target Y to ground if below ground plane
        if (targetPosition.y < groundY)
            targetPosition.y = groundY;

        // Compute bone lengths from current world positions
        Vector3 hipPos = upperLegTransform.position;
        Vector3 kneePos = lowerLegTransform.position;
        Vector3 anklePos = footTransform.position;

        float upperLegLen = (kneePos - hipPos).magnitude;
        float lowerLegLen = (anklePos - kneePos).magnitude;

        // Guard against degenerate bone lengths
        if (upperLegLen < 0.001f || lowerLegLen < 0.001f)
            return false;

        // Vector from hip to target
        Vector3 hipToTarget = targetPosition - hipPos;
        float dist = hipToTarget.magnitude;

        // Edge case: zero distance is degenerate
        if (dist < MinHipToTargetDistance)
            return false;

        // Edge case: target beyond full leg extension — gracefully degrade
        float maxReach = upperLegLen + lowerLegLen;
        if (dist > maxReach)
        {
            // Scale direction to max reach
            hipToTarget *= maxReach / dist;
            dist = maxReach;
        }

        // Law of cosines: find the angle at the hip joint
        // a² + b² - 2ab·cos(C) = c²
        // Where: a = upperLegLen, b = dist (hip to target), c = lowerLegLen
        // cos(C) = (a² + b² - c²) / (2ab)
        float cosAngle = (upperLegLen * upperLegLen + dist * dist - lowerLegLen * lowerLegLen)
                        / (2f * upperLegLen * dist);

        // Clamp to valid acos range
        cosAngle = Mathf.Clamp(cosAngle, -1f, 1f);

        // Compute knee bend angle (angle between upper leg direction and hip-to-target direction)
        float kneeAngle = Mathf.Acos(cosAngle);

        // Clamp knee angle to anatomical limits [0°, 160°]
        if (kneeAngle > MaxKneeAngleRad)
        {
            kneeAngle = MaxKneeAngleRad;
            // Recalculate the effective distance with the clamped angle
            // dist' = sqrt(a² + c² - 2ac·cos(kneeAngle))
            float newDistSq = upperLegLen * upperLegLen + lowerLegLen * lowerLegLen
                             - 2f * upperLegLen * lowerLegLen * Mathf.Cos(kneeAngle);
            float newDist = Mathf.Sqrt(Mathf.Max(0f, newDistSq));
            if (newDist > MinHipToTargetDistance)
            {
                hipToTarget = (targetPosition - hipPos);
                if (hipToTarget.sqrMagnitude > 0.000001f)
                {
                    hipToTarget = hipToTarget.normalized * newDist;
                    dist = newDist;
                }
            }
        }

        // Guard against NaN/Infinity in angle computation
        if (!IsValid(kneeAngle))
            return false;

        // Direction from hip to target (normalized)
        Vector3 dir = hipToTarget.normalized;
        if (dir.sqrMagnitude < 0.000001f)
            return false;

        // Compute bend normal: the perpendicular direction the knee bends toward
        // Use the character's forward vector as the primary bend axis so knees bend in the
        // sagittal plane (forward/backward relative to the character), not toward world-up.
        Vector3 bendAxis = rootTransform.forward;
        Vector3 sideDir = Vector3.Cross(dir, bendAxis);
        float sideMag = sideDir.magnitude;

        Vector3 kneeTarget;
        Vector3 bendNormal;

        if (sideMag < 0.001f)
        {
            // dir is nearly parallel to forward — use world up as fallback
            sideDir = Vector3.Cross(dir, Vector3.up);
            float fallbackMag = sideDir.magnitude;
            if (fallbackMag < 0.001f)
            {
                // dir is nearly parallel to both forward and up — degenerate, just extend straight
                kneeTarget = hipPos + dir * upperLegLen;
                bendNormal = Vector3.up;
            }
            else
            {
                sideDir /= fallbackMag;
                bendNormal = Vector3.Cross(dir, sideDir);
                bendNormal /= bendNormal.magnitude;

                float adj = Mathf.Cos(kneeAngle) * upperLegLen;
                float opp = Mathf.Sin(kneeAngle) * upperLegLen;

                // Apply bend sign even in fallback path so knees bend backward for forward foot placements.
                // bendNormal (Cross(dir, sideDir)) points DOWN when foot is forward and below hip.
                // bendSign = -1 for forward targets, so knee offset = -bendNormal*opp points UP.
                float forwardDot = Vector3.Dot(dir, bendAxis);
                float bendSign = forwardDot > 0f ? -1f : 1f;

                kneeTarget = hipPos + dir * adj + bendNormal * opp * bendSign;
            }
        }
        else
        {
            sideDir /= sideMag; // normalized side direction
            // bendNormal is perpendicular to both dir and sideDir. When the foot target is below
            // the hip, bendNormal points DOWN (toward ground). The knee bends along -bendNormal (UP).
            bendNormal = Vector3.Cross(dir, sideDir);
            if (bendNormal.sqrMagnitude < 0.001f)
            {
                // Degenerate case — just extend straight
                kneeTarget = hipPos + dir * upperLegLen;
                bendNormal = Vector3.up;
            }
            else
            {
                bendNormal /= bendNormal.magnitude;

                float adj = Mathf.Cos(kneeAngle) * upperLegLen;  // distance along dir
                float opp = Mathf.Sin(kneeAngle) * upperLegLen;  // distance perpendicular (bend amount)

                // Determine bend sign: the knee should bend "backward" relative to the character.
                // bendNormal (Cross(dir, sideDir)) points DOWN when foot is forward and below hip.
                // bendSign = -1 for forward targets, so knee offset = -bendNormal*opp points UP
                // (knee bends upward/backward). bendSign = +1 for rear targets.
                float forwardDot = Vector3.Dot(dir, bendAxis);
                float bendSign = forwardDot > 0f ? -1f : 1f;

                kneeTarget = hipPos + dir * adj + bendNormal * opp * bendSign;
            }
        }

        // Guard against NaN/Infinity in knee target position
        if (!IsValid(kneeTarget.x) || !IsValid(kneeTarget.y) || !IsValid(kneeTarget.z))
            return false;

        // --- Set upper leg rotation ---
        // upperLeg should point from hip toward knee
        Vector3 upperLegDir = kneeTarget - hipPos;
        if (upperLegDir.sqrMagnitude < 0.000001f)
            return false;

        Quaternion targetUpperRot = Quaternion.LookRotation(upperLegDir, bendNormal);

        // Guard against NaN/Infinity in computed rotation
        if (!IsValid(targetUpperRot.x) || !IsValid(targetUpperRot.y) ||
            !IsValid(targetUpperRot.z) || !IsValid(targetUpperRot.w))
            return false;

        // Convert world rotation to local rotation for upper leg
        if (upperLegTransform.parent != null)
        {
            upperLegTransform.localRotation = Quaternion.Inverse(upperLegTransform.parent.rotation) * targetUpperRot;
        }
        else
        {
            upperLegTransform.localRotation = targetUpperRot;
        }

        // --- Set lower leg rotation ---
        // lowerLeg should point from knee toward foot target
        Vector3 lowerLegDir = targetPosition - kneeTarget;
        if (lowerLegDir.sqrMagnitude < 0.000001f)
        {
            // Degenerate: knee is at target, just point down
            lowerLegDir = -bendNormal;
        }

        Quaternion targetLowerRot = Quaternion.LookRotation(lowerLegDir, bendNormal);

        // Guard against NaN/Infinity in computed rotation
        if (!IsValid(targetLowerRot.x) || !IsValid(targetLowerRot.y) ||
            !IsValid(targetLowerRot.z) || !IsValid(targetLowerRot.w))
            return false;

        // Convert world rotation to local rotation for lower leg
        if (lowerLegTransform.parent != null)
        {
            lowerLegTransform.localRotation = Quaternion.Inverse(lowerLegTransform.parent.rotation) * targetLowerRot;
        }
        else
        {
            lowerLegTransform.localRotation = targetLowerRot;
        }

        // --- Set foot rotation ---
        // Foot sole must always face the ground. bendNormal (Cross(dir, sideDir)) points DOWN
        // when the foot target is forward and below the hip, but points UP when the target is
        // behind the hip. To handle both cases, sign bendNormal so its Y component is always
        // negative (points toward ground). Unity's humanoid rig convention has the foot bone's
        // local Y-axis pointing DOWN through the sole, so mapping sole_dir to local Y achieves
        // sole-faces-ground regardless of target direction.
        // For the foot's forward direction, project the character's forward vector onto the
        // plane perpendicular to soleDir. This keeps the foot facing the character's forward
        // direction while staying orthogonal to the sole direction.
        Vector3 soleDir;
        if (Mathf.Abs(bendNormal.y) < 0.001f)
        {
            // bendNormal is nearly horizontal (degenerate side case) — use pure down
            soleDir = Vector3.down;
        }
        else if (bendNormal.y < 0f)
        {
            soleDir = bendNormal; // already points down
        }
        else
        {
            soleDir = -bendNormal; // negate so it points down
        }

        // Project character forward onto the plane perpendicular to soleDir
        Vector3 footForward = rootTransform.forward - soleDir * Vector3.Dot(rootTransform.forward, soleDir);
        if (footForward.sqrMagnitude >= 0.000001f)
        {
            footForward.Normalize();
        }
        else
        {
            // Character forward is parallel to soleDir (degenerate) — use dir horizontal component
            Vector3 dirHorizontal = new Vector3(dir.x, 0f, dir.z);
            if (dirHorizontal.sqrMagnitude > 0.001f)
                footForward = dirHorizontal.normalized;
            else
                footForward = sideDir.normalized; // last resort fallback
        }

        Quaternion targetFootRot = Quaternion.LookRotation(footForward, soleDir);
        // Guard against NaN/Infinity in computed rotation
        if (!IsValid(targetFootRot.x) || !IsValid(targetFootRot.y) ||
            !IsValid(targetFootRot.z) || !IsValid(targetFootRot.w))
            return false;

        // Convert world rotation to local rotation for foot
        if (footTransform.parent != null)
        {
            footTransform.localRotation = Quaternion.Inverse(footTransform.parent.rotation) * targetFootRot;
        }
        else
        {
            footTransform.localRotation = targetFootRot;
        }

        return true;
    }
}
