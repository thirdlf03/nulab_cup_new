using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using Oculus.Interaction.Samples;

namespace NulabCup
{
    /// <summary>
    /// Backlog 課題作成 UI を制御する。
    /// - 起動時に優先度を API から取得して DropDown に設定
    /// - ボタンクリックで TextInputField の値と DropDown の選択を送信して課題作成
    /// </summary>
    public class BacklogIssueCreator : MonoBehaviour
    {
        [Header("Backlog Settings")]
        [SerializeField] int m_ProjectId = 746593;
        [SerializeField] int m_IssueTypeId = 3984946;
        [SerializeField, Tooltip("0 の場合は DropDown の選択を使用。1以上で固定の優先度IDを指定")]
        int m_PriorityId = 2;

        [Header("UI References")]
        [SerializeField] TMP_InputField m_SummaryInput;
        [SerializeField] Dropdown m_PriorityDropdown;
        [SerializeField] TMP_Dropdown m_PriorityDropdownTMP;
        [SerializeField] Button m_CreateButton;
        [SerializeField] DropDownGroup m_PriorityDropDownGroup;

        [Header("Sphere Spawn")]
        [SerializeField] GameObject m_SpherePrefab;
        [SerializeField] Transform m_SpawnPositionReference;
        [SerializeField] float m_SpawnDistance = 2f;
        [SerializeField] float m_SpawnHeightOffset = 1.5f;

        List<BacklogApiClient.Priority> m_Priorities = new();

        void Start()
        {
            ResolveUiReferences();
            if (m_CreateButton != null)
                m_CreateButton.onClick.AddListener(OnCreateButtonClicked);

            StartCoroutine(LoadPriorities());
        }

        void ResolveUiReferences()
        {
            if (m_SummaryInput == null)
                m_SummaryInput = FindComponentInChildren<TMP_InputField>("TextInputField");
            if (m_SummaryInput == null && TryFind("TextInputField", out var inputGo))
                m_SummaryInput = inputGo.GetComponentInChildren<TMP_InputField>(true);

            if (m_CreateButton == null)
                m_CreateButton = FindComponentInChildren<Button>("PrimaryButton_IconAndLabel_UnityUIButton");
            if (m_CreateButton == null && TryFind("PrimaryButton_IconAndLabel_UnityUIButton", out var btnGo))
                m_CreateButton = btnGo.GetComponentInChildren<Button>(true);

            // Dropdown: Create Button と同じ親の子から検索（QDS の DropDown1LineTextOnly は DropDownGroup を使用）
            if (m_PriorityDropdown == null && m_PriorityDropdownTMP == null && m_PriorityDropDownGroup == null)
            {
                if (TryFind("DropDown1LineTextOnly", out var dropGo))
                {
                    m_PriorityDropdown = dropGo.GetComponentInChildren<Dropdown>(true);
                    m_PriorityDropdownTMP = dropGo.GetComponentInChildren<TMP_Dropdown>(true);
                    m_PriorityDropDownGroup = dropGo.GetComponentInChildren<DropDownGroup>(true);
                }
                if (m_PriorityDropDownGroup == null && m_CreateButton != null)
                {
                    var parent = m_CreateButton.transform.parent;
                    if (parent != null)
                    {
                        m_PriorityDropdown = parent.GetComponentInChildren<Dropdown>(true);
                        m_PriorityDropdownTMP = parent.GetComponentInChildren<TMP_Dropdown>(true);
                        m_PriorityDropDownGroup = parent.GetComponentInChildren<DropDownGroup>(true);
                    }
                }
                if (m_PriorityDropdown == null && m_PriorityDropdownTMP == null && m_PriorityDropDownGroup == null)
                {
                    m_PriorityDropdown = FindComponentInChildren<Dropdown>("DropDown1LineTextOnly");
                    m_PriorityDropdownTMP = FindComponentInChildren<TMP_Dropdown>("DropDown1LineTextOnly");
                    m_PriorityDropDownGroup = FindComponentInChildren<DropDownGroup>("DropDown1LineTextOnly");
                }
            }
        }

        T FindComponentInChildren<T>(string name) where T : Component
        {
            var t = transform;
            while (t != null)
            {
                var c = t.GetComponentInChildren<T>(true);
                if (c != null)
                    return c;
                t = t.parent;
            }
#if UNITY_2023_1_OR_NEWER
            return FindFirstObjectByType<T>(FindObjectsInactive.Include);
#else
            var arr = FindObjectsOfType<T>(true);
            return arr != null && arr.Length > 0 ? arr[0] : null;
#endif
        }

        static T FindInChildren<T>(Transform root, string name) where T : Component
        {
            if (root == null) return null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == name && t.TryGetComponent<T>(out var c))
                    return c;
            }
            return null;
        }

        bool TryFind(string name, out GameObject go)
        {
            go = GameObject.Find(name);
            if (go == null)
            {
                var all = FindObjectsOfType<Transform>(true);
                foreach (var t in all)
                {
                    if (t.name == name)
                    {
                        go = t.gameObject;
                        return true;
                    }
                }
            }
            return go != null;
        }

        IEnumerator LoadPriorities()
        {
            yield return BacklogApiClient.FetchPriorities(
                list =>
                {
                    m_Priorities = list ?? new List<BacklogApiClient.Priority>();
                    ApplyPrioritiesToDropdown();
                },
                err => Debug.LogError($"[BacklogIssueCreator] Failed to load priorities: {err}"));
        }

        void ApplyPrioritiesToDropdown()
        {
            if (m_PriorityDropdown != null)
            {
                m_PriorityDropdown.ClearOptions();
                var options = new List<Dropdown.OptionData>();
                foreach (var p in m_Priorities)
                    options.Add(new Dropdown.OptionData(p.name));
                m_PriorityDropdown.AddOptions(options);
                m_PriorityDropdown.SetValueWithoutNotify(0);
                m_PriorityDropdown.RefreshShownValue();
            }
            else if (m_PriorityDropdownTMP != null)
            {
                m_PriorityDropdownTMP.ClearOptions();
                var options = new List<TMP_Dropdown.OptionData>();
                foreach (var p in m_Priorities)
                    options.Add(new TMP_Dropdown.OptionData(p.name));
                m_PriorityDropdownTMP.AddOptions(options);
                m_PriorityDropdownTMP.SetValueWithoutNotify(0);
                m_PriorityDropdownTMP.RefreshShownValue();
            }
            else if (m_PriorityDropDownGroup != null)
            {
                ApplyPrioritiesToDropDownGroup();
            }
        }

        void ApplyPrioritiesToDropDownGroup()
        {
            var toggleGroup = m_PriorityDropDownGroup.GetComponentInChildren<ToggleGroup>(true);
            if (toggleGroup == null) return;

            var toggles = toggleGroup.GetComponentsInChildren<Toggle>(true)
                .Where(t => t.group == toggleGroup).ToArray();

            for (int i = 0; i < m_Priorities.Count && i < toggles.Length; i++)
            {
                var title = FindInChildren<TextMeshProUGUI>(toggles[i].transform, "Title");
                if (title != null)
                    title.text = m_Priorities[i].name;
                toggles[i].gameObject.SetActive(true);
            }
            for (int i = m_Priorities.Count; i < toggles.Length; i++)
                toggles[i].gameObject.SetActive(false);
        }

        void OnCreateButtonClicked()
        {
            var summary = GetSummaryText();
            if (string.IsNullOrWhiteSpace(summary))
            {
                Debug.LogWarning("[BacklogIssueCreator] 概要を入力してください");
                return;
            }

            var priorityId = GetSelectedPriorityId();
            if (priorityId <= 0)
            {
                Debug.LogWarning("[BacklogIssueCreator] 優先度を選択してください");
                return;
            }

            if (m_CreateButton != null)
                m_CreateButton.interactable = false;

            StartCoroutine(CreateIssueCoroutine(summary.Trim(), priorityId));
        }

        string GetSummaryText()
        {
            if (m_SummaryInput != null)
                return m_SummaryInput.text;

            var inputField = GetComponentInChildren<InputField>(true);
            return inputField != null ? inputField.text : "";
        }

        int GetSelectedPriorityId()
        {
            if (m_PriorityId > 0)
                return m_PriorityId;

            int index;
            if (m_PriorityDropdown != null)
                index = m_PriorityDropdown.value;
            else if (m_PriorityDropdownTMP != null)
                index = m_PriorityDropdownTMP.value;
            else if (m_PriorityDropDownGroup != null)
                index = m_PriorityDropDownGroup.SelectedIndex;
            else
                return 0;

            if (index < 0 || index >= m_Priorities.Count)
                return 0;

            return m_Priorities[index].id;
        }

        IEnumerator CreateIssueCoroutine(string summary, int priorityId)
        {
            yield return BacklogApiClient.CreateIssue(
                m_ProjectId,
                summary,
                m_IssueTypeId,
                priorityId,
                json =>
                {
                    Debug.Log($"[BacklogIssueCreator] 課題を作成しました: {json}");
                    if (m_SummaryInput != null)
                        m_SummaryInput.text = "";
                    var inputField = GetComponentInChildren<InputField>(true);
                    if (inputField != null)
                        inputField.text = "";
                    SpawnSphereWithIssueId(json);
                },
                err =>
                {
                    Debug.LogError($"[BacklogIssueCreator] 課題作成に失敗しました: {err}");
                });

            if (m_CreateButton != null)
                m_CreateButton.interactable = true;
        }

        void SpawnSphereWithIssueId(string createIssueJson)
        {
            if (m_SpherePrefab == null)
                return;

            int issueId = 0;
            string issueKey = null;
            if (!string.IsNullOrEmpty(createIssueJson))
            {
                var response = JsonUtility.FromJson<BacklogApiClient.CreateIssueResponse>(createIssueJson);
                if (response != null)
                {
                    issueId = response.id;
                    issueKey = response.issueKey;
                }
            }

            var spawnPos = GetSpawnPosition();
            var sphere = Instantiate(m_SpherePrefab, spawnPos, Quaternion.identity);

            var holder = sphere.GetComponent<BacklogIssueIdHolder>();
            if (holder == null)
                holder = sphere.AddComponent<BacklogIssueIdHolder>();
            holder.SetIssueData(issueId, issueKey);

            if (sphere.TryGetComponent<Rigidbody>(out var rb))
            {
#if UNITY_6000_0_OR_NEWER
                rb.linearVelocity = Vector3.zero;
#else
#pragma warning disable CS0618
                rb.velocity = Vector3.zero;
#pragma warning restore CS0618
#endif
                rb.angularVelocity = Vector3.zero;
            }
        }

        Vector3 GetSpawnPosition()
        {
            if (m_SpawnPositionReference != null)
                return m_SpawnPositionReference.position + m_SpawnPositionReference.forward * m_SpawnDistance + Vector3.up * m_SpawnHeightOffset;

            var cam = Camera.main;
            if (cam != null)
                return cam.transform.position + cam.transform.forward * m_SpawnDistance + Vector3.up * m_SpawnHeightOffset;

            return transform.position + Vector3.forward * m_SpawnDistance + Vector3.up * m_SpawnHeightOffset;
        }
    }
}
