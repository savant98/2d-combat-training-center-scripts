using TMPro;
using UnityEngine;

/// <summary>분석기를 시작하지 않은 타격만 Dummy 바로 옆에 간단히 표시한다.</summary>
public sealed class SelfTrainingDummyReadout : MonoBehaviour
{
    [SerializeField] private TrainingDummyHealth target;
    [SerializeField] private CanvasGroup visibility;
    [SerializeField] private TextMeshProUGUI totalText, maximumText, dpsText;
    public void Bind(TrainingDummyHealth value) { target = value; }
    private void OnEnable()
    {
        if (target == null) target = GetComponentInParent<TrainingDummyHealth>();
        if (visibility != null) visibility.alpha = 0f;
    }
    private void LateUpdate()
    {
        if (target == null || visibility == null) return;
        visibility.alpha = target.IsPracticeVisible ? 1f : 0f;
        visibility.blocksRaycasts = visibility.interactable = false;
        if (!target.IsPracticeVisible) return;
        var stats = target.PracticeStatistics;
        if (totalText != null) totalText.text = stats.TotalDamage.ToString("N1");
        if (maximumText != null) maximumText.text = stats.MaximumHit.ToString("N1");
        if (dpsText != null) dpsText.text = stats.Dps.ToString("N1");
    }
}
