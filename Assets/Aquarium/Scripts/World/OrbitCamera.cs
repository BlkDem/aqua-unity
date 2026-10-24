// The orbit camera for looking around the aquarium: drag to turn, right-drag to pan, wheel or
// pinch to zoom (the web version's OrbitControls). The camera director moves it too: flights
// to a view, following a fish, handing back after the cinematic modes.
using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using Touch = UnityEngine.InputSystem.EnhancedTouch.Touch;

namespace Aquarium.World
{
    public sealed class OrbitCamera : MonoBehaviour
    {
        public Vector3 target = new Vector3(0, 2.6f, 0);
        public float minDistance = 0.8f, maxDistance = 26;
        [Tooltip("Lowest the camera may go, as the web version's maxPolarAngle (0.62 pi from straight up)")]
        public float maxPolar = Mathf.PI * 0.62f;
        public float rotateSpeed = 0.25f, damping = 8;
        [Tooltip("How much nearer one notch of the wheel brings the camera (the Input System gives about 1 per notch on every platform)")]
        public float zoomPerNotch = 0.05f;
        /// <summary>True while the pointer is over the interface: drags and the wheel are not the camera's then.</summary>
        public Func<bool> inputBlocked;

        float yaw, pitch, distance;
        Vector3 goalTarget;
        float goalYaw, goalPitch, goalDistance;
        bool dragging, started;

        void OnEnable() => EnhancedTouchSupport.Enable();

        void Start()
        {
            if (!started) Place(transform.position, target);
        }

        /// <summary>Put the camera at pos looking at look, at once (the user takes over from there).</summary>
        public void Place(Vector3 pos, Vector3 look)
        {
            started = true;
            target = goalTarget = look;
            var off = pos - look;
            distance = goalDistance = Mathf.Max(off.magnitude, 0.01f);
            yaw = goalYaw = Mathf.Atan2(off.x, off.z) * Mathf.Rad2Deg;
            pitch = goalPitch = Mathf.Asin(Mathf.Clamp(off.y / distance, -1, 1)) * Mathf.Rad2Deg;
            Apply();
        }

        /// <summary>Keep looking at a moving point (following a fish): the camera keeps its angle and distance.</summary>
        public void Follow(Vector3 point) => goalTarget = point;

        void LateUpdate()
        {
            bool blocked = inputBlocked != null && inputBlocked();
            var mouse = Mouse.current;
            if (Touch.activeTouches.Count > 0)
            {
                var touches = Touch.activeTouches;
                if (touches.Count == 1 && !blocked) { var d = touches[0].delta; goalYaw += d.x * rotateSpeed; goalPitch -= d.y * rotateSpeed; }
                else if (touches.Count >= 2)
                {
                    var a = touches[0]; var b = touches[1];
                    float now = (a.screenPosition - b.screenPosition).magnitude, before = (a.screenPosition - a.delta - (b.screenPosition - b.delta)).magnitude;
                    if (before > 1) goalDistance *= before / Mathf.Max(1, now);
                }
            }
            else if (mouse != null)
            {
                // a drag belongs to the camera only if it started over the aquarium
                if (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame) dragging = !blocked;
                if (!mouse.leftButton.isPressed && !mouse.rightButton.isPressed) dragging = false;
                Vector2 d = mouse.delta.ReadValue();
                if (dragging && mouse.leftButton.isPressed) { goalYaw += d.x * rotateSpeed; goalPitch -= d.y * rotateSpeed; }
                if (dragging && mouse.rightButton.isPressed)
                    goalTarget += (-transform.right * d.x + -transform.up * d.y) * goalDistance * 0.0015f;
                float wheel = mouse.scroll.ReadValue().y;
                if (wheel != 0 && !blocked) goalDistance *= Mathf.Pow(1 - zoomPerNotch, Mathf.Clamp(wheel, -5, 5));
            }
            float minPitch = 90 - maxPolar * Mathf.Rad2Deg;
            goalPitch = Mathf.Clamp(goalPitch, minPitch, 89);
            goalDistance = Mathf.Clamp(goalDistance, minDistance, maxDistance);
            float k = 1 - Mathf.Exp(-damping * Time.unscaledDeltaTime);
            yaw = Mathf.LerpAngle(yaw, goalYaw, k);
            pitch = Mathf.Lerp(pitch, goalPitch, k);
            distance = Mathf.Lerp(distance, goalDistance, k);
            target = Vector3.Lerp(target, goalTarget, k);
            Apply();
        }

        void Apply()
        {
            var rot = Quaternion.Euler(pitch, yaw + 180, 0);
            transform.position = target - rot * Vector3.forward * distance;
            transform.rotation = rot;
        }
    }
}
