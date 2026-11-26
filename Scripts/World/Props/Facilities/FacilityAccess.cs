using System;
using System.Collections.Generic;
using UnityEngine;

// 던전 이동·상호작용 시설(경로 크레인·고정 크레인·수리대·텔레포트·목표 레버·출구 문)이 함께 쓰는 사용 조건.
// - 사용 시점은 두 가지다(FacilityUse). AfterClear(레버·텔레포트·수리대)는 지금 방을 클리어한 뒤에만,
//   Anytime(크레인 2종 — 붙기)은 전투 중에도 쓴다(4차: 승강기·차폐막 스위치·캐터펄트는 삭제).
// - 던전 안 AfterClear는 DungeonFacilityHost.Current(DungeonManager)가 '지금 방 클리어·관 통과 아님·던전 진행 중·패배 아님'을 판단한다.
//   던전 안 Anytime은 호스트가 IDungeonFacilityRoute를 구현하면 CanUseFacilitiesInCombat(방 클리어 불문)으로, 구현하지 않은 호스트면 AfterClear 규칙으로 본다.
// - Wave 없는 장면(Lobby 등)이면 Current가 null이므로 늘 쓸 수 있다(두 사용 시점 모두).
// - 기본 진행 Wave(던전 아님)에서는 쓰지 않는다: '방 클리어' 신호가 없어 전투 중에 탈 수 있고, 탑승 구속(WavePlayerStart)이
//   피해·잡기를 막아 오르내리기만 반복해도 무적이 되기 때문이다(판단값 — 기본 진행 Wave에 놓으면 꺼진 받침처럼 안내가 뜨지 않는다).
//   Anytime도 던전 밖에서는 이 규칙을 그대로 따른다(던전 밖 규칙 유지 — DESIGN_V3 §3).
// - Wave 진행 중이면 Wave 준비(구속 상자·크레인 운반) 동안과 Wave 사이에는 쓰지 않는다(두 사용 시점 모두).
// - 7차(DESIGN_V7 V5): 목표 레버는 CanUseObjective로 묻는다. 호스트가 IDungeonObjectiveGate를 구현하면 CanCompleteObjectives(방 클리어 뒤 또는 '레버 대기' — 적 전멸 뒤
//   문이 잠긴 동안), 아니면 기존 HostAllows다. 텔레포트·수리대·출구 문은 그대로 CanUse(레버 대기 중에는 거절)다.
/// <summary>시설을 언제 쓸 수 있는지(3차 계약).</summary>
public enum FacilityUse
{
    /// <summary>지금 방을 클리어한 뒤에만(기존 규칙 — 레버·텔레포트·수리대 등).</summary>
    AfterClear = 0,
    /// <summary>전투 중에도(경로 크레인·고정 크레인). 던전에서는 IDungeonFacilityRoute.CanUseFacilitiesInCombat로 판단한다.</summary>
    Anytime = 1,
}

public static class FacilityAccess
{
    /// <summary>던전 진행이 시설 사용을 허락하는지. 던전 밖이면 Wave 진행 장치가 없을 때만 true(기본 진행 Wave에서는 false).</summary>
    public static bool HostAllows
    {
        get
        {
            var host = DungeonFacilityHost.Current;
            return host != null ? host.CanUseFacilities : WaveManager.ActiveInstance == null;
        }
    }

    /// <summary>던전 안에서 진행 중인지(DungeonFacilityHost.Current가 있는지).</summary>
    public static bool InDungeon => DungeonFacilityHost.Current != null;

    /// <summary>Wave가 진행 중이고 준비(구속) 중이 아닌지. Wave 진행 장치가 없으면 true.</summary>
    public static bool WaveAllows
    {
        get
        {
            var waves = WaveManager.ActiveInstance;
            return waves == null || (waves.IsWaveActive && !waves.IsPreparingWave);
        }
    }

    /// <summary>Wave가 끝나거나 멈췄는지(진행 중인 탑승을 끊는 조건). Wave 진행 장치가 없으면 false.</summary>
    public static bool WaveHalted
    {
        get
        {
            var waves = WaveManager.ActiveInstance;
            return waves != null && !waves.IsWaveActive;
        }
    }

    /// <summary>Player가 시설을 쓸 수 있는 상태인지(살아 있고, 입력을 받고, 다른 구속·잡기에 걸려 있지 않음).</summary>
    public static bool PlayerFree(PlayerController player)
    {
        if (player == null || !player.isActiveAndEnabled) return false;
        if (player.Health != null && (player.Health.IsDead || player.Health.IsDeathPending)) return false;
        if (!player.CanReceiveInput) return false;
        if (player.Motor == null || player.Motor.IsConfined) return false;
        return !WavePlayerStart.IsHolding(player);
    }

    /// <summary>시설 사용 조건 전부(던전 진행·Wave 진행·Player 상태).</summary>
    public static bool CanUse(PlayerController player) => HostAllows && WaveAllows && PlayerFree(player);

    /// <summary>
    /// 7차 V5: 던전 진행이 목표 실물(레버)을 허락하는지. 호스트가 IDungeonObjectiveGate면 CanCompleteObjectives(방 클리어 뒤 또는 레버 대기 — 전투 중에는 false),
    /// 아니면 <see cref="HostAllows"/>(6차까지의 규칙 — 회귀의 가짜 호스트 등).
    /// </summary>
    public static bool ObjectiveAllows
    {
        get
        {
            var host = DungeonFacilityHost.Current;
            return host is IDungeonObjectiveGate gate ? gate.CanCompleteObjectives : HostAllows;
        }
    }

    /// <summary>7차 V5: 목표 실물(레버) 사용 조건 전부(<see cref="ObjectiveAllows"/>·Wave 진행·Player 상태).</summary>
    public static bool CanUseObjective(PlayerController player) => ObjectiveAllows && WaveAllows && PlayerFree(player);

    /// <summary>
    /// 전투 중 시설(FacilityUse.Anytime)을 던전·Wave 진행이 허락하는지(Player 상태는 보지 않는다).
    /// 던전 호스트가 IDungeonFacilityRoute를 구현하면 CanUseFacilitiesInCombat(방 클리어 불문), 구현하지 않으면 기존 CanUseFacilities,
    /// 던전 밖이면 기존 규칙(Wave 진행 장치가 없을 때만). 어느 쪽이든 Wave 준비 중·Wave 사이에는 false.
    /// </summary>
    public static bool CombatAllows
    {
        get
        {
            var host = DungeonFacilityHost.Current;
            if (host is IDungeonFacilityRoute route) return route.CanUseFacilitiesInCombat && WaveAllows;
            if (host != null) return host.CanUseFacilities && WaveAllows;
            return HostAllows && WaveAllows;
        }
    }

    /// <summary>던전·Wave 진행이 이 사용 시점의 시설을 지금 허락하는지(Player 상태는 보지 않는다). 강조 효과의 '쓸 수 있음' 맥동 판정에 쓴다.</summary>
    public static bool Allows(FacilityUse use) => use == FacilityUse.Anytime ? CombatAllows : HostAllows && WaveAllows;

    /// <summary>
    /// 사용 시점 구분을 둔 시설 사용 조건. AfterClear는 <see cref="CanUse(PlayerController)"/>와 같다.
    /// Anytime은 <see cref="CombatAllows"/>(던전 안이면 IDungeonFacilityRoute.CanUseFacilitiesInCombat) + Player 상태.
    /// </summary>
    public static bool CanUse(PlayerController player, FacilityUse use)
    {
        switch (use)
        {
            case FacilityUse.Anytime:
                return CombatAllows && PlayerFree(player);
            case FacilityUse.AfterClear:
            default:
                return CanUse(player);
        }
    }

    private static readonly IReadOnlyList<IDungeonPassagePoint> NoPassages = Array.Empty<IDungeonPassagePoint>();

    /// <summary>
    /// 지금 방에 선 연결 출입구(던전 호스트가 IDungeonFacilityRoute를 구현할 때만, 아니면 빈 목록). 시설이 출입구 바닥 높이를 알아야 할 때 쓴다.
    /// 방을 세울 때 Prop이 출입구보다 먼저 생기므로 OnPropSpawned에서는 비어 있다 — SetRoomPowered(출입구가 선 뒤)나 사용 시점에 읽는다.
    /// </summary>
    public static IReadOnlyList<IDungeonPassagePoint> RoomPassages
        => DungeonFacilityHost.Current is IDungeonFacilityRoute route && route.RoomPassages != null ? route.RoomPassages : NoPassages;

    /// <summary>출입구 바닥 높이(월드 y). DungeonDoor면 FloorY, 아니면 나와서 선 몸통 중심(StandPoint)에서 몸통 반높이 0.755를 뺀다.</summary>
    public static float PassageFloorY(IDungeonPassagePoint passage)
    {
        if (passage is DungeonDoor door) return door.FloorY;
        return passage != null ? passage.StandPoint.y - .755f : float.NaN;
    }

    /// <summary>
    /// 층(던전)당 한 번 쓰는 표식을 썼는지 미리 본다. 계약 IDungeonFacilityHost에는 '쓰기'만 있어서, 호스트가 선택 인터페이스
    /// IDungeonFacilityTokens를 함께 구현할 때만 정확하다(아니면 false — 실제로 쓰려 할 때 TryUseFloorToken이 거절한다).
    /// </summary>
    public static bool IsFloorTokenUsed(string key)
    {
        return DungeonFacilityHost.Current is IDungeonFacilityTokens tokens && tokens.IsFloorTokenUsed(key);
    }

    /// <summary>Player 몸통 사각형(월드). 구속 중이면 모터가 쓰는 몸통, 아니면 Collider 경계(물리 질의 없음).</summary>
    public static Rect BodyRect(PlayerController player)
    {
        var motor = player != null ? player.Motor : null;
        if (motor != null && motor.IsConfined) return motor.ConfinedBody;
        if (player != null && player.TryGetComponent(out Collider2D body))
        {
            var b = body.bounds;
            return Rect.MinMaxRect(b.min.x, b.min.y, b.max.x, b.max.y);
        }
        Vector2 feet = player != null ? (Vector2)player.transform.position : Vector2.zero;
        return new Rect(feet.x - .35f, feet.y, .7f, 1.51f);
    }

    /// <summary>몸통 바닥 가운데(월드).</summary>
    public static Vector2 Feet(PlayerController player)
    {
        var body = BodyRect(player);
        return new Vector2(body.center.x, body.yMin);
    }

    /// <summary>점에서 회전 없는 사각형까지의 거리(안이면 0).</summary>
    public static float DistanceToRect(Vector2 point, Rect rect)
    {
        float dx = Mathf.Max(rect.xMin - point.x, 0f, point.x - rect.xMax);
        float dy = Mathf.Max(rect.yMin - point.y, 0f, point.y - rect.yMax);
        return Mathf.Sqrt(dx * dx + dy * dy);
    }

    /// <summary>Player가 지금 무엇을 하고 있든 상호작용 상태를 끝낸다(IInteractable.Interact 안에서 부르지 않으면 Interact 상태로 굳는다).</summary>
    public static void EndInteract(PlayerController player)
    {
        if (player != null && player.AbilityManager != null && player.AbilityManager.InteractAbility != null)
            player.AbilityManager.InteractAbility.Interrupt();
    }
}

/// <summary>
/// (선택) 시설 호스트가 층당 1회 표식을 쓰지 않고 미리 볼 수 있게 하는 인터페이스. DungeonManager가 구현하면 수리대가
/// 같은 층에서 다시 세워질 때 처음부터 '방전' 상태로 선다(계약 변경 제안 — 구현이 없어도 동작은 같고 표시만 늦다).
/// </summary>
public interface IDungeonFacilityTokens
{
    bool IsFloorTokenUsed(string key);
}
