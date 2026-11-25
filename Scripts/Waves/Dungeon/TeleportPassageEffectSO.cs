using System;
using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// 텔레포트 시설로 짝 방에 옮기는 연출. 정렬 → 충전 → 섬광으로 화면 덮기 → (덮인 동안) 방 교체·도착 패드로 순간이동 → 화면 걷기 → 도착 착지.
/// </summary>
/// <remarks>
/// - 재사용(새 Clip·Player.prefab 수정 없음): Player Animator의 기존 bool <c>Warp</c>(true = Player_WarpStart, 이벤트 StartWarp → PlayerWarpStart 효과,
///   false = Player_WarpEnd, 이벤트 SplashWarp → PlayerSplashWarp 도착 폭발), 화면은 UIManager 공용 페이드·와이어프레임(FadeWireframeTransitionSO와 같은 방식).
///   Warp를 켠 뒤 1.08초 안에 끈다(WarpStart의 1.08초 SplashWarp 이벤트가 출발 쪽에서 한 번 더 터지지 않게).
/// - Player 위치는 모터 구속 이동(<c>RaycastMotor2D.ShiftConfinement</c>, 패드 가운데로 정렬)과 기존 순간이동 경로
///   (<c>WavePlayerStart.Relocate</c> → <c>PlayerController.ResetPlayerPosition</c> + Cinemachine warp)로만 바꾼다. 구속은 관 통과와 같은 WaveManager의
///   WavePlayerStart.HoldForPassage(피해 무시·잡기 차단). Wave가 끝나거나 패배하면 WaveManager가 함께 푼다.
/// - 끊기거나 예외가 나도 finally에서 Warp false·화면 페이드 0·와이어프레임 0·구속 해제를 보장한다. 바깥에서 StopCoroutine으로 끊으면 finally가
///   돌지 않으므로 DungeonManager는 관 통과와 같은 Stack 실행기(Dispose)로 끊는다.
/// - 이 에셋은 공용 데이터라 진행 상태는 지역 변수에만 둔다.
/// </remarks>
[CreateAssetMenu(menuName = "Training/Dungeon Passage/Teleport", fileName = "TeleportPassage")]
public sealed class TeleportPassageEffectSO : DungeonPassageEffectSO
{
    private const string WarpParameter = "Warp";
    private const string ArrivalFeedbackKey = "SplashWarp";
    private const string DepartFeedbackKey = "StartWarp";

    [Header("정렬")]
    [Tooltip("Player 몸통 중심을 출발 패드 가운데(RecessPoint)로 옮기는 시간(초).")]
    [Min(.05f)] public float alignDuration = .22f;

    [Header("충전")]
    [Tooltip("Warp 자세로 바꾼 뒤 패드 고리가 가속하고 빛 기둥이 서는 시간(초).")]
    [Min(0f)] public float chargeDuration = .5f;

    [Header("화면")]
    [Tooltip("섬광으로 화면이 다 덮이는 시간(초).")]
    [Min(.02f)] public float flashOut = .12f;
    [Tooltip("덮인 채 방을 바꾸고 기다리는 시간(초).")]
    [Min(0f)] public float holdCovered = .14f;
    [Tooltip("화면이 걷히는 시간(초).")]
    [Min(.02f)] public float flashIn = .3f;
    [Tooltip("와이어프레임 색(중립 기능 파랑).")]
    public Color wireColor = new Color(.25f, .65f, 1f, 1f);
    [Tooltip("와이어프레임 세기(FadeWireframeTransitionSO 기본 3).")]
    [Min(0f)] public float wireStrength = 3f;

    [Header("도착")]
    [Tooltip("도착 착지 자세를 보여 준 뒤 풀어 주기까지(초).")]
    [Min(0f)] public float arriveDuration = .4f;

    public override IEnumerator Play(DungeonPassageContext context)
    {
        if (context == null || context.Player == null || context.From == null) yield break;
        var player = context.Player;
        var from = context.From;
        WavePlayerStart hold = null;
        bool ownsHold = false, warping = false, faded = false;
        var tick = new WaitForFixedUpdate();
        try
        {
            // 1) 구속: Wave 진행이 쓰는 WavePlayerStart로 잡는다(관 통과와 같은 holder).
            hold = HoldFor(player, out ownsHold);

            // 2) 정렬: 물리 틱마다 구속 영역을 옮겨 몸통 중심을 패드 가운데로.
            var motor = player.Motor;
            if (motor != null && motor.IsConfined)
            {
                Vector2 start = BodyCenter(player), end = from.RecessPoint, last = start;
                for (float time = 0f; time < alignDuration;)
                {
                    yield return tick;
                    if (!motor.IsConfined) yield break;
                    time += Time.fixedDeltaTime;
                    float t = Mathf.Clamp01(time / alignDuration);
                    Vector2 point = Vector2.Lerp(start, end, t * t * (3f - 2f * t));
                    motor.ShiftConfinement(point - last);
                    last = point;
                }
                yield return tick;
            }

            // 3) 충전: 패드 고리 가속·빛 기둥(PlayIntake) + Player Warp 자세(StartWarp 효과는 Clip 이벤트가 낸다).
            //    구속은 스턴이 아니므로 충전 중에도 공격할 수 있다. Warp bool은 AnyState 전이라 무장 Animation(판정 곡선 포함)을 끊으므로,
            //    공격 중이면 켜지 않고, 충전 도중 공격을 시작하면 끈다(WavePlayerStart.Relocate와 같은 원칙 — 자세는 두고 효과만).
            from.PlayIntake();
            warping = !Attacking(player) && SetWarp(player, true);
            if (!warping) PlayAnimationFeedback(player, DepartFeedbackKey);
            for (float time = 0f; time < chargeDuration; time += Time.deltaTime)
            {
                yield return null;
                if (warping && Attacking(player)) { SetWarp(player, false); warping = false; }
            }

            // 4) 섬광으로 덮기 → 방 교체 → 도착 패드로 순간이동(카메라 즉시 맞춤).
            faded = true;
            if (UIManager.Instance != null)
            {
                UIManager.Instance.StartScreenFade(1f, flashOut);
                UIManager.Instance.StartWireframeEffect(wireStrength, wireColor, flashOut);
            }
            for (float time = 0f; time < flashOut; time += Time.deltaTime) yield return null;

            context.SwapRoom?.Invoke();
            var arrival = context.Arrival != null ? context.Arrival() : null;
            if (arrival == null) yield break;   // 도착 패드가 없으면 DungeonManager가 즉시 교체·순간이동으로 마무리한다.
            Warp(player, arrival.RecessPoint);
            for (float time = 0f; time < holdCovered; time += Time.deltaTime) yield return null;

            // 5) 화면 걷기 + 도착 착지(Warp false → Player_WarpEnd → SplashWarp 도착 폭발) + 도착 패드 폭발.
            if (UIManager.Instance != null)
            {
                UIManager.Instance.StartWireframeEffect(0f, wireColor, flashIn);
                UIManager.Instance.StartScreenFade(0f, flashIn);
            }
            faded = false;
            arrival.PlayEject();
            if (warping) { SetWarp(player, false); warping = false; }
            else PlayAnimationFeedback(player, ArrivalFeedbackKey);
            for (float time = 0f; time < arriveDuration; time += Time.deltaTime) yield return null;

            context.Completed = true;
        }
        finally
        {
            // Warp 자세·화면 덮기가 남지 않게 먼저 되돌리고, 구속을 푼다(앞쪽에서 문제가 생겨도 구속은 반드시 푼다).
            try
            {
                if (warping) SetWarp(player, false);
                if (faded && UIManager.Instance != null)
                {
                    UIManager.Instance.StartWireframeEffect(0f, wireColor, .1f);
                    UIManager.Instance.StartScreenFade(0f, .1f);
                }
            }
            finally { if (ownsHold && hold != null) hold.Release(); }
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

    // Animator에 Warp bool이 있으면 바꾸고 true(바꿨음)를 돌려준다.
    private static bool Attacking(PlayerController player)
    {
        var abilities = player != null ? player.AbilityManager : null;
        return abilities != null && abilities.AttackAbility != null && abilities.AttackAbility.IsActive;
    }

    private static bool SetWarp(PlayerController player, bool value)
    {
        var animator = player != null ? player.animator : null;
        if (animator == null || !animator.isActiveAndEnabled || !HasParameter(animator, WarpParameter)) return false;
        animator.SetBool(WarpParameter, value);
        return true;
    }

    private static bool HasParameter(Animator animator, string name)
    {
        foreach (var parameter in animator.parameters)
            if (parameter.type == AnimatorControllerParameterType.Bool && parameter.name == name) return true;
        return false;
    }

    private static void PlayAnimationFeedback(PlayerController player, string key)
    {
        if (player == null || player.animator == null) return;
        if (player.animator.TryGetComponent(out AnimationFeedbackReceiver receiver)) receiver.PlayAnimationFeedback(key);
    }

    // 몸통 중심(월드). 구속 중이면 모터가 쓰는 몸통, 아니면 Collider 경계(물리 질의 없음).
    private static Vector2 BodyCenter(PlayerController player) => FacilityAccess.BodyRect(player).center;

    // 몸통 중심이 bodyCenter에 오도록 Transform 위치를 환산해 기존 순간이동 경로로 옮기고, 카메라를 끊김 없이 바로 맞춘다(PipePassageEffectSO와 같음).
    private static void Warp(PlayerController player, Vector2 bodyCenter)
    {
        Vector2 offset = BodyCenter(player) - (Vector2)player.transform.position;
        Vector3 target = new Vector3(bodyCenter.x - offset.x, bodyCenter.y - offset.y, player.transform.position.z);
        WavePlayerStart.Relocate(player, target, false);
        var room = RoomManager.Instance != null ? RoomManager.Instance.CurrentRoom : null;
        if (room != null && room.VirtualCamera != null) room.VirtualCamera.PreviousStateIsValid = false;
        var view = Camera.main;
        if (view != null && view.TryGetComponent(out CinemachineBrain brain) && brain.ActiveVirtualCamera is CinemachineVirtualCameraBase active)
            active.PreviousStateIsValid = false;
    }
}
