using UnityEngine;

/// <summary>
/// Spawns warrior pairs in a grid layout, facing each other at sword-length distance.
/// 
/// Layout:
/// - Pairs are placed in a grid pattern on the XZ plane.
/// - Each pair faces each other along the Z axis.
/// - Pair separation: distance between the two warriors in a pair (default 1.5m ≈ sword reach).
/// - Inter-pair distance: spacing between adjacent pairs in the grid (default 6m).
/// </summary>
public class WarriorSpawner : MonoBehaviour
{
    [Header("Warrior Prefab")]
    [Tooltip("The warrior prefab to instantiate. Must have WarriorAgent, WarriorBoneController, and WarriorObservationEncoder components.")]
    [SerializeField] GameObject warriorPrefab;
    public GameObject WarriorPrefab { get => warriorPrefab; set => warriorPrefab = value; }

    [Header("Layout")]
    [Tooltip("Number of warrior pairs to spawn (total warriors = pairs × 2).")]
    [SerializeField] int pairCount=10;
    public int PairCount { get => pairCount; set => pairCount = value; }

    [Tooltip("Distance between the two warriors in each pair. Default 1.5m (approximate sword reach).")]
    [SerializeField] float pairSeparation = 1.5f;
    public float PairSeparation { get => pairSeparation; set => pairSeparation = value; }

    [Tooltip("Distance between adjacent pairs in the grid.")]
    [SerializeField] float interPairDistance = 6f;
    public float InterPairDistance { get => interPairDistance; set => interPairDistance = value; }

    /// <summary>The spawned warrior agents, ordered by pair: [A0, B0, A1, B1, ...]</summary>
    GameObject[] spawnedWarriors;

    /// <summary>Destroys all currently spawned warriors.</summary>
    public void ClearWarriors()
    {
        if (spawnedWarriors != null)
        {
            foreach (GameObject warrior in spawnedWarriors)
            {
                if (warrior != null)
                    Destroy(warrior);
            }
        }
        spawnedWarriors = null;
    }

    /// <summary>
    /// Spawns warrior pairs in a grid layout.
    /// Warriors in each pair face each other along the Z axis.
    /// </summary>
    /// <returns>Array of spawned warrior GameObjects, ordered by pair.</returns>
    public GameObject[] SpawnWarriors()
    {
        ClearWarriors();

        int totalWarriors = pairCount * 2;
        spawnedWarriors = new GameObject[totalWarriors];

        // Calculate grid dimensions
        int cols = Mathf.CeilToInt(Mathf.Sqrt(pairCount));
        int rows = Mathf.CeilToInt((float)pairCount / cols);

        // Center the grid around origin
        float gridOffsetX = -(cols - 1) * interPairDistance * 0.5f;
        float gridOffsetZ = -(rows - 1) * interPairDistance * 0.5f;

        int pairIndex = 0;
        for (int row = 0; row < rows && pairIndex < pairCount; row++)
        {
            for (int col = 0; col < cols && pairIndex < pairCount; col++)
            {
                // Center position for this pair
                Vector3 centerPos = new Vector3(
                    gridOffsetX + col * interPairDistance,
                    0f,
                    gridOffsetZ + row * interPairDistance);

                // Warrior A: offset -Z/2, facing +Z (toward opponent)
                Vector3 posA = centerPos + Vector3.forward * (pairSeparation * 0.5f);
                Quaternion rotA = Quaternion.LookRotation(-Vector3.forward); // Face right (90° Y rotation)

                // Warrior B: offset +Z/2, facing -Z (toward opponent)
                Vector3 posB = centerPos - Vector3.forward * (pairSeparation * 0.5f);
                Quaternion rotB = Quaternion.LookRotation(Vector3.forward); // Face left (270° Y rotation)

                // Instantiate warrior A
                GameObject warriorA = Instantiate(warriorPrefab, posA, rotA);
                spawnedWarriors[pairIndex * 2] = warriorA;

                // Instantiate warrior B
                GameObject warriorB = Instantiate(warriorPrefab, posB, rotB);
                spawnedWarriors[pairIndex * 2 + 1] = warriorB;

                pairIndex++;
            }
        }

        return spawnedWarriors;
    }

    /// <summary>Gets the spawned warrior instances.</summary>
    public GameObject[] GetSpawnedWarriors() => spawnedWarriors;
}
