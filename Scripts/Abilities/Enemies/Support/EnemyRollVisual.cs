using UnityEngine;

// 바퀴를 실제 수평 이동 거리만큼 굴리고, AnimationClip이 지정한 공회전·제동을 더하는 표시 전용 컴포넌트다.
// 물리 위치와 충돌은 바꾸지 않고, Animator가 다루지 않는 바퀴 Transform의 Z 회전만 갱신한다.
// Animator가 slipSpeed·rollGrip을 기록할 수 있도록 Animator와 같은 GameObject(Model)에 둔다.
// FacingObject의 좌우 반전이 바뀌어도 화면에서 보이는 회전 각이 끊기지 않도록 월드 기준 각도를 유지한다.
public class EnemyRollVisual : MonoBehaviour
{
    [SerializeField] private Transform rollTarget;
    [Tooltip("이동 거리를 읽는 Transform입니다. 비워 두면 이 컴포넌트의 Transform을 사용합니다.")]
    [SerializeField] private Transform motionSource;
    [Tooltip("화면에서 보이는 바퀴의 반지름(칸)입니다. 이동 거리 / 반지름만큼 회전합니다.")]
    [SerializeField, Min(0.01f)] private float worldRadius = 0.7f;
    [Tooltip("한 프레임 이동이 이 거리보다 크면 순간 이동으로 보고 회전하지 않습니다.")]
    [SerializeField, Min(0.1f)] private float teleportDistance = 3f;

    [Header("Animated")]
    [Tooltip("이동과 별개로 바라보는 방향으로 더 도는 공회전 속도(도/초)입니다. AnimationClip에서 기록합니다.")]
    [SerializeField] private float slipSpeed;
    [Tooltip("이동 거리를 회전에 반영하는 비율입니다. 1은 노면을 잡고 구르고, 0은 바퀴가 잠긴 채 미끄러집니다.")]
    [SerializeField, Range(0f, 1f)] private float rollGrip = 1f;

    private float worldAngle;
    private Vector3 lastPosition;
    private bool hasLastPosition;

    public float WorldAngle => worldAngle;
    public float SlipSpeed => slipSpeed;
    public float RollGrip => rollGrip;

    private void OnEnable()
    {
        // Pool에서 다시 꺼낸 직후 위치가 옮겨지므로 다음 프레임부터 이동량을 계산한다.
        hasLastPosition = false;
    }

    private void LateUpdate()
    {
        if (rollTarget == null)
            return;

        // 부모가 좌우 반전되면 로컬 Z 회전이 화면에서 반대로 보이므로 부호를 맞춘다.
        Vector3 parentScale = rollTarget.parent != null ? rollTarget.parent.lossyScale : Vector3.one;
        float mirror = parentScale.x * parentScale.y < 0f ? -1f : 1f;

        Vector3 position = (motionSource != null ? motionSource : transform).position;
        float delta = 0f;

        if (hasLastPosition)
        {
            float deltaX = position.x - lastPosition.x;
            // 오른쪽으로 구르면 시계 방향(월드 Z 음수)으로 회전한다.
            if (Mathf.Abs(deltaX) < teleportDistance)
                delta -= deltaX / worldRadius * Mathf.Rad2Deg * Mathf.Clamp01(rollGrip);
        }

        // 공회전은 바라보는 방향으로 굴러가는 회전 방향과 같다.
        delta -= mirror * slipSpeed * Time.deltaTime;
        worldAngle = Mathf.Repeat(worldAngle + delta, 360f);

        lastPosition = position;
        hasLastPosition = true;

        rollTarget.localRotation = Quaternion.Euler(0f, 0f, worldAngle * mirror);
    }
}
