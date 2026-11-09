using UnityEngine;

// 바닥에서 이동하는 동안 Player와 지면이 닿는 지점에서 궤적과 불꽃을 낸다.
// MoveAbility가 Prefab을 Player 아래에 만든다. 이동 중인지는 상태 머신의 PlayerState.Move로 매 프레임 직접 확인한다(잡기처럼 Ability Tick이 멈춰도 꺼지게 한다).
// 궤적은 표시 전용이며 물리 질의를 하지 않는다.
public sealed class PlayerGroundTrail : MonoBehaviour
{
    [SerializeField] private TrailRenderer trail;
    [SerializeField] private ParticleSystem sparks;
    [SerializeField, Tooltip("한 프레임에 이 거리(칸)보다 멀리 옮겨지면 순간 이동으로 보고 궤적을 끊습니다.")]
    private float teleportDistance = 1.5f;

    private PlayerStateMachine stateMachine;
    private Vector3 lastPosition;
    private bool emitting;

    public void Initialize(PlayerStateMachine machine)
    {
        stateMachine = machine;
        lastPosition = transform.position;
        emitting = true;        // 처음 호출에서 반드시 끄도록 반대 값으로 시작한다
        SetEmitting(false);
        trail.Clear();
    }

    private void LateUpdate()
    {
        if (stateMachine == null)
            return;

        // 방 이동처럼 순간 이동하면 이전 자리와 선이 이어지지 않게 지운다.
        Vector3 position = transform.position;
        if ((position - lastPosition).sqrMagnitude > teleportDistance * teleportDistance)
            trail.Clear();
        lastPosition = position;

        SetEmitting(stateMachine.CurrentState == PlayerState.Move);
    }

    private void SetEmitting(bool value)
    {
        if (emitting == value)
            return;

        emitting = value;
        trail.emitting = value;
        var emission = sparks.emission;
        emission.enabled = value;
    }
}
