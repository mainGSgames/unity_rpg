using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.InputSystem;

namespace Unity.BossRoom.CameraUtils
{
    public class CameraController : MonoBehaviour
    {
        const string k_CMCameraTag = "CMCamera";

        CinemachineCamera m_CinemachineCamera;
        CinemachineOrbitalFollow m_OrbitalFollow;

        [SerializeField]
        float m_MouseXSensitivity = 0.2f;
        [SerializeField]
        float m_MouseYSensitivity = 0.1f;
        [SerializeField]
        bool m_HoldRightMouseToLook = false;

        void Start()
        {
            AttachCamera();
        }

        void AttachCamera()
        {
            var cinemachineCameraGameObject = GameObject.FindGameObjectWithTag(k_CMCameraTag);
            Assert.IsNotNull(cinemachineCameraGameObject);

            m_CinemachineCamera = cinemachineCameraGameObject.GetComponent<CinemachineCamera>();
            Assert.IsNotNull(m_CinemachineCamera, "CameraController.AttachCamera: Couldn't find gameplay CinemachineCamera");

            if (m_CinemachineCamera != null)
            {
                // camera body / aim
                m_CinemachineCamera.Follow = transform;
                m_CinemachineCamera.LookAt = transform;
            }

            m_OrbitalFollow = cinemachineCameraGameObject.GetComponent<CinemachineOrbitalFollow>();
            Assert.IsNotNull(m_OrbitalFollow, "CameraController.AttachCamera: Couldn't find gameplay CinemachineOrbitalFollow");

            if (m_OrbitalFollow != null)
            {
                // Configure OrbitalFollow for classic third-person sphere orbit
                m_OrbitalFollow.OrbitStyle = CinemachineOrbitalFollow.OrbitStyles.Sphere;
                m_OrbitalFollow.Radius = 6.5f; // comfortable third-person distance

                // Horizontal (yaw): wrap, no recentering
                var h = m_OrbitalFollow.HorizontalAxis;
                h.Wrap = true;
                h.Recentering.Enabled = false;
                h.Range = new Vector2(-180f, 180f);
                m_OrbitalFollow.HorizontalAxis = h;

                // Vertical (pitch): set a reasonable range and default value, no recentering
                var v = m_OrbitalFollow.VerticalAxis;
                v.Wrap = false;
                v.Recentering.Enabled = false;
                v.Range = new Vector2(-25f, 60f);
                v.Value = 15f; // slight downward look by default
                m_OrbitalFollow.VerticalAxis = v;

                // slight shoulder offset
                m_OrbitalFollow.TargetOffset = new Vector3(0.5f, 1.6f, 0f);
            }
        }

        void LateUpdate()
        {
            if (m_OrbitalFollow == null) return;

            // classic third-person mouse-look (now always-on by default)
            bool lookActive = true;
            Vector2 delta = Mouse.current != null
                ? Mouse.current.delta.ReadValue()
                : new Vector2(Input.GetAxis("Mouse X"), Input.GetAxis("Mouse Y"));

            if (lookActive)
            {
                // horizontal orbit (yaw)
                m_OrbitalFollow.HorizontalAxis.Value += delta.x * m_MouseXSensitivity;

                // vertical tilt (pitch in degrees, clamped to axis range)
                var pitch = m_OrbitalFollow.VerticalAxis.Value;
                pitch -= delta.y * m_MouseYSensitivity; // mouse up -> look up
                var r = m_OrbitalFollow.VerticalAxis.Range;
                m_OrbitalFollow.VerticalAxis.Value = Mathf.Clamp(pitch, r.x, r.y);
            }
        }
    }
}
