using System.Collections.Generic;
using UnityEngine;

public class AiBrain : MonoBehaviour
{
    [SerializeField] private AiAbility[] abilities;
    public IReadOnlyList<AiAbility> Abilities => abilities;
    public bool IsPaused => isPaused;
    public AiAbility current;
    public bool isPaused = false;
    public string LastAbilityKey { get; private set; }

    private AiAbility forcedAbility;
    private bool forcedIgnoresCooldown;
    private AiPatternDirector patternDirector;
    private Dictionary<string, AiAbility> abilityMap;
    private long selectionOrder;
    private bool initialized;
    private bool spawnPending;
    // 실행 중 전환 판단(TryYieldToPattern)의 주기 확인. 패턴 실행마다(LastSelectionOrder) 개체별 위상만큼 늦춰 시작한다.
    private EnemyCheckTimer yieldCheckTimer;
    private long yieldCheckOrder = -1;

    private void Start() => EnsureInitialized();

    private void EnsureInitialized()
    {
        if (initialized) return;
        initialized = true;
        abilities ??= new AiAbility[0];
        var controller = GetComponent<EnemyController>();
        patternDirector = GetComponent<AiPatternDirector>();
        foreach (var ability in abilities)
            if (ability != null) ability.Initialize(this, controller);
        BuildAbilityMap();
        if (patternDirector != null) patternDirector.Initialize(this, controller);
        if (spawnPending) ResetForSpawn();
    }

    private void Update()
    {
        if (isPaused) return;
        if (current != null && current.IsRunning)
        {
            current.UpdateAbility();
            if (IsYieldCheckDue()) TryYieldToPattern();
            return;
        }
        SelectNextAbility();
    }

    /// <summary>후속 연계를 예약한다. 기본값은 종료 기준 쿨다운을 지키며 준비될 때까지 예약을 유지한다.</summary>
    public void ForceNextAbility(AiAbility ability, bool ignoreCooldown = false)
    {
        if (ability == null || !Contains(ability)) return;
        forcedAbility = ability;
        forcedIgnoresCooldown = ignoreCooldown;
    }

    private bool Contains(AiAbility ability) => abilities != null && System.Array.IndexOf(abilities, ability) >= 0;

    private bool CanStart(AiAbility ability, bool ignoreCooldown = false)
        => ability != null && Contains(ability) && ability.Data != null && ability.isActiveAndEnabled
            && !ability.IsRunning && (ignoreCooldown || ability.IsReady());

    private void StartSelected(AiAbility ability)
    {
        current = ability;
        current.StartAbility();
    }

    internal void NotifyAbilityStarted(AiAbility ability)
    {
        ability.RecordSelection(++selectionOrder);
        if (patternDirector != null) patternDirector.NotifyAbilityStarted(ability);
        LastAbilityKey = ability.AbilityKey;
    }

    // 순찰처럼 실행 중 자리를 넘기는 패턴은 감지 규칙(범위·사격선 광선)과 페이즈 갱신을 패턴의 YieldCheckInterval마다 묻는다.
    // 0이면 예전처럼 매 프레임 묻는다. 감지 반응은 최대 그 간격만큼 늦어진다.
    private bool IsYieldCheckDue()
    {
        if (current == null || !current.CanYieldToPattern) return false;
        float interval = current.YieldCheckInterval;
        if (interval <= 0f) return true;
        if (yieldCheckOrder != current.LastSelectionOrder)
        {
            yieldCheckOrder = current.LastSelectionOrder;
            yieldCheckTimer.StartStaggered(interval);
        }
        bool due = yieldCheckTimer.Due(interval);
        EnemyCheckStats.Add(due ? EnemyCheckKind.PatrolYield : EnemyCheckKind.PatrolYieldSkipped);
        return due;
    }

    private void TryYieldToPattern()
    {
        if (current == null || !current.IsRunning || !current.CanYieldToPattern) return;
        bool ignoreCooldown = false;
        AiAbility next = patternDirector != null
            ? patternDirector.GetAbsoluteAbility(out ignoreCooldown) : null;
        if (next == null)
        {
            // 예약은 현재 순찰을 중단시키지 않고, 기존 Profile의 양보 경로를 보존한다.
            if (forcedAbility != null || patternDirector == null) return;
            ignoreCooldown = false;
            next = patternDirector.ResolveNextAbility(current);
        }
        if (next == current || !CanStart(next, ignoreCooldown)) return;
        current.StopAbility();
        StartSelected(next);
    }

    public AiAbility GetAbilityByKey(string abilityKey)
    {
        if (string.IsNullOrEmpty(abilityKey)) return null;
        if (abilityMap == null) BuildAbilityMap();
        abilityMap.TryGetValue(abilityKey, out AiAbility ability);
        return ability;
    }

    private void BuildAbilityMap()
    {
        abilityMap = new Dictionary<string, AiAbility>();
        if (abilities == null) return;
        foreach (var ability in abilities)
        {
            if (ability == null || string.IsNullOrWhiteSpace(ability.AbilityKey)) continue;
            if (abilityMap.ContainsKey(ability.AbilityKey))
            {
                Debug.LogWarning($"[AiBrain] 중복 패턴 키: {ability.AbilityKey}. 패턴마다 고유 키를 지정하세요.", ability);
                continue;
            }
            abilityMap.Add(ability.AbilityKey, ability);
        }
    }

    private void SelectNextAbility()
    {
        // 페이즈 전환 같은 절대 우선 규칙은 예약과 일반 점수보다 먼저 평가한다.
        bool ignoreCooldown = false;
        var absolute = patternDirector != null ? patternDirector.GetAbsoluteAbility(out ignoreCooldown) : null;
        if (CanStart(absolute, ignoreCooldown))
        {
            StartSelected(absolute);
            return;
        }
        if (forcedAbility != null)
        {
            if (!forcedAbility.isActiveAndEnabled || forcedAbility.Data == null)
                forcedAbility = null;
            else
            {
                if (!CanStart(forcedAbility, forcedIgnoresCooldown)) return;
                var next = forcedAbility;
                forcedAbility = null;
                StartSelected(next);
                return;
            }
        }
        AiAbility selected = null;
        float highestScore = float.NegativeInfinity;
        foreach (var ability in abilities)
        {
            if (!CanStart(ability) || !ability.AllowWeightedSelection) continue;
            if (patternDirector != null && !patternDirector.IsCandidateAvailable(ability)) continue;
            float score = patternDirector != null ? patternDirector.EvaluateScore(ability) : ability.CurrentWeight;
            if (float.IsNaN(score)) continue;
            if (selected == null || score > highestScore
                || (score == highestScore && ability.LastSelectionOrder < selected.LastSelectionOrder))
            {
                selected = ability;
                highestScore = score;
            }
        }
        // 기존 규칙은 최종 후보에 한 번만 적용한다. 순환 규칙의 평가 부작용을 방지한다.
        if (patternDirector != null) selected = patternDirector.ResolveNextAbility(selected);
        if (CanStart(selected)) StartSelected(selected);
    }

    private void StopAllAbilities()
    {
        if (abilities != null)
            foreach (var ability in abilities)
                if (ability != null && ability.IsRunning) ability.StopAbility();
        current = null;
    }

    public void PauseBrain()
    {
        isPaused = true;
        StopAllAbilities();
        forcedAbility = null;
    }

    /// <summary>쉴드 Break 복귀는 점수·쿨다운을 초기화하지 않는다.</summary>
    public void ResumeBrain()
    {
        EnsureInitialized();
        isPaused = false;
    }

    /// <summary>풀의 새 스폰에만 사용한다. 첫 행동도 기본 점수와 Profile로 선택한다.</summary>
    public void ResetForSpawn()
    {
        if (!initialized) { spawnPending = true; return; }
        spawnPending = false;
        StopAllAbilities();
        forcedAbility = null;
        selectionOrder = 0;
        yieldCheckOrder = -1;
        LastAbilityKey = null;
        foreach (var ability in abilities)
            if (ability != null) ability.ResetForSpawn();
        if (patternDirector != null) patternDirector.ResetForSpawn();
    }
}
