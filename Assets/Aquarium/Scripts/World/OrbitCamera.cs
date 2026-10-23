// A simple orbit camera for looking around the aquarium: drag to turn, right-drag to pan,
// wheel to zoom (the web version's OrbitControls). The full camera director comes later.
using UnityEngine;
using UnityEngine.InputSystem;

namespace Aquarium.World
{
    public sealed class OrbitCamera : MonoBehaviour
    {
        public Vector3 target = new Vector3(0, 2.6f, 0);
        public float minDistance = 0.8f, maxDistance = 26;
        [Tooltip("Lowest the camera may go, as the web version's maxPolarAngle (0.62 pi from straight up)")]
        public float maxPolar = Mathf.PI * 0.62f;
        public float rotateSpeed = 0.25f, zoomSpeed = 0.0015f, damping = 8;

        float yaw, pitch, distance;
        Vector3 goalTarget;
        float goalYaw, goalPitch, goalDistance;

        void Start()
        {
            var off = transform.position - target;
            distance = goalDistance = off.magnitude;
            yaw = goalYaw = Mathf.Atan2(off.x, off.z) * Mathf.Rad2Deg;
            pitch = goalPitch = Mathf.Asin(off.y / distance) * Mathf.Rad2Deg;
            goalTarget = target;
        }

        void LateUpdate()
        {
            var mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 d = mouse.delta.ReadValue();
                if (mouse.leftButton.isPressed) { goalYaw += d.x * rotateSpeed; goalPitch -= d.y * rotateSpeed; }
                if (mouse.rightButton.isPressed)
                    goalTarget += (-transform.right * d.x + -transform.up * d.y) * goalDistance * 0.0015f;
                float wheel = mouse.scroll.ReadValue().y;
                if (wheel != 0) goalDistance *= 1 - wheel * zoomSpeed;
            }
            float minPitch = 90 - maxPolar * Mathf.Rad2Deg;
            goalPitch = Mathf.Clamp(goalPitch, minPitch, 89);
            goalDistance = Mathf.Clamp(goalDistance, minDistance, maxDistance);
            float k = 1 - Mathf.Exp(-damping * Time.unscaledDeltaTime);
            yaw = Mathf.LerpAngle(yaw, goalYaw, k);
            pitch = Mathf.Lerp(pitch, goalPitch, k);
            distance = Mathf.Lerp(distance, goalDistance, k);
            target = Vector3.Lerp(target, goalTarget, k);
            var rot = Quaternion.Euler(pitch, yaw + 180, 0);
            transform.position = target - rot * Vector3.forward * distance;
            transform.rotation = rot;
        }
    }
}
