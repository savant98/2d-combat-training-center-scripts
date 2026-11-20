using UnityEngine;

[System.Serializable]
public struct GradeRule
{
    public Grade grade;
    public int maxHits;
    public float maxTimeSeconds;
}

[CreateAssetMenu(menuName ="Training/TrainingDefinition")]
public class TrainingDefinitionSO : ScriptableObject
{
    public string trainingId;
    public string displayName;

    // 수료 보상(훈련 필증)과 훈련 메뉴의 순서는 정의가 아니라 훈련 목록(TrainingCatalogSO)이 정한다.
    [Header("Grade Rules")]
    public GradeRule[] rules;

    public Grade Evaluate(int hits, float timeSeconds)
    {
        // rules는 높은 등급부터 들어온다고 가정
        foreach (GradeRule rule in rules)
        {
            bool hitOk = (rule.maxHits < 0) || (hits <= rule.maxHits);
            bool timeOk = (rule.maxTimeSeconds < 0) || (timeSeconds <= rule.maxTimeSeconds);
            if (hitOk && timeOk)
            {
                return rule.grade;
            }
        }
        return Grade.Poor;
    }
}
