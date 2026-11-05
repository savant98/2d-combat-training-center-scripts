using UnityEngine;

// 비행 적의 외형을 수평 비행 방향으로 기울이는 표시 전용 컴포넌트다. 오른쪽으로 날면 위쪽이 오른쪽으로 기운다.
// EnemyController의 이동 속도와 넉백(외부 힘)을 읽으며, 물리 위치와 충돌은 바꾸지 않는다.
// Animator가 다루지 않는 Transform(tiltTarget)의 Z 회전만 갱신하고, FacingObject의 좌우 반전을 보정해 화면 기준 각도를 유지한다.
public class EnemyFlightTilt : MonoBehaviour
{
    [SerializeField] private Transform tiltTarget;
    [Tooltip("속도를 읽을 적입니다. 비워 두면 부모에서 찾습니다.")]
    [SerializeField] private EnemyController controller;
    [Tooltip("이 수평 속도(칸/초)에서 최대 기울기가 됩니다.")]
    [SerializeField, Min(0.01f)] private float speedForMaxTilt = 7f;
    [Tooltip("최대 기울기(도)입니다.")]
    [SerializeField, Range(0f, 45f)] private float maxTilt = 12f;
    [Tooltip("기울기가 목표 각도를 따라가는 속도(1/초)입니다. 클수록 빨리 기웁니다.")]
    [SerializeField, Min(0f)] private float response = 6f;

    private float angle;

    // 화면 기준 현재 기울기(도)다. 음수는 시계 방향이다.
    public float Angle => angle;

    private void Awake()
    {
        if (controller == null)
            controller = GetComponentInParent<EnemyController>();
    }

    private void OnEnable()
    {
        // Pool에서 다시 꺼냈을 때 이전 기울기가 남지 않게 한다.
        angle = 0f;
        if (tiltTarget != null)
            tiltTarget.localRotation = Quaternion.identity;
    }

    private void LateUpdate()
    {
        if (tiltTarget == null || controller == null)
            return;

        float speedX = controller.Velocity.x + (controller.IgnoreExternalForces ? 0f : controller.externalForce.x);
        float goal = -Mathf.Clamp(speedX / speedForMaxTilt, -1f, 1f) * maxTilt;
        angle = Mathf.Lerp(angle, goal, 1f - Mathf.Exp(-response * Time.deltaTime));

        // 부모가 좌우 반전되면 로컬 Z 회전이 화면에서 반대로 보이므로 부호를 맞춘다.
        Vector3 parentScale = tiltTarget.parent != null ? tiltTarget.parent.lossyScale : Vector3.one;
        float mirror = parentScale.x * parentScale.y < 0f ? -1f : 1f;
        tiltTarget.localRotation = Quaternion.Euler(0f, 0f, angle * mirror);
    }
}
