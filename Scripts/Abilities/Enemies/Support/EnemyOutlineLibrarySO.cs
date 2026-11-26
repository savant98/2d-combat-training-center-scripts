using System;
using System.Collections.Generic;
using UnityEngine;

// 적 외곽선 대비 선(EnemyOutlineStroke)이 쓰는 거리장 Sprite 표(2026-11-26).
// GlowMultiply 부품의 보이는 모양은 Sprite 색 × 재질 마스크(_EmissionTex)이므로 둘의 조합마다 거리장을 하나씩 둔다.
// Animation이 Sprite를 바꾸거나 재질이 바뀌어도 표에서 찾아 따라간다. 표와 거리장 텍스처는 tools/Unity/EnemyOutlineStrokeSetup.cs가 만든다.
[CreateAssetMenu(menuName = "Enemy/Outline Library", fileName = "EnemyOutlineLibrary")]
public sealed class EnemyOutlineLibrarySO : ScriptableObject
{
    [Serializable]
    public struct Entry
    {
        public Sprite source;
        [Tooltip("원본 재질의 _EmissionTex(보이는 영역 마스크)")]
        public Texture mask;
        public Sprite stroke;
        [Tooltip("거리장 R 채널이 나타내는 거리 범위(거리장 텍셀, ±)")]
        public float range;
        [Tooltip("원본 사각형 바깥에 붙인 여백(거리장 텍셀). 9-slice 크기 계산에 쓴다")]
        public float padding;
    }

    [SerializeField] private List<Entry> entries = new();
    private Dictionary<(Sprite, Texture), Entry> lookup;

    public IReadOnlyList<Entry> Entries => entries;

    public bool TryFind(Sprite source, Texture mask, out Entry entry)
    {
        entry = default;
        if (source == null) return false;
        if (lookup == null)
        {
            lookup = new Dictionary<(Sprite, Texture), Entry>(entries.Count);
            foreach (var item in entries)
                if (item.source != null && item.stroke != null) lookup[(item.source, item.mask)] = item;
        }
        return lookup.TryGetValue((source, mask), out entry);
    }

    private void OnValidate() => lookup = null;

#if UNITY_EDITOR
    // 생성 도구 전용: 같은 조합이 있으면 바꾸고 없으면 더한다.
    public void SetEntry(Entry entry)
    {
        int index = entries.FindIndex(e => e.source == entry.source && e.mask == entry.mask);
        if (index >= 0) entries[index] = entry;
        else entries.Add(entry);
        lookup = null;
    }

    public void Clear()
    {
        entries.Clear();
        lookup = null;
    }
#endif
}
