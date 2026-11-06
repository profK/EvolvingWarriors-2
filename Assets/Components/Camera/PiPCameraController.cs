using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Assembly-CSharp-Editor")]

/// <summary>
/// Picture-in-picture camera controller that displays the highest-scoring warrior's combat pair.
/// 
/// Features:
/// - Fixed three-quarter viewing angle (configurable horizontal angle + elevation)
/// - Auto-detects highest-scoring warrior via population scanning
/// - Frames both warriors tightly with adaptive distance based on FOV/aspect
/// - Smooth transitions when switching pairs (~0.75s default)
/// - PiP UI overlay in upper-right corner (25% width, 16:9 aspect)
/// - Handles round resets, generation changes, destroyed warriors, ties, no-valid-pair
/// - Periodic leader checks to avoid per-frame scene searches
/// </summary>
public class PiPCameraController : MonoBehaviour
{
    #region Inspector Fields

    [Header("References")]
    [Tooltip("The secondary camera that renders the PiP view.")]
    [SerializeField] Camera pipCamera;

    [Tooltip("The RenderTexture the PiP camera renders to.")]
    [SerializeField] RenderTexture renderTexture;

    [Tooltip("The UI RawImage that displays the PiP RenderTexture.")]
    [SerializeField] RawImage pipRawImage;

    [Tooltip("The EvolutionSimulationManager to read state from. Auto-resolved if not assigned.")]
    [SerializeField] internal EvolutionSimulationManager simulationManager;

    [Header("Camera Angle")]
    [Tooltip("Horizontal angle from the pair midpoint to the camera (35-45° recommended).")]
    [Range(10f, 60f)]
    [SerializeField] float horizontalAngle = 40f;

    [Tooltip("Vertical elevation angle above the pair (15-25° recommended).")]
    [Range(5f, 45f)]
    [SerializeField] float elevationAngle = 20f;

    [Tooltip("Field of view for the PiP camera (~50° recommended).")]
    [Range(20f, 90f)]
    [SerializeField] float fieldOfView = 50f;

    [Header("Framing")]
    [Tooltip("Extra horizontal padding around the pair in meters.")]
    [Range(0.1f, 3f)]
    [SerializeField] float horizontalPadding = 0.8f;

    [Tooltip("Extra vertical padding around the pair in meters.")]
    [Range(0.1f, 3f)]
    [SerializeField] float verticalPadding = 1.0f;

    [Tooltip("Reference height used to estimate warrior bounding box when warriors are not yet positioned.")]
    [Range(0.3f, 2f)]
    [SerializeField] float referenceWarriorHeight = 1.2f;

    [Header("Transitions")]
    [Tooltip("Duration of smooth camera position/rotation transitions (0 = instant).")]
    [Range(0f, 2f)]
    [SerializeField] float transitionDuration = 0.75f;

    [Tooltip("Enable smooth transitions. When disabled, camera snaps instantly.")]
    [SerializeField] bool smoothTransitions = true;

    [Header("Leader Detection")]
    [Tooltip("Interval in seconds between checking for the highest-scoring warrior.")]
    [Range(0.1f, 5f)]
    [SerializeField] float leaderCheckInterval = 1.0f;

    [Tooltip("Minimum score difference to trigger a camera switch. Prevents flickering on ties.")]
    [Range(0f, 10f)]
    [SerializeField] float minimumScoreDelta = 2f;

    [Header("Visibility")]
    [Tooltip("Show PiP only during Running and Evaluating states.")]
    [SerializeField] bool hideDuringNonActiveStates = true;

    [Tooltip("Manually enable/disable the PiP overlay.")]
    [SerializeField] bool enablePiP = true;

    #endregion

    #region Internal State

    private WarriorAgent currentTargetA;
    private WarriorAgent currentTargetB;
    private Vector3 targetPosition;
    private Vector3 targetLookAt;
    private float transitionProgress;
    private bool isTransitioning;
    private float leaderCheckTimer;
    private List<WarriorAgent> cachedPopulation;

    #endregion

    #region Public Properties

    public Camera PiPCamera
    {
        get => pipCamera;
        set => pipCamera = value;
    }

    public RenderTexture RenderTexture
    {
        get => renderTexture;
        set => renderTexture = value;
    }

    public RawImage PiPRawImage
    {
        get => pipRawImage;
        set => pipRawImage = value;
    }

    public EvolutionSimulationManager SimulationManager
    {
        get => simulationManager;
        set => simulationManager = value;
    }

    #endregion

    #region Unity Lifecycle

    private void Awake()
    {
        // Auto-resolve references if not assigned
        if (simulationManager == null)
        {
            simulationManager = FindObjectOfType<EvolutionSimulationManager>();
        }

        // Configure the PiP camera
        if (pipCamera != null)
        {
            pipCamera.fieldOfView = fieldOfView;
            pipCamera.targetTexture = renderTexture;
            pipCamera.cullingMask &= ~(1 << LayerMask.NameToLayer("UI"));
        }

        // Wire the RawImage
        if (pipRawImage != null && renderTexture != null)
        {
            pipRawImage.texture = renderTexture;
        }

        // Initial visibility
        UpdateVisibility();
        transitionProgress = 1f; // start fully transitioned
    }

    private void Start()
    {
        // Initialize cached population
        cachedPopulation = new List<WarriorAgent>();
        leaderCheckTimer = leaderCheckInterval; // check immediately on first update
    }

    private void Update()
    {
        if (!enablePiP || pipCamera == null) return;

        // Handle transitions
        if (isTransitioning)
        {
            transitionProgress += Time.deltaTime / Mathf.Max(transitionDuration, 0.01f);

            if (transitionProgress >= 1f)
            {
                transitionProgress = 1f;
                isTransitioning = false;
                pipCamera.transform.position = targetPosition;
                pipCamera.transform.LookAt(targetLookAt);
            }
            else
            {
                float eased = SmoothStep(transitionProgress);
                Vector3 startPos = transitionStartPosition;
                Vector3 startLook = transitionStartLookAt;
                pipCamera.transform.position = Vector3.LerpUnclamped(startPos, targetPosition, eased);
                Vector3 lerpedLook = Vector3.LerpUnclamped(startLook, targetLookAt, eased);
                pipCamera.transform.LookAt(lerpedLook);
            }
        }

        // Periodic leader check
        leaderCheckTimer -= Time.deltaTime;
        if (leaderCheckTimer <= 0f)
        {
            leaderCheckTimer = leaderCheckInterval;
            RefreshPopulationAndLeader();
        }

        // Track current targets if set
        if (currentTargetA != null && currentTargetB != null)
        {
            UpdateCameraToFollowPair();
        }

        // Visibility updates
        UpdateVisibility();
    }

    #endregion

    #region Population & Leader Detection

    /// <summary>
    /// Refresh the cached population and check for a new leader.
    /// Call this periodically or after a round reset.
    /// </summary>
    public void RefreshPopulationAndLeader()
    {
        if (simulationManager == null) return;

        var spawner = simulationManager.Spawner;
        if (spawner == null) return;

        var spawnedWarriors = spawner.GetSpawnedWarriors();
        if (spawnedWarriors == null || spawnedWarriors.Length == 0)
        {
            ClearTargets();
            return;
        }

        // Cache the population
        cachedPopulation.Clear();
        foreach (var warriorGO in spawnedWarriors)
        {
            if (warriorGO != null)
            {
                var agent = warriorGO.GetComponent<WarriorAgent>();
                if (agent != null && agent.IsActive)
                {
                    cachedPopulation.Add(agent);
                }
            }
        }

        if (cachedPopulation.Count == 0)
        {
            ClearTargets();
            return;
        }

        // Find highest-scoring warrior
        WarriorAgent leader = null;
        float highestScore = float.NegativeInfinity;

        foreach (var agent in cachedPopulation)
        {
            if (agent.Score > highestScore)
            {
                highestScore = agent.Score;
                leader = agent;
            }
        }

        if (leader == null)
        {
            ClearTargets();
            return;
        }

        // Check if we need to switch targets
        bool needSwitch = false;

        if (currentTargetA == null || currentTargetB == null)
        {
            needSwitch = true;
        }
        else if (leader != currentTargetA && leader != currentTargetB)
        {
            // Leader changed - check score delta
            float currentBestScore = Mathf.Max(currentTargetA.Score, currentTargetB.Score);
            if (leader.Score - currentBestScore >= minimumScoreDelta)
            {
                needSwitch = true;
            }
        }

        if (needSwitch)
        {
            SetTargetPair(leader);
        }
    }

    /// <summary>
    /// Set the PiP camera to track a specific warrior and their opponent.
    /// </summary>
    public void SetTargetPair(WarriorAgent warrior)
    {
        if (warrior == null || !warrior.IsActive)
        {
            ClearTargets();
            return;
        }

        WarriorAgent opponent = warrior.Opponent;
        if (opponent == null || !opponent.IsActive)
        {
            // No valid opponent, skip
            return;
        }

        currentTargetA = warrior;
        currentTargetB = opponent;

        // Start transition
        StartTransition();
    }

    /// <summary>
    /// Clear the current PiP targets.
    /// </summary>
    public void ClearTargets()
    {
        currentTargetA = null;
        currentTargetB = null;
        isTransitioning = false;
    }

    #endregion

    #region Camera Positioning

    /// <summary>
    /// Update camera position to follow the current pair.
    /// The camera maintains a fixed directional angle but adjusts distance to frame both warriors.
    /// </summary>
    private void UpdateCameraToFollowPair()
    {
        if (currentTargetA == null || currentTargetB == null) return;

        // Midpoint between the two warriors
        Vector3 midpoint = (currentTargetA.transform.position + currentTargetB.transform.position) * 0.5f;

        // Compute required distance to frame both warriors
        float distance = CalculateFramingDistance(midpoint);

        // Compute camera direction from angles
        Vector3 direction = CalculateCameraDirection();

        // Target position and look-at
        Vector3 newPos = (midpoint - direction * distance)+
                         new Vector3(0,5,0);
        Vector3 newLook = midpoint;

        // Apply transition or direct
        if (smoothTransitions && isTransitioning)
        {
            // Handled in Update()
            targetPosition = newPos;
            targetLookAt = newLook;
        }
        else if (!smoothTransitions)
        {
            pipCamera.transform.position = newPos;
            pipCamera.transform.LookAt(newLook);
        }
        else
        {
            // No active transition, just update directly
            pipCamera.transform.position = newPos;
            pipCamera.transform.LookAt(newLook);
        }
    }

    /// <summary>
    /// Calculate the camera distance needed to frame both warriors within the viewport.
    /// Uses FOV, aspect ratio, and padding to compute the required distance.
    /// </summary>
    private float CalculateFramingDistance(Vector3 midpoint)
    {
        // Get bounding extents of both warriors
        Vector3 combinedExtents = CalculatePairExtents();

        // Vertical FOV in radians
        float fovRad = Mathf.Deg2Rad * fieldOfView;

        // Distance needed for vertical framing
        float requiredHeight = combinedExtents.y * 2f + verticalPadding;
        float verticalDistance = requiredHeight / (2f * Mathf.Tan(fovRad * 0.5f));

        // Distance needed for horizontal framing (account for aspect)
        float aspect = (float)Screen.width / (float)Screen.height;
        float horizontalFOV = 2f * Mathf.Atan(Mathf.Tan(fovRad * 0.5f) * aspect);
        float requiredWidth = combinedExtents.x * 2f + horizontalPadding;
        float horizontalDistance = requiredWidth / (2f * Mathf.Tan(horizontalFOV * 0.5f));

        // Use the larger distance to ensure both fit
        return Mathf.Max(verticalDistance, horizontalDistance);
    }

    /// <summary>
    /// Calculate the combined bounding extents of both warriors relative to their midpoint.
    /// </summary>
    private Vector3 CalculatePairExtents()
    {
        if (currentTargetA == null || currentTargetB == null)
            return new Vector3(1f, referenceWarriorHeight * 0.5f, 1f);

        Vector3 posA = currentTargetA.transform.position;
        Vector3 posB = currentTargetB.transform.position;
        Vector3 midpoint = (posA + posB) * 0.5f;

        // Offset from midpoint
        Vector3 offsetA = posA - midpoint;
        Vector3 offsetB = posB - midpoint;

        // Horizontal extent: max x distance + warrior width
        float warriorWidth = 0.4f; // approximate warrior width
        float maxX = Mathf.Max(Mathf.Abs(offsetA.x), Mathf.Abs(offsetB.x)) + warriorWidth;

        // Vertical extent: use reference height (warriors are roughly same height)
        float maxY = referenceWarriorHeight * 0.5f;

        // We only care about X (horizontal spread) and Y (height) for framing
        return new Vector3(maxX, maxY);
    }

    /// <summary>
    /// Calculate the camera direction vector from the configured angles.
    /// The camera is placed at (horizontalAngle, elevationAngle) relative to the pair.
    /// </summary>
    private Vector3 CalculateCameraDirection()
    {
        float hRad = Mathf.Deg2Rad * horizontalAngle;
        float eRad = Mathf.Deg2Rad * elevationAngle;

        // Direction from midpoint to camera (positive X = right, positive Y = up, positive Z = forward)
        // We want the camera offset along X and Z based on horizontal angle, and Y for elevation
        float horizontalLength = Mathf.Cos(eRad);
        float x = Mathf.Sin(hRad) * horizontalLength;
        float z = Mathf.Cos(hRad) * horizontalLength;
        float y = Mathf.Sin(eRad);

        return new Vector3(x, y, z).normalized;
    }

    #endregion

    #region Transitions

    private Vector3 transitionStartPosition;
    private Vector3 transitionStartLookAt;

    private void StartTransition()
    {
        if (pipCamera == null) return;

        transitionStartPosition = pipCamera.transform.position;
        transitionStartLookAt = pipCamera.transform.position
            + pipCamera.transform.forward * Mathf.Max(targetPosition.magnitude, 10f);

        // If the camera hasn't moved yet, use a sensible default start position
        if (transitionStartPosition == Vector3.zero)
        {
            Vector3 defaultMid = new Vector3(0f, 20f, 0f);
            Vector3 dir = CalculateCameraDirection();
            transitionStartPosition = defaultMid - dir * 8f;
            transitionStartLookAt = defaultMid;
        }

        transitionProgress = 0f;
        isTransitioning = true;
    }

    #endregion

    #region Visibility

    private void UpdateVisibility()
    {
        bool shouldShow = enablePiP;

        if (hideDuringNonActiveStates && simulationManager != null)
        {
            SimulationState current = simulationManager.State;
            shouldShow = (current == SimulationState.Running || current == SimulationState.Evaluating);
        }

        // Apply visibility
        if (pipRawImage != null)
            pipRawImage.gameObject.SetActive(shouldShow);

        if (pipCamera != null)
            pipCamera.enabled = shouldShow;
    }

    #endregion

    #region Utilities

    private static float SmoothStep(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }

    #endregion
}