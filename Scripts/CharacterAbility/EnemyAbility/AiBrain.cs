using System.Linq;
using UnityEngine;
using System.Collections.Generic;

public class AiBrain : MonoBehaviour
{
    [SerializeField] private AiAbility[] abilities;

    public bool IsPaused => isPaused;
    public AiAbility current;

    public bool isPaused = false;
    
    private AiAbility forcedAbility;
    private AiPatternDirector patternDirector;

    private Dictionary<string, AiAbility> abilityMap;

    private void Start()
    {
        var controller = GetComponent<EnemyController>();
        patternDirector = GetComponent<AiPatternDirector>();

        foreach (var a in abilities)
            a.Initialize(this, controller);

        BuildAbilityMap();

        if (patternDirector != null)
            patternDirector.Initialize(this, controller);
    }

    private void Update()
    {
        if (isPaused)
            return;

        // 실행 중이면 해당 Ability 계속 수행
        if (current != null && current.IsRunning)
        {
            current.UpdateAbility();
            return;
        }

        // 다음 Ability 선택
        SelectNextAbility();
    }
    public void ForceNextAbility(AiAbility ability)
    {
        forcedAbility = ability;
    }
    public AiAbility GetAbilityByKey(string abilityKey)
    {
        if (string.IsNullOrEmpty(abilityKey))
            return null;

        if (abilityMap == null)
            BuildAbilityMap();

        abilityMap.TryGetValue(abilityKey, out AiAbility ability);
        return ability;
    }

    private void BuildAbilityMap()
    {
        abilityMap = new Dictionary<string, AiAbility>();

        for (int i = 0; i < abilities.Length; i++)
        {
            AiAbility ability = abilities[i];
            if (ability == null)
                continue;

            if (string.IsNullOrWhiteSpace(ability.AbilityKey))
                continue;

            if (abilityMap.ContainsKey(ability.AbilityKey))
            {
                Debug.LogWarning(
                    $"[AiBrain] Duplicate AbilityKey detected: {ability.AbilityKey}",
                    ability);
                continue;
            }

            abilityMap.Add(ability.AbilityKey, ability);
        }
    }
    private void SelectNextAbility()
    {
        // 강제 어빌리티 연결
        if (forcedAbility != null)
        {
            current = forcedAbility;
            forcedAbility = null;
            current.StartAbility();
            return;
        }

        // 사용할 수 있는 Ability만 필터링
        List<AiAbility> readyList = abilities.Where(a => a.IsReady() && a.AllowRandomSelection).ToList();

        if (readyList.Count == 0)
            return; // 아무것도 준비된 게 없다면 대기

        int idx = Random.Range(0, readyList.Count);
        AiAbility selected = readyList[idx];

        if (patternDirector != null)
            selected = patternDirector.ResolveNextAbility(selected);

        if (selected == null || (!selected.IsReady() && selected.AllowRandomSelection))
            return;

        current = selected;
        current.StartAbility();
    }
    private void StopAllAbilities()
    {
        foreach (var a in abilities)
        {
            if (a.IsRunning)
                a.StopAbility();
        }

        current = null;
    }
    public void PauseBrain()
    {
        isPaused = true;
        StopAllAbilities();
    }
    public void ResumeBrain()
    {
        isPaused = false;

        if (abilities.Length <= 0)
            return;

        ForceNextAbility(abilities[0]);
    }
}
