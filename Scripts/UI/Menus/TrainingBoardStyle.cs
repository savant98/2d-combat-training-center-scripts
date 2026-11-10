using UnityEngine;

// 훈련 상황판·등급 하락 알림·Wave 전환·결과 보드가 함께 쓰는 표기와 색(2026-11-10 TrainingRoom 상황판 리메이크).
// PlayerHUD의 사이파이 색(반투명 어두운 판 + 얇은 청록 선)에 맞춘 UI 전용 값이다.
// 훈련 메뉴가 계속 쓰는 TrainingScoreFormatHelper(Core)의 문구·색은 바꾸지 않는다.
public static class TrainingBoardStyle
{
    // PlayerHUD 판 색(PlayerHudSetup의 Line·LineDim·LineBright·SlotFill과 같은 값)
    public static readonly Color Line = new Color32(0x2b, 0xc8, 0xe2, 0xff);
    public static readonly Color LineDim = new Color32(0x17, 0x4b, 0x5c, 0xff);
    public static readonly Color LineBright = new Color32(0x8f, 0xf4, 0xff, 0xff);
    public static readonly Color SlotFill = new Color32(0x0b, 0x17, 0x26, 0xff);
    public static readonly Color Warning = new Color32(0xff, 0x5a, 0x3c, 0xff);
    public static readonly Color Gold = new Color32(0xff, 0xd1, 0x66, 0xff);

    public static string GradeLabel(Grade grade) => grade switch
    {
        Grade.Flawless => "무결점",
        Grade.Excellent => "우수",
        Grade.Pass => "통과",
        _ => "미흡",
    };

    // 등급 색: 무결점 청록, 우수 금색, 통과 초록(PlayerHUD 체력 칸), 미흡 빨강.
    public static Color GradeColor(Grade grade) => grade switch
    {
        Grade.Flawless => new Color32(0x66, 0xf6, 0xff, 0xff),
        Grade.Excellent => new Color32(0xff, 0xd1, 0x66, 0xff),
        Grade.Pass => new Color32(0x33, 0xf2, 0x59, 0xff),
        _ => new Color32(0xff, 0x44, 0x33, 0xff),
    };

    // 패배 사유(패배 결과 화면).
    public static string FailReasonLabel(TrainingFailReason reason) => reason switch
    {
        TrainingFailReason.HitLimit => "피격 한도 초과",
        TrainingFailReason.TimeLimit => "제한 시간 초과",
        _ => "체력 소진",
    };

    // 피해·회복량: 반올림한 정수에 천 단위 구분(쉴드 재생 때문에 적용량이 소수일 수 있다).
    public static string FormatAmount(float value) =>
        Mathf.RoundToInt(value).ToString("N0", System.Globalization.CultureInfo.InvariantCulture);

    // 비율(0~1)을 소수 한 자리 %로. 자율 훈련장의 "기여 비율"과 같은 표기다("45.2%").
    public static string FormatPercent(float ratio) =>
        (Mathf.Clamp01(ratio) * 100f).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "%";

    // 분:초. 남은 시간은 올림(시작할 때 03:00), 경과 시간은 내림이다.
    public static string FormatTime(float seconds, bool countdown)
    {
        int total = Mathf.Max(0, countdown ? Mathf.CeilToInt(seconds - 0.0001f) : Mathf.FloorToInt(seconds));
        return $"{total / 60:00}:{total % 60:00}";
    }

    // 통과(Pass) 등급 조건. 상황판의 "남은 피격"·"남은 시간"은 통과 등급을 잃기까지 남은 양이다(-1은 제한 없음).
    // 옛 상황판은 rules[2]를 직접 읽었다. 순서가 바뀌어도 맞도록 등급으로 찾고, 없으면 통과에 가장 가까운 아래 규칙을 쓴다.
    public static void GetPassLimits(TrainingDefinitionSO training, out int maxHits, out float maxTimeSeconds)
    {
        maxHits = -1;
        maxTimeSeconds = -1f;
        if (training == null || training.rules == null) return;
        int best = -1;
        for (int i = 0; i < training.rules.Length; i++)
        {
            Grade g = training.rules[i].grade;
            if (g == Grade.Pass) { best = i; break; }
            if (g > Grade.Pass && (best < 0 || g < training.rules[best].grade)) best = i;
        }
        if (best < 0) return;
        maxHits = training.rules[best].maxHits;
        maxTimeSeconds = training.rules[best].maxTimeSeconds;
    }
}
