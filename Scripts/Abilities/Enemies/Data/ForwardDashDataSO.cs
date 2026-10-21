using UnityEngine;

[CreateAssetMenu(menuName = "Enemy/Ability Data/Forward Dash Data")]
[UnityEngine.Scripting.APIUpdating.MovedFrom(false, sourceNamespace: "", sourceAssembly: "Assembly-CSharp", sourceClassName: "ForwardDashData")]
public class ForwardDashDataSO : EnemyAbilityDataSO
{
    [Header("ForwardDash Settings")]
    public float dashDistance = 5f;  // 총 이동 거리
    public float dashDuration = 0.5f; // 대략적인 수행 시간
    public AnimationCurve speedCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);

    [Header("Additional Feedback")]
    public FeedbackDataSO sparkFeedback;
    public float sparkInterval = 0.1f;

    [Header("Preview (Optional)")]
    public LaserPreviewLine prevLinePrefab;
    public float prevLineWidth = 0.2f;
    public float prevLineLength = 10f; // dashDistance 대신 따로 써도 좋음
    public Color prevLineColor = Color.red;
    public LayerMask prevLineBlockMask;
}
