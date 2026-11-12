using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class AiPatternContext
{
    private readonly HashSet<AiPatternScoreRuleSO> consumedRules = new HashSet<AiPatternScoreRuleSO>();
    public bool IsConsumed(AiPatternScoreRuleSO rule) => consumedRules.Contains(rule);
    public void Consume(AiPatternScoreRuleSO rule) => consumedRules.Add(rule);

    // 페이즈 상태는 개체마다 따로 가진다(Profile SO는 여러 개체가 공유한다). 스폰마다 새 문맥으로 시작한다.
    private readonly List<AiAbility> phaseEntryQueue = new List<AiAbility>();
    /// <summary>현재 페이즈 번호. 스폰 뒤 첫 평가 전에는 -1이다.</summary>
    public int PhaseIndex { get; private set; } = -1;

    public void EnterPhase(int index)
    {
        PhaseIndex = index;
        phaseEntryQueue.Clear();
    }

    /// <summary>페이즈에 들어온 뒤 가장 먼저 쓸 패턴을 등록한 순서대로 줄 세운다.</summary>
    public void QueuePhaseEntry(AiAbility ability)
    {
        if (ability != null && !phaseEntryQueue.Contains(ability)) phaseEntryQueue.Add(ability);
    }

    public AiAbility PeekPhaseEntry()
    {
        // 꺼졌거나 데이터가 없는 패턴은 기다리지 않고 건너뛴다.
        while (phaseEntryQueue.Count > 0 && (phaseEntryQueue[0] == null
            || !phaseEntryQueue[0].isActiveAndEnabled || phaseEntryQueue[0].Data == null))
            phaseEntryQueue.RemoveAt(0);
        return phaseEntryQueue.Count > 0 ? phaseEntryQueue[0] : null;
    }

    public void CompletePhaseEntry(AiAbility ability) => phaseEntryQueue.Remove(ability);

    public AiBrain Brain { get; private set; }
    public EnemyController Controller { get; private set; }
    public Transform PlayerTransform { get; private set; }

    public void Initialize(AiBrain brain, EnemyController controller, Transform playerTransform)
    {
        Brain = brain;
        Controller = controller;
        PlayerTransform = playerTransform;
    }

    public void SetPlayerTransform(Transform playerTransform)
    {
        PlayerTransform = playerTransform;
    }

    public float GetPlayerDistanceX()
    {
        if (PlayerTransform == null || Controller == null)
            return float.MaxValue;

        return Mathf.Abs(PlayerTransform.position.x - Controller.transform.position.x);
    }

    public float GetHealthRatio()
    {
        var health = Controller != null ? Controller.Health : null;
        return health != null && health.maxHP > 0f ? health.currentHP / health.maxHP : 1f;
    }
}