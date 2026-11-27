using System.Text;

/// <summary>
/// 던전 UI(미니맵·목표 패널·현장 단말기) 글자 도우미(3차, 2026-11-24).
/// esamanru Medium SDF는 정적 아틀라스라 공백(U+0020) 글자가 없다(ASCII 나머지와 KS X 1001 한글 2,350자는 있음 — 2026-11-24 글꼴 에셋 확인).
/// 그래서 공백은 TMP 여백 태그 &lt;space=…em&gt;로 바꿔 넣는다(방 상태 띠·무장 카드·화상 표시와 같은 방식, richText 필요).
/// 실행 중 쓰는 한글·기호는 <see cref="Glyphs"/>에 모아 두고 생성 도구 Verify가 글꼴에 있는지 확인한다(가운뎃점·낫표·도형 기호는 글꼴에 없어 쓰지 않는다).
/// 4차(2026-11-25): 가운뎃점(·) 대신 마침표를 TMP 세로 이동 태그로 글자 가운데 높이에 올린 <see cref="MidDot"/>를 쓴다(글꼴에 없는 글자를 쓰지 않는 같은 원칙).
///   난이도 단계 이름(입문·하급·중급·상급·특급)·출구 문구 글자를 <see cref="Glyphs"/>에 더했다. ▲·▮ 같은 도형은 글자가 아니라 생성 Mesh로 그린다.
/// 5차(2026-11-25, DESIGN_V5 S8·S9): 방 클리어 문구 '잠금 해제!'(금·해·!)와 큰 지도 범례 '크레인 경로'·'크레인 고정'(크·로·정)의 글자를 더했다.
/// 7차(2026-11-26, DESIGN_V7 §1.4·§1.6): 현장 단말기 계기판형 정보 판('남은 피격'·'남은 시간'·'적 출현 x1.30'·'진행'·미확인 '?' — 은·적·x·진·행·?)과
///   지도 조작 바닥줄('[방향키/드래그] 이동  [휠] 확대/축소' — 축·소·휠·드·래·그)의 글자를 더했다(모두 esamanru 정적 글꼴에 있음 — 2026-11-26 글꼴 문자표 확인).
///   7차 V2로 범례 크레인 2칸은 지웠지만 크·로·정은 다른 문구가 쓸 수 있어 그대로 둔다. '×'·'▸'·'▲'는 글꼴에 없어 'x'·Mesh로 그린다.
/// </summary>
public static class DungeonUiText
{
    /// <summary>공백 한 칸의 폭(em).</summary>
    public const string SpaceTag = "<space=0.35em>";

    /// <summary>
    /// 가운뎃점 대용(4차): 마침표를 0.3em 올려 글자 가운데 높이에 둔다. 정적 글꼴에 U+00B7이 없어서다. 앞뒤 여백은 부르는 쪽이 붙인다(<see cref="AppendSeparator"/>).
    /// </summary>
    public const string MidDot = "<voffset=0.3em>.</voffset>";

    /// <summary>현장 단말기·목표 패널이 실행 중 쓰는 모든 고정 글자(숫자·영문 대문자·기호 포함). 생성 도구가 글꼴에 있는지 검사한다.</summary>
    public const string Glyphs =
        "0123456789/:+-()[]%." +
        "ABCDEFGHIJKLMNOPQRSTUVWXYZ" +
        "현장단말기전투구역지도클리어닫방향키이동훈련록없음경과시간제한피격회등급유남" +
        "준하내추가목표완료발견미확인재잠긴출입텔레포트수대위치상황예무결점우통흡버작" +
        "난문중특다계까최고김개" +
        "금해!크로정" +
        "은적x진행?축소휠드래그" +
        "튼선택실" +
        "상세맵메뉴취아니오처부터할요을비돌갈";

    /// <summary>공백을 여백 태그로 바꾼다(공백이 없으면 같은 문자열을 그대로 돌려준다 — 할당 없음).</summary>
    public static string Static(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf(' ') < 0) return text;
        return text.Replace(" ", SpaceTag);
    }

    /// <summary>StringBuilder에 글자를 붙이며 공백은 여백 태그로 바꾼다.</summary>
    public static StringBuilder AppendStatic(this StringBuilder builder, string text)
    {
        if (string.IsNullOrEmpty(text)) return builder;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == ' ') builder.Append(SpaceTag);
            else builder.Append(c);
        }
        return builder;
    }

    /// <summary>항목 구분: 여백 + 가운뎃점 대용 + 여백('A · B'처럼 보인다).</summary>
    public static StringBuilder AppendSeparator(this StringBuilder builder)
    {
        return builder.Append(SpaceTag).Append(MidDot).Append(SpaceTag);
    }

    /// <summary>초를 mm:ss로(음수는 0).</summary>
    public static StringBuilder AppendClock(this StringBuilder builder, float seconds)
    {
        int total = UnityEngine.Mathf.Max(0, UnityEngine.Mathf.FloorToInt(seconds));
        int minutes = total / 60, secs = total % 60;
        if (minutes < 10) builder.Append('0');
        builder.Append(minutes).Append(':');
        if (secs < 10) builder.Append('0');
        builder.Append(secs);
        return builder;
    }

    /// <summary>7차: 배율을 'x1.30'처럼 소수 둘째 자리까지 붙인다(반올림, 음수는 0 — '×'는 정적 글꼴에 없어 소문자 x).</summary>
    public static StringBuilder AppendMultiplier(this StringBuilder builder, float value)
    {
        int hundredths = UnityEngine.Mathf.Max(0, UnityEngine.Mathf.RoundToInt(value * 100f));
        builder.Append('x').Append(hundredths / 100).Append('.');
        int frac = hundredths % 100;
        if (frac < 10) builder.Append('0');
        builder.Append(frac);
        return builder;
    }

    /// <summary>Color32를 TMP 색 태그 값(#RRGGBB)으로.</summary>
    public static StringBuilder AppendColorTag(this StringBuilder builder, UnityEngine.Color color)
    {
        builder.Append("<color=#").Append(UnityEngine.ColorUtility.ToHtmlStringRGB(color)).Append('>');
        return builder;
    }
}
