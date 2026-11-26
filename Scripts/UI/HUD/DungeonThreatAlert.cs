using System.Globalization;
using UnityEngine;

/// <summary>
/// 던전 난이도 상승 알림의 문지기(7차 DESIGN_V7 V8). 실제 표시는 방 상태 띠 Prefab의 부품 <see cref="DungeonThreatRise"/>(오른쪽 HUD 열 미니맵 아래 '난이도 상승 계기')가 하고,
/// 이 클래스는 지금 그 계기를 띄울 수 있는지만 판단한다(DungeonRoomStatus가 묻는다).
/// - 6차(T12)의 '훈련 상황판 등급 알림 재사용'(TrainingScoreboardUI.PlayCustomAlert → TrainingGradeAlertUI.PlayCustom)은 사용자 요청('등급 변경과 구분')으로 없앴다 —
///   상황판·등급 하락 알림은 origin/main과 같은 코드다(이 클래스는 상황판의 읽기 전용 IsRunActive만 읽는다).
/// - 띄울 수 없으면(규칙에서 끔·상황판 없음·훈련 기록 중이 아님 — 튜토리얼·검사) 부르는 쪽이 4차 상태 띠 THREAT 단계로 대신한다.
/// 정적 글꼴(esamanru)에는 공백·× 글자가 없어 공백은 여백 태그(DungeonUiText.Static), ×는 x로 쓴다.
/// </summary>
public static class DungeonThreatAlert
{
    private static TrainingScoreboardUI board;
    private static float nextSearch = float.NegativeInfinity;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCache()
    {
        board = null;
        nextSearch = float.NegativeInfinity;
    }

    /// <summary>규칙에서 알림을 켰는지(규칙 SO가 없으면 켠 것으로 본다).</summary>
    public static bool Enabled
    {
        get
        {
            var rules = DungeonRulesSO.Default;
            return rules == null || rules.threatAlert;
        }
    }

    /// <summary>지금 난이도 상승 계기를 띄울 수 있는지(켜짐 + 훈련 상황판이 훈련 기록 중).</summary>
    public static bool Available
    {
        get
        {
            if (!Enabled) return false;
            var target = Board();
            return target != null && target.IsRunActive;
        }
    }

    /// <summary>배율 글자 'x1.30'(× 대신 x). 계기 아래 줄이 Play 때 한 번 만든다.</summary>
    public static string DensityText(float density) => "x" + density.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>6차 아래 줄 글('적 출현 x1.30', 공백은 여백 태그) — 7차 계기는 이전·새 배율을 따로 쓴다(DensityText). 옛 검사·도구 호환용.</summary>
    public static string DetailText(float density) => DungeonUiText.Static("적 출현 " + DensityText(density));

    // 장면의 훈련 상황판(실제 시간 1초에 한 번까지만 다시 찾는다 — DungeonHudGate와 같은 방식).
    private static TrainingScoreboardUI Board()
    {
        if (board != null) return board;
        float now = Time.realtimeSinceStartup;
        // 찾아 둔 상황판이 파괴됐으면(장면 다시 읽기) 바로 다시 찾고, 기한이 1초보다 멀면(실제 시간 기준이 뒤로 감) 그 기한을 버린다(DungeonHudGate와 같음).
        bool lost = !object.ReferenceEquals(board, null);
        if (!lost && now < nextSearch && nextSearch - now <= 1f) return null;
        nextSearch = now + 1f;
        board = Object.FindFirstObjectByType<TrainingScoreboardUI>();
        return board;
    }
}
