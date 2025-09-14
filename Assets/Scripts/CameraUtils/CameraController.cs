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
                // set a lower angle to be closer to over-the-shoulder by default
                m_OrbitalFollow.VerticalAxis.Value = 0.1f;
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
                // horizontal orbit
                m_OrbitalFollow.HorizontalAxis.Value += delta.x * m_MouseXSensitivity;
                // vertical tilt [0..1]
                var v = m_OrbitalFollow.VerticalAxis.Value;
                v -= delta.y * m_MouseYSensitivity * 0.01f; // scale down for finer tilt
                m_OrbitalFollow.VerticalAxis.Value = Mathf.Clamp01(v);
            }
        }
    }
}
