using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Wave 격자 편집기에서 우클릭으로 복사한 배치 묶음을 팔레트 왼쪽 아래 3칸에 보관한다.
/// 프로젝트 폴더의 Library에 저장해 창을 닫았다 열어도 남고, 저장소에는 올라가지 않는다.
/// </summary>
[FilePath("Library/WaveEditorCopySlots.asset", FilePathAttribute.Location.ProjectFolder)]
public sealed class WaveCopySlots : ScriptableSingleton<WaveCopySlots>
{
    public const int Count = 3;
    [SerializeField] private WaveCopyClip[] slots = new WaveCopyClip[Count];

    public WaveCopyClip this[int index]
    {
        get
        {
            Ensure();
            return index >= 0 && index < Count ? slots[index] : new WaveCopyClip();
        }
    }

    public void Set(int index, WaveCopyClip clip)
    {
        if (index < 0 || index >= Count) return;
        Ensure();
        slots[index] = clip ?? new WaveCopyClip();
        Save(true);
    }

    public void Clear(int index) => Set(index, new WaveCopyClip());

    private void Ensure()
    {
        if (slots == null || slots.Length != Count) Array.Resize(ref slots, Count);
        for (int i = 0; i < Count; i++) if (slots[i] == null) slots[i] = new WaveCopyClip();
    }
}

/// <summary>복사한 범위 하나. 항목 위치는 범위 왼쪽 아래 칸 모서리 기준이다.</summary>
[Serializable]
public sealed class WaveCopyClip
{
    public string label = string.Empty;
    public Vector2Int size = Vector2Int.one;
    public WaveCopyItem[] items = Array.Empty<WaveCopyItem>();
    public bool IsEmpty => items == null || items.Length == 0;
}

[Serializable]
public sealed class WaveCopyItem
{
    /// <summary>복사한 배치 정보(Prefab·방향·세부 설정·수명 등). 위치와 생성 시각은 붙일 때 다시 정한다.</summary>
    public WaveSpawnInfo info = new WaveSpawnInfo();
    /// <summary>복사 범위 왼쪽 아래 칸 모서리에서 생성 좌표까지(칸).</summary>
    public Vector2 offset;
    /// <summary>범위에서 가장 먼저 생성되는 항목과의 생성 시각 차이(초).</summary>
    public float timeOffset;
}
