using System.Collections.Generic;
using UnityEngine;

/// <summary>던전에서 함정 외형을 장식·시설 Prop 앞에 표시하고 풀에 돌려보낼 때 원래 순서로 되돌린다.</summary>
[DisallowMultipleComponent]
public sealed class DungeonHazardSorting : MonoBehaviour
{
    // Default의 내부 순서는 유지하고, Player 레이어의 출입구 기본 순서(6)보다 앞에서 그린다.
    private const int PropFrontOffset = 8;
    private readonly List<(Renderer renderer, int order)> changed = new();

    public static void MoveInFrontOfProps(GameObject target)
    {
        if (!target.TryGetComponent(out DungeonHazardSorting sorting))
            sorting = target.AddComponent<DungeonHazardSorting>();
        sorting.Apply();
    }

    private void Apply()
    {
        if (changed.Count != 0) return;
        int layer = SortingLayer.NameToID("Player");
        foreach (var renderer in GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.sortingLayerID != 0 || renderer.CompareTag("EditorOnly")) continue;
            changed.Add((renderer, renderer.sortingOrder));
            renderer.sortingLayerID = layer;
            renderer.sortingOrder = PropFrontOffset + renderer.sortingOrder;
        }
    }

    private void OnDisable()
    {
        foreach (var (renderer, order) in changed)
        {
            if (renderer == null) continue;
            renderer.sortingLayerID = 0;
            renderer.sortingOrder = order;
        }
        changed.Clear();
    }
}
