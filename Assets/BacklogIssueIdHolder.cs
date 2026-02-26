using UnityEngine;

namespace NulabCup
{
    /// <summary>
    /// Backlog 課題の ID を保持するコンポーネント。
    /// BacklogIssueCreator が sphere をスポーンする際に、POST レスポンスから取得した ID を設定する。
    /// </summary>
    public class BacklogIssueIdHolder : MonoBehaviour
    {
        [SerializeField] int m_IssueId;
        [SerializeField] string m_IssueKey;

        public int IssueId => m_IssueId;
        public string IssueKey => m_IssueKey;

        /// <summary>
        /// Backlog API のレスポンスから取得した課題情報を設定する。
        /// </summary>
        public void SetIssueData(int issueId, string issueKey)
        {
            m_IssueId = issueId;
            m_IssueKey = issueKey ?? "";
        }
    }
}
