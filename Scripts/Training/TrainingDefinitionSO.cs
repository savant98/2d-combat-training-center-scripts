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

    [Header("Reward Settings")]
    public int baseRewardGold = 100;

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

    public int GetRewardGoldByGrade(Grade grade)
    {
        float multiplier = 1.0f;
        switch (grade)
        {
            case Grade.Flawless: multiplier = 1.5f; break; // 최고 랭크 2배
            case Grade.Excellent: multiplier = 1.2f; break;
            default: multiplier = 1f; break;
        }
        return Mathf.RoundToInt(baseRewardGold * multiplier);
    }
}
