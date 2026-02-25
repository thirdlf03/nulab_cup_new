using UnityEngine;

namespace NulabCup
{
    [DefaultExecutionOrder(-10000)]
    public class App120HzConfigurator : MonoBehaviour
    {
        [SerializeField, Range(30, 240)] int m_TargetFrameRate = 120;
        [SerializeField] bool m_ApplyOnAwake = true;
        [SerializeField] bool m_SetQuestDisplayFrequency = true;
        [SerializeField] bool m_LogResult = true;

        void Awake()
        {
            if (m_ApplyOnAwake)
                ApplySettings();
        }

        public void ApplySettings()
        {
            QualitySettings.vSyncCount = 0;
            Application.targetFrameRate = m_TargetFrameRate;

#if UNITY_ANDROID && !UNITY_EDITOR
            float beforeHz = 0f;
            float appliedHz = 0f;
            bool displayApplied = false;

            if (m_SetQuestDisplayFrequency)
                displayApplied = TryApplyQuestDisplayFrequency(m_TargetFrameRate, out beforeHz, out appliedHz);

            if (m_LogResult)
            {
                Debug.Log($"[App120HzConfigurator] targetFrameRate={Application.targetFrameRate}, vSync={QualitySettings.vSyncCount}, displayApplied={displayApplied}, beforeHz={beforeHz:0.##}, appliedHz={appliedHz:0.##}");
            }
#else
            if (m_LogResult)
            {
                Debug.Log($"[App120HzConfigurator] targetFrameRate={Application.targetFrameRate}, vSync={QualitySettings.vSyncCount}");
            }
#endif
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        bool TryApplyQuestDisplayFrequency(int targetHz, out float beforeHz, out float appliedHz)
        {
            beforeHz = 0f;
            appliedHz = 0f;

            var display = OVRManager.display;
            if (display == null)
                return false;

            beforeHz = display.displayFrequency;
            float[] available = display.displayFrequenciesAvailable;

            if (available == null || available.Length == 0)
            {
                display.displayFrequency = targetHz;
                appliedHz = display.displayFrequency;
                return appliedHz > 0f;
            }

            float bestHz = SelectBestFrequency(available, targetHz);
            display.displayFrequency = bestHz;
            appliedHz = display.displayFrequency;
            return appliedHz > 0f;
        }

        float SelectBestFrequency(float[] available, int targetHz)
        {
            float best = available[0];
            float bestDistance = Mathf.Abs(best - targetHz);

            for (int i = 1; i < available.Length; i++)
            {
                float candidate = available[i];
                float distance = Mathf.Abs(candidate - targetHz);

                if (distance < bestDistance)
                {
                    best = candidate;
                    bestDistance = distance;
                }
                else if (Mathf.Approximately(distance, bestDistance) && candidate > best)
                {
                    best = candidate;
                }
            }

            return best;
        }
#endif
    }
}
