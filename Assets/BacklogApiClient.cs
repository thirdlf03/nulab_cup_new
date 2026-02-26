using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace NulabCup
{
    /// <summary>
    /// Backlog API の優先度・課題作成用クライアント。
    /// </summary>
    public static class BacklogApiClient
    {
        const string BaseUrl = "https://aiwh60018.backlog.com/api/v2";
        const string ApiKey = "zSmIZrqgKib5AUQnj9hrweuOEgtwJrzxVvqSdYkcGnKYDAoN8YNTIw8Qh4fiVjZH";

        [System.Serializable]
        public class Priority
        {
            public int id;
            public string name;
        }

        [System.Serializable]
        public class PriorityList
        {
            public List<Priority> items;
        }

        /// <summary>
        /// 課題作成 API のレスポンス（id, issueKey のみパース用）。
        /// </summary>
        [System.Serializable]
        public class CreateIssueResponse
        {
            public int id;
            public string issueKey;
        }

        public static string GetPrioritiesUrl()
        {
            return $"{BaseUrl}/priorities?apiKey={ApiKey}";
        }

        public static string GetCreateIssueUrl()
        {
            return $"{BaseUrl}/issues?apiKey={ApiKey}";
        }

        /// <summary>
        /// 優先度一覧を取得するコルーチン。
        /// </summary>
        public static IEnumerator FetchPriorities(System.Action<List<Priority>> onSuccess, System.Action<string> onError)
        {
            var url = GetPrioritiesUrl();
            using (var req = UnityWebRequest.Get(url))
            {
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke($"{req.responseCode}: {req.error}");
                    yield break;
                }

                var json = req.downloadHandler.text;
                var list = JsonHelper.FromJsonArray<Priority>(json);
                if (list != null)
                    onSuccess?.Invoke(list);
                else
                    onError?.Invoke("Failed to parse priorities JSON");
            }
        }

        /// <summary>
        /// 課題を作成するコルーチン。
        /// </summary>
        public static IEnumerator CreateIssue(
            int projectId,
            string summary,
            int issueTypeId,
            int priorityId,
            System.Action<string> onSuccess,
            System.Action<string> onError)
        {
            var url = GetCreateIssueUrl();
            var form = new WWWForm();
            form.AddField("projectId", projectId);
            form.AddField("summary", summary ?? "");
            form.AddField("issueTypeId", issueTypeId);
            form.AddField("priorityId", priorityId);

            using (var req = UnityWebRequest.Post(url, form))
            {
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke($"{req.responseCode}: {req.error}");
                    yield break;
                }

                var json = req.downloadHandler.text;
                onSuccess?.Invoke(json);
            }
        }
    }

    /// <summary>
    /// Unity の JsonUtility は配列のルートを直接デシリアライズできないため、
    /// ラッパーで JSON 配列をパースするヘルパー。
    /// </summary>
    public static class JsonHelper
    {
        public static List<T> FromJsonArray<T>(string json) where T : class
        {
            if (string.IsNullOrEmpty(json))
                return null;

            var wrapped = "{\"items\":" + json + "}";
            var wrapper = JsonUtility.FromJson<Wrapper<T>>(wrapped);
            if (wrapper?.items == null)
                return null;

            return new List<T>(wrapper.items);
        }

        [System.Serializable]
        class Wrapper<T>
        {
            public T[] items;
        }
    }
}
