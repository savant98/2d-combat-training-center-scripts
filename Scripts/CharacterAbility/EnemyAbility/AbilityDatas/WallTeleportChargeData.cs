using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/Ability Data/Wall Teleoprt Charge")]
public class WallTeleportChargeData : EnemyAbilityData
{
    [Header("Charge Teleport")]
    public float teleportTriggerTime = 0.2f;
    public Vector2 chargeTeleportOffset = new Vector2(8f, 1.5f); // x = 최대 탐색 거리, y = 바닥 기준 높이
    public float teleportDisappearDelay = 0.05f;
    public float teleportAppearDelay = 0.05f;
    public float wallFrontOffset = 0.35f;

    [Header("Recover Teleport")]
    public Vector2 recoverTeleportOffset = new Vector2(6f, 1.5f); // x = 플레이어 기준 좌우 거리, y = 바닥 기준 높이

    [Header("Ground Check")]
    public float groundCheckStartHeight = 5f;
    public float groundCheckDistance = 50f;
    public LayerMask teleportBlockMask;

    [Header("Charge")]
    public float chargeSpeed = 12f;

    [Header("Preview")]
    public LaserPreviewLine previewLinePrefab;
    public float previewLineLength = 20f;
    public float previewLineWidth = 0.18f;
    public Color previewLineColor = Color.red;
    public LayerMask previewBlockMask;

    [Header("Feedback")]
    public FeedbackData teleportStartFeedback;
    public FeedbackData teleportEndFeedback;
}