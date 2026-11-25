using System;
using UnityEngine;

/// <summary>
/// 훈련 테마별 Prop 교체표. 템플릿은 기본 테마(기본 훈련장)의 Prop으로 저작하고, 다른 훈련은 이 표로 같은 자리에 테마 Prop을 놓는다.
/// 던전 설정에 테마를 지정하지 않으면 템플릿 그대로 쓴다.
/// </summary>
[CreateAssetMenu(menuName = "Training/Dungeon Theme", fileName = "DungeonTheme")]
public class DungeonThemeSO : ScriptableObject
{
    [Serializable]
    public sealed class PropSwap
    {
        public GameObject from;
        public GameObject to;
    }

    public PropSwap[] swaps = Array.Empty<PropSwap>();

    /// <summary>prefab을 이 테마의 Prefab으로 바꾼다. 교체표에 없으면 그대로 돌려준다.</summary>
    public GameObject Resolve(GameObject prefab)
    {
        if (prefab == null || swaps == null) return prefab;
        foreach (var swap in swaps)
            if (swap != null && swap.from == prefab && swap.to != null) return swap.to;
        return prefab;
    }
}
