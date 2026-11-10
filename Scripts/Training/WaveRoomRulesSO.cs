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
    private static WaveRoomRulesSO cached;
    public static WaveRoomRulesSO Default => cached != null ? cached : cached=Resources.Load<WaveRoomRulesSO>("Training/WaveRoomRules");
}
