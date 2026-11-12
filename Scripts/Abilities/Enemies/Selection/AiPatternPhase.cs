using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>한 페이즈에 등록한 패턴 하나. 같은 패턴도 페이즈마다 선택 수치를 따로 가진다.</summary>
[Serializable]
public sealed class AiPhasePattern
{
    [Tooltip("대상 EnemyAbilityDataSO의 abilityKey입니다.")]
    public string abilityKey;
    [Tooltip("이 페이즈에 들어온 뒤 가장 먼저 한 번 사용합니다. 쿨타임을 기다리지 않습니다.")]
    public bool useOnPhaseEnter;
    [Tooltip("이 페이즈에 들어올 때의 초기 점수입니다. 확률이 아니라 최고 점수를 선택합니다.")]
    public float baseWeight = 100f;
    [Tooltip("패턴 종료·중단 시 자신의 점수에 더합니다. 음수는 반복 사용을 억제합니다.")]
    public float weightChangeOnUse = -100f;
    [Tooltip("실행하지 않는 동안 초당 회복하는 점수입니다.")]
    [Min(0f)] public float weightRecoveryPerSecond;
    [Tooltip("Recover까지 종료하거나 중단한 시점부터 재사용까지의 대기 시간입니다.")]
    [Min(0f)] public float cooldown = 1f;
}

/// <summary>체력 비율로 들어가는 페이즈와 그 페이즈에서 점수로 고를 수 있는 패턴 목록.</summary>
[Serializable]
public sealed class AiPatternPhase
{
    public string displayName = "1페이즈";
    [Tooltip("체력 비율이 이 값 미만이 되면 이 페이즈로 들어갑니다. 첫 페이즈는 스폰부터 적용되므로 이 값을 쓰지 않습니다.")]
    [Range(0f, 1f)] public float enterBelowHealthRatio = 1f;
    public List<AiPhasePattern> patterns = new List<AiPhasePattern>();

    public AiPhasePattern Find(string abilityKey)
    {
        if (string.IsNullOrEmpty(abilityKey) || patterns == null) return null;
        foreach (var pattern in patterns)
            if (pattern != null && pattern.abilityKey == abilityKey) return pattern;
        return null;
    }
}
