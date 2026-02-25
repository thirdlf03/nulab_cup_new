using UnityEngine;
using Oculus.Interaction;
using Oculus.Interaction.HandGrab;
using Meta.XR.MRUtilityKit;

namespace NulabCup
{
    /// <summary>
    /// Meta XR Interaction SDK 向けの投擲可能キューブ。
    /// 必要な Interactable コンポーネントを自動で構成する。
    /// </summary>
    [DisallowMultipleComponent]
    public class ThrowableCube : MonoBehaviour
    {
        [Header("Meta XR Interaction")]
        [SerializeField] bool m_EnableControllerGrab = true;
        [SerializeField] bool m_EnableHandGrab = true;
        [SerializeField] int m_MaxGrabPoints = 1;
        [SerializeField] bool m_ForceKinematicDisabledOnThrow = true;

        [Header("MRUK Ground Physics")]
        [SerializeField] bool m_EnableMrukGroundPhysics = true;
        [SerializeField, Min(0.05f)] float m_GroundRayStartOffset = 0.2f;
        [SerializeField, Min(0.1f)] float m_GroundRayDistance = 2.5f;
        [SerializeField, Min(0f)] float m_GroundSnapDistance = 0.03f;
        [SerializeField, Min(0f)] float m_PenetrationEpsilon = 0.002f;
        [SerializeField, Range(0f, 1f)] float m_Bounce = 0.35f;
        [SerializeField, Range(0f, 1f)] float m_TangentDamping = 0.92f;
        [SerializeField, Min(0f)] float m_MinBounceSpeed = 0.2f;
        [SerializeField] bool m_DebugGroundRay;

        static readonly LabelFilter s_GroundFilter = new(
            MRUKAnchor.SceneLabels.FLOOR | MRUKAnchor.SceneLabels.GLOBAL_MESH,
            MRUKAnchor.ComponentType.All);

        Rigidbody m_Rigidbody;
        Collider m_Collider;

        void Reset()
        {
            ConfigureMetaComponents();
            CachePhysicsComponents();
        }

        void Awake()
        {
            ConfigureMetaComponents();
            CachePhysicsComponents();
        }

        void OnValidate()
        {
            if (!Application.isPlaying)
            {
                ConfigureMetaComponents();
                CachePhysicsComponents();
            }
        }

        void FixedUpdate()
        {
            ApplyMrukGroundPhysics();
        }

        void ConfigureMetaComponents()
        {
            var rb = GetOrAdd<Rigidbody>();
            m_Rigidbody = rb;
            var grabbable = GetOrAdd<Grabbable>();

            grabbable.MaxGrabPoints = Mathf.Max(-1, m_MaxGrabPoints);
            grabbable.ForceKinematicDisabled = m_ForceKinematicDisabledOnThrow;

            if (m_EnableControllerGrab)
            {
                var grabInteractable = GetOrAdd<GrabInteractable>();
                grabInteractable.InjectRigidbody(rb);
                grabInteractable.InjectOptionalPointableElement(grabbable);
            }

            if (m_EnableHandGrab)
            {
                var handGrabInteractable = GetOrAdd<HandGrabInteractable>();
                handGrabInteractable.InjectRigidbody(rb);
                handGrabInteractable.InjectOptionalPointableElement(grabbable);
            }
        }

        void CachePhysicsComponents()
        {
            if (!TryGetComponent(out m_Rigidbody))
                m_Rigidbody = null;

            if (!TryGetComponent(out m_Collider))
                m_Collider = null;
        }

        void ApplyMrukGroundPhysics()
        {
            if (!m_EnableMrukGroundPhysics || m_Rigidbody == null || m_Collider == null || m_Rigidbody.isKinematic)
                return;

            var mruk = MRUK.Instance;
            if (mruk == null || !mruk.IsInitialized)
                return;

            var room = mruk.GetCurrentRoom();
            if (room == null && mruk.Rooms.Count > 0)
                room = mruk.Rooms[0];
            if (room == null)
                return;

            var bounds = m_Collider.bounds;
            var bottomY = bounds.min.y;
            var rayOrigin = new Vector3(bounds.center.x, bottomY + m_GroundRayStartOffset, bounds.center.z);
            var ray = new Ray(rayOrigin, Vector3.down);

            if (!room.Raycast(ray, m_GroundRayDistance, s_GroundFilter, out var hit, out var _))
            {
                if (m_DebugGroundRay)
                    Debug.DrawRay(rayOrigin, Vector3.down * m_GroundRayDistance, Color.red, Time.fixedDeltaTime);
                return;
            }

            if (m_DebugGroundRay)
                Debug.DrawLine(rayOrigin, hit.point, Color.green, Time.fixedDeltaTime);

            float separationToGround = bottomY - hit.point.y;
            if (separationToGround > m_GroundSnapDistance)
                return;

            float penetration = hit.point.y - bottomY;
            if (penetration > 0f)
            {
                transform.position += hit.normal * (penetration + m_PenetrationEpsilon);
            }

            var velocity = GetLinearVelocity();
            float normalSpeed = Vector3.Dot(velocity, hit.normal);
            if (normalSpeed >= 0f)
                return;

            var tangentVelocity = velocity - hit.normal * normalSpeed;
            float bouncedNormalSpeed = Mathf.Abs(normalSpeed);
            if (bouncedNormalSpeed < m_MinBounceSpeed)
            {
                bouncedNormalSpeed = 0f;
            }
            else
            {
                bouncedNormalSpeed *= m_Bounce;
            }

            SetLinearVelocity(hit.normal * bouncedNormalSpeed + tangentVelocity * m_TangentDamping);
        }

        Vector3 GetLinearVelocity()
        {
#if UNITY_6000_0_OR_NEWER
            return m_Rigidbody.linearVelocity;
#else
#pragma warning disable CS0618
            return m_Rigidbody.velocity;
#pragma warning restore CS0618
#endif
        }

        void SetLinearVelocity(Vector3 velocity)
        {
#if UNITY_6000_0_OR_NEWER
            m_Rigidbody.linearVelocity = velocity;
#else
#pragma warning disable CS0618
            m_Rigidbody.velocity = velocity;
#pragma warning restore CS0618
#endif
        }

        T GetOrAdd<T>() where T : Component
        {
            if (TryGetComponent<T>(out var component))
                return component;

            return gameObject.AddComponent<T>();
        }
    }
}
