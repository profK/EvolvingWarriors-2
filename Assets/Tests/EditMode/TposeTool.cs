
using UnityEngine;
using UnityEditor;

public static class HumanoidTPoseTool
{
    [MenuItem("Tools/Humanoid/Set T-Pose")]
    public static void SetTPose()
    {
        GameObject selected = Selection.activeGameObject;

        if (selected == null)
        {
            Debug.LogError("Select a humanoid character.");
            return;
        }

        Animator animator =
            selected.GetComponentInChildren<Animator>();

        if (animator == null ||
            animator.avatar == null ||
            !animator.avatar.isValid ||
            !animator.avatar.isHuman)
        {
            Debug.LogError("Valid Humanoid Animator required.");
            return;
        }

        Transform[] bones =
            animator.GetComponentsInChildren<Transform>(true);

        Undo.RecordObjects(bones, "Set Humanoid T-Pose");

        // Character-relative directions.
        Vector3 right = animator.transform.right;
        Vector3 down = -animator.transform.up;

        // Arms
        AlignChain(
            animator,
            HumanBodyBones.LeftUpperArm,
            HumanBodyBones.LeftLowerArm,
            HumanBodyBones.LeftHand,
            -right
        );

        AlignChain(
            animator,
            HumanBodyBones.RightUpperArm,
            HumanBodyBones.RightLowerArm,
            HumanBodyBones.RightHand,
            right
        );

        // Legs
        AlignChain(
            animator,
            HumanBodyBones.LeftUpperLeg,
            HumanBodyBones.LeftLowerLeg,
            HumanBodyBones.LeftFoot,
            down
        );

        AlignChain(
            animator,
            HumanBodyBones.RightUpperLeg,
            HumanBodyBones.RightLowerLeg,
            HumanBodyBones.RightFoot,
            down
        );

        foreach (Transform bone in bones)
        {
            EditorUtility.SetDirty(bone);

            if (PrefabUtility.IsPartOfPrefabInstance(bone))
            {
                PrefabUtility.RecordPrefabInstancePropertyModifications(
                    bone);
            }
        }

        Debug.Log("Full T-pose applied to " + selected.name);
    }

    private static void AlignChain(
        Animator animator,
        HumanBodyBones upper,
        HumanBodyBones lower,
        HumanBodyBones end,
        Vector3 direction)
    {
        Transform upperBone = animator.GetBoneTransform(upper);
        Transform lowerBone = animator.GetBoneTransform(lower);
        Transform endBone = animator.GetBoneTransform(end);

        if (upperBone == null ||
            lowerBone == null ||
            endBone == null)
        {
            Debug.LogWarning(
                "Missing bone in chain: " + upper);
            return;
        }

        AlignBone(upperBone, lowerBone, direction);
        AlignBone(lowerBone, endBone, direction);
    }

    private static void AlignBone(
        Transform bone,
        Transform child,
        Vector3 targetDirection)
    {
        Vector3 currentDirection =
            child.position - bone.position;

        if (currentDirection.sqrMagnitude < 0.000001f)
            return;

        Quaternion rotation =
            Quaternion.FromToRotation(
                currentDirection.normalized,
                targetDirection.normalized
            );

        bone.rotation = rotation * bone.rotation;
    }

    [MenuItem("Tools/Humanoid/Set T-Pose", true)]
    private static bool Validate()
    {
        return Selection.activeGameObject != null;
    }
}
