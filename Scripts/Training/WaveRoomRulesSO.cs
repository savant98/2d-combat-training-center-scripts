using UnityEngine;

// 모든 방과 셀 편집기가 공유하는 자동 마감·생성 규칙이다.
[CreateAssetMenu(menuName="Game/Wave Room Rules")]
public sealed class WaveRoomRulesSO : ScriptableObject
{
    public WaveTerrainCellView platformCell;
    public WaveTerrainCellView oneWayCell;
    public Material terrainLineMaterial;
    public float terrainLineWidth=.05f;
    public float terrainLineInset=.025f;
    public FeedbackDataSO platformSpawnFeedback;
    public Sprite blinderSprite;
    public Material blinderMaterial;
    public const float BlinderRows=3f;
    [Header("지형 패널 연출")]
    [Tooltip("지형 칸을 끼우고 뺄 때 패널 뒤에 보이는 수납구·기계팔의 재질입니다(Wave/TerrainPanelRig, 일반 혼합).")]
    public Material panelRigMaterial;
    [Tooltip("패널이 맞물리거나 풀리는 순간의 섬광 재질입니다(Wave/TerrainPanelRig, 가산 혼합).")]
    public Material panelFlashMaterial;
    [Tooltip("패널들이 움직이기 시작할 때 한 번 내는 구동음입니다.")]
    public FeedbackDataSO panelDriveFeedback;
    [Tooltip("패널 한 장이 제자리에 맞물릴 때 내는 소리입니다. 겹치는 횟수는 Feedback의 소리 설정이 제한합니다.")]
    public FeedbackDataSO panelLockFeedback;
    [Tooltip("패널 한 장의 잠금이 풀릴 때 내는 소리입니다.")]
    public FeedbackDataSO panelReleaseFeedback;
    [Tooltip("켜면 생성 외형 발판과 OneWay 발판도 기계팔 패널로 끼우고 뺍니다. 끄면 각 Prefab의 등장·소멸 곡선을 씁니다.")]
    public bool panelGeneratedTerrain=true;
    [Header("Wave 준비 중 Player 구속")]
    [Tooltip("구속 상자 표시 Prefab입니다. 비우면 표시 없이 이동만 막습니다.")]
    public WavePlayerContainment playerContainment;
    [Tooltip("구속 상자 안에서 좌우로 움직일 수 있는 여유(칸)입니다.")]
    public float containmentSideMargin=.12f;
    [Tooltip("구속 상자 안에서 머리 위로 뜰 수 있는 여유(칸)입니다.")]
    public float containmentTopMargin=.12f;
    [Header("Wave 준비 중 Player 크레인 이동")]
    [Tooltip("시작 좌표로 옮기는 Wave에서 크레인이 구속 상자를 들어 올리는 높이(칸)입니다.")]
    public float craneLiftHeight=2.5f;
    [Tooltip("들어 올리는 데 걸리는 시간(초)입니다. 내려놓을 때도 같습니다.")]
    public float craneLiftDuration=.45f;
    [Tooltip("옮기는 빠르기(칸/초)입니다. 걸리는 시간은 아래 최소·최대 사이로 맞춥니다.")]
    public float craneTravelSpeed=26f;
    [Tooltip("옮기는 데 걸리는 가장 짧은 시간(초)입니다. 가까운 곳도 이만큼은 걸립니다.")]
    public float craneTravelMinDuration=.55f;
    [Tooltip("옮기는 데 걸리는 가장 긴 시간(초)입니다. 아주 먼 곳은 이 시간에 맞춰 더 빨리 옮깁니다.")]
    public float craneTravelMaxDuration=2.6f;
    [Header("TrainingRoom 입장 로딩")]
    [Tooltip("입장 로딩 동안 암전 위에 보여 주는 진행 표시 Prefab입니다. 비우면 표시 없이 로딩만 합니다.")]
    public TrainingRoomLoadingPanel roomLoadingPanel;
    private static WaveRoomRulesSO cached;
    public static WaveRoomRulesSO Default => cached != null ? cached : cached=Resources.Load<WaveRoomRulesSO>("Training/WaveRoomRules");
}
