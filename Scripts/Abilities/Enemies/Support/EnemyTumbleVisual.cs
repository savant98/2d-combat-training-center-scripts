using UnityEngine;

// 정사각형 몸체가 진행 방향 아래 모서리를 축으로 90도씩 넘어가는 모습을 보여 주는 표시 전용 컴포넌트다.
// 순찰 패턴이 한 번 넘어가기를 시작할 때 출발 위치와 방향을 알려 주면, 실제 수평 이동 거리(motionSource)에서
// 넘어간 각도를 거꾸로 계산해 외형 루트(tumbleTarget)를 돌리고 들어 올린다.
// 각도가 실제 위치에서 나오므로 물리 이동이 막히거나 늦어도 축 모서리는 바닥의 한 점에 고정되어 미끄러지지 않는다.
// 물리 위치와 충돌 상자는 바꾸지 않는다. tumbleTarget은 Animator가 기록하지 않는 Transform(Model의 부모)이어야 한다.
public class EnemyTumbleVisual : MonoBehaviour
{
    [Tooltip("회전·들림을 적용할 외형 루트입니다. Animator가 기록하지 않는 Transform(Model의 부모)이어야 합니다.")]
    [SerializeField] private Transform tumbleTarget;
    [Tooltip("이동 거리를 읽는 Transform(적 루트)입니다. 비워 두면 이 컴포넌트의 Transform을 사용합니다.")]
    [SerializeField] private Transform motionSource;

    private bool stepping;
    private float stepStartX;
    private float size = 1f;
    private int direction = 1;

    public bool IsStepping => stepping;
    // 이번에 넘어간 각도(도). 0은 바닥에 놓인 자세, 90은 다음 면이 바닥에 닿은 자세다.
    public float Angle { get; private set; }

    private Transform Source => motionSource != null ? motionSource : transform;

    // Pool에서 다시 꺼내면 바닥에 놓인 자세부터 시작한다.
    private void OnEnable() => EndStep();

    public void BeginStep(float startX, int stepDirection, float tumbleSize)
    {
        stepping = true;
        stepStartX = startX;
        direction = stepDirection >= 0 ? 1 : -1;
        size = Mathf.Max(0.01f, tumbleSize);
        Apply(0f);
    }

    public void EndStep()
    {
        stepping = false;
        Apply(0f);
    }

    private void LateUpdate()
    {
        if (!stepping)
            return;

        // 이번 프레임의 물리 이동이 끝난 실제 위치로 각도를 구한다.
        float offset = (Source.position.x - stepStartX) * direction;
        Apply(AngleFromOffset(size, offset));
    }

    private void Apply(float angle)
    {
        Angle = angle;
        if (tumbleTarget == null)
            return;

        // 오른쪽으로 넘어가면 시계 방향(월드 Z 음수)으로 돈다.
        // 부모(FacingObject)가 좌우 반전되면 로컬 회전이 화면에서 반대로 보이므로 부호를 맞춘다.
        Transform parent = tumbleTarget.parent;
        float mirror = parent != null && parent.lossyScale.x * parent.lossyScale.y < 0f ? -1f : 1f;
        tumbleTarget.localRotation = Quaternion.Euler(0f, 0f, -angle * direction * mirror);
        tumbleTarget.localPosition = new Vector3(0f, Lift(size, angle), 0f);
    }

    // ===== 모서리 축 회전의 기하 =====
    // 한 변 s인 정사각형이 앞쪽 아래 모서리를 축으로 θ만큼 넘어가면 중심은 그 모서리를 중심으로 반지름 s/√2인 원을 따라간다.
    // 바닥에 놓였을 때의 중심을 기준으로 수평 이동은 s/2·(1 + sinθ - cosθ), 높이 변화는 s/2·(sinθ + cosθ - 1)이다.

    // 넘어간 각도(도) → 중심의 수평 이동(칸). 0도에서 0, 90도에서 한 변이다.
    public static float ForwardOffset(float size, float angle)
    {
        float radians = angle * Mathf.Deg2Rad;
        return size * 0.5f * (1f + Mathf.Sin(radians) - Mathf.Cos(radians));
    }

    // 중심의 수평 이동(칸) → 넘어간 각도(도). 0~한 변 밖의 값은 0~90도로 자른다.
    // sinθ - cosθ = √2·sin(θ - 45도)이므로 θ = 45도 + asin((2d/s - 1) / √2)이다.
    public static float AngleFromOffset(float size, float offset)
    {
        float normalized = Mathf.Clamp(2f * offset / size - 1f, -1f, 1f);
        return 45f + Mathf.Asin(normalized / Mathf.Sqrt(2f)) * Mathf.Rad2Deg;
    }

    // 넘어간 각도(도) → 바닥에 놓였을 때보다 중심이 높아진 거리(칸). 45도에서 가장 높다(s·(√2 - 1)/2).
    public static float Lift(float size, float angle)
    {
        float radians = angle * Mathf.Deg2Rad;
        return size * 0.5f * (Mathf.Sin(radians) + Mathf.Cos(radians) - 1f);
    }
}
