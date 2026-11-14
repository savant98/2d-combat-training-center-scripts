using System.Text;
using TMPro;
using UnityEngine;

/// <summary>훈련 메뉴와 단말기에 같은 최고 기록 표기와 실제 성적 조건을 표시한다.</summary>
[DisallowMultipleComponent]
public sealed class TrainingRecordView : MonoBehaviour
{
    [SerializeField] private TrainingDefinitionSO training;
    [SerializeField] private TextMeshProUGUI gradeText, hitCountText, timeText, conditionsText;

    private void OnEnable()
    {
        if (Application.isPlaying) Refresh();
    }

    public void Refresh()
    {
        var data = DataManager.Instance;
        var best = training != null && data != null ? data.GetTrainingRoomRecord(training.trainingId) : null;
        bool hasRecord = DataManager.IsCleared(best);
        gradeText.text = "-";
        gradeText.color = Color.white;
        hitCountText.text = hasRecord ? best.bestHitCount.ToString() : "-";
        timeText.text = hasRecord ? TrainingScoreFormatHelper.FormatTime(best.bestTimeSeconds) : "-";
        if (hasRecord) TrainingScoreFormatHelper.GradeToText(best.bestGrade, gradeText);
        conditionsText.text = FormatConditions(training);
    }

    public static string FormatConditions(TrainingDefinitionSO definition)
    {
        if (definition == null || definition.rules == null) return "-";
        var text = new StringBuilder();
        foreach (var rule in definition.rules)
        {
            if (rule.grade == Grade.Poor) continue;
            string name, color;
            switch (rule.grade)
            {
                case Grade.Flawless: name = "무결점"; color = "00FFFF"; break;
                case Grade.Excellent: name = "우수"; color = "FFFF00"; break;
                case Grade.Pass: name = "통과"; color = "00FF00"; break;
                default: continue;
            }
            if (text.Length > 0) text.Append('\n');
            text.Append("<color=#").Append(color).Append('>').Append(name).Append("</color>  ");
            text.Append(rule.maxHits < 0 ? "피격 제한 없음" : $"피격 {rule.maxHits}회 이하");
            text.Append(" · ");
            text.Append(rule.maxTimeSeconds < 0 ? "시간 제한 없음" : TrainingScoreFormatHelper.FormatTime(rule.maxTimeSeconds) + " 이내");
        }
        return text.Length > 0 ? text.ToString() : "-";
    }
}
