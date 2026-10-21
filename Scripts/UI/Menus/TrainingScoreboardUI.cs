using DG.Tweening;
using System.Collections;
using TMPro;
using UnityEngine;

public class TrainingScoreboardUI : MonoBehaviour
{
    [Header("Scoreboard")]
    [SerializeField] private float scoreBoardUpdateInterval = 1f;
    [SerializeField] private CanvasGroup scoreBoardCanvasGroup;
    [SerializeField] private TextMeshProUGUI hitText;
    [SerializeField] private TextMeshProUGUI timeText;
    [SerializeField] private TextMeshProUGUI gradeText;

    private TrainingRunTracker tracker;
    private bool scoreBoardOn = false;
    private Coroutine coroutine;
    public void ShowTrainingScoreboard(TrainingRunTracker tracker)
    {
        this.tracker = tracker;
        scoreBoardOn = true;
        scoreBoardCanvasGroup.DOFade(1f, 1f);

        if (coroutine != null)
            StopCoroutine(coroutine);

        coroutine = StartCoroutine(UpdateScoreBoardRoutine());
    }
    public void HideTrainingScoreboard()
    {
        scoreBoardOn = false;
        scoreBoardCanvasGroup.DOFade(0f, 1f);
    }
    private IEnumerator UpdateScoreBoardRoutine()
    {
        var loopValue = new WaitForSeconds(scoreBoardUpdateInterval);
        while (scoreBoardOn)
        {
            yield return loopValue;

            hitText.text = (tracker.TrainingDefinition.rules[2].maxHits - tracker.CurrentHItCount).ToString();
            timeText.text = TrainingScoreFormatHelper.FormatTime(tracker.TrainingDefinition.rules[2].maxTimeSeconds - tracker.CurrentTimeSeconds);
            TrainingScoreFormatHelper.GradeToText(tracker.CurrentPredictedGrade, gradeText);
        }
    }
}
