using System;
using System.Collections.Generic;
using UnityEngine;

// 화면 가독성 처리(배경 깊이 안개·멀리 있는 상호작용 시설 강조 줄이기) 수치와 적용 범위. 실행 중에는 Resources/Camera/ScreenReadability를 읽는다.
// 리소스(Sprite·Material·정렬 순서)는 바꾸지 않는다. ScreenReadability가 실행 중에만 안개 판을 그리고 시설 강조 세기를 낮춘다.
[CreateAssetMenu(fileName = "ScreenReadability", menuName = "Camera/Screen Readability")]
public sealed class ScreenReadabilitySO : ScriptableObject
{
    [Serializable]
    public struct HazeLayer
    {
        [Tooltip("이름(Inspector 표시용).")]
        public string name;
        [Tooltip("Default 정렬 층의 이 순서까지(같은 순서 안에서는 가장 마지막) 그려진 배경을 덮는다. 발판(0)·Prop·적·Player(Player 정렬 층)는 덮지 않는다.")]
        public int sortingOrder;
        [Tooltip("안개 색(sRGB). 배경을 이 색 쪽으로 섞어 명도 범위·채도·발광을 함께 줄인다.")]
        public Color color;
        [Range(0f, 1f), Tooltip("섞는 비율. 0.5면 배경의 명암 차와 채도가 절반으로 줄어든다.")]
        public float alpha;
    }

    // 배경별 안개. 안개는 배경의 명암 차·채도·발광을 줄이되 배경을 플레이 층(발판·가산 발광 Player)의 명도로 다가가게 하지 않는다 —
    // 어두운 배경 + 밝은 발판이면 기본(어두운) 안개, 발판보다 밝은 배경(튜토리얼처럼 어두운 발판과 밝게 빛나는 Player 사이)이면 배경 중앙 명도의 무채색 안개.
    // 어두운 안개를 밝은 배경에 깔면 배경이 발판 명도로 다가가 발판 대비가 준다. 밝은 안개를 깔면 Player가 덜 보인다.
    [Serializable]
    public struct BackdropHaze
    {
        [Tooltip("이 배경 Prefab(WaveDataSO.roomBackdrop 방 배경 교체·Wave별 backgroundPrefab)이 켜진 방에서는 기본 층 대신 아래 층을 쓴다.")]
        public GameObject backdrop;
        [Tooltip("이 배경의 안개 층(뒤에서 앞 순서). 규칙은 기본 층과 같다(정렬 −1 이하, 무채색에 가까운 색).")]
        public HazeLayer[] layers;
    }

    private static ScreenReadabilitySO cached;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCache() => cached = null;

    public static ScreenReadabilitySO Default => cached != null ? cached : cached = Resources.Load<ScreenReadabilitySO>("Camera/ScreenReadability");

    [Tooltip("끄면 처리 전 화면으로 돌아간다.")]
    public bool readabilityEnabled = true;
    [Tooltip("이 종류의 방에 있을 때 처리를 켠다. 기본은 모든 방(Lobby·던전과 일반 Wave 방·보스·결과·자율훈련장) — 앞으로 만들 배경도 정렬 규칙만 지키면 자동 적용된다.")]
    public RoomType[] roomTypes = { RoomType.Lobby, RoomType.Training, RoomType.Boss, RoomType.Result, RoomType.SelfTraining };
    [Tooltip("비워 두면 위 종류의 모든 방에 적용한다. 템플릿을 넣으면 던전에서는 그 템플릿으로 만든 방에만 적용한다(새 처리를 한 곳에서 검수할 때).")]
    public List<DungeonRoomTemplateSO> reviewTemplates = new();
    [Tooltip("검수 메뉴 '검수 방으로 이동'이 찾아갈 던전 템플릿.")]
    public DungeonRoomTemplateSO previewTemplate;

    [Header("배경 깊이 안개(Hollow Knight·Ori처럼 먼 층일수록 안개에 묻힌다)")]
    [Tooltip("뒤에서 앞 순서. 먼 층 안개는 먼 구조물만, 배경 앞 안개는 관측창·설비·케이블까지 배경 전체를 덮는다(먼 층은 두 장이 겹쳐 더 흐려진다).")]
    public HazeLayer[] hazeLayers =
    {
        new HazeLayer { name = "먼 층", sortingOrder = -33, color = new Color(.06f, .065f, .075f), alpha = .35f },
        new HazeLayer { name = "배경 앞", sortingOrder = -1, color = new Color(.065f, .07f, .08f), alpha = .62f },
    };
    [Tooltip("배경 Prefab별 안개(목록에 없는 배경·던전 벽면 키트 방·Lobby 같은 Scene 배경은 위 기본 층). 배경이 발판보다 밝으면 여기에 배경 중앙 명도의 무채색 안개를 넣는다(투명도는 기본 층과 같게).")]
    public BackdropHaze[] backdropHazes = Array.Empty<BackdropHaze>();

    /// <summary>이 배경 Prefab에 쓸 안개 층(지정이 없으면 기본 층).</summary>
    public HazeLayer[] LayersFor(GameObject backdrop)
    {
        if (backdrop != null && backdropHazes != null)
            foreach (var entry in backdropHazes)
                if (entry.backdrop == backdrop && entry.layers != null && entry.layers.Length > 0) return entry.layers;
        return hazeLayers;
    }

    [Header("멀리 있는 상호작용 시설(깜박임 줄이기)")]
    [Tooltip("Player와 이 거리(칸) 안의 시설은 강조(깜박임·빛 점)를 그대로 둔다.")]
    public float facilityNoticeNear = 6f;
    [Tooltip("이 거리(칸)부터는 아래 차분한 강조로 바꾼다(그 사이는 섞는다).")]
    public float facilityNoticeFar = 10f;
    [Tooltip("멀리 있는 '쓸 수 있음' 시설의 깜박임 최고 세기(원래 2.9, 바닥 0.35). 범위 안 꾸준한 밝기 2.2보다 낮게 둬 가까운 대상이 먼저 보이게 한다.")]
    public float distantBlinkPeak = 1.1f;
    [Range(0f, 1f), Tooltip("멀리 있는 시설의 테두리 빛 점 세기 배율(0이면 숨김).")]
    public float distantCometScale = 0f;
}
