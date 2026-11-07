using UnityEngine;

// 구르기 착지를 모터 이동 이후에 재생한다. 외형 반동·벽 회귀·등장에는 중복해서 재생하지 않는다.
[DefaultExecutionOrder(50)]
public class BulwarkLandingFeedback : MonoBehaviour
{
    [SerializeField] private EnemyController controller;
    [SerializeField] private TumblePatrolAbility patrol;
    [SerializeField] private Collider2D groundCollider;
    [SerializeField] private FeedbackDataSO landingFeedback;

    private bool pending;
    private float expectedLandingX;
    public int ImpactCount { get; private set; }
    public Vector3 LastImpactPosition { get; private set; }
    public float LastImpactTime { get; private set; }

    private void OnEnable()
    {
        pending = false;
        ImpactCount = 0;
        if (patrol != null) patrol.OnFaceLanded += QueueLanding;
    }

    private void OnDisable()
    {
        if (patrol != null) patrol.OnFaceLanded -= QueueLanding;
        pending = false;
    }

    private void QueueLanding(float targetX)
    {
        expectedLandingX = targetX;
        pending = true;
    }

    private void FixedUpdate()
    {
        if (controller == null || patrol == null || controller.Brain.IsPaused || !patrol.IsRunning)
        {
            pending = false;
            return;
        }
        if (!pending) return;
        // 마지막 물리 이동을 벽이 막았다면 실제로 새 면을 놓은 것이 아니므로 연출하지 않는다.
        if (Mathf.Abs(controller.transform.position.x - expectedLandingX) > 0.03f)
        {
            pending = false;
            return;
        }
        if (!controller.IsGrounded) return;
        pending = false;
        if (landingFeedback == null || groundCollider == null || FeedbackPlayer.Instance == null) return;

        // 축 정렬 모터 Collider의 아랫면을 쓴다. 회전하는 Model이나 별도 지형 질의에 의존하지 않는다.
        LastImpactPosition = new Vector3(controller.transform.position.x, groundCollider.bounds.min.y + 0.04f, controller.transform.position.z);
        LastImpactTime = Time.time;
        ImpactCount++;
        FeedbackPlayer.Instance.PlayExact(landingFeedback, LastImpactPosition, Quaternion.identity);
    }
}
