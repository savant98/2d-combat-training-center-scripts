using UnityEngine;

// Player 대시가 진행되는 동안 몸을 따라다니는 효과(궤적·톱니 잔상·속도선·분사).
// DashAbility가 실행 중에 Prefab을 Player 아래에 만든다. 표시 전용이며 위치 이동이나 물리 질의를 하지 않는다.
// FacingModel 아래에 두면 왼쪽을 볼 때 음수 배율이 되어 Local 정렬 파티클(분사)의 방향이 뒤집히지 않으므로, Player 루트 아래에서 이 오브젝트의 회전으로 좌우를 맞춘다.
// 시작·제동 효과는 이 Transform의 회전을 FeedbackPlayer에 넘겨 같은 방향으로 재생한다.
public sealed class PlayerDashVisual : MonoBehaviour
{
    [SerializeField] private TrailRenderer coreTrail;   // 가운데 밝은 궤적
    [SerializeField] private TrailRenderer wideTrail;   // 몸 폭의 옅은 빛 띠
    [SerializeField] private ParticleSystem gearGhosts; // 지나간 자리에 남는 톱니 잔상(월드 공간)
    [SerializeField] private ParticleSystem speedLines; // 몸 둘레에 남는 짧은 속도선(월드 공간)
    [SerializeField] private ParticleSystem exhaust;    // 몸 뒤로 뻗는 분사(몸을 따라감)
    [SerializeField, Tooltip("한 프레임에 이 거리(칸)보다 멀리 옮겨지면 순간 이동으로 보고 궤적을 끊습니다. 대시 자체의 한 프레임 이동보다 커야 합니다.")]
    private float teleportDistance = 3f;
    [SerializeField, Tooltip("분사 수명은 대시 시간에 이 값(초)을 더합니다.")]
    private float exhaustExtraLife = 0.01f;

    private Vector3 lastPosition;

    public void Initialize()
    {
        lastPosition = transform.position;
        SetEmitting(false);
        coreTrail.Clear();
        wideTrail.Clear();
        exhaust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    public void Begin(int facing, float duration)
    {
        transform.localRotation = Quaternion.Euler(0f, 0f, facing >= 0 ? 0f : 180f);
        SetEmitting(true);

        var main = exhaust.main;
        main.startLifetime = duration + exhaustExtraLife;
        exhaust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        exhaust.Play(true);
    }

    public void End()
    {
        SetEmitting(false);
        exhaust.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    private void LateUpdate()
    {
        // 대시 중 방 이동처럼 순간 이동하면 이전 자리와 선이 이어지지 않게 지운다.
        Vector3 position = transform.position;
        if ((position - lastPosition).sqrMagnitude > teleportDistance * teleportDistance)
        {
            coreTrail.Clear();
            wideTrail.Clear();
        }
        lastPosition = position;
    }

    private void SetEmitting(bool value)
    {
        coreTrail.emitting = value;
        wideTrail.emitting = value;
        var ghosts = gearGhosts.emission;
        ghosts.enabled = value;
        var lines = speedLines.emission;
        lines.enabled = value;
    }
}
