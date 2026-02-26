using System.Collections;
using UnityEngine;
using Fusion;

namespace NulabCup
{
    /// <summary>
    /// シーンに配置したオブジェクトにアタッチする。
    /// Backlog 課題が入った球が当たると、このターゲットの assigneeId に課題をアサインし、球を消す。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class AssignableTarget : MonoBehaviour
    {
        [Header("Backlog Assignee")]
        [SerializeField, Tooltip("Backlog の担当者ユーザー ID")]
        int m_AssigneeId = 2339745;

        [Header("Optional")]
        [SerializeField, Tooltip("表示用の名前（デバッグ用）")]
        string m_DisplayName;

        public int AssigneeId => m_AssigneeId;
        public string DisplayName => m_DisplayName;

        public void SetAssignee(int assigneeId, string displayName = null)
        {
            m_AssigneeId = assigneeId;
            m_DisplayName = displayName ?? "";
        }

        void OnCollisionEnter(Collision collision)
        {
            TryHandleSphereHit(collision.gameObject);
        }

        void OnTriggerEnter(Collider other)
        {
            TryHandleSphereHit(other.gameObject);
        }

        void TryHandleSphereHit(GameObject other)
        {
            if (other == null)
                return;

            var holder = other.GetComponent<BacklogIssueIdHolder>();
            if (holder == null || holder.IssueId <= 0)
                return;

            if (m_AssigneeId <= 0)
            {
                Debug.LogWarning($"[AssignableTarget] assigneeId が未設定です: {gameObject.name}");
                return;
            }

            int issueId = holder.IssueId;
            string issueKey = holder.IssueKey;

            StartCoroutine(AssignAndDestroyCoroutine(other, issueId, issueKey));
        }

        IEnumerator AssignAndDestroyCoroutine(GameObject sphere, int issueId, string issueKey)
        {
            // 球を即座に非表示にして二重処理を防ぐ
            sphere.SetActive(false);

            yield return BacklogApiClient.AssignIssue(
                issueId,
                m_AssigneeId,
                () =>
                {
                    var namePart = string.IsNullOrEmpty(m_DisplayName) ? "" : $" ({m_DisplayName})";
                    Debug.Log($"[AssignableTarget] 課題 #{issueKey} を担当者 ID {m_AssigneeId}{namePart} にアサインしました");
                },
                err => Debug.LogError($"[AssignableTarget] 課題アサインに失敗: {err}"));

            DestroySphere(sphere);
        }

        void DestroySphere(GameObject sphere)
        {
            if (sphere == null)
                return;

            if (sphere.TryGetComponent<NetworkObject>(out var networkObject) &&
                networkObject.IsValid &&
                networkObject.HasStateAuthority)
            {
                var runner = networkObject.Runner;
                if (runner != null && runner.IsRunning)
                {
                    runner.Despawn(networkObject);
                    return;
                }
            }

            Destroy(sphere);
        }
    }
}
