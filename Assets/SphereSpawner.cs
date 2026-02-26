using System.Collections.Generic;
using Fusion;
using UnityEngine;
using Oculus.Interaction.Input;
using Meta.XR.MRUtilityKit;

namespace NulabCup
{
    /// <summary>
    /// Meta XR Interaction SDK の右手トラッキングからサムズアップ（👍）を検出してスフィアをスポーンする。
    /// 親指が上向き＋他の指が握られている状態で判定する。
    /// CubeSpawner と同様のロジックで Sphere を召喚する。
    /// </summary>
    public class SphereSpawner : MonoBehaviour
    {
        [Header("Hand Tracking")]
        [SerializeField] UnityEngine.Object m_RightHandSource;

        [Header("Spawn Settings")]
        [SerializeField] GameObject m_SpherePrefab;
        [SerializeField] float m_TargetHeightOffset = 0.3f;
        [SerializeField] float m_RainHeight = 2.0f;
        [SerializeField] float m_RainRadius = 0.25f;
        [SerializeField] int m_MaxSpheres = 10;
        [SerializeField] float m_Cooldown = 2.0f;

        [Header("Thumbs Up Detection")]
        [SerializeField] float m_FingerTipToRootThreshold = 0.11f;
        [SerializeField] float m_ThumbUpDot = 0.7f;

        [Header("MRUK Spawn")]
        [SerializeField] bool m_UseMrukFloorHeight = true;
        [SerializeField, Min(0.5f)] float m_FloorRayStartHeight = 2.5f;
        [SerializeField, Min(0.5f)] float m_FloorRayDistance = 6.0f;
        [SerializeField] bool m_DebugFloorRay = false;

        [Header("Networking")]
        [SerializeField] NetworkRunner m_Runner;

        readonly List<GameObject> m_SpawnedSpheres = new();
        float m_LastSpawnTime;
        bool m_WasThumbsUp;
        bool m_IsThumbsUpNow;
        bool m_IsRightHandTracked;

        static readonly LabelFilter s_FloorFilter = new(
            MRUKAnchor.SceneLabels.FLOOR | MRUKAnchor.SceneLabels.GLOBAL_MESH,
            MRUKAnchor.ComponentType.All);

        public bool IsThumbsUpNow => m_IsThumbsUpNow;
        public int SpawnedSphereCount => m_SpawnedSpheres.Count;
        public int MaxSpheres => m_MaxSpheres;
        public bool IsRightHandTracked => m_IsRightHandTracked;
        public string MrukTrackingStatus
        {
            get
            {
                var mruk = MRUK.Instance;
                if (mruk == null)
                    return "Missing";
                if (!mruk.IsInitialized)
                    return "Initializing";

                var room = mruk.GetCurrentRoom();
                if (room == null && mruk.Rooms.Count > 0)
                    room = mruk.Rooms[0];

                return room != null ? "Tracking" : "NoRoom";
            }
        }

        void Update()
        {
            var rightHand = GetRightHand();
            if (rightHand == null || !rightHand.IsConnected || !rightHand.IsTrackedDataValid)
            {
                m_IsRightHandTracked = false;
                m_IsThumbsUpNow = false;
                m_WasThumbsUp = false;
                return;
            }

            m_IsRightHandTracked = true;

            bool isThumbsUp = IsThumbsUp(rightHand);
            m_IsThumbsUpNow = isThumbsUp;

            if (isThumbsUp && !m_WasThumbsUp && Time.time - m_LastSpawnTime >= m_Cooldown)
            {
                SpawnSphere(rightHand);
                m_LastSpawnTime = Time.time;
            }

            m_WasThumbsUp = isThumbsUp;
        }

        IHand GetRightHand()
        {
            if (TryResolveRightHandFromSource(m_RightHandSource, out var explicitHand))
                return explicitHand;

            var handRefs = FindObjectsOfType<HandRef>(true);
            foreach (var handRef in handRefs)
            {
                if (handRef == null || handRef.Hand == null)
                    continue;

                if (IsUsableRightHand(handRef.Hand))
                {
                    m_RightHandSource = handRef;
                    return handRef.Hand;
                }
            }

            var dominantHandRefs = FindObjectsOfType<DominantHandRef>(true);
            foreach (var dominant in dominantHandRefs)
            {
                if (dominant == null)
                    continue;

                if (IsUsableRightHand(dominant))
                {
                    m_RightHandSource = dominant;
                    return dominant;
                }
            }

            var hands = FindObjectsOfType<Hand>(true);
            foreach (var trackedHand in hands)
            {
                if (trackedHand == null)
                    continue;

                if (IsUsableRightHand(trackedHand))
                {
                    m_RightHandSource = trackedHand;
                    return trackedHand;
                }
            }

            return null;
        }

        bool TryResolveRightHandFromSource(UnityEngine.Object source, out IHand hand)
        {
            hand = null;
            if (source == null)
                return false;

            if (source is IHand directHand)
            {
                if (IsUsableRightHand(directHand))
                {
                    hand = directHand;
                    return true;
                }

                return false;
            }

            if (source is Component component)
            {
                if (TryFindRightHandFromComponent(component, out hand))
                    return true;
            }
            else if (source is GameObject gameObject)
            {
                if (TryFindRightHandFromGameObject(gameObject, out hand))
                    return true;
            }

            return false;
        }

        bool TryFindRightHandFromComponent(Component component, out IHand hand)
        {
            hand = null;

            if (component is MonoBehaviour mono)
            {
                if (mono is IHand ownHand && IsUsableRightHand(ownHand))
                {
                    hand = ownHand;
                    return true;
                }

                var children = mono.GetComponentsInChildren<MonoBehaviour>(true);
                for (int i = 0; i < children.Length; i++)
                {
                    if (children[i] is IHand childHand && IsUsableRightHand(childHand))
                    {
                        hand = childHand;
                        return true;
                    }
                }

                var parents = mono.GetComponentsInParent<MonoBehaviour>(true);
                for (int i = 0; i < parents.Length; i++)
                {
                    if (parents[i] is IHand parentHand && IsUsableRightHand(parentHand))
                    {
                        hand = parentHand;
                        return true;
                    }
                }
            }

            return false;
        }

        bool TryFindRightHandFromGameObject(GameObject gameObject, out IHand hand)
        {
            hand = null;
            var components = gameObject.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] is IHand candidate && IsUsableRightHand(candidate))
                {
                    hand = candidate;
                    return true;
                }
            }

            return false;
        }

        bool IsUsableRightHand(IHand hand)
        {
            return hand != null
                && hand.Handedness == Handedness.Right
                && hand.IsConnected
                && hand.IsTrackedDataValid;
        }

        bool IsThumbsUp(IHand hand)
        {
            // 親指の先端が上を向いているか
            if (!hand.GetJointPose(HandJointId.HandThumbTip, out var thumbTipPose))
                return false;
            if (!hand.GetJointPose(HandJointId.HandThumb2, out var thumbProxPose))
                return false;

            var thumbDir = (thumbTipPose.position - thumbProxPose.position).normalized;
            if (Vector3.Dot(thumbDir, Vector3.up) < m_ThumbUpDot)
                return false;

            if (!hand.GetRootPose(out var rootPose))
                return false;

            float scaledCurlThreshold = Mathf.Max(0.01f, m_FingerTipToRootThreshold * Mathf.Max(0.5f, hand.Scale));

            // 他の4本指が握られているか（先端が手首根元に近い）
            if (!IsFingerCurled(hand, HandJointId.HandIndexTip, rootPose.position, scaledCurlThreshold))
                return false;
            if (!IsFingerCurled(hand, HandJointId.HandMiddleTip, rootPose.position, scaledCurlThreshold))
                return false;
            if (!IsFingerCurled(hand, HandJointId.HandRingTip, rootPose.position, scaledCurlThreshold))
                return false;
            if (!IsFingerCurled(hand, HandJointId.HandPinkyTip, rootPose.position, scaledCurlThreshold))
                return false;

            return true;
        }

        bool IsFingerCurled(IHand hand, HandJointId tipId, Vector3 rootPosition, float threshold)
        {
            if (!hand.GetJointPose(tipId, out var tipPose))
                return false;

            return Vector3.Distance(tipPose.position, rootPosition) < threshold;
        }

        void SpawnSphere(IHand hand)
        {
            if (m_SpherePrefab == null)
                return;

            if (!hand.GetRootPose(out var palmPose))
                return;

            // 外部で破棄されたスフィアをリストから除去
            m_SpawnedSpheres.RemoveAll(s => s == null);

            var targetPos = palmPose.position + Vector3.up * m_TargetHeightOffset;
            var horizontalOffset = Random.insideUnitCircle * m_RainRadius;
            var spawnXZ = targetPos + new Vector3(horizontalOffset.x, 0f, horizontalOffset.y);

            var baseHeight = targetPos.y;
            if (m_UseMrukFloorHeight && TryGetFloorHeight(spawnXZ, out var floorY))
            {
                baseHeight = floorY + m_TargetHeightOffset;
            }

            var spawnPos = new Vector3(spawnXZ.x, baseHeight + m_RainHeight, spawnXZ.z);
            var runner = ResolveRunner();
            bool isNetworkSpawn = IsNetworkSpawnEnabled(runner);
            GameObject sphere = null;

            if (isNetworkSpawn)
            {
                var networkObject = runner.Spawn(
                    m_SpherePrefab,
                    spawnPos,
                    Quaternion.identity,
                    null,
                    (_, spawnedObject) => ResetRigidbodyVelocity(spawnedObject.gameObject));

                if (networkObject != null)
                    sphere = networkObject.gameObject;
            }
            else
            {
                sphere = Instantiate(m_SpherePrefab, spawnPos, Quaternion.identity);
                ResetRigidbodyVelocity(sphere);
            }

            if (sphere == null)
                return;

            m_SpawnedSpheres.Add(sphere);
            RemoveOverflowSpheres(runner, isNetworkSpawn);
        }

        NetworkRunner ResolveRunner()
        {
            if (m_Runner != null)
                return m_Runner;

#if UNITY_2023_1_OR_NEWER
            m_Runner = FindFirstObjectByType<NetworkRunner>();
#else
            m_Runner = FindObjectOfType<NetworkRunner>();
#endif
            return m_Runner;
        }

        static bool IsNetworkSpawnEnabled(NetworkRunner runner)
        {
            return runner != null && runner.IsRunning && !runner.IsSinglePlayer;
        }

        void RemoveOverflowSpheres(NetworkRunner runner, bool isNetworkSpawn)
        {
            while (m_SpawnedSpheres.Count > m_MaxSpheres)
            {
                var oldest = m_SpawnedSpheres[0];
                m_SpawnedSpheres.RemoveAt(0);
                if (oldest == null)
                    continue;

                if (isNetworkSpawn && oldest.TryGetComponent<NetworkObject>(out var networkObject))
                {
                    if (networkObject.HasStateAuthority)
                        runner.Despawn(networkObject);
                    continue;
                }

                Destroy(oldest);
            }
        }

        static void ResetRigidbodyVelocity(GameObject spawnedObject)
        {
            if (spawnedObject == null || !spawnedObject.TryGetComponent<Rigidbody>(out var rb))
                return;

#if UNITY_6000_0_OR_NEWER
            rb.linearVelocity = Vector3.zero;
#else
#pragma warning disable CS0618
            rb.velocity = Vector3.zero;
#pragma warning restore CS0618
#endif
            rb.angularVelocity = Vector3.zero;
        }

        bool TryGetFloorHeight(Vector3 worldPosition, out float floorY)
        {
            floorY = 0f;

            var mruk = MRUK.Instance;
            if (mruk == null || !mruk.IsInitialized)
                return false;

            var room = mruk.GetCurrentRoom();
            if (room == null && mruk.Rooms.Count > 0)
                room = mruk.Rooms[0];
            if (room == null)
                return false;

            var rayOrigin = worldPosition + Vector3.up * m_FloorRayStartHeight;
            var ray = new Ray(rayOrigin, Vector3.down);

            if (!room.Raycast(ray, m_FloorRayDistance, s_FloorFilter, out var hit, out var _))
            {
                if (m_DebugFloorRay)
                    Debug.DrawRay(rayOrigin, Vector3.down * m_FloorRayDistance, Color.red, 0.15f);
                return false;
            }

            if (m_DebugFloorRay)
                Debug.DrawLine(rayOrigin, hit.point, Color.cyan, 0.15f);

            floorY = hit.point.y;
            return true;
        }
    }
}
