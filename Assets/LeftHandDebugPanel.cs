using UnityEngine;
using Oculus.Interaction.Input;

namespace NulabCup
{
    /// <summary>
    /// 左手に追従する簡易デバッグパネル。
    /// CubeSpawner のジェスチャー状態・キューブ数・MRUK状態を表示する。
    /// </summary>
    public class LeftHandDebugPanel : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] CubeSpawner m_CubeSpawner;
        [SerializeField] Object m_LeftHandSource;

        [Header("Panel Follow")]
        [SerializeField] Vector3 m_LocalOffset = new(-0.09f, 0.08f, 0.02f);
        [SerializeField] Vector3 m_RotationOffsetEuler = new(8f, 180f, 0f);

        [Header("Panel Style")]
        [SerializeField, Min(0.05f)] float m_PanelWidth = 0.22f;
        [SerializeField, Min(0.05f)] float m_PanelHeight = 0.12f;
        [SerializeField] Color m_BackgroundColor = new(0f, 0f, 0f, 0.85f);
        [SerializeField] Color m_TextColor = Color.white;

        Transform m_PanelRoot;
        TextMesh m_TextMesh;

        void Awake()
        {
            if (m_CubeSpawner == null)
                m_CubeSpawner = FindObjectOfType<CubeSpawner>(true);

            CreatePanelIfNeeded();
        }

        void LateUpdate()
        {
            if (m_PanelRoot == null || m_TextMesh == null)
                return;

            bool hasLeftHand = TryGetLeftHand(out var leftHand);
            Pose leftRootPose = Pose.identity;
            bool hasPose = hasLeftHand && leftHand.GetRootPose(out leftRootPose);

            if (hasPose)
            {
                var poseRotation = leftRootPose.rotation * Quaternion.Euler(m_RotationOffsetEuler);
                m_PanelRoot.SetPositionAndRotation(
                    leftRootPose.position + leftRootPose.rotation * m_LocalOffset,
                    poseRotation);
            }

            UpdateText(hasLeftHand, hasPose, leftHand);
        }

        void CreatePanelIfNeeded()
        {
            if (m_PanelRoot != null)
                return;

            var root = new GameObject("LeftHandDebugPanelRoot");
            m_PanelRoot = root.transform;
            m_PanelRoot.SetParent(transform, false);

            var background = GameObject.CreatePrimitive(PrimitiveType.Quad);
            background.name = "Background";
            background.transform.SetParent(m_PanelRoot, false);
            background.transform.localScale = new Vector3(m_PanelWidth, m_PanelHeight, 1f);
            background.transform.localPosition = new Vector3(0f, 0f, 0.002f);

            if (background.TryGetComponent<Collider>(out var collider))
                Destroy(collider);

            if (background.TryGetComponent<MeshRenderer>(out var renderer))
            {
                var shader = Shader.Find("Unlit/Color");
                if (shader != null)
                {
                    renderer.material = new Material(shader) { color = m_BackgroundColor };
                }
            }

            var textObject = new GameObject("Text");
            textObject.transform.SetParent(m_PanelRoot, false);
            textObject.transform.localPosition = new Vector3(-m_PanelWidth * 0.47f, m_PanelHeight * 0.42f, 0f);

            m_TextMesh = textObject.AddComponent<TextMesh>();
            m_TextMesh.anchor = TextAnchor.UpperLeft;
            m_TextMesh.alignment = TextAlignment.Left;
            m_TextMesh.fontSize = 64;
            m_TextMesh.characterSize = 0.003f;
            m_TextMesh.color = m_TextColor;
            m_TextMesh.text = "Left Hand Debug Panel";
        }

        void UpdateText(bool hasLeftHand, bool hasPose, IHand leftHand)
        {
            if (m_CubeSpawner == null)
            {
                m_TextMesh.text = "CubeSpawner: Not Found";
                return;
            }

            string gestureText = m_CubeSpawner.IsThumbsUpNow ? "ThumbsUp" : "None";
            string cubeCountText = $"{m_CubeSpawner.SpawnedCubeCount}/{m_CubeSpawner.MaxCubes}";
            string mrukText = m_CubeSpawner.MrukTrackingStatus;
            string leftState = hasLeftHand
                ? (hasPose ? "Tracked" : "NoPose")
                : "Missing";
            string rightState = m_CubeSpawner.IsRightHandTracked ? "Tracked" : "Missing";

            m_TextMesh.text =
                "[Debug]\n" +
                $"Gesture: {gestureText}\n" +
                $"Cubes: {cubeCountText}\n" +
                $"MRUK: {mrukText}\n" +
                $"LeftHand: {leftState}\n" +
                $"RightHand: {rightState}";
        }

        bool TryGetLeftHand(out IHand hand)
        {
            hand = null;

            if (TryResolveLeftHandFromSource(m_LeftHandSource, out var explicitHand))
                return AssignAndReturn(explicitHand, out hand);

            var handRefs = FindObjectsOfType<HandRef>(true);
            for (int i = 0; i < handRefs.Length; i++)
            {
                var handRef = handRefs[i];
                if (handRef == null || handRef.Hand == null)
                    continue;

                if (IsUsableLeftHand(handRef.Hand))
                    return AssignAndReturn(handRef.Hand, out hand);
            }

            var dominantRefs = FindObjectsOfType<DominantHandRef>(true);
            for (int i = 0; i < dominantRefs.Length; i++)
            {
                var dominant = dominantRefs[i];
                if (dominant == null)
                    continue;

                if (IsUsableLeftHand(dominant))
                    return AssignAndReturn(dominant, out hand);
            }

            var hands = FindObjectsOfType<Hand>(true);
            for (int i = 0; i < hands.Length; i++)
            {
                if (IsUsableLeftHand(hands[i]))
                    return AssignAndReturn(hands[i], out hand);
            }

            return false;
        }

        bool AssignAndReturn(IHand resolved, out IHand hand)
        {
            hand = resolved;
            m_LeftHandSource = resolved as Object;
            return true;
        }

        bool TryResolveLeftHandFromSource(Object source, out IHand hand)
        {
            hand = null;
            if (source == null)
                return false;

            if (source is IHand directHand)
            {
                if (IsUsableLeftHand(directHand))
                {
                    hand = directHand;
                    return true;
                }

                return false;
            }

            if (source is Component component)
                return TryFindLeftHandFromComponent(component, out hand);

            if (source is GameObject gameObject)
                return TryFindLeftHandFromGameObject(gameObject, out hand);

            return false;
        }

        bool TryFindLeftHandFromComponent(Component component, out IHand hand)
        {
            hand = null;
            if (component is not MonoBehaviour mono)
                return false;

            if (mono is IHand ownHand && IsUsableLeftHand(ownHand))
            {
                hand = ownHand;
                return true;
            }

            var children = mono.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i] is IHand childHand && IsUsableLeftHand(childHand))
                {
                    hand = childHand;
                    return true;
                }
            }

            var parents = mono.GetComponentsInParent<MonoBehaviour>(true);
            for (int i = 0; i < parents.Length; i++)
            {
                if (parents[i] is IHand parentHand && IsUsableLeftHand(parentHand))
                {
                    hand = parentHand;
                    return true;
                }
            }

            return false;
        }

        bool TryFindLeftHandFromGameObject(GameObject gameObject, out IHand hand)
        {
            hand = null;
            var components = gameObject.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < components.Length; i++)
            {
                if (components[i] is IHand candidate && IsUsableLeftHand(candidate))
                {
                    hand = candidate;
                    return true;
                }
            }

            return false;
        }

        bool IsUsableLeftHand(IHand hand)
        {
            return hand != null
                && hand.Handedness == Handedness.Left
                && hand.IsConnected
                && hand.IsTrackedDataValid;
        }
    }
}
