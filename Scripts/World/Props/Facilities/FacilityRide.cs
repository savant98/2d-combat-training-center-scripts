using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Player를 붙잡아 옮기는 시설(경로 크레인·고정 크레인)의 공통 탑승 처리. 이동은 모두 기존 경로만 쓴다.
/// 4차: 크레인은 붙기(FacilityZip)로만 탄다. 붙기 실행 개체가 이미 구속한 Player를 BeginHandoff로 넘겨받는다(접지·PlayerFree 검사 생략 — 구속은 보유자 여럿 규칙으로 끊기지 않는다).
/// - 구속: 시설 루트의 자체 WavePlayerStart.HoldForPassage(구속 상자 표시 없이 Hold). WaveManager의 holder를 쓰지 않아
///   IsPreparingWave(튜토리얼·코스 판정)를 오염시키지 않고, 시설이 꺼지면(풀 반환 SetActive false) WavePlayerStart.OnDisable이 스스로 푼다.
///   보호(protect)를 켜면 잡힌 동안 피해를 무시하고, 끄면 피해는 받고 잡기만 막는다(전투 중 크레인 — DESIGN_V3 '보호 없는 구속').
/// - 입력: blockInput이면 UIManager.PushGameplayBlock(입력 정지·Velocity 0·중력 정지). 소유 표시로 Push한 만큼만 Pop한다(finally·OnDisable·풀 반환 세 곳에서 End).
///   끄면 무장 행동은 그대로 쓰고 이동만 구속이 막는다(전투 중 크레인 — 점프로 내린다).
/// - 이동: 물리 틱마다 RaycastMotor2D.ShiftConfinement(구속 영역과 몸통을 함께 옮긴다 — 위치 직접 대입 없음).
/// - 착지: RaycastMotor2D.RaycastFloorBelow·IsBodyBlockedAt(모터 질의 재사용). 끊겼을 때 몸이 지형과 겹치면 출발 자리로 Relocate(기존 순간이동 경로).
/// - 던전: 탑승 동안 DungeonFacilityHost.SetFacilityBusy(true) — 던전 클리어 뒤 다음 Wave 준비(크레인 운반)가 겹치지 않게 기다리게 한다.
/// 코루틴 안쪽 순서는 부르는 쪽이 for (var s = Haul(...); s.MoveNext();) yield return s.Current; 로 직접 돌린다(안쪽 예외에도 바깥 finally가 돈다).
/// </summary>
public sealed class FacilityRide
{
    private readonly MonoBehaviour owner;
    private WavePlayerStart hold;
    private bool pushedBlock;
    private IDungeonFacilityHost busyHost;
    private Vector2 originFeet;

    public FacilityRide(MonoBehaviour owner) => this.owner = owner;

    public PlayerController Rider { get; private set; }
    public bool IsRiding => Rider != null;
    /// <summary>탑승을 시작한 횟수(검사용).</summary>
    public int Rides { get; private set; }
    /// <summary>탑승이 끊겨 끝난 횟수(검사용).</summary>
    public int Interruptions { get; private set; }
    public Vector2 OriginFeet => originFeet;
    /// <summary>지금(또는 마지막) 탑승이 피해 보호를 하는지(검사용).</summary>
    public bool Protects { get; private set; } = true;
    /// <summary>지금(또는 마지막) 탑승이 입력을 막는지(검사용).</summary>
    public bool BlocksInput { get; private set; } = true;

    /// <summary>탑승을 시작할 수 있는지(클리어 뒤 시설 사용 조건 + 접지).</summary>
    public static bool CanBoard(PlayerController player) => CanBoard(player, FacilityUse.AfterClear);

    /// <summary>탑승을 시작할 수 있는지(사용 시점별 시설 사용 조건 + 접지).</summary>
    public static bool CanBoard(PlayerController player, FacilityUse use) => FacilityAccess.CanUse(player, use) && player.IsGrounded;

    /// <summary>구속(보호)·입력 정지·바쁨 알림을 걸고 탑승을 시작한다(기존 규칙). 실패하면 아무것도 남기지 않는다.</summary>
    public bool Begin(PlayerController player) => Begin(player, FacilityUse.AfterClear, true, true);

    /// <summary>
    /// 사용 시점·보호·입력 정지를 골라 탑승을 시작한다. 실패하면 아무것도 남기지 않는다.
    /// protect = false면 보호 없는 구속(피해를 받고 잡기만 막음), blockInput = false면 무장 행동을 막지 않는다(진행 중인 공격도 끊지 않는다).
    /// </summary>
    public bool Begin(PlayerController player, FacilityUse use, bool protect, bool blockInput)
    {
        if (IsRiding || owner == null || !CanBoard(player, use)) return false;
        if (hold == null && !owner.TryGetComponent(out hold)) hold = owner.gameObject.AddComponent<WavePlayerStart>();
        var abilities = player.AbilityManager;
        if (abilities != null)
        {
            if (abilities.InteractAbility != null) abilities.InteractAbility.Interrupt();
            if (abilities.DashAbility != null) { abilities.DashAbility.Interrupt(); abilities.DashAbility.ClearPendingInput(); }
            if (abilities.JumpAbility != null) abilities.JumpAbility.ClearPendingInput();
            if (blockInput && abilities.AttackAbility != null && abilities.AttackAbility.AttackPhase != AbilityState.None) abilities.AttackAbility.Interrupt();
        }
        hold.HoldForPassage(protect);
        if (!hold.IsHoldingPlayer || player.Motor == null || !player.Motor.IsConfined)
        {
            if (hold.IsHoldingPlayer) hold.Release();
            return false;
        }
        Rider = player;
        Protects = protect;
        BlocksInput = blockInput;
        if (blockInput && UIManager.Instance != null)
        {
            UIManager.Instance.PushGameplayBlock();
            pushedBlock = true;
        }
        player.Velocity = Vector2.zero;
        player.externalForce = Vector2.zero;
        busyHost = DungeonFacilityHost.Current;
        busyHost?.SetFacilityBusy(owner, true);
        originFeet = Feet;
        Rides++;
        return true;
    }

    /// <summary>
    /// 4차: 붙기 도착 인계. 다른 보유자(붙기 실행 개체)가 이미 구속한 Player를 이 시설의 보유자로도 잡는다. 접지·PlayerFree 검사는 하지 않고
    /// '이미 구속됨'·살아 있음·사용 시점 허락(FacilityAccess.Allows)만 본다. 보유자 여럿 규칙(WavePlayerStart.Hold — 첫 보유자만 구속)이라 구속 영역은
    /// 그대로 이어지고, 부른 쪽이 자기 보유자를 놓으면 이 시설의 구속만 남는다. 입력·진행 중인 공격·점프·대시 선입력은 건드리지 않는다(붙기가 이미 정리했다).
    /// 실패하면 아무것도 남기지 않는다.
    /// </summary>
    public bool BeginHandoff(PlayerController player, FacilityUse use, bool protect)
    {
        if (IsRiding || owner == null || player == null || !player.isActiveAndEnabled) return false;
        if (player.Health != null && (player.Health.IsDead || player.Health.IsDeathPending)) return false;
        if (player.Motor == null || !player.Motor.IsConfined || !WavePlayerStart.IsHolding(player)) return false;
        if (!FacilityAccess.Allows(use)) return false;
        if (hold == null && !owner.TryGetComponent(out hold)) hold = owner.gameObject.AddComponent<WavePlayerStart>();
        hold.HoldForPassage(protect);
        if (!hold.IsHoldingPlayer || !player.Motor.IsConfined)
        {
            if (hold.IsHoldingPlayer) hold.Release();
            return false;
        }
        Rider = player;
        Protects = protect;
        BlocksInput = false;
        player.Velocity = Vector2.zero;
        player.externalForce = Vector2.zero;
        busyHost = DungeonFacilityHost.Current;
        busyHost?.SetFacilityBusy(owner, true);
        originFeet = Feet;
        Rides++;
        return true;
    }

    /// <summary>
    /// 4차: 몸통 바닥 가운데를 열린 다각선 points를 따라 일정 속도 speed(칸/초)로 옮긴다. 시작·끝 ease초 동안만 가감속한다(꺾이는 점에서 멈칫하지 않는다).
    /// 경로가 짧아 가감속 구간이 겹치면 가속도를 유지한 채 최고 속도를 낮춘다(삼각 속도 곡선). 물리 틱마다 ShiftConfinement. onStep(지금 점, 속도, 간 거리).
    /// </summary>
    public IEnumerator HaulPath(Vector2[] points, float speed, float ease, Action<Vector2, Vector2, float> onStep = null)
    {
        if (points == null || points.Length < 2) yield break;
        var tick = new WaitForFixedUpdate();
        float length = FacilityMesh.PathLength(points);
        speed = Mathf.Max(.5f, speed);
        ease = Mathf.Max(.001f, ease);
        Vector2 last = points[0];
        float duration = PathDuration(length, speed, ease);
        for (float time = 0f; time < duration;)
        {
            yield return tick;
            if (Interrupted) yield break;
            float dt = Time.fixedDeltaTime;
            time = Mathf.Min(duration, time + dt);
            float s = PathDistance(time, length, speed, ease);
            Vector2 point = FacilityMesh.PointAlong(points, s, out _);
            Rider.Motor.ShiftConfinement(point - last);
            onStep?.Invoke(point, dt > 0f ? (point - last) / dt : Vector2.zero, s);
            last = point;
        }
        onStep?.Invoke(points[points.Length - 1], Vector2.zero, length);
    }

    /// <summary>길이 length를 속도 speed·가감속 ease초로 가는 데 걸리는 시간(가감속이 겹치면 삼각 곡선).</summary>
    public static float PathDuration(float length, float speed, float ease)
    {
        if (length <= 0f) return 0f;
        float accel = speed / Mathf.Max(.001f, ease);
        if (length >= speed * ease) return length / speed + ease;
        return 2f * Mathf.Sqrt(length / accel);
    }

    /// <summary>시각 time에 간 거리(PathDuration과 같은 속도 곡선).</summary>
    public static float PathDistance(float time, float length, float speed, float ease)
    {
        if (length <= 0f) return 0f;
        float accel = speed / Mathf.Max(.001f, ease);
        float total = PathDuration(length, speed, ease);
        time = Mathf.Clamp(time, 0f, total);
        if (length >= speed * ease)
        {
            if (time < ease) return .5f * accel * time * time;
            if (time > total - ease) { float r = total - time; return length - .5f * accel * r * r; }
            return speed * ease * .5f + speed * (time - ease);
        }
        float half = total * .5f;
        if (time <= half) return .5f * accel * time * time;
        float rest = total - time;
        return length - .5f * accel * rest * rest;
    }

    /// <summary>탑승을 끊어야 하는지(구속이 다른 경로로 풀림·사망·Wave 종료/패배·Player 꺼짐).</summary>
    public bool Interrupted
    {
        get
        {
            var p = Rider;
            if (p == null || !p.isActiveAndEnabled || p.Motor == null || !p.Motor.IsConfined) return true;
            if (p.Health != null && p.Health.IsDead) return true;
            return FacilityAccess.WaveHalted;
        }
    }

    /// <summary>지금 몸통 바닥 가운데(월드).</summary>
    public Vector2 Feet => FacilityAccess.Feet(Rider);
    public float BodyHeight => FacilityAccess.BodyRect(Rider).height;

    /// <summary>몸통 바닥 가운데를 from에서 to로 duration초 동안 옮긴다(물리 틱마다 ShiftConfinement). onStep(지금 점, 속도).</summary>
    public IEnumerator Haul(Vector2 from, Vector2 to, float duration, Action<Vector2, Vector2> onStep = null)
    {
        var tick = new WaitForFixedUpdate();
        Vector2 last = from;
        duration = Mathf.Max(.02f, duration);
        for (float time = 0f; time < duration;)
        {
            yield return tick;
            if (Interrupted) yield break;
            float dt = Time.fixedDeltaTime;
            time += dt;
            float t = Mathf.Clamp01(time / duration);
            Vector2 point = Vector2.Lerp(from, to, t * t * (3f - 2f * t));
            Rider.Motor.ShiftConfinement(point - last);
            onStep?.Invoke(point, dt > 0f ? (point - last) / dt : Vector2.zero);
            last = point;
        }
        onStep?.Invoke(to, Vector2.zero);
    }

    /// <summary>몸 바닥이 feet에 놓인다고 보고 설 바닥 높이를 찾는다(없으면 NaN). 머리 위 지형은 바닥으로 고르지 않는다.</summary>
    public float FloorBelow(Vector2 feet, float below)
    {
        if (Rider == null || Rider.Motor == null) return float.NaN;
        float above = Mathf.Min(1.2f, BodyHeight);
        var hit = Rider.Motor.RaycastFloorBelow(feet, above, below);
        return hit.collider != null && hit.distance > .02f ? hit.point.y + .012f : float.NaN;
    }

    /// <summary>몸 바닥 가운데를 feet로 옮겼을 때 몸이 지형과 겹치는지.</summary>
    public bool BlockedAt(Vector2 feet) => Rider != null && Rider.Motor != null && Rider.Motor.IsBodyBlockedAt(feet - Feet);

    /// <summary>
    /// 탑승을 끝낸다(여러 번 불러도 한 번만 처리). interrupted면 몸이 지형과 겹칠 때 출발 자리로 옮긴 뒤 푼다(비어 있으면 그 자리에서 풀려 떨어진다).
    /// 입력 정지 → 구속 → 바쁨 알림 순으로, 하나가 실패해도 나머지를 푼다.
    /// </summary>
    public void End(bool interrupted)
    {
        var player = Rider;
        Rider = null;
        if (player == null && !pushedBlock && busyHost == null && (hold == null || !hold.IsHoldingPlayer)) return;
        if (interrupted) Interruptions++;
        try
        {
            if (interrupted && player != null && player.Motor != null && player.Motor.IsConfined && player.Motor.IsBodyBlockedAt(Vector2.zero))
                RelocateFeet(player, originFeet);
        }
        catch (Exception e) { Debug.LogException(e); }
        finally
        {
            try
            {
                if (pushedBlock)
                {
                    pushedBlock = false;
                    if (UIManager.Instance != null) UIManager.Instance.PopGameplayBlock();
                }
            }
            finally
            {
                try { if (hold != null && hold.IsHoldingPlayer) hold.Release(); }
                finally
                {
                    var host = busyHost;
                    busyHost = null;
                    host?.SetFacilityBusy(owner, false);
                }
            }
        }
    }

    /// <summary>몸통 바닥 가운데가 feet에 오도록 기존 순간이동 경로(WavePlayerStart.Relocate)로 옮긴다.</summary>
    public static void RelocateFeet(PlayerController player, Vector2 feet)
    {
        var body = FacilityAccess.BodyRect(player);
        Vector2 offset = new Vector2(body.center.x, body.yMin) - (Vector2)player.transform.position;
        WavePlayerStart.Relocate(player, new Vector3(feet.x - offset.x, feet.y - offset.y, player.transform.position.z), false);
    }
}
