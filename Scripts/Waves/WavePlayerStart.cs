using System.Collections;
using System.Collections.Generic;
using Unity.Cinemachine;
using UnityEngine;

// 지형 준비 동안 Player를 구속 상자에 가두고, 시작 좌표로 옮기는 Wave에서는 크레인으로 그 상자를 들어 옮긴다.
// 방 전환의 UI 잠금 초기화와 독립적이다.
// 입력과 진행 중인 무장(회복·드론 소환 포함)은 건드리지 않는다. 이동만 RaycastMotor2D의 이동 구속이 막고,
// 갇힌 동안의 피해는 PlayerHealth가 막는다. WaveManager와 코스 Stage가 함께 잡아도 상자는 Player마다 하나다.
// 크레인 이동은 구속 영역을 물리 틱마다 조금씩 옮기는 것이다(RaycastMotor2D.ShiftConfinement). Player는 기존 이동 경로로 상자에 실려 가므로
// 위치를 직접 바꾸지 않고, 옮겨지는 동안에도 상자 안에서 움직이고 무장을 쓸 수 있다.
[DisallowMultipleComponent]
public sealed class WavePlayerStart : MonoBehaviour
{
    private static readonly HashSet<WavePlayerStart> holders = new();
    private static readonly int ArrivalState = Animator.StringToHash("Player_WarpEnd");
    private const string ArrivalFeedbackKey = "SplashWarp";
    private const float DefaultSideMargin = .12f, DefaultTopMargin = .12f;
    // 구속을 시작하거나 옮긴 뒤 이만큼 지나도 갇혀 있을 때만 상자를 그린다. 바뀔 지형이 없어 바로 풀리는 준비에는 상자가 번쩍이지 않는다.
    private const float ShowDelay = .1f;
    // 크레인이 내려와 물 때까지 기다리는 한도. 표시 Prefab이 없거나 문제가 생겨도 준비가 멈추지 않는다.
    private const float AttachLimit = 2f;
    // 시작 좌표와의 차가 이보다 작으면(가로·세로 칸) 옮길 필요가 없는 것으로 본다.
    private static readonly Vector2 NearStart = new Vector2(1f, 2f);
    private const float DefaultLift = 2.5f;
    private static WavePlayerContainment containment;
    private static AnimatorCullingMode savedCulling;
    private static float confinedAt;
    private PlayerController player;
    private Coroutine carry;
    private Coroutine presentationSampling;
    private WaveCarryPresentation carryPresentation;
    private bool terrainReady;
    private bool lifted;            // 크레인이 상자를 들기 시작했는지(그 전에 끊기면 제자리에 둔다)
    private Vector3 carryOrigin, carryDestination;   // 들기 전에 서 있던 자리와 옮겨 놓을 시작 좌표(월드)
    private WaveCellTerrain carryTerrain;
    public bool IsHoldingPlayer { get; private set; }
    /// <summary>크레인이 구속 상자를 시작 좌표로 옮기고 있는지(내려와 무는 것부터 내려놓을 때까지).</summary>
    public bool IsCarrying { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetHolders() { holders.Clear(); containment = null; }
    // TrainingRoom 입장 로딩에서 구속 상자 표시를 미리 만들어 둔다(처음 갇히는 순간에 Prefab을 만들지 않는다).
    public static void Prewarm()
    {
        var rules = WaveRoomRulesSO.Default;
        if (containment != null || rules == null || rules.playerContainment == null) return;
        containment = Instantiate(rules.playerContainment);
        containment.gameObject.SetActive(false);
    }
    public static bool IsHolding(PlayerController target)
    {
        foreach (var holder in holders) if (holder != null && holder.player == target) return true;
        return false;
    }

    public void Hold()
    {
        if (IsHoldingPlayer) return;
        player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player == null) return;
        bool first = !IsHolding(player);
        IsHoldingPlayer = true;
        holders.Add(this);
        if (first) Confine(player);
    }

    // 지금 서 있는(또는 떠 있는) 자리에 몸통보다 조금 큰 상자를 씌운다. 속도와 진행 중인 동작은 그대로 두고 상자가 막게 한다.
    private static void Confine(PlayerController target)
    {
        // 잡힌 채로는 가두지 않는다(잡기 연출은 Player를 직접 옮긴다). 갇힌 동안에는 새 잡기도 성립하지 않는다.
        if (target.AbilityManager != null && target.AbilityManager.Grab != null) target.AbilityManager.Grab.Abort();
        savedCulling = target.animator.cullingMode;
        target.animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        FitConfinement(target);
        confinedAt = Time.time;
    }
    // 잠깐 기다려도 계속 갇혀 있으면 상자를 조립한다.
    private void Update()
    {
        if (!IsHoldingPlayer || player == null || Time.time - confinedAt < ShowDelay) return;
        ShowContainment(player);
    }
    // 구속 상자 표시를 켠다(이미 켜져 있으면 그대로 둔다).
    private static void ShowContainment(PlayerController target)
    {
        if (containment != null && containment.IsEngaged) return;
        var rules = WaveRoomRulesSO.Default;
        if (containment == null && rules != null && rules.playerContainment != null)
            containment = Instantiate(rules.playerContainment);
        if (containment != null) containment.Engage(target);
    }
    private static void FitConfinement(PlayerController target)
    {
        var rules = WaveRoomRulesSO.Default;
        target.Motor.ConfineAroundBody(rules != null ? rules.containmentSideMargin : DefaultSideMargin,
            rules != null ? rules.containmentTopMargin : DefaultTopMargin);
    }
    private static void Free(PlayerController target)
    {
        // 이동 구속부터 푼다. 표시를 거두는 쪽(효과음·연출)에서 문제가 생겨도 Player가 갇힌 채 남지 않는다.
        if (target != null)
        {
            target.Motor.ClearConfinement();
            // 상자 벽으로 밀던 힘이 풀리는 순간 튀어 나가지 않게 한다.
            target.externalForce = Vector2.zero;
            target.animator.cullingMode = savedCulling;
        }
        if (containment != null) containment.Disengage();
    }

    // Player를 순간이동시킨다(위치 초기화 장치·코스 복귀). 진행 중인 무장은 끊지 않고, 옮긴 뒤 이어지면 안 되는 대시와 눌러 둔 점프·대시 입력만 지운다.
    public static void Relocate(PlayerController target, Vector3 position, bool effect)
    {
        var abilities = target.AbilityManager;
        if (abilities != null)
        {
            if (abilities.DashAbility != null) { abilities.DashAbility.Interrupt(); abilities.DashAbility.ClearPendingInput(); }
            if (abilities.JumpAbility != null) abilities.JumpAbility.ClearPendingInput();
        }
        target.externalForce = Vector2.zero;
        Vector3 before = target.transform.position;
        target.ResetPlayerPosition(position);
        foreach (var holder in holders) if (holder != null && holder.player == target) holder.carryPresentation?.ResetPose();
        CinemachineCore.OnTargetObjectWarped(target.transform, target.transform.position - before);
        // 갇힌 채 옮기면 구속은 ResetPlayerPosition이 도착한 자리로 옮긴다. 떠난 자리의 상자는 풀고, 계속 갇혀 있으면 잠시 뒤 그 자리에 다시 조립한다.
        if (IsHolding(target))
        {
            confinedAt = Time.time;
            if (containment != null && containment.IsEngaged) containment.Disengage();
        }
        if (!effect || target.animator == null) return;
        bool attacking = abilities != null && abilities.AttackAbility != null && abilities.AttackAbility.IsActive;
        if (!attacking && target.animator.HasState(0, ArrivalState))
        {
            target.animator.ResetTrigger("TitleTransition");
            // 같은 상태에서 연속으로 복귀해도 원본 SplashWarp AnimationEvent를 다시 재생한다.
            target.animator.Play(ArrivalState, 0, 0);
        }
        // 무장 Animation이 재생 중이면 자세는 그대로 두고 도착 효과만 낸다.
        else if (attacking && target.animator.TryGetComponent(out AnimationFeedbackReceiver receiver))
            receiver.PlayAnimationFeedback(ArrivalFeedbackKey);
    }

    /// <summary>
    /// 시작 좌표로 옮기는 Wave면 크레인 이동을 시작한다. 지형 준비와 함께 진행하도록 지형을 바꾸기 전에 부른다.
    /// 옮기지 않는 Wave에서는 아무것도 하지 않는다(제자리에 갇혀 지형이 바뀌기를 기다린다).
    /// </summary>
    public void BeginCarry(WaveDataSO data, int index, Transform coordinates)
    {
        if (player == null || !IsHoldingPlayer || IsCarrying || !data.waves[index].teleportAtStart) return;
        terrainReady = lifted = false;
        carryOrigin = player.transform.position;
        carryDestination = coordinates.TransformPoint(WaveRoomState.PlayerStartAt(data, index));
        var terrain = coordinates.GetComponent<WaveCellTerrain>();
        // 이미 시작 좌표 근처에 서 있으면 옮기지 않는다(제자리에서 들었다 놓지 않는다). 시작 좌표는 바닥에서 조금 떠 있게 적는 일이 많아 세로는 넉넉하게 본다.
        // 다만 넘어갈 Wave의 지형이 지금 갇혀 있는 자리에 새로 생기면, 그대로 두면 지형 속에 남으므로 근처여도 옮긴다.
        Vector2 away = carryDestination - carryOrigin;
        if (Mathf.Abs(away.x) < NearStart.x && Mathf.Abs(away.y) < NearStart.y
            && (terrain == null || !terrain.WillAddTerrain(index, RoomRect(coordinates, player.Motor.Confinement)))) return;
        carryTerrain = terrain;
        // 카메라·배경 범위는 지형을 바꾸기 전에 넓혀 둔다. 크레인이 내려와 물 때까지 미루면, 그 사이에 실내가 줄어드는 Wave에서
        // 아직 제자리에 있는 Player가 카메라 범위 밖에 놓인다. 표시 쪽 문제로 Wave 준비가 멈추지 않게 한다.
        if (carryTerrain != null)
        {
            var rules = WaveRoomRulesSO.Default;
            try
            {
                carryTerrain.BeginCarry(coordinates.InverseTransformPoint(carryOrigin), coordinates.InverseTransformPoint(carryDestination),
                    rules != null ? rules.craneLiftHeight : DefaultLift);
            }
            catch (System.Exception e) { Debug.LogException(e, this); }
        }
        IsCarrying = true;
        carry = StartCoroutine(CarryRoutine());
        // 코루틴을 시작하지 못했으면 옮기기 상태를 남기지 않는다(FinishCarry가 끝없이 기다리지 않게).
        if (carry == null) EndCarry(CarryEnd.Failed);
    }
    // 월드 사각형을 방 좌표 사각형으로 옮긴다.
    private static Rect RoomRect(Transform coordinates, Rect world)
    {
        Vector2 a = coordinates.InverseTransformPoint(world.min), b = coordinates.InverseTransformPoint(world.max);
        return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
    }
    /// <summary>지형 준비가 끝났음을 알리고(이때부터 내려놓을 수 있다) 크레인 이동이 끝날 때까지 기다린다.</summary>
    public IEnumerator FinishCarry()
    {
        terrainReady = true;
        var terrain = GetComponentInParent<WaveCellTerrain>();
        // 마지막 카메라 경계가 정착한 뒤 구속을 풀고 Wave를 시작한다.
        while (IsCarrying || (terrain != null && terrain.IsCameraSettling)) yield return null;
    }

    // 크레인 이동: 내려와 물기 → 들기 → 옮기기 → (지형이 다 바뀔 때까지 들고 기다리기) → 내려놓기.
    // 들고 옮기고 내려놓는 데 시간이 걸리므로 보통은 지형이 먼저 끝난다.
    private IEnumerator CarryRoutine()
    {
        bool finished = false;
        try
        {
            var rules = WaveRoomRulesSO.Default;
            float lift = rules != null ? rules.craneLiftHeight : DefaultLift;
            float liftDuration = Mathf.Max(.05f, rules != null ? rules.craneLiftDuration : .45f);
            float speed = Mathf.Max(1f, rules != null ? rules.craneTravelSpeed : 26f);
            float shortest = rules != null ? rules.craneTravelMinDuration : .55f, longest = rules != null ? rules.craneTravelMaxDuration : 2.6f;
            var target = player;
            var motor = target.Motor;
            // 옮길 것이 정해져 있으므로 상자를 바로 조립하고 크레인을 내린다.
            ShowContainment(target);
            StopCarryPresentation();
            carryPresentation = new WaveCarryPresentation(target, containment);
            presentationSampling = StartCoroutine(SampleCarryPresentation());
            if (containment != null) containment.AttachCrane();
            float limit = Time.time + AttachLimit;
            while (containment != null && containment.Target == target && !containment.CraneAttached && Time.time < limit) yield return null;

            // 지금 몸이 선 자리(가로는 몸 가운데, 세로는 상자 바닥)를 시작 좌표에 선 몸의 바닥 가운데로 옮긴다.
            // 상자 안에서 한쪽으로 치우쳐 있어도 그 치우침째로 옮겨 시작 좌표에 맞게 내려놓는다.
            Rect body = motor.ConfinedBody, area = motor.Confinement;
            Vector2 bodyOffset = body.center - (Vector2)target.transform.position;
            Vector2 from = new Vector2(body.center.x, area.yMin);
            Vector2 to = new Vector2(carryDestination.x + bodyOffset.x, carryDestination.y + bodyOffset.y - body.height * .5f);
            Vector2 raised = Vector2.up * lift;
            lifted = true;
            // 구간마다 옮기는 순서를 여기서 직접 돌린다(Unity에 따로 맡기지 않는다). 안에서 예외가 나도 아래 finally까지 풀려 나온다.
            for (var step = Haul(motor, from, from + raised, liftDuration); step.MoveNext();) yield return step.Current;
            float travel = Mathf.Clamp(Vector2.Distance(from, to) / speed, shortest, Mathf.Max(shortest, longest));
            for (var step = Haul(motor, from + raised, to + raised, travel); step.MoveNext();) yield return step.Current;
            // 내려놓을 자리의 지형이 다 바뀔 때까지 들고 기다린다.
            while (!terrainReady) yield return null;
            for (var step = Haul(motor, to + raised, new Vector2(to.x, SettleHeight(motor, to, body.height)), liftDuration); step.MoveNext();) yield return step.Current;
            // 마지막으로 넘긴 이동량이 반영된 뒤에 끝낸다.
            yield return new WaitForFixedUpdate();
            finished = true;
        }
        finally
        {
            // 다 옮겼을 때와 도중에 예외가 났을 때 모두 여기서 끝낸다. 예외로 끊겨도 옮기기 상태가 남지 않아 FinishCarry가 끝없이 기다리지 않는다.
            // (StopCoroutine으로 끊긴 경우는 여기로 오지 않고 Release가 끝낸다.)
            EndCarry(finished ? CarryEnd.Arrived : CarryEnd.Failed);
        }
    }
    // 상자를 내려놓을 높이: 시작 좌표에 선 몸의 발밑(feet) 근처에 실제 바닥이 있으면 그 위에 맞춘다.
    // 시작 좌표가 바닥에서 조금 떠 있거나 묻혀 있어도 상자가 바닥에 닿게 내려놓는다. 근처에 바닥이 없으면(공중 시작) 좌표 그대로 둔다.
    // 바닥은 기존 이동과 같은 모터 질의로 찾되 몸이 시작 좌표에 놓인 것으로 본다. 들려 있는 몸 기준으로 보면 시작 좌표 위쪽의 통과 발판을 바닥으로 고른다.
    private static float SettleHeight(RaycastMotor2D motor, Vector2 feet, float bodyHeight)
    {
        const float below = 3f, clearance = .012f;
        // 바닥에 묻힌 시작 좌표를 위해 발 위쪽도 보되 몸 높이는 넘지 않는다(머리 위 지형을 바닥으로 고르지 않는다).
        float above = Mathf.Min(1.2f, bodyHeight);
        var floor = motor.RaycastFloorBelow(feet, above, below);
        // 광선이 지형 속에서 시작했으면(거리 0) 바닥으로 보지 않는다.
        return floor.collider != null && floor.distance > .02f ? floor.point.y + clearance : feet.y;
    }
    // 구속 영역의 바닥 가운데를 from에서 to로 옮긴다. 물리 틱마다 옮길 양을 모터에 넘기면 몸통이 함께 실려 간다.
    private IEnumerator Haul(RaycastMotor2D motor, Vector2 from, Vector2 to, float duration)
    {
        var tick = new WaitForFixedUpdate();
        Vector2 last = from;
        for (float time = 0f; time < duration;)
        {
            yield return tick;
            if (!motor.IsConfined) yield break;
            time += Time.fixedDeltaTime;
            float t = Mathf.Clamp01(time / duration);
            Vector2 point = Vector2.Lerp(from, to, t * t * (3f - 2f * t));
            motor.ShiftConfinement(point - last);
            if (containment != null) containment.DriveCrane((point - last) / Time.fixedDeltaTime);
            last = point;
        }
        if (containment != null) containment.DriveCrane(Vector2.zero);
    }
    // 크레인 이동이 끝난 까닭: 다 옮김 / 옮기다 실패했지만 Wave는 이어짐 / Wave 준비가 끊김(패배·강제 정리·다시 시작·비활성화).
    private enum CarryEnd { Arrived, Failed, Interrupted }
    // 크레인 이동을 끝낸다. 옮기기 상태부터 지우고, 뒤따르는 처리(자리 정하기·카메라 범위·크레인 표시) 가운데 하나가 실패해도 나머지는 실행한다.
    private void EndCarry(CarryEnd end)
    {
        if (!IsCarrying) return;
        IsCarrying = false;
        carry = null;
        var terrain = carryTerrain;
        carryTerrain = null;
        bool arrived = true;
        try
        {
            if (player != null)
            {
                // 옮기다 실패했으면 Wave가 이어지므로 시작 좌표에 바로 둔다(크레인 이동이 생기기 전의 순간이동과 같다).
                if (end == CarryEnd.Failed) Relocate(player, carryDestination, false);
                // 준비가 끊겼으면 Wave는 이어지지 않는다. 들기 전이면 제자리에 두고, 들어 옮기던 중이면 놓을 자리를 정한다.
                else if (end == CarryEnd.Interrupted) arrived = lifted && SettleInterrupted(player);
            }
        }
        finally
        {
            try { if (terrain != null) terrain.EndCarry(arrived); }
            // 걸쇠를 풀고 올려 보낸다. 상자는 Release가 푼다.
            finally
            {
                if (end != CarryEnd.Arrived) StopCarryPresentation();
                if (containment != null) containment.DetachCrane();
            }
        }
    }
    // 들어 옮기던 도중에 끊겼을 때 Player를 둘 자리를 정한다. 시작 좌표로 옮겼으면 true.
    // 지금 자리가 비어 있으면 그대로 둔다(순간이동 없이 상자가 풀리며 떨어진다). 지형을 지나던 중이라 몸이 지형과 겹쳐 있을 때만
    // 시작 좌표로, 그곳도 막혀 있으면(지형이 바뀌다 만 상태) 들기 전에 서 있던 자리로 옮긴다.
    private bool SettleInterrupted(PlayerController target)
    {
        var motor = target.Motor;
        if (!motor.IsBodyBlockedAt(Vector2.zero)) return false;
        Vector2 here = target.transform.position;
        if (motor.IsBodyBlockedAt((Vector2)carryDestination - here) && !motor.IsBodyBlockedAt((Vector2)carryOrigin - here))
        {
            Relocate(target, carryOrigin, false);
            return false;
        }
        Relocate(target, carryDestination, false);
        return true;
    }

    private IEnumerator SampleCarryPresentation()
    {
        var tick = new WaitForFixedUpdate();
        while (IsHoldingPlayer && carryPresentation != null)
        {
            yield return tick;
            carryPresentation?.SamplePhysics();
        }
    }
    private void StopCarryPresentation()
    {
        if (presentationSampling != null) StopCoroutine(presentationSampling);
        presentationSampling = null;
        carryPresentation?.Dispose();
        carryPresentation = null;
    }
    public void Release()
    {
        bool held = IsHoldingPlayer;
        var target = player;
        if (carry != null) StopCoroutine(carry);
        try { EndCarry(CarryEnd.Interrupted); }
        finally
        {
            StopCarryPresentation();
            holders.Remove(this);
            IsHoldingPlayer = false;
            player = null;
            // 다른 곳이 아직 잡고 있으면 상자를 유지한다.
            if (held && !IsHolding(target)) Free(target);
        }
    }
    private void OnDisable() => Release();
    private void OnDestroy() => Release();
}
