using System;
using TMPro;
using UnityEngine;

/// <summary>두 훈련 화면이 같은 성적 구획과 실제 평가 조건을 사용한다.</summary>
public sealed class TrainingGradeConditionsView : MonoBehaviour
{
    [Serializable] public sealed class Card
    {
        public Grade grade;
        public TextMeshProUGUI hits, time, logic;
    }
    [SerializeField] private TrainingDefinitionSO training;
    [SerializeField] private UnityEngine.UI.GridLayoutGroup grid;
    [SerializeField] private Card[] cards;
    private Vector2 previousSize;
    private void OnEnable() { Refresh(); Fit(); }
    private void LateUpdate() { Fit(); }
    public void SetTraining(TrainingDefinitionSO value) { training = value; Refresh(); }
    private void Fit()
    {
        if (grid == null) return;
        Vector2 size = ((RectTransform)transform).rect.size;
        if (size == previousSize) return;
        previousSize = size;
        grid.cellSize = new Vector2(Mathf.Max(1, (size.x - grid.padding.horizontal - grid.spacing.x) * .5f),
            Mathf.Max(1, (size.y - grid.padding.vertical - grid.spacing.y) * .5f));
    }
    public void Refresh()
    {
        if (cards == null) return;
        foreach (var card in cards)
        {
            if (card.hits == null || card.time == null || card.logic == null) continue;
            Describe(training, card.grade, out string hits, out string time, out string logic);
            card.hits.text = hits; card.time.text = time; card.logic.text = logic;
        }
    }
    public static void Describe(TrainingDefinitionSO definition, Grade grade, out string hits, out string time, out string logic)
    {
        hits = time = "-"; logic = "";
        if (definition == null || definition.rules == null) return;
        Grade lookup = grade == Grade.Poor ? Grade.Pass : grade;
        foreach (var rule in definition.rules)
        {
            if (rule.grade != lookup) continue;
            if (grade == Grade.Poor)
            {
                // Poor의 숫자 규칙을 지나도 Evaluate는 Poor를 반환하므로 통과 한도의 여집합을 표시한다.
                hits = rule.maxHits >= 0 && rule.maxHits < int.MaxValue ? $"{rule.maxHits + 1}회 이상" : "피격 평가 제외";
                time = rule.maxTimeSeconds >= 0 ? TrainingScoreFormatHelper.FormatTime(rule.maxTimeSeconds) + " 초과" : "시간 평가 제외";
                logic = rule.maxHits >= 0 && rule.maxTimeSeconds >= 0 ? "또는" : "기준 미달";
            }
            else
            {
                hits = rule.maxHits >= 0 ? $"{rule.maxHits}회 이하" : "피격 제한 없음";
                time = rule.maxTimeSeconds >= 0 ? TrainingScoreFormatHelper.FormatTime(rule.maxTimeSeconds) + " 이내" : "시간 제한 없음";
                logic = rule.maxHits >= 0 && rule.maxTimeSeconds >= 0 ? "모두 충족" : "";
            }
            return;
        }
        if (grade == Grade.Poor) { hits = "상위 등급"; time = "조건 미충족"; }
    }
}
