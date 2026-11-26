using UnityEngine;

/// <summary>
/// (선택, 6차 T2) 상호작용 키로 시작한 시설 작동(레버 당김·수리 등)이 아직 진행 중인지 알린다.
/// 시설 부품(IFacilityInteractTarget을 구현한 부모)이 함께 구현하면, 중재자가 그 동안 크레인 붙기 표시·입력을 막는다.
/// 구현하지 않은 시설은 처리 뒤 최소 시간(FacilityInteractArbiter.MinBusy)만 막는다.
/// </summary>
public interface IFacilityBusy
{
    /// <summary>지금 작동 중인지(레버 Pulling·수리대 Repairing 등).</summary>
    bool FacilityBusy { get; }
}

/// <summary>
/// 상호작용 키(↑) 중재(6차 DESIGN_V6 T2 안 A — 시설 파일만, CP949 Player 코드 무수정). 한 번의 ↑는 정확히 한 대상만 처리한다.
/// - Preferred(player): 프레임당 한 번, 키 안내를 띄울 수 있는 상호작용 지점(FacilityInteractPoint.Live 중 InRange && ShowFacilityHint) 가운데
///   Player 발 ↔ 트리거 상자 거리가 가장 가까운 것(물리 질의 없음). 같은 거리(TieEpsilon 안)면 종류 순위 출구 > 레버 > 수리대 > 텔레포트 > 그 밖.
///   키 안내는 이 지점에만 뜨고(안내 1개 — 실행 순서·표시 페이드와 무관), InteractAbility가 다른 지점 Collider를 골라도 이 지점이 처리한다.
/// - MarkHandled(point, accepted): 지점이 이번 ↑를 처리(받아들임 또는 거절음)했다고 기록한다. HandledFrame = 그 프레임.
///   FacilityZip은 HandledFrame ≥ 누른 프레임이면 그 ↑의 붙기 선입력을 버린다(구독 순서와 무관 — 확정 대기 0.08초 전에 걸러진다).
///   붙기에 양보한 거절(FacilityInteractPoint.ZipYields)은 기록하지 않는다.
/// - IsBusy: 받아들인 처리 뒤 max(MinBusy, busySeconds) 동안, 그리고 처리한 시설이 IFacilityBusy로 작동 중이라고 알리는 동안(상한 MaxBusy) true.
///   이 동안 FacilityZip은 크레인 직선·키 안내를 숨기고 붙기 입력을 받지 않는다(수리 1.1초·컷씬을 끈 레버 당김 0.6초 — 판단값 D3).
/// 정적 상태는 Play 시작 때 지운다(도메인 다시 읽기 끔 대비).
/// </summary>
public static class FacilityInteractArbiter
{
    /// <summary>받아들인 처리 뒤 크레인 붙기를 막는 최소 시간(초, 판단값).</summary>
    public const float MinBusy = .15f;
    /// <summary>IFacilityBusy가 작동 중이라고 계속 알려도 이 시간이 지나면 풀어 준다(초, 안전 상한).</summary>
    public const float MaxBusy = 3f;
    /// <summary>거리가 이 폭(칸) 안으로 같으면 종류 순위로 정한다.</summary>
    public const float TieEpsilon = 1e-3f;

    /// <summary>종류 순위(작을수록 우선, 거리가 같을 때만 쓴다).</summary>
    public const int RankExit = 0, RankLever = 1, RankRepair = 2, RankTeleport = 3, RankOther = 4;

    private static int cachedFrame = -1;
    private static PlayerController cachedPlayer;
    private static FacilityInteractPoint cached;
    private static int handledFrame = -1;
    private static float handledAt = -999f;
    private static float busyUntil = -999f;
    private static IFacilityBusy busySource;
    private static FacilityInteractPoint lastHandled;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        cachedFrame = handledFrame = -1;
        cachedPlayer = null;
        cached = lastHandled = null;
        handledAt = busyUntil = -999f;
        busySource = null;
        Handled = Accepted = 0;
    }

    /// <summary>마지막으로 ↑를 처리한 프레임(없으면 −1).</summary>
    public static int HandledFrame => handledFrame;
    /// <summary>이번 프레임에 이미 어떤 지점이 ↑를 처리했는지.</summary>
    public static bool HandledThisFrame => handledFrame == Time.frameCount;
    /// <summary>마지막으로 처리한 지점(검사용).</summary>
    public static FacilityInteractPoint LastHandled => lastHandled;
    /// <summary>처리 기록 수·그중 받아들인 수(검사용).</summary>
    public static int Handled { get; private set; }
    public static int Accepted { get; private set; }

    /// <summary>
    /// 이번 ↑를 받을 상호작용 지점(키 안내를 띄울 수 있는 지점 중 가장 가까운 것, 없으면 null). 프레임당 한 번 계산해 기억한다.
    /// </summary>
    public static FacilityInteractPoint Preferred(PlayerController player)
    {
        if (player == null) return null;
        int frame = Time.frameCount;
        if (frame == cachedFrame && ReferenceEquals(player, cachedPlayer) && (cached == null || cached.isActiveAndEnabled)) return cached;
        cachedFrame = frame;
        cachedPlayer = player;
        cached = Pick(player);
        return cached;
    }

    /// <summary>이번 프레임 기억을 버린다(검사에서 같은 프레임에 Player를 옮긴 뒤 다시 물을 때).</summary>
    public static void Invalidate() => cachedFrame = -1;

    private static FacilityInteractPoint Pick(PlayerController player)
    {
        var points = FacilityInteractPoint.Live;
        FacilityInteractPoint best = null;
        float bestDistance = float.MaxValue;
        int bestRank = int.MaxValue;
        Vector2 feet = player.transform.position;
        for (int i = 0; i < points.Count; i++)
        {
            var point = points[i];
            if (point == null || !point.isActiveAndEnabled) continue;
            float distance = FacilityAccess.DistanceToRect(feet, point.Area);
            if (distance > point.HintRange) continue;
            int rank = point.Rank;
            // 이미 고른 것보다 확실히 멀거나, 같은 거리인데 순위가 낮으면 안내 조건을 묻지 않는다.
            if (best != null && (distance > bestDistance + TieEpsilon || (distance > bestDistance - TieEpsilon && rank >= bestRank))) continue;
            if (!point.WantsHint(player)) continue;
            best = point;
            bestDistance = distance;
            bestRank = rank;
        }
        return best;
    }

    /// <summary>
    /// 지점이 이번 ↑를 처리했다(accepted = 시설이 받아들여 작동을 시작함, false = 거절음으로 끝남). 거절이어도 같은 ↑로 붙지 않게 프레임은 기록한다.
    /// accepted면 max(MinBusy, busySeconds) 동안 + 시설의 IFacilityBusy가 작동 중인 동안 IsBusy.
    /// </summary>
    public static void MarkHandled(FacilityInteractPoint point, bool accepted, float busySeconds = 0f)
    {
        handledFrame = Time.frameCount;
        lastHandled = point;
        Handled++;
        cachedFrame = -1;   // 처리로 시설 상태가 바뀌었다 — 이번 프레임의 남은 판단은 새로 묻는다
        if (!accepted) return;
        Accepted++;
        handledAt = Time.time;
        busyUntil = Mathf.Max(busyUntil, Time.time + Mathf.Max(MinBusy, busySeconds));
        var busy = point != null ? point.Target as IFacilityBusy : null;
        if (busy != null && busy.FacilityBusy) busySource = busy;
    }

    /// <summary>받아들인 시설 작동이 아직 진행 중인지(크레인 붙기 표시·입력 잠금).</summary>
    public static bool IsBusy
    {
        get
        {
            float now = Time.time;
            if (now < busyUntil) return true;
            if (busySource == null) return false;
            if ((busySource is Object unity && unity == null) || now - handledAt > MaxBusy || !busySource.FacilityBusy)
            {
                busySource = null;
                return false;
            }
            return true;
        }
    }

    /// <summary>시설 종류 순위(거리가 같을 때만 쓴다 — 출구 > 레버 > 수리대 > 텔레포트 > 그 밖).</summary>
    public static int RankOf(IFacilityInteractTarget target)
    {
        if (target is PropExitGate) return RankExit;
        if (target is PropObjectiveLever) return RankLever;
        if (target is PropRepairStation) return RankRepair;
        if (target is DungeonTeleporter) return RankTeleport;
        return RankOther;
    }
}
