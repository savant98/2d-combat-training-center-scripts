using System;
using System.Collections;
using UnityEngine;

// 돌진 베기(2026-11-17). 칼(Sword) 바탕의 Gear 1단계 기술.
// 사용자 지시 "기술 시전 시간은 0.1초로 매우 짧고, 기술을 사용하면 정면으로 대쉬 거리의 2배를 이동하며, 속도도 대쉬의 2배야.
//   이때 이동한 거리 사이의 공간에 다단히트(4타, 0.1초 간격, 히트 판정은 한 번으로 그냥 연출로 4번 맞는 것) 데미지가 들어가."
//   2차(같은 날): "데미지 틱 들어가는 속도를 빠르게, 이펙트도 더 빠르게 나타나게 해(1.5배)" — 타격 간격은 1/15초(SwordDashSlash.hitInterval).
//   3차(2026-11-18): "돌진 베기 속도도 지금보다 더 빨라야 해", Sephiria '일섬' 참고 — 거의 순간이동 같은 직선 돌진(135칸/초), 도착 뒤 납도하고 지나간 길이 한꺼번에 베인다(SwordDashSlash.cutDelay).
// - 준비(windupTime)가 끝나면 보던 방향으로 dashDistance만큼 dashSpeed로 나아간다. 대시(DashAbility)처럼 Velocity만 주고,
//   실제 이동은 CharacterController2D.FixedUpdate → RaycastMotor2D.Move가 한다(위치를 직접 바꾸지 않는다). 벽에 막히면 그 자리에서 끝난다.
// - 나아가는 동안에는 대시처럼 중력과 외부 힘(넉백·반동·점프대)을 받지 않는다. 대시처럼 무적은 아니다.
// - 지나간 길의 판정·납도·4타·궤적은 SwordDashSlash(풀 개체)가 맡는다. 돌진이 끝나면 길을 넘겨주고 바로 회수(납도 자세)로 넘어간다.
// - 연출은 칼 모형으로 한다(해머 세트에 넣어도 칼을 뽑아 든다).
[CreateAssetMenu(menuName = "Weapon/SwordDash")]
public class SwordDashWeaponSO : WeaponBaseSO
{
    [Header("Dash")]
    [Tooltip("보던 방향으로 나아가는 거리(칸). 대시(Player_Dash_Data: 30칸/초 × 0.15초 = 4.5칸)의 2배다.")]
    [SerializeField, Min(0.1f)] private float dashDistance = 9f;
    [Tooltip("나아가는 속도(칸/초). 대시(30칸/초)의 4.5배 — 9칸을 1/15초에 지나간다(2026-11-18 사용자 지시 '더 빨라야').")]
    [SerializeField, Min(1f)] private float dashSpeed = 135f;

    [Header("Slash")]
    [Tooltip("지나간 길의 궤적·칼자국과 판정(4타). 돌진이 시작될 때 꺼낸다.")]
    [SerializeField] private SwordDashSlash slashPrefab;
    [Tooltip("지나온 길이 처음 베이는 순간 한 번 재생한다.")]
    [SerializeField] private FeedbackDataSO cutFeedback;
    [Tooltip("적이 맞을 때마다 그 자리에서 재생한다(마지막 타격 제외).")]
    [SerializeField] private FeedbackDataSO hitFeedback;
    [Tooltip("마지막 타격(마무리)에 재생한다. 비우면 hitFeedback.")]
    [SerializeField] private FeedbackDataSO finalHitFeedback;
    [Tooltip("도착한 뒤 베는 순간 직전, 칼을 칼집에 집어넣는 납도 소리.")]
    [SerializeField] private FeedbackDataSO sheatheFeedback;

    public float DashDistance => dashDistance;
    public float DashSpeed => dashSpeed;
    public SwordDashSlash SlashPrefab => slashPrefab;

    public override string PresentationWeaponName => "Sword";
    public override bool RecoverEndsAtIdle => true;

    public override IEnumerator WeaponUseSequence(CharacterController2D controller, Action forceRecover = null)
    {
        PlayerController player = controller as PlayerController;
        if (player == null || slashPrefab == null || WorldEntityPool.Instance == null)
            yield break;
        PlayerWeaponAbility attack = player.AbilityManager.AttackAbility;

        int facing = controller.Facing >= 0 ? 1 : -1;
        Vector3 start = controller.transform.position;
        SwordDashSlash slash = WorldEntityPool.Instance.Spawn(slashPrefab, start, Quaternion.identity);
        slash.SetHit(hitFeedback, finalHitFeedback, sheatheFeedback, cutFeedback, (owner, point) => OnHitEnemy?.Invoke(owner, point));
        slash.Initialize(controller, WithSource(damageInfo));   // 밀리는 방향은 돌진을 시작한 자리와 방향을 기준으로 한다
        slash.Begin(facing, dashDistance);

        // 공격이 끊기면(피격·잡기) 이 코루틴들은 StopAllCoroutines로 멈춘다. 그때 외부 힘 무시는 PlayerWeaponAbility.Interrupt가 되돌리고,
        // 길은 SwordDashSlash가 스스로 마무리한다.
        var run = new DashRun(controller, facing, start.x, dashDistance, dashSpeed);
        controller.IgnoreExternalForces = true;
        Coroutine steps = attack.StartCoroutine(run.FixedSteps());
        // 실행 시간(executeTime)이 다 되면 막혀서 못 간 것으로 보고 그만둔다.
        while (!run.Done && attack.AttackPhase == AbilityState.Execute)
        {
            run.Apply();
            slash.Track(controller.transform.position);
            yield return null;
        }
        attack.StopCoroutine(steps);
        controller.Velocity = Vector2.zero;
        controller.IgnoreExternalForces = false;
        slash.Finish(controller.transform.position);
        forceRecover?.Invoke();
    }

    // 돌진 한 번의 속도 제어.
    // 공격 중에는 이동 입력(MoveAbility)이 프레임마다 Velocity.x를 0으로 만들므로 프레임마다(Update 뒤) 다시 주고,
    // 한 프레임에 물리 틱이 여러 번 도는 경우에도 거리를 넘지 않도록 틱마다(FixedUpdate 뒤) 남은 거리에 맞춰 다시 준다.
    private sealed class DashRun
    {
        private const float Arrived = 0.0005f;      // 남은 거리가 이보다 작으면 다 온 것으로 본다(칸)
        private const float StuckMove = 0.001f;     // 한 틱에 이보다 적게 움직이면 막힌 것으로 센다(칸)
        private const int StuckTicks = 3;           // 연속으로 이만큼 막히면 끝낸다

        private readonly CharacterController2D controller;
        private readonly int facing;
        private readonly float startX, distance, speed;

        public bool Done { get; private set; }

        public DashRun(CharacterController2D controller, int facing, float startX, float distance, float speed)
        {
            this.controller = controller;
            this.facing = facing;
            this.startX = startX;
            this.distance = distance;
            this.speed = speed;
        }

        // 다음 물리 틱의 속도를 정한다. 마지막 틱은 남은 거리만큼만 가도록 줄인다.
        public void Apply()
        {
            if (Done) return;
            float remaining = distance - (controller.transform.position.x - startX) * facing;
            if (remaining <= Arrived)
            {
                Done = true;
                controller.Velocity = Vector2.zero;
                return;
            }
            controller.Velocity = new Vector2(facing * Mathf.Min(speed, remaining / Time.fixedDeltaTime), 0f);
        }

        public IEnumerator FixedSteps()
        {
            var wait = new WaitForFixedUpdate();
            float lastX = controller.transform.position.x;
            int stuck = 0;
            while (!Done)
            {
                yield return wait;
                float x = controller.transform.position.x;
                stuck = Mathf.Abs(x - lastX) < StuckMove ? stuck + 1 : 0;
                lastX = x;
                if (stuck >= StuckTicks)
                {
                    // 벽에 막혔다. 지나온 데까지만 길로 삼는다.
                    Done = true;
                    controller.Velocity = Vector2.zero;
                    yield break;
                }
                Apply();
            }
        }
    }
}
