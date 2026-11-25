using System;
using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 관 통과 방 이동 연출(v1). 출발 출입구가 Player를 빨아들이고 → 화면을 관 속 오버레이로 덮은 동안 방을 바꾸고 도착 출입구 안쪽으로 순간이동한 뒤
/// → 관 속을 지나 → 오버레이를 걷고 도착 출입구가 Player를 방 안으로 내보낸다.
/// </summary>
/// <remarks>
/// - Player 위치는 모터 구속 이동(<c>RaycastMotor2D.ShiftConfinement</c>)과 기존 순간이동 경로(<c>WavePlayerStart.Relocate</c> → <c>PlayerController.ResetPlayerPosition</c>
///   + Cinemachine warp)로만 바꾼다. 구속은 <c>WavePlayerStart.HoldForPassage</c>(구속 상자 표시 없이 Hold — 피해 무시·잡기 차단 보호 유지).
/// - 이 에셋은 여러 번 동시에 쓰일 수 있는 공용 데이터라 진행 상태는 지역 변수에만 둔다(오버레이 표시 개체만 한 번 만들어 재사용).
/// - 한 겹 순서로 직접 돌린다(안쪽 순서를 yield return IEnumerator로 넘기지 않는다). 안에서 예외가 나도 finally가 구속을 풀고 오버레이를 걷는다.
///   바깥에서 StopCoroutine으로 끊으면 finally가 실행되지 않으므로, 끊는 쪽(DungeonManager)은 WavePlayerStart.Release를 따로 불러야 한다
///   (WaveManager의 ReleasePlayer도 같은 WavePlayerStart를 푼다). 오버레이는 구동이 끊기면 스스로 걷힌다.
/// - 연출 시간은 게임 시간(Time.deltaTime·물리 틱) 기준이라 게임을 멈추면 함께 멈춘다. 오버레이의 흐름 애니메이션만 실제 시간이다.
/// </remarks>
[CreateAssetMenu(menuName = "Training/Dungeon Passage/Pipe", fileName = "PipePassage")]
public sealed class PipePassageEffectSO : DungeonPassageEffectSO
{
    [Header("흡입")]
    [Tooltip("출발 출입구가 Player 몸통 중심을 입구 안쪽(RecessPoint)으로 끌어들이는 시간(초).")]
    [Min(.05f)] public float intakeDuration = .3f;
    [Tooltip("흡입 가속 곡선의 지수(1 = 일정, 클수록 처음엔 느리고 끝에서 빨라진다).")]
    [Range(1f, 4f)] public float intakeAcceleration = 2.2f;

    [Header("화면 덮기")]
    [Tooltip("관 속 오버레이가 진행 방향 쪽에서 밀려와 화면을 다 덮는 시간(초).")]
    [Min(.02f)] public float coverDuration = .15f;
    [Tooltip("다 덮인 채 관 속을 지나는 시간(초). 이 동안 리브·이음매가 진행 반대 방향으로 흐른다.")]
    [Min(0f)] public float travelDuration = .35f;
    [Tooltip("오버레이가 걷히는 시간(초).")]
    [Min(.02f)] public float revealDuration = .15f;

    [Header("배출")]
    [Tooltip("도착 출입구가 Player를 입구 안쪽(RecessPoint)에서 방 안(StandPoint)으로 내보내는 시간(초).")]
    [Min(.05f)] public float ejectDuration = .3f;
    [Tooltip("배출 감속 곡선의 지수(1 = 일정, 클수록 처음에 빠르게 튀어나와 끝에서 느려진다).")]
    [Range(1f, 4f)] public float ejectDeceleration = 2f;

    [NonSerialized] private DungeonPassageOverlay overlay;

    public override IEnumerator Play(DungeonPassageContext context)
    {
        if (context == null || context.Player == null) yield break;
        var player = context.Player;
        // 출발·도착 지점: 일반화된 지점(From/Arrival)이 있으면 그것을, 없으면 기존 출입구(FromDoor/ArrivalDoor)를 쓴다.
        IDungeonPassagePoint from = context.From != null ? context.From : context.FromDoor;
        if (from == null) yield break;
        var rules = DungeonRulesSO.Default;
        WavePlayerStart hold = null;
        bool ownsHold = false;
        DungeonPassageOverlay screen = null;
        var tick = new WaitForFixedUpdate();
        try
        {
            // 1) 구속: Wave 진행이 쓰는 WavePlayerStart로 잡는다(Wave가 끝나거나 초기화되면 WaveManager가 함께 푼다).
            hold = HoldFor(player, out ownsHold);
            var motor = player.Motor;
            Face(player, from.Outward.x);
            from.PlayIntake();
            Play(rules != null ? rules.passageEnterFeedback : null, PointPosition(from));

            // 2) 흡입: 물리 틱마다 구속 영역을 옮겨 몸통 중심을 입구 안쪽으로 끌어들인다(가속).
            if (motor != null && motor.IsConfined)
            {
                Vector2 start = BodyCenter(player), end = from.RecessPoint, last = start;
                for (float time = 0f; time < intakeDuration;)
                {
                    yield return tick;
                    if (!motor.IsConfined) yield break;
                    time += Time.fixedDeltaTime;
                    float t = Mathf.Clamp01(time / intakeDuration);
                    Vector2 point = Vector2.LerpUnclamped(start, end, Mathf.Pow(t, intakeAcceleration));
                    motor.ShiftConfinement(point - last);
                    last = point;
                    Face(player, from.Outward.x);
                }
                // 마지막으로 넘긴 이동량이 반영된 뒤에 화면을 덮는다.
                yield return tick;
            }

            // 3) 화면 덮기 → 방 교체 → 도착 출입구 안쪽으로 순간이동(카메라 즉시 맞춤).
            screen = Overlay(rules);
            if (screen != null) screen.Begin(from.Outward);
            for (float time = 0f; time < coverDuration; time += Time.deltaTime)
            {
                if (screen != null) screen.SetCoverage(Ease(time / coverDuration), 0f);
                yield return null;
            }
            if (screen != null) screen.SetCoverage(1f, 0f);

            context.SwapRoom?.Invoke();
            IDungeonPassagePoint arrival = context.Arrival != null ? context.Arrival() : null;
            if (arrival == null && context.ArrivalDoor != null) arrival = context.ArrivalDoor();
            if (arrival == null) yield break;   // 도착 출입구가 없으면 DungeonManager가 즉시 교체·순간이동으로 마무리한다.
            Warp(player, arrival.RecessPoint);
            Face(player, -arrival.Outward.x);

            // 4) 관 속 진행 → 오버레이 걷기.
            for (float time = 0f; time < travelDuration; time += Time.deltaTime)
            {
                if (screen != null) screen.SetCoverage(1f, 0f);
                yield return null;
            }
            arrival.PlayEject();
            Play(rules != null ? rules.passageExitFeedback : null, PointPosition(arrival));
            for (float time = 0f; time < revealDuration; time += Time.deltaTime)
            {
                if (screen != null) screen.SetCoverage(1f, Ease(time / revealDuration));
                yield return null;
            }
            if (screen != null) screen.Hide();

            // 5) 배출: 입구 안쪽에서 방 안으로 내보낸다(감속). Player는 방 안쪽을 본다.
            motor = player.Motor;
            if (motor != null && motor.IsConfined)
            {
                Vector2 start = BodyCenter(player), end = arrival.StandPoint, last = start;
                for (float time = 0f; time < ejectDuration;)
                {
                    yield return tick;
                    if (!motor.IsConfined) yield break;
                    time += Time.fixedDeltaTime;
                    float t = Mathf.Clamp01(time / ejectDuration);
                    Vector2 point = Vector2.LerpUnclamped(start, end, 1f - Mathf.Pow(1f - t, ejectDeceleration));
                    motor.ShiftConfinement(point - last);
                    last = point;
                    Face(player, -arrival.Outward.x);
                }
                yield return tick;
            }
            else
            {
                // 구속하지 못했으면(WaveManager 없음) 이동 연출 없이 서는 자리로 바로 옮긴다.
                Warp(player, arrival.StandPoint);
            }

            // 6) 끝.
            context.Completed = true;
        }
        finally
        {
            // 구속부터 푼다. 오버레이 쪽에서 문제가 생겨도 Player가 갇힌 채 남지 않는다.
            try { if (ownsHold && hold != null) hold.Release(); }
            finally { if (screen != null) screen.Hide(); }
        }
    }

    // WaveManager가 쓰는 WavePlayerStart(없으면 붙인다)로 구속한다. 이미 다른 쪽이 잡고 있으면 그 구속을 그대로 쓰고 풀지 않는다.
    private static WavePlayerStart HoldFor(PlayerController player, out bool owns)
    {
        owns = false;
        var waves = WaveManager.ActiveInstance;
        if (waves == null) return null;
        if (!waves.TryGetComponent(out WavePlayerStart hold)) hold = waves.gameObject.AddComponent<WavePlayerStart>();
        if (hold.IsHoldingPlayer) return hold;
        hold.HoldForPassage();
        owns = hold.IsHoldingPlayer;
        return hold;
    }

    private DungeonPassageOverlay Overlay(DungeonRulesSO rules)
    {
        if (overlay != null) return overlay;
        var prefab = rules != null ? rules.passageOverlayPrefab : null;
        var instance = DungeonUiPlacement.Instantiate(prefab, DungeonUiPlacement.Slot.BeforeFade);
        if (instance == null) return null;
        if (!instance.TryGetComponent(out overlay))
        {
            Destroy(instance);
            overlay = null;
            return null;
        }
        overlay.Hide();
        return overlay;
    }

    // 몸통 중심(월드). 구속 중이면 모터가 쓰는 몸통, 아니면 Collider 경계(물리 질의 없음).
    private static Vector2 BodyCenter(PlayerController player)
    {
        var motor = player.Motor;
        if (motor != null && motor.IsConfined) return motor.ConfinedBody.center;
        if (player.TryGetComponent(out Collider2D body)) return body.bounds.center;
        return (Vector2)player.transform.position + Vector2.up * .755f;
    }

    // 몸통 중심이 bodyCenter에 오도록 Transform 위치를 환산해 기존 순간이동 경로로 옮기고, 카메라를 끊김 없이 바로 맞춘다.
    private static void Warp(PlayerController player, Vector2 bodyCenter)
    {
        Vector2 offset = BodyCenter(player) - (Vector2)player.transform.position;
        Vector3 target = new Vector3(bodyCenter.x - offset.x, bodyCenter.y - offset.y, player.transform.position.z);
        // ResetPlayerPosition(구속 영역 다시 맞춤 RefitConfinement 포함) + 대시·눌러 둔 입력 정리 + CinemachineCore.OnTargetObjectWarped.
        WavePlayerStart.Relocate(player, target, false);
        // 이전 카메라 상태에서 보간하지 않고 새 방 경계(SwapRoom이 즉시 맞춘 Confiner)로 바로 맞춘다(FieldElevatorRide 선례).
        var room = RoomManager.Instance != null ? RoomManager.Instance.CurrentRoom : null;
        if (room != null && room.VirtualCamera != null) room.VirtualCamera.PreviousStateIsValid = false;
        var view = Camera.main;
        if (view != null && view.TryGetComponent(out CinemachineBrain brain) && brain.ActiveVirtualCamera is CinemachineVirtualCameraBase active)
            active.PreviousStateIsValid = false;
    }

    // Player가 sign 쪽(+1 = 오른쪽)을 보게 한다. 마우스 조준 중에는 조준 방향을 따르므로 건드리지 않는다.
    private static void Face(PlayerController player, float sign)
    {
        if (player == null || Mathf.Abs(sign) < .01f || player.UsesMouseAim || player.LockFacing) return;
        int facing = sign > 0f ? 1 : -1;
        player.FacingUpdate(facing);
        if (player.facingModel != null) player.facingModel.transform.localScale = new Vector3(facing, 1f, 1f);
    }

    // 지점의 월드 위치(소리·효과 재생용). 컴포넌트면 그 Transform, 아니면 몸통 중심.
    private static Vector3 PointPosition(IDungeonPassagePoint point)
    {
        if (point is Component component && component != null) return component.transform.position;
        return point != null ? (Vector3)point.RecessPoint : Vector3.zero;
    }

    private static void Play(FeedbackDataSO data, Vector3 position)
    {
        if (data != null && FeedbackPlayer.Instance != null) FeedbackPlayer.Instance.PlayExact(data, position, Quaternion.identity);
    }

    private static float Ease(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }
}
