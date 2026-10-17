using UnityEngine;

/// <summary>
/// Flying camera controller with drag-to-look and WASD movement.
/// WASD moves the camera forward/backward and strafe left/right relative to camera orientation.
/// Left mouse button drag controls turning: drag left turns left, drag right turns right.
/// Drag upward tilts the camera up, drag downward tilts the camera down.
/// </summary>
public class FirstPersonCameraController : MonoBehaviour
{
    [Header("Movement")]
    [Tooltip("Movement speed in units per second.")]
    [SerializeField] float moveSpeed = 5f;

    [Header("Mouse Look")]
    [Tooltip("Mouse sensitivity for yaw (horizontal rotation).")]
    [SerializeField] float mouseSensitivityX = 0.5f;

    [Tooltip("Mouse sensitivity for pitch (vertical rotation).")]
    [SerializeField] float mouseSensitivityY = 0.5f;

    [Tooltip("Minimum pitch angle (downwards).")]
    [SerializeField] float minPitch = -80f;

    [Tooltip("Maximum pitch angle (upwards).")]
    [SerializeField] float maxPitch = 80f;

    /// <summary>Current pitch angle in degrees.</summary>
    float pitchAngle;

    /// <summary>Current yaw angle in degrees.</summary>
    float yawAngle;

    /// <summary>Mouse position when drag started, used to compute drag delta.</summary>
    Vector2 lastMousePosition;

    void Start()
    {
        // Keep cursor visible for drag-to-look interaction.
        Cursor.lockState = CursorLockMode.None;
        Cursor.visible = true;

        // Initialize rotation from the camera's current transform.
        yawAngle = transform.eulerAngles.y;
        pitchAngle = transform.eulerAngles.x;
    }

    void Update()
    {
        HandleMouseLook();
        HandleMovement();
    }

    void HandleMouseLook()
    {
        if (Input.GetMouseButtonDown(0))
        {
            // Left mouse button pressed - record starting position for drag.
            lastMousePosition = Input.mousePosition;
            return;
        }

        if (Input.GetMouseButton(0))
        {
            // Left mouse button held - compute drag delta from last frame's position.
            Vector2 currentMousePosition = Input.mousePosition;
            Vector2 dragDelta = currentMousePosition - lastMousePosition;

            // Drag left turns left (negative yaw change).
            // Drag right turns right (positive yaw change).
            yawAngle += dragDelta.x * mouseSensitivityX;

            // Drag upward (positive y on screen) tilts up (positive pitch change).
            // Drag downward (negative y on screen) tilts down (negative pitch change).
            pitchAngle -= dragDelta.y * mouseSensitivityY;

            // Clamp pitch to avoid flipping.
            pitchAngle = Mathf.Clamp(pitchAngle, minPitch, maxPitch);

            // Update last position to current so we measure incremental drag.
            lastMousePosition = currentMousePosition;
        }

        // Apply rotation: yaw around Y axis, pitch around local X axis.
        transform.rotation = Quaternion.Euler(pitchAngle, yawAngle, 0f);
    }

    void HandleMovement()
    {
        // WASD movement relative to camera orientation.
        // W/S moves the camera along its local forward vector.
        // A/D strafes left/right relative to the camera's right vector.
        float moveForward = Input.GetAxis("Vertical");   // W = +1, S = -1
        float moveStrafe = Input.GetAxis("Horizontal");  // D = +1, A = -1

        // Use the camera's local forward and right vectors directly.
        // No projection or normalization — move exactly where the camera is pointing.
        Vector3 forward = transform.forward;
        Vector3 right = transform.right;

        // Calculate movement delta.
        Vector3 movement = (forward * moveForward + right * moveStrafe) * moveSpeed * Time.deltaTime;

        transform.position += movement;
    }
}
