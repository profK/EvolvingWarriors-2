using UnityEngine;

/// <summary>
/// First-person camera controller.
/// WASD moves the camera forward/backward and strafe left/right.
/// Mouse left/right rotates the camera counter-clockwise/clockwise (yaw).
/// Mouse up/down tilts the camera downwards/upwards (pitch).
/// </summary>
public class FirstPersonCameraController : MonoBehaviour
{
    [Header("Movement")]
    [Tooltip("Movement speed in units per second.")]
    [SerializeField] float moveSpeed = 5f;

    [Header("Mouse Look")]
    [Tooltip("Mouse sensitivity for yaw (horizontal rotation).")]
    [SerializeField] float mouseSensitivityX = 2f;

    [Tooltip("Mouse sensitivity for pitch (vertical rotation).")]
    [SerializeField] float mouseSensitivityY = 2f;

    [Tooltip("Minimum pitch angle (downwards).")]
    [SerializeField] float minPitch = -80f;

    [Tooltip("Maximum pitch angle (upwards).")]
    [SerializeField] float maxPitch = 80f;

    /// <summary>Current pitch angle in degrees.</summary>
    float pitchAngle;

    /// <summary>Current yaw angle in degrees.</summary>
    float yawAngle;

    void Start()
    {
        // Lock and hide the cursor so mouse movement is captured for camera look.
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

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
        // Mouse horizontal movement controls yaw (left/right rotation).
        // Mouse left -> counter-clockwise (negative yaw change).
        // Mouse right -> clockwise (positive yaw change).
        float mouseX = Input.GetAxis("Mouse X");
        yawAngle += mouseX * mouseSensitivityX;

        // Mouse vertical movement controls pitch (up/down tilt).
        // Mouse forward (up on screen) -> tilt downwards (negative pitch change).
        // Mouse backward (down on screen) -> tilt upwards (positive pitch change).
        float mouseY = Input.GetAxis("Mouse Y");
        pitchAngle -= mouseY * mouseSensitivityY;

        // Clamp pitch to avoid flipping.
        pitchAngle = Mathf.Clamp(pitchAngle, minPitch, maxPitch);

        // Apply rotation: yaw around Y axis, pitch around local X axis.
        transform.rotation = Quaternion.Euler(pitchAngle, yawAngle, 0f);
    }

    void HandleMovement()
    {
        // WASD movement relative to camera orientation.
        // W/S moves forward/backward along the camera's forward vector (flattened to horizontal).
        // A/D strafes left/right along the camera's right vector.
        float moveForward = Input.GetAxis("Vertical");   // W = +1, S = -1
        float moveStrafe = Input.GetAxis("Horizontal");  // D = +1, A = -1

        // Forward/backward direction, flattened to horizontal plane.
        Vector3 forward = transform.forward;
        forward.y = 0f;
        forward.Normalize();

        // Right direction (already horizontal for a level camera).
        Vector3 right = transform.right;
        right.y = 0f;
        right.Normalize();

        // Calculate movement delta.
        Vector3 movement = (forward * moveForward + right * moveStrafe) * moveSpeed * Time.deltaTime;

        transform.position += movement;
    }
}
